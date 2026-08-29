using AwesomeAssertions;
using Evaluatr.Core.Evals;

namespace Evaluatr.Core.UnitTests;

public class EvalSuiteValidatorTests
{
    [Fact]
    public void DetermineMode_AllReposPresent_ReturnsRepo()
    {
        var suite = Suite(
            Eval("a", repo: "owner/one"),
            Eval("b", repo: "owner/two"));

        EvalSuiteValidator.DetermineMode(suite).Should().Be(SuiteExecutionMode.Repo);
    }

    [Fact]
    public void DetermineMode_AllBaseCommitsNoRepo_ReturnsCommit()
    {
        var suite = Suite(
            Eval("a", baseCommit: "abc123"),
            Eval("b", baseCommit: "def456"));

        EvalSuiteValidator.DetermineMode(suite).Should().Be(SuiteExecutionMode.Commit);
    }

    [Fact]
    public void DetermineMode_NoRepoNoBaseCommit_ReturnsLiveWorkspace()
    {
        var suite = Suite(
            Eval("a"),
            Eval("b"));

        EvalSuiteValidator.DetermineMode(suite).Should().Be(SuiteExecutionMode.LiveWorkspace);
    }

    [Fact]
    public void DetermineMode_EmptySuite_ReturnsLiveWorkspace()
    {
        var suite = Suite();

        EvalSuiteValidator.DetermineMode(suite).Should().Be(SuiteExecutionMode.LiveWorkspace);
    }

    [Fact]
    public void DetermineMode_RepoAndNonRepoMix_Throws()
    {
        var suite = Suite(
            Eval("a", repo: "owner/one"),
            Eval("b"));

        var act = () => EvalSuiteValidator.DetermineMode(suite);

        act.Should().Throw<EvalSuiteValidationException>()
            .WithMessage("*repo*")
            .Which.Message.Should().Contain("b");
    }

    [Fact]
    public void DetermineMode_BaseCommitAndLiveWorkspaceMix_Throws()
    {
        var suite = Suite(
            Eval("a", baseCommit: "abc123"),
            Eval("b"));

        var act = () => EvalSuiteValidator.DetermineMode(suite);

        act.Should().Throw<EvalSuiteValidationException>()
            .WithMessage("*baseCommit*")
            .Which.Message.Should().Contain("b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DetermineMode_MissingHarness_Throws(string? harness)
    {
        var suite = new EvalSuite
        {
            Name = "test suite",
            Harness = harness!,
            Evals = [Eval("a")],
        };

        var act = () => EvalSuiteValidator.DetermineMode(suite);

        act.Should().Throw<EvalSuiteValidationException>()
            .WithMessage("*harness*");
    }

    [Fact]
    public void DetermineMode_DifferentReposAllPresent_DoesNotThrow()
    {
        var suite = Suite(
            Eval("a", repo: "owner/one", baseCommit: "abc"),
            Eval("b", repo: "owner/two", baseCommit: "def"));

        EvalSuiteValidator.DetermineMode(suite).Should().Be(SuiteExecutionMode.Repo);
    }

    private static EvalSuite Suite(params Eval[] evals) => new()
    {
        Name = "test suite",
        Harness = "copilot",
        Evals = evals.ToList(),
    };

    private static Eval Eval(string name, string? repo = null, string? baseCommit = null) => new()
    {
        Name = name,
        Description = $"description for {name}",
        Repo = repo,
        BaseCommit = baseCommit,
        Prompt = "do something",
        Graders = [],
    };
}
