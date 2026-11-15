using System.CommandLine;
using Evaluatr.Cli.Commands;
using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;

namespace Evaluatr.Cli.UnitTests;

public class GradingCliTests
{
    [Theory]
    [InlineData(EvalVerdict.Pass, 0)]
    [InlineData(EvalVerdict.Fail, 1)]
    [InlineData(EvalVerdict.Error, 1)]
    public async Task ReportsVerdictAndOutcomes_ContinuesNextEval_AndReturnsAggregateExitCode(EvalVerdict verdict, int expectedExit)
    {
        using var suites = new TempSuiteDirectory();
        var runner = new FakeEvalRunner
        {
            MakeReport = eval => new EvalReport
            {
                EvalName = eval.Name,
                Verdict = verdict,
                GradeResults =
                [
                    new GradeResult { Name = "match", Outcome = GradeOutcome.Pass, Detail = "matched" },
                    new GradeResult { Name = "absent", Outcome = GradeOutcome.Fail, Detail = "File not found." },
                    new GradeResult { Name = "unreadable", Outcome = GradeOutcome.Error, Detail = "Cannot read file." },
                    new GradeResult { Name = "later", Outcome = GradeOutcome.Skipped, Detail = "Blocked by required assertion 'absent'." },
                    new RubricGradeResult { Name = "quality", Outcome = GradeOutcome.Pass, Detail = "good", Score = 1m },
                ],
            },
        };
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var command = RunCommand.Build(_ => runner);

        var exit = await command.Parse(["--directory", suites.Path]).InvokeAsync(
            new InvocationConfiguration { Output = stdout, Error = stderr });

        Assert.Equal(expectedExit, exit);
        Assert.Equal(2, runner.RanEvals.Count);
        Assert.Empty(stderr.ToString());
        var text = stdout.ToString();
        Assert.Contains($"Completed / {verdict.ToString().ToUpperInvariant()}", text);
        Assert.Contains("[PASS] match", text);
        Assert.Contains("[FAIL] absent", text);
        Assert.Contains("[ERROR] unreadable", text);
        Assert.Contains("[SKIPPED] later", text);
        Assert.Contains("Blocked by required assertion 'absent'.", text);
        Assert.Contains("[PASS] quality (score: 1)", text);
        Assert.DoesNotContain("match (score:", text);
    }

    [Fact]
    public async Task InvalidAssertion_FailsBeforeAnyRunnerStarts()
    {
        var yaml = TempSuiteDirectory.MakeSuiteYaml("bad", "task")
            .Replace("graders: []", """
                graders:
                      - name: legacy
                        type: assertion
                        assertion: prose
                """);
        using var suite = new TempSuiteFile(yaml);
        var runner = new FakeEvalRunner();
        var stderr = new StringWriter();
        var command = RunCommand.Build(_ => runner);

        var exit = await command.Parse(["--file", suite.Path]).InvokeAsync(
            new InvocationConfiguration { Output = new StringWriter(), Error = stderr });

        Assert.Equal(1, exit);
        Assert.Empty(runner.RanEvals);
        Assert.Contains("legacy 'assertion:'", stderr.ToString());
    }
}
