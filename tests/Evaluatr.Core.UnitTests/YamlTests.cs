using AwesomeAssertions;
using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;

namespace Evaluatr.Core.UnitTests;

public class YamlTests
{
    [Fact]
    public void YamlParses_EvalSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "sample.yaml");

        var suite = EvalSuiteParser.ParseFile(path);

        suite.Name.Should().Be("test eval suite");
        suite.Harness.Should().Be("copilot");
        suite.Evals.Should().HaveCount(1);

        var eval = suite.Evals[0];
        eval.Name.Should().Be("test eval task");
        eval.Description.Should().Be("this is a test eval task, it won't actually do anything");
        eval.Repo.Should().Be("test/repo");
        eval.BaseCommit.Should().Be("reallyShouldBeAHash");
        eval.Prompt.Should().Be("I think this is still how you define a multiline string.\n\nThis should be parsed correctly.\n");
        eval.Agent.Should().Be("default");
    }

    [Fact]
    public void YamlParses_Graders()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "sample.yaml");

        var suite = EvalSuiteParser.ParseFile(path);

        var graders = suite.Evals[0].Graders;
        graders.Should().HaveCount(2);

        var assertionGrader = graders[0].Should().BeOfType<AssertionGrader>().Subject;
        assertionGrader.Name.Should().Be("skill check");
        assertionGrader.Description.Should().BeNull();
        assertionGrader.Type.Should().Be(GraderType.Assertion);
        assertionGrader.Check.Should().Be(new TranscriptContainsCheck("sample assertion"));
        assertionGrader.Required.Should().BeFalse();

        var rubricGrader = graders[1].Should().BeOfType<RubricGrader>().Subject;
        rubricGrader.Name.Should().Be("rubric checker");
        rubricGrader.Description.Should().BeNull();
        rubricGrader.Type.Should().Be(GraderType.Rubric);
        rubricGrader.Rubric.Should().Be("Verify the output yadda yadda");
    }
}
