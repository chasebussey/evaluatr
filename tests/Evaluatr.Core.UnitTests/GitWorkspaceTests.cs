using AwesomeAssertions;
using Evaluatr.Core.Runner;

namespace Evaluatr.Core.UnitTests;

public class GitWorkspaceTests
{
    [Fact]
    public async Task IsInsideWorkTree_ReturnsTrue_ForNormalRepo()
    {
        using var repo = TestGitRepository.CreateWithCommit();

        var result = await GitWorkspace.IsInsideWorkTreeAsync(repo.Path);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsInsideWorkTree_ReturnsFalse_ForNonRepo()
    {
        using var dir = TestGitRepository.CreateNonRepo();

        var result = await GitWorkspace.IsInsideWorkTreeAsync(dir.Path);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsInsideWorkTree_ReturnsFalse_ForBareRepo()
    {
        using var repo = TestGitRepository.CreateBare();

        var result = await GitWorkspace.IsInsideWorkTreeAsync(repo.Path);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsDirty_ReturnsFalse_ForCleanRepo()
    {
        using var repo = TestGitRepository.CreateWithCommit();

        var result = await GitWorkspace.IsDirtyAsync(repo.Path);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsDirty_ReturnsTrue_WithUntrackedFile()
    {
        using var repo = TestGitRepository.CreateWithCommit();
        repo.WriteFile("untracked.txt", "new content");

        var result = await GitWorkspace.IsDirtyAsync(repo.Path);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsDirty_ReturnsTrue_WithModifiedTrackedFile()
    {
        using var repo = TestGitRepository.CreateWithCommit("tracked.txt", "original");
        repo.WriteFile("tracked.txt", "modified");

        var result = await GitWorkspace.IsDirtyAsync(repo.Path);

        result.Should().BeTrue();
    }
}
