using System.Text;
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
/// afterwards, each in its own dedicated Copilot instance. The report's usage
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
        var gradeResults = await RunGradersAsync(
            eval, options, evalResult, workingDirectory, cancellationToken);

        var combinedUsage = gradeResults.Aggregate(
            evalResult.UsageSummary,
            (usage, grade) => usage.Add(grade.Usage));

        return new EvalReport
        {
            EvalName = eval.Name,
            Status = EvalRunStatus.Completed,
            EvalResult = evalResult,
            GradeResults = gradeResults,
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

        var transcript = new StringBuilder();
        var inputTokens = 0L;
        var outputTokens = 0L;
        var result = new TaskCompletionSource<EvalResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var _ = cancellationToken.Register(
            () => result.TrySetCanceled(cancellationToken));

        session.On<SessionEvent>(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg:
                    transcript.Append(msg.Data.Content);
                    break;

                case AssistantUsageEvent usage:
                    // assistant.usage carries both input and output token counts.
                    inputTokens += usage.Data.InputTokens ?? 0L;
                    outputTokens += usage.Data.OutputTokens ?? 0L;
                    break;

                case SessionIdleEvent idle when idle.Data.Aborted == true:
                    result.TrySetCanceled();
                    break;

                case SessionIdleEvent:
                    result.TrySetResult(new EvalResult
                    {
                        Transcript = transcript.ToString(),
                        UsageSummary = new UsageSummary
                        {
                            InputTokens = (int)inputTokens,
                            OutputTokens = (int)outputTokens,
                        },
                    });
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
    /// Runs the eval's graders: assertion graders first (in-process), then rubric
    /// graders, each in its own dedicated Copilot instance.
    /// </summary>
    private static async Task<IReadOnlyList<GradeResult>> RunGradersAsync(
        Eval eval,
        EvalRunOptions options,
        EvalResult evalResult,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var results = new List<GradeResult>(eval.Graders.Count);

        // Assertion graders run first, in declared order, without a Copilot instance.
        var assertionContext = new GradingContext
        {
            EvalResult = evalResult,
            WorkingDirectory = workingDirectory,
        };

        foreach (var grader in eval.Graders.Where(g => g.Type == GraderType.Assertion))
        {
            results.Add(await grader.GradeAsync(assertionContext, cancellationToken));
        }

        // Rubric graders run next, each in its own dedicated Copilot instance so
        // that grading is isolated from eval execution (and from each other).
        foreach (var grader in eval.Graders.Where(g => g.Type == GraderType.Rubric))
        {
            await using var instance = new RubricCopilotInstance(options, workingDirectory);
            var context = new GradingContext
            {
                EvalResult = evalResult,
                WorkingDirectory = workingDirectory,
                CreateGraderSessionAsync = instance.CreateSessionAsync,
            };

            results.Add(await grader.GradeAsync(context, cancellationToken));
        }

        return results;
    }

    /// <summary>
    /// A dedicated Copilot instance for a single rubric grader. The underlying
    /// client is started lazily on first session request, so a grader that never
    /// needs an LLM incurs no runtime cost.
    /// </summary>
    private sealed class RubricCopilotInstance(EvalRunOptions options, string workingDirectory)
        : IAsyncDisposable
    {
        private readonly List<CopilotGraderSession> _sessions = [];
        private CopilotClient? _client;

        public async Task<IGraderSession> CreateSessionAsync(CancellationToken cancellationToken)
        {
            if (_client is null)
            {
                _client = new CopilotClient(new CopilotClientOptions
                {
                    GitHubToken = options.GitHubToken,
                });
                await _client.StartAsync();
            }

            var session = await _client.CreateSessionAsync(new SessionConfig
            {
                Model = options.Model ?? DefaultModel,
                WorkingDirectory = workingDirectory,
                OnPermissionRequest = PermissionHandler.ApproveAll,
            });

            var graderSession = new CopilotGraderSession(session);
            _sessions.Add(graderSession);
            return graderSession;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var session in _sessions)
            {
                await session.DisposeAsync();
            }

            if (_client is not null)
            {
                await _client.DisposeAsync();
            }
        }
    }
}
