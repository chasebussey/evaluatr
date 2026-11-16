using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;

namespace Evaluatr.Core.UnitTests;

public class GradingRunnerTests
{
    private static readonly GradingContext Context = new()
    {
        WorkingDirectory = Path.GetTempPath(),
        TaskPrompt = "Complete the task.",
        EvalResult = new EvalResult { Transcript = "done", UsageSummary = new UsageSummary() },
    };

    [Fact]
    public async Task OrdersRequiredOptionalThenRubrics_Stably()
    {
        var visited = new List<string>();
        var graders = new IGrader[]
        {
            Rubric("rubric-1"),
            Assertion("optional-1", false), Assertion("required-1", true),
            Assertion("optional-2", false), Assertion("required-2", true),
            Rubric("rubric-2"),
        };

        var context = Context with
        {
            CreateGraderSessionAsync = (options, _) =>
            {
                visited.Add(options.Model!);
                return Task.FromResult<IGraderSession>(new FakeGraderSession(
                    """{"criteria":[{"id":"quality","score":0.9,"explanation":"good"}]}"""));
            },
        };
        var report = await GradingRunner.RunAsync(graders, context);

        Assert.Equal(new[] { "required-1", "required-2", "optional-1", "optional-2", "rubric-1", "rubric-2" },
            report.Results.Select(result => result.Name));
        Assert.Equal(new[] { "rubric-1", "rubric-2" }, visited);
        Assert.Equal(EvalVerdict.Pass, report.Verdict);
    }

    [Theory]
    [InlineData(GradeOutcome.Fail)]
    [InlineData(GradeOutcome.Error)]
    public async Task RequiredNonPass_FailsEvalAndSkipsAllRemainingGraders(GradeOutcome outcome)
    {
        var graders = new IGrader[]
        {
            Assertion("optional", false),
            Rubric("rubric"),
            Assertion("blocker", true, outcome),
            Assertion("next-required", true),
        };

        var sessionsStarted = 0;
        var context = Context with
        {
            CreateGraderSessionAsync = (_, _) =>
            {
                sessionsStarted++;
                throw new InvalidOperationException("Rubric must not start.");
            },
        };
        var report = await GradingRunner.RunAsync(graders, context);

        Assert.Equal(0, sessionsStarted);

        Assert.Equal(EvalVerdict.Fail, report.Verdict);
        Assert.Equal(outcome, report.Results[0].Outcome);
        Assert.All(report.Results.Skip(1), result =>
        {
            Assert.Equal(GradeOutcome.Skipped, result.Outcome);
            Assert.Contains("blocker", result.Detail);
        });
    }

    [Theory]
    [InlineData(GradeOutcome.Fail)]
    [InlineData(GradeOutcome.Error)]
    public async Task OptionalNonPass_IsDiagnosticAndContinues(GradeOutcome outcome)
    {
        var report = await GradingRunner.RunAsync(
            [Assertion("diagnostic", false, outcome), Assertion("next", false)], Context);

        Assert.Equal(EvalVerdict.Pass, report.Verdict);
        Assert.Equal(GradeOutcome.Pass, report.Results[1].Outcome);
    }

    [Fact]
    public async Task UnexpectedCheckError_IsReportedAndDoesNotStopOptionalGrading()
    {
        var broken = new AssertionGrader { Name = "broken", Check = new ThrowingCheck() };

        var report = await GradingRunner.RunAsync([broken, Assertion("next", false)], Context);

        Assert.Equal(GradeOutcome.Error, report.Results[0].Outcome);
        Assert.Equal(GradeOutcome.Pass, report.Results[1].Outcome);
        Assert.Equal(EvalVerdict.Pass, report.Verdict);
    }

    [Fact]
    public async Task MissingJudgeFactory_IsAnErrorNotAFalsePass()
    {
        var report = await GradingRunner.RunAsync(
            [Rubric("rubric")], Context);

        Assert.Equal(EvalVerdict.Error, report.Verdict);
        Assert.Equal(GradeOutcome.Error, report.Results[0].Outcome);
        Assert.Null(Assert.IsType<RubricGradeResult>(report.Results[0]).Score);
    }

    [Fact]
    public async Task Cancellation_StopsRatherThanReportingError()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GradingRunner.RunAsync(
            [Assertion("check", true)], Context, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task CancellationDuringGrading_IsNotConvertedToError()
    {
        var grader = new AssertionGrader { Name = "canceled", Check = new CancelingCheck() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GradingRunner.RunAsync([grader, Assertion("next", false)], Context));
    }

    [Fact]
    public async Task EmptyGrading_HasNoBlockingResult()
    {
        var report = await GradingRunner.RunAsync([], Context);

        Assert.Equal(EvalVerdict.Pass, report.Verdict);
        Assert.Empty(report.Results);
    }

    private static RubricGrader Rubric(string name) => new()
    {
        Name = name,
        Model = name,
        PassThreshold = 0.8m,
        Criteria = [new RubricCriterion { Id = "quality", Description = "Quality" }],
    };

    private static AssertionGrader Assertion(string name, bool required, GradeOutcome outcome = GradeOutcome.Pass) => new()
    {
        Name = name, Required = required, Check = new FixedCheck(outcome),
    };

    private sealed record FixedCheck(GradeOutcome Outcome) : AssertionCheck
    {
        public override void Validate() { }
        public override Task<CheckResult> EvaluateAsync(GradingContext context, CancellationToken cancellationToken = default)
            => Task.FromResult(new CheckResult(Outcome, "test result"));
    }

    private sealed record CancelingCheck : AssertionCheck
    {
        public override void Validate() { }
        public override Task<CheckResult> EvaluateAsync(GradingContext context, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException();
    }

    private sealed record ThrowingCheck : AssertionCheck
    {
        public override void Validate() { }
        public override Task<CheckResult> EvaluateAsync(GradingContext context, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Could not inspect output.");
    }
}
