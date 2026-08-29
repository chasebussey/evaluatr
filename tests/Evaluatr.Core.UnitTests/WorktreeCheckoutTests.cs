using AwesomeAssertions;
using Evaluatr.Core.Runner;

namespace Evaluatr.Core.UnitTests;

public class WorktreeCheckoutTests
{
    [Fact]
    public async Task CreateAsync_MaterializesRequestedCommitState()
    {
        using var repo = TestGitRepository.CreateWithCommit("file.txt", "v1");
        var firstCommit = repo.Head();

        // Second commit adds a new file that must NOT appear in the older worktree.
        repo.WriteFile("added-later.txt", "v2");
        repo.CommitAll("second commit");

        using var worktree = await WorktreeCheckout.CreateAsync(repo.Path, firstCommit);

        File.ReadAllText(Path.Combine(worktree.Path, "file.txt")).Should().Be("v1");
        File.Exists(Path.Combine(worktree.Path, "added-later.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task CreateAsync_DoesNotMutateSourceRepoHead()
    {
        using var repo = TestGitRepository.CreateWithCommit("file.txt", "v1");
        var firstCommit = repo.Head();
        repo.WriteFile("second.txt", "v2");
        var secondCommit = repo.CommitAll("second commit");

        using var worktree = await WorktreeCheckout.CreateAsync(repo.Path, firstCommit);

        repo.Head().Should().Be(secondCommit);
    }

    [Fact]
    public async Task CreateAsync_Throws_ForUnknownCommit()
    {
        using var repo = TestGitRepository.CreateWithCommit();

        var act = async () => await WorktreeCheckout.CreateAsync(repo.Path, "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef");

        await act.Should().ThrowAsync<GitCheckoutException>();
    }

    [Fact]
    public async Task Dispose_RemovesWorktreeAndTempDirectory()
    {
        using var repo = TestGitRepository.CreateWithCommit();
        var head = repo.Head();

        string worktreePath;
        using (var worktree = await WorktreeCheckout.CreateAsync(repo.Path, head))
        {
            worktreePath = worktree.Path;
            Directory.Exists(worktreePath).Should().BeTrue();
        }

        Directory.Exists(worktreePath).Should().BeFalse();
        repo.Git("worktree", "list").Should().NotContain(worktreePath);
    }
}
