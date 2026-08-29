using System.Diagnostics;

namespace Evaluatr.Copilot.UnitTests;

/// <summary>
/// A disposable temp directory, optionally initialized as a real git repository,
/// for exercising the git-backed helpers against live git behavior.
/// </summary>
internal sealed class TestGitRepository : IDisposable
{
    public string Path { get; }

    private TestGitRepository(string path) => Path = path;

    /// <summary>Creates an empty temp directory that is not a git repository.</summary>
    public static TestGitRepository CreateNonRepo()
    {
        var path = NewTempPath();
        Directory.CreateDirectory(path);
        return new TestGitRepository(path);
    }

    /// <summary>Creates a temp directory initialized as a bare git repository.</summary>
    public static TestGitRepository CreateBare()
    {
        var path = NewTempPath();
        Directory.CreateDirectory(path);
        var repo = new TestGitRepository(path);
        repo.Git("init", "--bare", "-q");
        return repo;
    }

    /// <summary>
    /// Creates a temp directory initialized as a git repository with an initial
    /// commit. Configures a local identity so commits succeed unattended.
    /// </summary>
    public static TestGitRepository CreateWithCommit(string fileName = "initial.txt", string content = "initial")
    {
        var path = NewTempPath();
        Directory.CreateDirectory(path);
        var repo = new TestGitRepository(path);
        repo.Git("init", "-q");
        repo.Git("config", "user.email", "test@example.com");
        repo.Git("config", "user.name", "Evaluatr Tests");
        repo.WriteFile(fileName, content);
        repo.Git("add", "-A");
        repo.Git("commit", "-qm", "initial commit");
        return repo;
    }

    /// <summary>Writes a file (relative to the repo root), creating directories.</summary>
    public void WriteFile(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(full, content);
    }

    /// <summary>Commits all current changes and returns the resulting commit sha.</summary>
    public string CommitAll(string message)
    {
        Git("add", "-A");
        Git("commit", "-qm", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    /// <summary>Returns the current HEAD commit sha.</summary>
    public string Head() => Git("rev-parse", "HEAD").Trim();

    /// <summary>Runs a git command in this repo and returns stdout. Throws on failure.</summary>
    public string Git(params string[] args)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} failed ({process.ExitCode}): {stderr}");
        }

        return stdout;
    }

    private static string NewTempPath() =>
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "evaluatr-gittest-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                // Clear read-only attributes git may set under .git before delete.
                foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(Path, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
