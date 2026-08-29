using System.Diagnostics;

namespace Evaluatr.Core.Runner;

/// <summary>
/// A throwaway detached <c>git worktree</c> created from a local repository at a
/// specific commit. Disposing removes the worktree (keeping git's bookkeeping
/// clean) and deletes the temp directory (best effort).
/// </summary>
/// <remarks>
/// Used for "commit suite" evals: the cwd repository supplies the object
/// database, and each eval materializes its <c>baseCommit</c> into an isolated
/// worktree without touching the user's HEAD or working tree.
/// </remarks>
public sealed class WorktreeCheckout : IDisposable
{
    private readonly string _repoDirectory;

    /// <summary>The temp directory containing the detached worktree.</summary>
    public string Path { get; }

    private WorktreeCheckout(string repoDirectory, string path)
    {
        _repoDirectory = repoDirectory;
        Path = path;
    }

    /// <summary>
    /// Adds a detached worktree of <paramref name="repoDirectory"/> at
    /// <paramref name="baseCommit"/> in a fresh temp directory.
    /// </summary>
    public static async Task<WorktreeCheckout> CreateAsync(
        string repoDirectory,
        string baseCommit,
        CancellationToken cancellationToken = default)
    {
        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "evaluatr-wt-" + Guid.NewGuid().ToString("N"));

        var checkout = new WorktreeCheckout(repoDirectory, tempPath);
        try
        {
            await RunGitAsync(
                repoDirectory,
                baseCommit,
                cancellationToken,
                "worktree", "add", "--detach", tempPath, baseCommit);

            return checkout;
        }
        catch
        {
            checkout.Dispose();
            throw;
        }
    }

    private static async Task RunGitAsync(
        string repoDirectory,
        string baseCommit,
        CancellationToken cancellationToken,
        params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repoDirectory);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new GitCheckoutException(baseCommit, "the git process could not be started.");
        }
        catch (Exception ex) when (ex is not GitCheckoutException)
        {
            throw new GitCheckoutException(baseCommit, $"could not start git: {ex.Message}", ex);
        }

        using (process)
        {
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(stderr)
                    ? $"git worktree add for commit '{baseCommit}' exited with code {process.ExitCode}."
                    : stderr.Trim();
                throw new GitCheckoutException(baseCommit, detail);
            }
        }
    }

    public void Dispose()
    {
        // Best effort — remove the worktree so git's metadata under
        // <repo>/.git/worktrees stays clean, then delete the temp dir if it
        // survives. Never throw from dispose.
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(_repoDirectory);
            startInfo.ArgumentList.Add("worktree");
            startInfo.ArgumentList.Add("remove");
            startInfo.ArgumentList.Add("--force");
            startInfo.ArgumentList.Add(Path);

            using var process = Process.Start(startInfo);
            process?.WaitForExit();
        }
        catch
        {
            // Ignore — fall through to the directory cleanup below.
        }

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
