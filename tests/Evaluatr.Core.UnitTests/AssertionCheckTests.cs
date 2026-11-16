using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;

namespace Evaluatr.Core.UnitTests;

public sealed class AssertionCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "evaluatr-checks-" + Guid.NewGuid().ToString("N"));

    public AssertionCheckTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task FileExists_DistinguishesFileMissingFileAndDirectory()
    {
        File.WriteAllText(Path.Combine(_root, "present.txt"), "hello");
        Directory.CreateDirectory(Path.Combine(_root, "directory"));

        Assert.Equal(GradeOutcome.Pass, (await Grade(new FileExistsCheck("present.txt"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new FileExistsCheck("missing.txt"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new FileExistsCheck("missing/child.txt"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new FileExistsCheck("directory"))).Outcome);
    }

    [Fact]
    public async Task FileContains_UsesLiteralCaseSensitiveUtf8Matching()
    {
        File.WriteAllText(Path.Combine(_root, "text.txt"), "Héllo [world]\nnext");

        Assert.Equal(GradeOutcome.Pass, (await Grade(new FileContainsCheck("text.txt", "[world]\nnext"))).Outcome);
        Assert.Equal(GradeOutcome.Pass, (await Grade(new FileContainsCheck("text.txt", "Héllo"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new FileContainsCheck("text.txt", "héllo"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new FileContainsCheck("text.txt", ".*"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new FileContainsCheck("missing.txt", "text"))).Outcome);
    }

    [Fact]
    public async Task FileContains_MatchesAcrossReadBoundariesAndAcceptsUtf8Bom()
    {
        var content = new string('a', 4095) + "expected" + new string('b', 4096);
        File.WriteAllText(Path.Combine(_root, "large.txt"), content, new System.Text.UTF8Encoding(true));

        Assert.Equal(GradeOutcome.Pass, (await Grade(new FileContainsCheck("large.txt", "expected"))).Outcome);
        Assert.Equal(GradeOutcome.Pass, (await Grade(new FileContainsCheck("large.txt", content))).Outcome);
    }

    [Fact]
    public async Task FileContains_ValidatesUtf8EvenAfterFindingAMatch()
    {
        var content = System.Text.Encoding.UTF8.GetBytes("expected" + new string('a', 8192));
        File.WriteAllBytes(Path.Combine(_root, "invalid-tail.txt"), [..content, 0xff]);

        Assert.Equal(GradeOutcome.Error, (await Grade(new FileContainsCheck("invalid-tail.txt", "expected"))).Outcome);
    }

    [Fact]
    public async Task InvalidUtf8_IsErrorRatherThanFailure()
    {
        File.WriteAllBytes(Path.Combine(_root, "invalid.txt"), [0xff, 0xfe, 0xff]);

        var result = await Grade(new FileContainsCheck("invalid.txt", "text"));

        Assert.Equal(GradeOutcome.Error, result.Outcome);
        Assert.NotEmpty(result.Detail);
    }

    [Fact]
    public async Task UnreadableFile_IsErrorRatherThanFailure()
    {
        // Root bypasses Unix read permissions, so this check is not meaningful there.
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        var path = Path.Combine(_root, "private.txt");
        File.WriteAllText(path, "private");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            Assert.Equal(GradeOutcome.Error, (await Grade(new FileContainsCheck("private.txt", "private"))).Outcome);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task TranscriptContains_UsesLiteralCaseSensitiveMatching()
    {
        Assert.Equal(GradeOutcome.Pass, (await Grade(new TranscriptContainsCheck("[done]"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new TranscriptContainsCheck("[DONE]"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new TranscriptContainsCheck(".*"))).Outcome);
    }

    [Fact]
    public async Task ToolCalled_UsesExactNamesNotTranscriptMentions()
    {
        Assert.Equal(GradeOutcome.Pass, (await Grade(new ToolCalledCheck("bash"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new ToolCalledCheck("Bash"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new ToolCalledCheck("ba"))).Outcome);
        Assert.Equal(GradeOutcome.Fail, (await Grade(new ToolCalledCheck("mentioned-tool"))).Outcome);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("inside/../../outside")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("\\\\server\\share")]
    [InlineData("")]
    [InlineData(" ")]
    public void FilePaths_RejectInvalidConfiguration(string path)
    {
        Assert.Throws<EvalSuiteValidationException>(() => new FileExistsCheck(path).Validate());
        Assert.Throws<EvalSuiteValidationException>(() => new FileContainsCheck(path, "text").Validate());
    }

    [Fact]
    public async Task Symlinks_AllowInternalLinksButRejectExternalAndIntermediateEscapes()
    {
        // Windows requires additional symlink privileges. The Linux test environment
        // exercises file links, directory links, link chains, and target traversal.
        if (OperatingSystem.IsWindows()) return;

        var inside = Path.Combine(_root, "inside");
        var outside = _root + "-outside";
        Directory.CreateDirectory(inside);
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(inside, "file.txt"), "inside");
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside");
            File.CreateSymbolicLink(Path.Combine(_root, "internal"), "inside/file.txt");
            Directory.CreateSymbolicLink(Path.Combine(_root, "external"), outside);
            File.CreateSymbolicLink(Path.Combine(_root, "chain"), "external/secret.txt");
            File.CreateSymbolicLink(Path.Combine(_root, "hidden"), "external/../inside/file.txt");
            Directory.CreateSymbolicLink(Path.Combine(_root, "internal-dir"), inside);

            Assert.Equal(GradeOutcome.Pass, (await Grade(new FileExistsCheck("internal"))).Outcome);
            Assert.Equal(GradeOutcome.Pass, (await Grade(new FileContainsCheck("internal-dir/file.txt", "inside"))).Outcome);
            foreach (var path in new[] { "external/secret.txt", "chain", "hidden", "external/../inside/file.txt" })
            {
                var result = await Grade(new FileExistsCheck(path));
                Assert.Equal(GradeOutcome.Error, result.Outcome);
                Assert.Contains("outside", result.Detail);
            }
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task SymlinkLoop_IsError()
    {
        if (OperatingSystem.IsWindows()) return;
        File.CreateSymbolicLink(Path.Combine(_root, "loop"), "loop");

        Assert.Equal(GradeOutcome.Error, (await Grade(new FileExistsCheck("loop"))).Outcome);
    }

    [Fact]
    public async Task Cancellation_PropagatesRatherThanBecomingError()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Grade(new TranscriptContainsCheck("text"), cancellation.Token));
    }

    private Task<GradeResult> Grade(AssertionCheck check, CancellationToken cancellationToken = default)
    {
        var grader = new AssertionGrader { Name = "check", Check = check };
        var context = new GradingContext
        {
            WorkingDirectory = _root,
            TaskPrompt = "Complete the task.",
            EvalResult = new EvalResult
            {
                Transcript = "[done] mentioned-tool",
                UsageSummary = new UsageSummary(),
                ToolCalls = ["bash", "bash"],
            },
        };

        return grader.GradeAsync(context, cancellationToken);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
