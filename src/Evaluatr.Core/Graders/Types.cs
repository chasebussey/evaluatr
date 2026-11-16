using Evaluatr.Core.Evals;

namespace Evaluatr.Core.Graders;

/// <summary>A harness-neutral session used by LLM-backed graders.</summary>
public interface IGraderSession : IAsyncDisposable
{
    /// <summary>Sends a prompt and returns the reply.</summary>
    Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>The session's accumulated token usage.</summary>
    UsageSummary Usage { get; }
}

/// <summary>A named check or rubric applied to an agent's result.</summary>
public interface IGrader
{
    /// <summary>The grader's display name.</summary>
    string Name { get; }

    /// <summary>Optional description of what the grader checks.</summary>
    string? Description { get; }

    /// <summary>The kind of grader.</summary>
    GraderType Type { get; }

    /// <summary>Grades the supplied execution result.</summary>
    Task<GradeResult> GradeAsync(GradingContext context, CancellationToken cancellationToken = default);
}

/// <summary>The supported grader categories.</summary>
public enum GraderType
{
    Assertion,
    Rubric,
}

/// <summary>The result of evaluating a grader, distinct from execution completion.</summary>
public enum GradeOutcome
{
    /// <summary>The check matched.</summary>
    Pass,
    /// <summary>The check ran but did not match.</summary>
    Fail,
    /// <summary>The grader could not evaluate the result.</summary>
    Error,
    /// <summary>A required assertion blocked this grader.</summary>
    Skipped,
}

/// <summary>Everything a grader needs to inspect an eval's output.</summary>
public sealed record GradingContext
{
    /// <summary>The raw agent execution result.</summary>
    public required EvalResult EvalResult { get; init; }

    /// <summary>The workspace in which the eval ran.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>The original task prompt supplied to the executing agent.</summary>
    public required string TaskPrompt { get; init; }

    /// <summary>Creates an inspection-only session using the selected provider and model.</summary>
    public Func<GraderSessionOptions, CancellationToken, Task<IGraderSession>>? CreateGraderSessionAsync { get; init; }
}

/// <summary>A grader's outcome and diagnostic explanation. Assertions have no score.</summary>
public record GradeResult
{
    /// <summary>The grader's display name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the grader matched, failed, errored, or was skipped.</summary>
    public required GradeOutcome Outcome { get; init; }

    /// <summary>A human-readable explanation, including skip reasons.</summary>
    public required string Detail { get; init; }

    /// <summary>The grader's own token usage.</summary>
    public UsageSummary Usage { get; init; } = new();
}

/// <summary>A rubric outcome with an optional numeric score.</summary>
public sealed record RubricGradeResult : GradeResult
{
    /// <summary>The rubric score, absent when it could not be evaluated.</summary>
    public decimal? Score { get; init; }

    /// <summary>The configured inclusive passing threshold.</summary>
    public decimal? PassThreshold { get; init; }

    /// <summary>Validated criterion results in configuration order; empty on error.</summary>
    public IReadOnlyList<RubricCriterionResult> Criteria { get; init; } = [];
}

/// <summary>A single deterministic check, optionally gating further grading.</summary>
public sealed class AssertionGrader : IGrader
{
    /// <inheritdoc />
    public required string Name { get; init; }

    /// <inheritdoc />
    public string? Description { get; init; }

    /// <inheritdoc />
    public GraderType Type => GraderType.Assertion;

    /// <summary>Whether a failed or errored check blocks this eval and remaining graders.</summary>
    public bool Required { get; init; }

    /// <summary>The typed check to execute.</summary>
    public required AssertionCheck Check { get; init; }

    /// <inheritdoc />
    public async Task<GradeResult> GradeAsync(GradingContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var result = await Check.EvaluateAsync(context, cancellationToken);
            return new GradeResult { Name = Name, Outcome = result.Outcome, Detail = result.Detail };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException)
        {
            return new GradeResult { Name = Name, Outcome = GradeOutcome.Error, Detail = ex.Message };
        }
    }
}
