using System.Diagnostics;

namespace Evaluatr.Core.Runner;

/// <summary>
/// A temporary clone of an eval's repository, checked out to an optional commit.
/// Disposing deletes the temp directory (best effort).
/// </summary>
/// <remarks>
/// Cloning shells out to the <c>git</c> CLI over HTTPS using the GitHub
/// <c>owner/repo</c> slug.
/// </remarks>
public sealed class RepositoryCheckout : IDisposable
{
    /// <summary>The temp directory containing the checked-out working tree.</summary>
    public string Path { get; }

    private RepositoryCheckout(string path) => Path = path;

    /// <summary>
    /// Clones <paramref name="repo"/> into a fresh temp directory and, when
    /// <paramref name="baseCommit"/> is set, checks it out.
    /// </summary>
    public static async Task<RepositoryCheckout> CreateAsync(
        string repo,
        string? baseCommit,
        CancellationToken cancellationToken = default)
    {
        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "evaluatr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        var checkout = new RepositoryCheckout(tempPath);
        try
        {
            var url = $"https://github.com/{repo}.git";
            await RunGitAsync(repo, tempPath, cancellationToken, "clone", url, ".");

            if (!string.IsNullOrWhiteSpace(baseCommit))
            {
                await RunGitAsync(repo, tempPath, cancellationToken, "checkout", baseCommit);
            }

            return checkout;
        }
        catch
        {
            checkout.Dispose();
            throw;
        }
    }

    private static async Task RunGitAsync(
        string repo,
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new GitCheckoutException(repo, "the git process could not be started.");
        }
        catch (Exception ex) when (ex is not GitCheckoutException)
        {
            throw new GitCheckoutException(repo, $"could not start git: {ex.Message}", ex);
        }

        using (process)
        {
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr)
                    ? $"git {string.Join(' ', args)} exited with code {process.ExitCode}."
                    : stderr.Trim();
                throw new GitCheckoutException(repo, detail);
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best effort — a leaked temp directory should not fail an eval run.
        }
    }
}
