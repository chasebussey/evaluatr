using AwesomeAssertions;
using Evaluatr.Core.Evals;
using Evaluatr.Core.Runner;

namespace Evaluatr.Copilot.UnitTests;

public class CopilotEvalRunnerTests
{
    [Fact]
    public async Task RunAsync_LiveWorkspace_NonRepoDirectory_ThrowsWorkspaceInvariant()
    {
        using var dir = TestGitRepository.CreateNonRepo();
        var runner = new CopilotEvalRunner();
        var eval = LiveWorkspaceEval();

        var act = async () => await runner.RunAsync(
            eval, new EvalRunOptions { WorkingDirectory = dir.Path });

        await act.Should().ThrowAsync<WorkspaceInvariantException>()
            .Where(ex => ex.Message.Contains("not a git work tree"));
    }

    [Fact]
    public async Task RunAsync_LiveWorkspace_DirtyTree_WithoutAllowDirty_Throws()
    {
        using var repo = TestGitRepository.CreateWithCommit();
        repo.WriteFile("untracked.txt", "dirty");
        var runner = new CopilotEvalRunner();
        var eval = LiveWorkspaceEval();

        var act = async () => await runner.RunAsync(
            eval, new EvalRunOptions { WorkingDirectory = repo.Path, AllowDirty = false });

        await act.Should().ThrowAsync<WorkspaceInvariantException>()
            .Where(ex => ex.Message.Contains("uncommitted changes"));
    }

    [Fact]
    public async Task RunAsync_LiveWorkspace_DirtyTree_WithAllowDirty_PassesGuard()
    {
        using var repo = TestGitRepository.CreateWithCommit();
        repo.WriteFile("untracked.txt", "dirty");
        var runner = new CopilotEvalRunner();
        var eval = LiveWorkspaceEval();

        // With AllowDirty the dirty guard is bypassed; the run then proceeds to the
        // Copilot execution step, which fails without a live runtime. We assert only
        // that we got PAST the workspace-invariant guard (no WorkspaceInvariantException).
        Exception? thrown = null;
        try
        {
            await runner.RunAsync(
                eval, new EvalRunOptions { WorkingDirectory = repo.Path, AllowDirty = true });
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        thrown.Should().NotBeNull("the Copilot execution step cannot run without a live runtime");
        thrown.Should().NotBeOfType<WorkspaceInvariantException>();
    }

    private static Eval LiveWorkspaceEval() => new()
    {
        Name = "live-workspace eval",
        Description = "an eval with no repo and no baseCommit",
        Repo = null,
        BaseCommit = null,
        Prompt = "do something",
        Graders = [],
    };
}
