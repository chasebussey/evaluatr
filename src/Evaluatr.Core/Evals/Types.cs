using Evaluatr.Core.Graders;

namespace Evaluatr.Core.Evals;

public record EvalSuite
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Harness { get; init; }
    public required List<Eval> Evals { get; init; }
}

public record Eval
{
    public required string Name { get; init; }
    public required string Description { get; init; }

    /// <summary>
    /// The GitHub <c>owner/repo</c> slug to clone before running the eval. When
    /// set, the eval runs in an isolated temp clone. When <see langword="null"/>,
    /// the eval runs against the working directory (as a live-workspace eval, or
    /// — when <see cref="BaseCommit"/> is set — in a throwaway worktree).
    /// </summary>
    public string? Repo { get; init; }

    /// <summary>
    /// The commit to check out before running. With <see cref="Repo"/>, it is
    /// checked out in the temp clone. Without <see cref="Repo"/>, the eval runs in
    /// a throwaway detached worktree of the working directory at this commit. When
    /// <see langword="null"/>, the repository's default checkout (or the live
    /// working directory) is used.
    /// </summary>
    public string? BaseCommit { get; init; }

    public required string Prompt { get; init; }

    /// <summary>
    /// The harness agent to select for the run. When <see langword="null"/>, the
    /// harness's default agent is used.
    /// </summary>
    public string? Agent { get; init; }

    public required List<IGrader> Graders { get; init; }
}

public record EvalResult
{
    public required string Transcript { get; init; }
    public required UsageSummary UsageSummary { get; init; }

    /// <summary>Tool names observed at execution start in the agent session, including failed calls.</summary>
    public IReadOnlyList<string> ToolCalls { get; init; } = [];
}

/// <summary>
/// The outcome of executing a single <see cref="Eval"/>: its grade results and
/// the combined token usage of the run plus any grader Copilot usage.
/// </summary>
public record EvalReport
{
    /// <summary>The name of the eval this report describes.</summary>
    public required string EvalName { get; init; }

    /// <summary>The terminal state of the eval run.</summary>
    public EvalRunStatus Status { get; init; } = EvalRunStatus.Completed;

    /// <summary>The grading verdict, separate from whether execution completed.</summary>
    public EvalVerdict Verdict { get; init; } = EvalVerdict.Pass;

    /// <summary>Human-readable detail explaining a non-completed status.</summary>
    public string? StatusDetail { get; init; }

    /// <summary>The raw agent result. <see langword="null"/> when the eval did not run.</summary>
    public EvalResult? EvalResult { get; init; }

    /// <summary>Grades in execution order: required assertions, optional assertions, then rubrics.</summary>
    public IReadOnlyList<GradeResult> GradeResults { get; init; } = [];

    /// <summary>
    /// Combined token usage: the eval execution usage plus every grader's own
    /// usage (e.g. rubric grading).
    /// </summary>
    public UsageSummary UsageSummary { get; init; } = new();
}

/// <summary>The terminal state of an eval run.</summary>
public enum EvalRunStatus
{
    /// <summary>Agent execution and grading completed; some graders may have been skipped.</summary>
    Completed,
}

/// <summary>The aggregate grading verdict; optional assertions never block a pass.</summary>
public enum EvalVerdict
{
    /// <summary>All required assertions passed and no rubric failed or errored.</summary>
    Pass,
    /// <summary>A required assertion failed/errored, or a rubric failed.</summary>
    Fail,
    /// <summary>A rubric could not be evaluated.</summary>
    Error,
}

public record UsageSummary
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    public UsageSummary Add(UsageSummary usageSummary) =>
        new()
        {
            InputTokens = InputTokens + usageSummary.InputTokens,
            OutputTokens = OutputTokens + usageSummary.OutputTokens,
        };
}