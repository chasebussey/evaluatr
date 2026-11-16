using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;
using YamlDotNet.Core;

namespace Evaluatr.Core.UnitTests;

public class RubricSchemaTests
{
    [Fact]
    public void ParsesCriteriaDefaultsAndOverrides()
    {
        var suite = EvalSuiteParser.Parse(Suite("""
            provider: alternate
            model: judge-model
            passThreshold: 0.8
            criteria:
              - id: correctness
                description: Correct implementation
                weight: 3
              - id: maintainability
                description: Maintainable implementation
            """));
        var rubric = Assert.IsType<RubricGrader>(suite.Evals[0].Graders[0]);

        Assert.Equal("alternate", rubric.Provider);
        Assert.Equal("judge-model", rubric.Model);
        Assert.Equal(0.8m, rubric.PassThreshold);
        Assert.Equal(new[] { 3m, 1m }, rubric.Criteria.Select(criterion => criterion.Weight));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void ThresholdEndpointsAreValid_AndProviderModelDefaultToNull(string threshold)
    {
        var rubric = Assert.IsType<RubricGrader>(EvalSuiteParser.Parse(Suite(
            $"passThreshold: {threshold}\ncriteria:\n  - id: quality\n    description: Quality")).Evals[0].Graders[0]);

        Assert.Null(rubric.Provider);
        Assert.Null(rubric.Model);
    }

    [Theory]
    [InlineData("rubric: prose", "legacy 'rubric:'")]
    [InlineData("rubric: null", "legacy 'rubric:'")]
    [InlineData("criteria: []", "passThreshold")]
    [InlineData("passThreshold: -0.1\ncriteria: []", "between 0 and 1")]
    [InlineData("passThreshold: 1.1\ncriteria: []", "between 0 and 1")]
    [InlineData("passThreshold: 0.8", "at least one")]
    [InlineData("passThreshold: 0.8\ncriteria: []", "at least one")]
    [InlineData("passThreshold: 0.8\ncriteria:\n  - id: quality\n    description: Quality\n    weight: 0", "positive weight")]
    [InlineData("passThreshold: 0.8\ncriteria:\n  - id: quality\n    description: Quality\n    weight: -1", "positive weight")]
    [InlineData("passThreshold: 0.8\ncriteria:\n  - id: quality\n    description: ' '", "nonblank")]
    [InlineData("passThreshold: 0.8\ncriteria:\n  - id: ' '\n    description: Quality", "nonblank")]
    [InlineData("passThreshold: 0.8\ncriteria:\n  - id: quality\n    description: Quality\n  - id: quality\n    description: Other", "duplicate")]
    [InlineData("passThreshold: 0.8\nprovider: ' '\ncriteria: []", "must not be blank")]
    [InlineData("passThreshold: 0.8\nmodel: ''\ncriteria: []", "must not be blank")]
    [InlineData("passThreshold: 0.8\npath: other\ncriteria: []", "does not apply")]
    public void RejectsInvalidRubrics(string fields, string message)
    {
        var exception = Assert.Throws<EvalSuiteValidationException>(() => EvalSuiteParser.Parse(Suite(fields)));

        Assert.Contains(message, exception.Message);
    }

    [Theory]
    [InlineData("passThreshold: .nan\ncriteria: []")]
    [InlineData("passThreshold: .inf\ncriteria: []")]
    [InlineData("passThreshold: 0.8\ncriteria:\n  - id: quality\n    description: Quality\n    wieght: 2")]
    [InlineData("passThreshold: 0.8\ncriteria:\n  - id: quality\n    description: Quality\n    weight: 1\n    weight: 2")]
    public void RejectsInvalidNumericUnknownAndDuplicateFields(string fields)
    {
        Assert.ThrowsAny<YamlException>(() => EvalSuiteParser.Parse(Suite(fields)));
    }

    [Fact]
    public void PreflightValidatesProgrammaticRubrics()
    {
        var suite = EvalSuiteParser.Parse(Suite("passThreshold: 0.8\ncriteria:\n  - id: quality\n    description: Quality"));
        suite.Evals[0].Graders[0] = new RubricGrader
        {
            Name = "invalid weight", PassThreshold = 0.8m,
            Criteria =
            [
                new RubricCriterion { Id = "one", Description = "One", Weight = -1 },
            ],
        };

        var exception = Assert.Throws<EvalSuiteValidationException>(() => EvalSuiteValidator.DetermineMode(suite));

        Assert.Contains("positive weight", exception.Message);
    }

    private static string Suite(string fields) => """
        name: suite
        harness: copilot
        evals:
          - name: task
            description: test
            prompt: do something
            graders:
              - name: quality
                type: rubric
        """ + "\n        " + fields.Replace("\n", "\n        ") + "\n";
}
