using System.Text;
using Evaluatr.Core.Evals;

namespace Evaluatr.Core.Graders;

/// <summary>A deterministic assertion with check-specific fields and validation.</summary>
public abstract record AssertionCheck
{
    /// <summary>Rejects invalid configuration before agent execution.</summary>
    public abstract void Validate();

    /// <summary>Evaluates the check against the completed agent execution.</summary>
    public abstract Task<CheckResult> EvaluateAsync(GradingContext context, CancellationToken cancellationToken = default);

    internal static void RequireText(string? value, string field)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new EvalSuiteValidationException($"Assertion field '{field}' must not be empty.");
        }
    }
}

/// <summary>The outcome of one check before its grader name is attached.</summary>
public sealed record CheckResult(GradeOutcome Outcome, string Detail);

/// <summary>Checks that a workspace-relative path exists and is a file.</summary>
public sealed record FileExistsCheck(string Path) : AssertionCheck
{
    /// <inheritdoc />
    public override void Validate() => WorkspaceFile.ValidatePath(Path);

    /// <inheritdoc />
    public override Task<CheckResult> EvaluateAsync(GradingContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = WorkspaceFile.Resolve(context.WorkingDirectory, Path);
        var exists = WorkspaceFile.IsFile(file);

        return Task.FromResult(new CheckResult(
            exists ? GradeOutcome.Pass : GradeOutcome.Fail,
            exists ? $"File exists: {Path}" : $"File not found or not a file: {Path}"));
    }
}

/// <summary>Checks a UTF-8 file for literal, case-sensitive text.</summary>
public sealed record FileContainsCheck(string Path, string Text) : AssertionCheck
{
    /// <inheritdoc />
    public override void Validate()
    {
        WorkspaceFile.ValidatePath(Path);
        RequireText(Text, "text");
    }

    /// <inheritdoc />
    public override async Task<CheckResult> EvaluateAsync(GradingContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var file = WorkspaceFile.Resolve(context.WorkingDirectory, Path);
        if (!WorkspaceFile.IsFile(file))
        {
            return new CheckResult(GradeOutcome.Fail, $"File not found or not a file: {Path}");
        }

        await using var stream = File.OpenRead(file);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        var matched = await ContainsTextAsync(reader, cancellationToken);

        return new CheckResult(matched ? GradeOutcome.Pass : GradeOutcome.Fail,
            matched ? $"File contains expected text: {Path}" : $"Expected text not found in file: {Path}");
    }

    private async Task<bool> ContainsTextAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var overlap = "";
        var firstChunk = true;
        var matched = false;

        // Keep only enough overlap to match across chunk boundaries. Continue
        // decoding after a match so malformed UTF-8 anywhere still yields ERROR.
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0) break;

            var start = firstChunk && buffer[0] == '\uFEFF' ? 1 : 0;
            firstChunk = false;
            if (matched) continue;

            var window = overlap + new string(buffer, start, count - start);
            matched = window.Contains(Text, StringComparison.Ordinal);
            var overlapLength = Math.Min(Text.Length - 1, window.Length);
            overlap = window[^overlapLength..];
        }

        return matched;
    }
}

/// <summary>Checks assistant transcript text using literal, case-sensitive matching.</summary>
public sealed record TranscriptContainsCheck(string Text) : AssertionCheck
{
    /// <inheritdoc />
    public override void Validate() => RequireText(Text, "text");

    /// <inheritdoc />
    public override Task<CheckResult> EvaluateAsync(GradingContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var matched = context.EvalResult.Transcript.Contains(Text, StringComparison.Ordinal);

        return Task.FromResult(new CheckResult(matched ? GradeOutcome.Pass : GradeOutcome.Fail,
            matched ? "Transcript contains expected text." : "Expected text not found in transcript."));
    }
}

/// <summary>Checks whether a tool with this exact name started at least once, regardless of outcome.</summary>
public sealed record ToolCalledCheck(string Tool) : AssertionCheck
{
    /// <inheritdoc />
    public override void Validate()
    {
        if (string.IsNullOrWhiteSpace(Tool))
        {
            throw new EvalSuiteValidationException("Assertion field 'tool' must not be blank.");
        }
    }

    /// <inheritdoc />
    public override Task<CheckResult> EvaluateAsync(GradingContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var matched = context.EvalResult.ToolCalls.Contains(Tool, StringComparer.Ordinal);

        return Task.FromResult(new CheckResult(matched ? GradeOutcome.Pass : GradeOutcome.Fail,
            matched ? $"Tool was called: {Tool}" : $"Tool was not called: {Tool}"));
    }
}
