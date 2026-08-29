using System.Diagnostics;

namespace Evaluatr.Core.Runner;

/// <summary>
/// Static helpers for inspecting the git state of a working directory. All
/// helpers shell out to the <c>git</c> CLI and never throw for the "not a git
/// work tree" case — they classify it instead.
/// </summary>
public static class GitWorkspace
{
    /// <summary>
    /// Determines whether <paramref name="directory"/> is inside a git work tree
    /// (a normal repo on a branch, a subdirectory, a detached HEAD, or a linked
    /// worktree). Returns <see langword="false"/> for bare repos, locations
    /// inside <c>.git</c>, non-repos, and any git error.
    /// </summary>
    public static async Task<bool> IsInsideWorkTreeAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _) = await RunGitAsync(
            directory, cancellationToken, "rev-parse", "--is-inside-work-tree");

        return exitCode == 0 && string.Equals(stdout.Trim(), "true", StringComparison.Ordinal);
    }

    /// <summary>
    /// Determines whether <paramref name="directory"/> has uncommitted changes,
    /// including untracked files. Returns <see langword="true"/> when
    /// <c>git status --porcelain</c> produces any output.
    /// </summary>
    public static async Task<bool> IsDirtyAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, stderr) = await RunGitAsync(
            directory, cancellationToken, "status", "--porcelain");

        if (exitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(stderr)
                ? $"git status exited with code {exitCode}."
                : stderr.Trim();
            throw new WorkspaceInvariantException(
                $"Failed to inspect the working tree at '{directory}': {detail}");
        }

        return !string.IsNullOrWhiteSpace(stdout);
    }

    /// <summary>
    /// Runs <c>git -C &lt;directory&gt; &lt;args&gt;</c> and returns its exit code
    /// and captured output. Returns exit code <c>-1</c> when the process cannot be
    /// started rather than throwing.
    /// </summary>
    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunGitAsync(
        string directory,
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
        startInfo.ArgumentList.Add(directory);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch
        {
            return (-1, string.Empty, string.Empty);
        }

        if (process is null)
        {
            return (-1, string.Empty, string.Empty);
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return (process.ExitCode, stdout, stderr);
        }
    }
}
