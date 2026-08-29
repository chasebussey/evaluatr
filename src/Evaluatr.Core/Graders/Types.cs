using Evaluatr.Core.Evals;

namespace Evaluatr.Core.Graders;

/// <summary>
/// A harness-neutral session used by graders that reason with an LLM (e.g. rubric
/// grading). Each harness supplies its own implementation; Core stays free of any
/// agent SDK. The surface is intentionally minimal and should grow deliberately as
/// rubric grading is implemented.
/// </summary>
public interface IGraderSession : IAsyncDisposable
{
    /// <summary>Sends <paramref name="prompt"/> to the session and returns its reply.</summary>
    Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>The token usage this session has accumulated.</summary>
    UsageSummary Usage { get; }
}

/// <summary>
/// Grades an <see cref="EvalResult"/> and reports whether it passed, an optional
/// score, and any token usage the grader itself consumed.
/// </summary>
public interface IGrader
{
    /// <summary>The grader's display name.</summary>
    public string Name { get; }

    /// <summary>Optional human-readable description of what the grader checks.</summary>
    public string? Description { get; }

    /// <summary>The kind of grader, which drives execution ordering and isolation.</summary>
    public GraderType Type { get; }

    /// <summary>
    /// Grades the eval result described by <paramref name="context"/>.
    /// </summary>
    /// <param name="context">
    /// The grading context, carrying the eval result, the working directory the
    /// eval ran in, and (for graders that need an LLM) a factory that yields a
    /// dedicated harness session.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel grading.</param>
    Task<GradeResult> GradeAsync(GradingContext context, CancellationToken cancellationToken = default);
}

public enum GraderType
{
    Assertion,
    Rubric
}

/// <summary>
/// Everything a grader needs to evaluate an eval result. The runner decides how
/// each member is supplied, which is what lets rubric graders transparently run
/// in a dedicated harness instance.
/// </summary>
public sealed record GradingContext
{
    /// <summary>The result produced by executing the eval.</summary>
    public required EvalResult EvalResult { get; init; }

    /// <summary>The temp working directory the eval executed in.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Lazily creates a harness-neutral <see cref="IGraderSession"/> for graders
    /// that reason with an LLM. For rubric graders this is backed by a dedicated
    /// harness instance separate from the one used to execute the eval.
    /// <see langword="null"/> for assertion grading.
    /// </summary>
    public Func<CancellationToken, Task<IGraderSession>>? CreateGraderSessionAsync { get; init; }
}

public record GradeResult
{
    public required string Name { get; init; }
    public bool Passed { get; init; }
    public decimal Score { get; init; }

    /// <summary>
    /// The grader's own token usage. Zero for graders that do not call an LLM
    /// (e.g. assertion graders).
    /// </summary>
    public UsageSummary Usage { get; init; } = new();
}

public class AssertionGrader : IGrader
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public GraderType Type => GraderType.Assertion;

    public required string Assertion { get; set; }

    public Task<GradeResult> GradeAsync(
        GradingContext context,
        CancellationToken cancellationToken = default)
    {
        // TODO: implement real assertion evaluation against the eval result.
        // Orchestration only for now — returns a placeholder result.
        return Task.FromResult(new GradeResult
        {
            Name = Name,
            Passed = true,
        });
    }
}

public class RubricGrader : IGrader
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public GraderType Type => GraderType.Rubric;

    public required string Rubric { get; set; }

    public Task<GradeResult> GradeAsync(
        GradingContext context,
        CancellationToken cancellationToken = default)
    {
        // TODO: implement real rubric grading. This will use
        // context.CreateGraderSessionAsync to grade in a dedicated harness
        // instance and report the resulting token usage. Orchestration only for
        // now — returns a placeholder result.
        return Task.FromResult(new GradeResult
        {
            Name = Name,
            Passed = true,
        });
    }
}
