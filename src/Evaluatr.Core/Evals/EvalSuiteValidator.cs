namespace Evaluatr.Core.Evals;

/// <summary>
/// The execution mode inferred for an eval suite from its evals' <c>repo</c> and
/// <c>baseCommit</c> fields. Determines where each eval runs.
/// </summary>
public enum SuiteExecutionMode
{
    /// <summary>Every eval has a <c>repo</c>; each runs in its own temp clone.</summary>
    Repo,

    /// <summary>
    /// No eval has a <c>repo</c> and every eval has a <c>baseCommit</c>; each runs
    /// in its own throwaway detached worktree of the cwd repository.
    /// </summary>
    Commit,

    /// <summary>
    /// No eval has a <c>repo</c> or a <c>baseCommit</c>; all evals run in the
    /// shared current working directory.
    /// </summary>
    LiveWorkspace,
}

/// <summary>
/// Thrown when a suite is not internally consistent: either it omits the required
/// <c>harness</c> field, or it mixes execution modes across its evals (e.g. some
/// evals specify a <c>repo</c> and others do not). Mode validation constrains
/// field <em>presence</em>, not value — differing repos or commits within a mode
/// are allowed.
/// </summary>
public sealed class EvalSuiteValidationException : Exception
{
    /// <inheritdoc cref="EvalSuiteValidationException"/>
    public EvalSuiteValidationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Infers and validates a suite's <see cref="SuiteExecutionMode"/> from its
/// evals. Runs at load time, before any checkout or Copilot work, so the runner
/// only ever sees mode-consistent evals.
/// </summary>
public static class EvalSuiteValidator
{
    /// <summary>
    /// Determines the execution mode of <paramref name="suite"/>, enforcing the
    /// all-or-none presence rules for <c>repo</c> and <c>baseCommit</c>.
    /// </summary>
    /// <exception cref="EvalSuiteValidationException">
    /// Thrown when the suite omits <c>harness</c>, or when evals mix
    /// repo/workspace or commit/live-workspace modes.
    /// </exception>
    public static SuiteExecutionMode DetermineMode(EvalSuite suite)
    {
        ArgumentNullException.ThrowIfNull(suite);

        // 'harness' selects the runner and is required. YamlDotNet does not enforce
        // the model's 'required' modifier, so a suite that omits it deserializes
        // with a null harness; catch that here as an authoring error rather than
        // letting it surface downstream as a null-key lookup.
        if (string.IsNullOrWhiteSpace(suite.Harness))
        {
            throw new EvalSuiteValidationException(
                $"Suite '{suite.Name}' does not specify a 'harness'. Every suite must name " +
                "the harness that executes it (e.g. 'copilot').");
        }

        var evals = suite.Evals;

        // An empty suite is a no-op; treat it as a live workspace so it never
        // throws and needs no repo/commit context.
        if (evals is null || evals.Count == 0)
        {
            return SuiteExecutionMode.LiveWorkspace;
        }

        if (evals.Any(e => e.Repo is not null))
        {
            var withoutRepo = evals.Where(e => e.Repo is null).Select(e => e.Name).ToList();
            if (withoutRepo.Count > 0)
            {
                throw new EvalSuiteValidationException(
                    $"Suite '{suite.Name}' mixes repository and workspace evals: every eval " +
                    $"must specify 'repo' when any does. Eval(s) missing 'repo': " +
                    $"{string.Join(", ", withoutRepo)}.");
            }

            return SuiteExecutionMode.Repo;
        }

        if (evals.Any(e => e.BaseCommit is not null))
        {
            var withoutBaseCommit = evals.Where(e => e.BaseCommit is null).Select(e => e.Name).ToList();
            if (withoutBaseCommit.Count > 0)
            {
                throw new EvalSuiteValidationException(
                    $"Suite '{suite.Name}' mixes commit and live-workspace evals: every eval " +
                    $"must specify 'baseCommit' when any does (and none specify 'repo'). " +
                    $"Eval(s) missing 'baseCommit': {string.Join(", ", withoutBaseCommit)}.");
            }

            return SuiteExecutionMode.Commit;
        }

        return SuiteExecutionMode.LiveWorkspace;
    }
}
