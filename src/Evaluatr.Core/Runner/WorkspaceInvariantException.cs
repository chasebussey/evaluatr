namespace Evaluatr.Core.Runner;

/// <summary>
/// Thrown when an eval's resolved execution directory violates a workspace
/// safety invariant: the current working directory is not a git work tree, or it
/// has uncommitted changes and the run was not permitted to mutate a dirty tree.
/// </summary>
/// <remarks>
/// Distinct from <see cref="GitCheckoutException"/> — these are pre-execution
/// guardrail failures, not clone/checkout failures. The core runner enforces
/// these invariants non-interactively; any prompting lives in the CLI.
/// </remarks>
public sealed class WorkspaceInvariantException : Exception
{
    /// <inheritdoc cref="WorkspaceInvariantException"/>
    public WorkspaceInvariantException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
