namespace Evaluatr.Core.Runner;

/// <summary>
/// Thrown when cloning or checking out an eval's repository fails.
/// </summary>
public sealed class GitCheckoutException : Exception
{
    /// <summary>The <c>owner/repo</c> slug that failed to check out.</summary>
    public string Repo { get; }

    /// <inheritdoc cref="GitCheckoutException"/>
    public GitCheckoutException(string repo, string message, Exception? innerException = null)
        : base($"Failed to check out repository '{repo}': {message}", innerException)
    {
        Repo = repo;
    }
}
