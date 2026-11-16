using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;
using Evaluatr.Core.Runner;
using GitHub.Copilot;

namespace Evaluatr.Copilot;

/// <summary>
/// Executes evals end to end: checks out the target repository, drives a GitHub
/// Copilot SDK session to run the prompt, then grades the result and reports the
/// combined outcome.
/// </summary>
/// <remarks>
/// <para>
/// <b>Execution context</b> — resolved per eval from its own fields:
/// <list type="bullet">
///   <item><description>
///     <c>Repo != null</c> → cloned to a temp directory (checked out to
///     <c>BaseCommit</c> when set), deleted after the run.
///   </description></item>
///   <item><description>
///     <c>Repo == null &amp;&amp; BaseCommit != null</c> → a throwaway detached
///     <c>git worktree</c> of the working directory at <c>BaseCommit</c>, removed
///     after the run.
///   </description></item>
///   <item><description>
///     <c>Repo == null &amp;&amp; BaseCommit == null</c> → the working directory
///     itself, provided it is a git work tree and (unless
///     <see cref="EvalRunOptions.AllowDirty"/>) is clean.
///   </description></item>
/// </list>
/// Suite-level all-or-none consistency is enforced at load time; the runner only
/// sees mode-consistent evals and decides per eval from fields.
/// </para>
/// <para>
/// <b>Execution</b> — a fresh <see cref="CopilotClient"/> runs the prompt with
/// the temp checkout as its working directory. When the eval names an agent it
/// is selected via <c>session.Rpc.Agent.SelectAsync</c>. The transcript is the
/// concatenation of assistant message content; token usage is summed from
/// <see cref="AssistantUsageEvent"/>.
/// </para>
/// <para>
/// <b>Grading</b> — assertion graders run first, in-process. Rubric graders run
/// afterwards, each through the configured judge provider. The report's usage
/// combines the eval execution usage with every grader's own usage.
/// </para>
/// </remarks>
public sealed class CopilotEvalRunner : IEvalRunner
{
    /// <summary>
    /// The Copilot model used when <see cref="EvalRunOptions.Model"/> is not set.
    /// Core's neutral options bag intentionally carries no model default; the
    /// Copilot-specific default lives here.
    /// </summary>
    private const string DefaultModel = "gpt-4o";

    /// <inheritdoc />
    public async Task<EvalReport> RunAsync(
        Eval eval,
        EvalRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eval);
        ArgumentNullException.ThrowIfNull(options);

        EvalSuiteValidator.ValidateGraders(eval);

        // Repo eval → isolated temp clone.
        if (eval.Repo is not null)
        {
            using var checkout = await RepositoryCheckout.CreateAsync(
                eval.Repo, eval.BaseCommit, cancellationToken);
            return await RunInDirectoryAsync(eval, options, checkout.Path, cancellationToken);
        }

        // Workspace modes require the working directory to be a git work tree.
        var workingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : options.WorkingDirectory;

        if (!await GitWorkspace.IsInsideWorkTreeAsync(workingDirectory, cancellationToken))
        {
            throw new WorkspaceInvariantException(
                $"Cannot run eval '{eval.Name}': the working directory '{workingDirectory}' " +
                "is not a git work tree. Evals without a 'repo' must run inside a git " +
                "repository.");
        }

        // Commit eval → throwaway detached worktree at the requested commit.
        if (eval.BaseCommit is not null)
        {
            using var worktree = await WorktreeCheckout.CreateAsync(
                workingDirectory, eval.BaseCommit, cancellationToken);
            return await RunInDirectoryAsync(eval, options, worktree.Path, cancellationToken);
        }

        // Live-workspace eval → the working directory itself. Guard against a
        // dirty tree unless explicitly allowed.
        if (!options.AllowDirty && await GitWorkspace.IsDirtyAsync(workingDirectory, cancellationToken))
        {
            throw new WorkspaceInvariantException(
                $"Cannot run eval '{eval.Name}': the working directory '{workingDirectory}' " +
                "has uncommitted changes. Commit or stash them, or allow the run to proceed " +
                "against the dirty tree (e.g. pass --allow-dirty).");
        }

        return await RunInDirectoryAsync(eval, options, workingDirectory, cancellationToken);
    }

    /// <summary>
    /// Executes the eval and its graders in <paramref name="workingDirectory"/>,
    /// then assembles the combined report. The directory has already been resolved
    /// and validated for the eval's mode.
    /// </summary>
    private static async Task<EvalReport> RunInDirectoryAsync(
        Eval eval,
        EvalRunOptions options,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var evalResult = await ExecuteEvalAsync(eval, options, workingDirectory, cancellationToken);
        var grading = await RunGradersAsync(
            eval, options, evalResult, workingDirectory, cancellationToken);

        var combinedUsage = grading.Results.Aggregate(
            evalResult.UsageSummary,
            (usage, grade) => usage.Add(grade.Usage));

        return new EvalReport
        {
            EvalName = eval.Name,
            Status = EvalRunStatus.Completed,
            EvalResult = evalResult,
            Verdict = grading.Verdict,
            GradeResults = grading.Results,
            UsageSummary = combinedUsage,
        };
    }

    /// <summary>
    /// Runs the eval prompt in a Copilot session rooted at <paramref name="workingDirectory"/>.
    /// </summary>
    private static async Task<EvalResult> ExecuteEvalAsync(
        Eval eval,
        EvalRunOptions options,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var clientOptions = new CopilotClientOptions
        {
            GitHubToken = options.GitHubToken,
        };

        await using var client = new CopilotClient(clientOptions);
        await client.StartAsync();

        var sessionConfig = new SessionConfig
        {
            Model = options.Model ?? DefaultModel,
            WorkingDirectory = workingDirectory,
            // Auto-approve all tool-use permission requests so evals run
            // unattended. Evals are non-interactive by design.
            OnPermissionRequest = PermissionHandler.ApproveAll,
        };

        await using var session = await client.CreateSessionAsync(sessionConfig);

        if (eval.Agent is not null)
        {
            await session.Rpc.Agent.SelectAsync(eval.Agent, cancellationToken);
        }

        var collector = new EvalEventCollector();
        var result = new TaskCompletionSource<EvalResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var _ = cancellationToken.Register(
            () => result.TrySetCanceled(cancellationToken));

        using var subscription = session.On<SessionEvent>(evt =>
        {
            collector.Record(evt);
            switch (evt)
            {
                case SessionIdleEvent idle when idle.Data.Aborted == true:
                    result.TrySetCanceled();
                    break;

                case SessionIdleEvent:
                    result.TrySetResult(collector.Snapshot());
                    break;

                case SessionErrorEvent err:
                    result.TrySetException(new CopilotEvalException(
                        eval.Name,
                        err.Data.Message,
                        err.Data.ErrorCode));
                    break;
            }
        });

        await session.SendAsync(new MessageOptions { Prompt = eval.Prompt });

        return await result.Task;
    }

    /// <summary>
    /// Runs Core's grading policy using the provider-neutral factory from composition wiring.
    /// </summary>
    private static Task<GradingReport> RunGradersAsync(
        Eval eval,
        EvalRunOptions options,
        EvalResult evalResult,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var context = new GradingContext
        {
            EvalResult = evalResult,
            TaskPrompt = eval.Prompt,
            WorkingDirectory = workingDirectory,
            CreateGraderSessionAsync = options.CreateGraderSessionAsync,
        };

        return GradingRunner.RunAsync(eval.Graders, context, cancellationToken);
    }
}
