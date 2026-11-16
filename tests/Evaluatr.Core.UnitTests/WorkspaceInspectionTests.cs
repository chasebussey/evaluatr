using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;

namespace Evaluatr.Core.UnitTests;

public sealed class WorkspaceInspectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "evaluatr-inspection-" + Guid.NewGuid().ToString("N"));
    private readonly WorkspaceInspection _inspection;

    public WorkspaceInspectionTests()
    {
        Directory.CreateDirectory(_root);
        _inspection = new WorkspaceInspection(_root);
    }

    [Fact]
    public async Task ReadsBoundedCharacterRangesWithoutChangingFiles()
    {
        var file = Path.Combine(_root, "evidence.txt");
        File.WriteAllText(file, "0123456789");

        var first = await _inspection.ReadAsync("evidence.txt", count: 4);
        var last = await _inspection.ReadAsync("evidence.txt", offset: 4, count: 6);
        var pastEnd = await _inspection.ReadAsync("evidence.txt", offset: 20);

        Assert.Equal("0123", first.Content);
        Assert.Equal(4, first.NextOffset);
        Assert.Equal("456789", last.Content);
        Assert.Null(last.NextOffset);
        Assert.Empty(pastEnd.Content);
        Assert.Equal("0123456789", File.ReadAllText(file));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _inspection.ReadAsync("evidence.txt", count: 16001));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _inspection.ReadAsync("evidence.txt", offset: -1));
    }

    [Fact]
    public async Task ListsAndSearchesWithoutShellExecution()
    {
        Directory.CreateDirectory(Path.Combine(_root, "directory"));
        File.WriteAllText(Path.Combine(_root, "file.txt"), "first\nExpected [text]\nlast");

        var directory = _inspection.List();
        var matches = await _inspection.SearchAsync("file.txt", "Expected [text]");
        var mismatch = await _inspection.SearchAsync("file.txt", "expected");

        Assert.Equal(new[] { "directory", "file.txt" }, directory.Entries);
        Assert.False(directory.Truncated);
        Assert.Equal(new[] { 2 }, matches.Lines);
        Assert.Empty(mismatch.Lines);
        Assert.Empty((await _inspection.SearchAsync("file.txt", ".*")).Lines);
    }

    [Fact]
    public async Task AppliesDirectorySearchAndFileSizeLimits()
    {
        for (var index = 0; index < 205; index++) File.WriteAllText(Path.Combine(_root, $"file-{index}"), "");
        File.WriteAllText(Path.Combine(_root, "many.txt"), string.Join('\n', Enumerable.Repeat("match", 110)));
        File.WriteAllText(Path.Combine(_root, "large.txt"), new string('x', 262145));

        Assert.True(_inspection.List().Truncated);
        var matches = await _inspection.SearchAsync("many.txt", "match");
        Assert.True(matches.Truncated);
        Assert.Equal(100, matches.Lines.Count);
        await Assert.ThrowsAsync<IOException>(() => _inspection.SearchAsync("large.txt", "x"));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/outside")]
    public async Task RejectsEscapingPathsForEveryOperation(string path)
    {
        await Assert.ThrowsAsync<EvalSuiteValidationException>(() => _inspection.ReadAsync(path));
        Assert.Throws<EvalSuiteValidationException>(() => _inspection.List(path));
        await Assert.ThrowsAsync<EvalSuiteValidationException>(() => _inspection.SearchAsync(path, "text"));
    }

    [Fact]
    public async Task RejectsExternalDirectoryLinksForEveryOperation()
    {
        if (OperatingSystem.IsWindows()) return;
        var outside = _root + "-outside";
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        Directory.CreateSymbolicLink(Path.Combine(_root, "escape"), outside);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => _inspection.ReadAsync("escape/secret.txt"));
            Assert.Throws<IOException>(() => _inspection.List("escape"));
            await Assert.ThrowsAsync<IOException>(() => _inspection.SearchAsync("escape/secret.txt", "secret"));
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationStopsAllOperations()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _inspection.ReadAsync("missing", cancellationToken: cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => _inspection.List(cancellationToken: cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _inspection.SearchAsync("missing", "text", cancellation.Token));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
