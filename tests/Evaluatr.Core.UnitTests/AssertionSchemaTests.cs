using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;
using YamlDotNet.Core;

namespace Evaluatr.Core.UnitTests;

public class AssertionSchemaTests
{
    [Theory]
    [InlineData("check: file-exists\npath: output.txt", typeof(FileExistsCheck))]
    [InlineData("check: file-contains\npath: output.txt\ntext: expected", typeof(FileContainsCheck))]
    [InlineData("check: transcript-contains\ntext: expected", typeof(TranscriptContainsCheck))]
    [InlineData("check: tool-called\ntool: bash", typeof(ToolCalledCheck))]
    public void ParsesTypedChecks_RequiredDefaultsFalse(string fields, Type expectedType)
    {
        var suite = EvalSuiteParser.Parse(Suite(fields));
        var grader = Assert.IsType<AssertionGrader>(suite.Evals[0].Graders[0]);

        Assert.IsType(expectedType, grader.Check);
        Assert.False(grader.Required);
    }

    [Fact]
    public void ParsesRequiredTrue()
    {
        var suite = EvalSuiteParser.Parse(Suite("check: file-exists\npath: output.txt\nrequired: true"));

        Assert.True(Assert.IsType<AssertionGrader>(suite.Evals[0].Graders[0]).Required);
    }

    [Theory]
    [InlineData("assertion: some prose", "legacy 'assertion:'")]
    [InlineData("assertion: null\ncheck: tool-called\ntool: bash", "legacy 'assertion:'")]
    [InlineData("check: unknown", "unknown or missing check")]
    [InlineData("check: file-exists", "path")]
    [InlineData("check: file-contains\npath: output.txt", "text")]
    [InlineData("check: transcript-contains\ntext: ''", "text")]
    [InlineData("check: tool-called\ntool: ' '", "tool")]
    [InlineData("check: file-exists\npath: ../outside", "escape")]
    [InlineData("check: file-exists\npath: /outside", "relative")]
    [InlineData("check: file-exists\npath: output.txt\ntext: irrelevant", "does not apply")]
    [InlineData("check: tool-called\ntool: bash\npath: irrelevant", "does not apply")]
    public void RejectsInvalidAssertions(string fields, string message)
    {
        var exception = Assert.Throws<EvalSuiteValidationException>(() => EvalSuiteParser.Parse(Suite(fields)));

        Assert.Contains(message, exception.Message);
    }

    [Theory]
    [InlineData("check: file-exists\npath: output.txt\nrequried: true")]
    [InlineData("check: file-exists\npath: output.txt\nrequired: true\nrequired: false")]
    public void RejectsUnknownOrDuplicateKeys(string fields)
    {
        Assert.ThrowsAny<YamlException>(() => EvalSuiteParser.Parse(Suite(fields)));
    }

    [Fact]
    public void ProgrammaticallyConstructedChecks_AreValidatedDuringSuitePreflight()
    {
        var suite = EvalSuiteParser.Parse(Suite("check: file-exists\npath: output.txt"));
        suite.Evals[0].Graders[0] = new AssertionGrader { Name = "bad", Check = new FileExistsCheck("../outside") };

        Assert.Throws<EvalSuiteValidationException>(() => EvalSuiteValidator.DetermineMode(suite));
    }

    private static string Suite(string fields) => """
        name: suite
        harness: copilot
        evals:
          - name: task
            description: test
            prompt: do something
            graders:
              - name: check
                type: assertion
        """ + "\n        " + fields.Replace("\n", "\n        ") + "\n";
}
