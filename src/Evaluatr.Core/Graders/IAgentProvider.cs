namespace Evaluatr.Core.Graders;

/// <summary>An adapter capable of creating isolated judge conversations.</summary>
public interface IAgentProvider
{
    /// <summary>Whether the adapter enforces inspection-only workspace tools.</summary>
    bool SupportsInspectionOnlySessions { get; }

    /// <summary>Creates a fresh session; the caller owns and disposes it.</summary>
    Task<IGraderSession> CreateGraderSessionAsync(GraderSessionOptions options, CancellationToken cancellationToken = default);
}

/// <summary>A provider-neutral request for an inspection-only judge session.</summary>
public sealed record GraderSessionOptions
{
    /// <summary>Optional provider override; otherwise the composition root uses the suite harness.</summary>
    public string? Provider { get; init; }

    /// <summary>Optional model override; otherwise the provider uses its documented default.</summary>
    public string? Model { get; init; }

    /// <summary>The only workspace the judge may inspect.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Trusted grading instructions, separate from untrusted task evidence.</summary>
    public required string SystemMessage { get; init; }
}
