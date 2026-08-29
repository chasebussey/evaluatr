using System.CommandLine;
using System.Diagnostics;
using AwesomeAssertions;
using Evaluatr.Cli.Commands;
using Evaluatr.Core.Runner;

namespace Evaluatr.Cli.UnitTests;

public class CliTests
{
    /// <summary>A live-workspace suite: no eval declares a <c>repo</c> or <c>baseCommit</c>.</summary>
    private const string LiveWorkspaceSuiteYaml = """
        name: "live suite"
        harness: "copilot"
        evals:
          - name: "live task"
            description: "a live-workspace task"
            prompt: "Do something."
            graders: []
        """;

    /// <summary>A live-workspace suite whose harness is not registered.</summary>
    private const string UnknownHarnessSuiteYaml = """
        name: "unknown harness suite"
        harness: "does-not-exist"
        evals:
          - name: "live task"
            description: "a live-workspace task"
            prompt: "Do something."
            graders: []
        """;

    /// <summary>A suite that mixes a repo eval with a workspace eval — invalid.</summary>
    private const string MixedSuiteYaml = """
        name: "mixed suite"
        harness: "copilot"
        evals:
          - name: "repo task"
            description: "has a repo"
            repo: "test/repo"
            prompt: "Do something."
            graders: []
          - name: "workspace task"
            description: "has no repo"
            prompt: "Do something else."
            graders: []
        """;

    [Fact]
    public async Task Run_WithDirAndFile_WritesError()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var config = new InvocationConfiguration { Output = stdout, Error = stderr };

        var cmd = RunCommand.Build(_ => new FakeEvalRunner());
        var exitCode = await cmd.Parse(["--directory", ".", "--file", "x.yaml"]).InvokeAsync(config);

        exitCode.Should().Be(1);
        stderr.ToString().Should().Contain("Only one of --directory and --file may be defined.");
    }

    [Fact]
    public async Task Run_WithDirectory_LoadsMultipleSuites()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var config = new InvocationConfiguration { Output = stdout, Error = stderr };

        using var suiteDir = new TempSuiteDirectory();
        var runner = new FakeEvalRunner();

        var cmd = RunCommand.Build(_ => runner);
        var exitCode = await cmd.Parse(["--directory", suiteDir.Path, "--verbose"]).InvokeAsync(config);

        exitCode.Should().Be(0);
        stderr.ToString().Should().BeEmpty();
        stdout.ToString().Should().Contain("Loaded 2 eval suite(s).");
        runner.RanEvals.Should().HaveCount(2);
    }

    [Fact]
    public async Task Run_WithFile_LoadsEvalSuite()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var config = new InvocationConfiguration { Output = stdout, Error = stderr };

        using var suiteFile = new TempSuiteFile();
        var runner = new FakeEvalRunner();

        var cmd = RunCommand.Build(_ => runner);
        var exitCode = await cmd.Parse(["--file", suiteFile.Path, "--verbose"]).InvokeAsync(config);

        exitCode.Should().Be(0);
        stderr.ToString().Should().BeEmpty();
        stdout.ToString().Should().Contain("Loaded 1 eval suite(s).");
        runner.RanEvals.Should().ContainSingle();
    }

    [Fact]
    public async Task Run_WithNoArgs_LoadsFromDefaultEvalsDirectory()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var config = new InvocationConfiguration { Output = stdout, Error = stderr };

        var originalCwd = Directory.GetCurrentDirectory();
        var workingDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(workingDir);
        try
        {
            var evalsDir = Path.Combine(workingDir, ".evals");
            Directory.CreateDirectory(evalsDir);
            File.WriteAllText(
                Path.Combine(evalsDir, "suite-a.yaml"),
                TempSuiteDirectory.MakeSuiteYaml("suite a", "task a"));
            File.WriteAllText(
                Path.Combine(evalsDir, "suite-b.yaml"),
                TempSuiteDirectory.MakeSuiteYaml("suite b", "task b"));

            Directory.SetCurrentDirectory(workingDir);

            var cmd = RunCommand.Build(_ => new FakeEvalRunner());
            var exitCode = await cmd.Parse(["--verbose"]).InvokeAsync(config);

            exitCode.Should().Be(0);
            stderr.ToString().Should().BeEmpty();
            stdout.ToString().Should().Contain("Loaded 2 eval suite(s).");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
            Directory.Delete(workingDir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_WithNonexistentPath_ThrowsFileNotFound()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        // Disable the built-in exception handler so the exception propagates to the test
        // instead of being caught and reported through Error with a non-zero exit code.
        var config = new InvocationConfiguration
        {
            Output = stdout,
            Error = stderr,
            EnableDefaultExceptionHandler = false,
        };

        var missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        var cmd = RunCommand.Build(_ => new FakeEvalRunner());
        var act = async () => await cmd.Parse(["--directory", missing]).InvokeAsync(config);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Run_WithMixedSuite_WritesValidationErrorAndDoesNotRun()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var config = new InvocationConfiguration { Output = stdout, Error = stderr };

        using var suiteFile = new TempSuiteFile(MixedSuiteYaml);
        var runner = new FakeEvalRunner();

        var cmd = RunCommand.Build(_ => runner);
        var exitCode = await cmd.Parse(["--file", suiteFile.Path]).InvokeAsync(config);

        exitCode.Should().Be(1);
        stderr.ToString().Should().Contain("mixes repository and workspace evals");
        runner.RanEvals.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_WithHarness_RoutesToResolvedRunner()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var config = new InvocationConfiguration { Output = stdout, Error = stderr };

        using var suiteFile = new TempSuiteFile(LiveWorkspaceSuiteYaml);
        var runner = new FakeEvalRunner();
        var requestedHarnesses = new List<string>();

        // Resolver records the harness id it was asked for and returns the fake.
        var cmd = RunCommand.Build(harness =>
        {
            requestedHarnesses.Add(harness);
            return runner;
        });
        var exitCode = await cmd.Parse(["--file", suiteFile.Path, "--allow-dirty"]).InvokeAsync(config);

        exitCode.Should().Be(0);
        requestedHarnesses.Should().ContainSingle().Which.Should().Be("copilot");
        runner.RanEvals.Should().ContainSingle();
    }

    [Fact]
    public async Task Run_WithUnknownHarness_WritesErrorAndDoesNotRun()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var config = new InvocationConfiguration { Output = stdout, Error = stderr };

        using var suiteFile = new TempSuiteFile(UnknownHarnessSuiteYaml);
        var runner = new FakeEvalRunner();

        // A resolver that only knows "copilot"; the suite requests an unknown harness.
        var cmd = RunCommand.Build(harness =>
            string.Equals(harness, "copilot", StringComparison.OrdinalIgnoreCase) ? runner : null);
        var exitCode = await cmd.Parse(["--file", suiteFile.Path, "--allow-dirty"]).InvokeAsync(config);

        exitCode.Should().Be(1);
        stderr.ToString().Should().Contain("unknown harness 'does-not-exist'");
        runner.RanEvals.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_LiveWorkspace_WithAllowDirty_SkipsPromptAndPassesFlag()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var config = new InvocationConfiguration { Output = stdout, Error = stderr };

        using var suiteFile = new TempSuiteFile(LiveWorkspaceSuiteYaml);
        var runner = new FakeEvalRunner();

        // With --allow-dirty the CLI never inspects git or prompts, so this runs
        // regardless of the current working directory's git state. A null stdin
        // proves no prompt was attempted.
        var cmd = RunCommand.Build(_ => runner, standardInput: null);
        var exitCode = await cmd.Parse(["--file", suiteFile.Path, "--allow-dirty"]).InvokeAsync(config);

        exitCode.Should().Be(0);
        runner.RanEvals.Should().ContainSingle();
        runner.ReceivedOptions.Should().ContainSingle().Which.AllowDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Run_LiveWorkspace_DirtyTree_PromptApproved_Runs()
    {
        using var repo = new TempGitRepo(dirty: true);
        var suitePath = repo.WriteSuite(LiveWorkspaceSuiteYaml);

        var (exitCode, stdout, _, runner) =
            await RunInDirectoryAsync(repo.Path, ["--file", suitePath], stdin: "y\n");

        exitCode.Should().Be(0);
        stdout.Should().Contain("has uncommitted changes");
        runner.RanEvals.Should().ContainSingle();
        runner.ReceivedOptions.Should().ContainSingle().Which.AllowDirty.Should().BeTrue();
    }

    [Fact]
    public async Task Run_LiveWorkspace_DirtyTree_PromptDeclined_Aborts()
    {
        using var repo = new TempGitRepo(dirty: true);
        var suitePath = repo.WriteSuite(LiveWorkspaceSuiteYaml);

        var (exitCode, _, stderr, runner) =
            await RunInDirectoryAsync(repo.Path, ["--file", suitePath], stdin: "n\n");

        exitCode.Should().Be(1);
        stderr.Should().Contain("Aborted");
        runner.RanEvals.Should().BeEmpty();
    }

    /// <summary>
    /// Runs the <c>run</c> command with the current working directory set to
    /// <paramref name="workingDirectory"/> and the given fake stdin, restoring the cwd after.
    /// </summary>
    private static async Task<(int ExitCode, string StdOut, string StdErr, FakeEvalRunner Runner)>
        RunInDirectoryAsync(string workingDirectory, string[] args, string stdin)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var config = new InvocationConfiguration { Output = stdout, Error = stderr };
        var runner = new FakeEvalRunner();

        var originalCwd = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(workingDirectory);
        try
        {
            using var input = new StringReader(stdin);
            var cmd = RunCommand.Build(_ => runner, input);
            var exitCode = await cmd.Parse(args).InvokeAsync(config);
            return (exitCode, stdout.ToString(), stderr.ToString(), runner);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalCwd);
        }
    }

    /// <summary>A disposable temp git repository, optionally left with an uncommitted file.</summary>
    private sealed class TempGitRepo : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "evaluatr-cli-" + Guid.NewGuid().ToString("N"));

        public TempGitRepo(bool dirty)
        {
            Directory.CreateDirectory(Path);
            Git("init", "-q");
            Git("config", "user.email", "test@example.com");
            Git("config", "user.name", "Evaluatr Tests");
            File.WriteAllText(System.IO.Path.Combine(Path, "committed.txt"), "committed");
            Git("add", "-A");
            Git("commit", "-qm", "initial commit");

            if (dirty)
            {
                // An untracked file makes `git status --porcelain` non-empty.
                File.WriteAllText(System.IO.Path.Combine(Path, "scratch.txt"), "wip");
            }
        }

        public string WriteSuite(string yaml)
        {
            var suitePath = System.IO.Path.Combine(Path, "suite.yaml");
            File.WriteAllText(suitePath, yaml);
            return suitePath;
        }

        private void Git(params string[] args)
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
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"git {string.Join(' ', args)} failed: {process.StandardError.ReadToEnd()}");
            }
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
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
}
