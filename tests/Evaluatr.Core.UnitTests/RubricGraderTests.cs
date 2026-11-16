using System.Text.Json;
using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;

namespace Evaluatr.Core.UnitTests;

public class RubricGraderTests
{
    [Theory]
    [InlineData("0.8", GradeOutcome.Pass)]
    [InlineData("0.825", GradeOutcome.Pass)]
    [InlineData("0.8250001", GradeOutcome.Fail)]
    public async Task AggregatesConfiguredWeights_AndUsesInclusiveThreshold(string threshold, GradeOutcome outcome)
    {
        var judge = new FakeGraderSession("""
            {"criteria":[
              {"id":"maintainability","score":0.6,"explanation":"Some duplication."},
              {"id":"correctness","score":0.9,"explanation":"Requirements satisfied."}
            ]}
            """);
        var grader = Rubric(decimal.Parse(threshold, System.Globalization.CultureInfo.InvariantCulture));
        var result = Assert.IsType<RubricGradeResult>(await grader.GradeAsync(Context(judge)));

        Assert.Equal(0.825m, result.Score);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(new[] { "correctness", "maintainability" }, result.Criteria.Select(criterion => criterion.Id));
        Assert.Equal(new[] { 3m, 1m }, result.Criteria.Select(criterion => criterion.Weight));
        Assert.Equal(10, result.Usage.InputTokens);
        Assert.Equal(5, result.Usage.OutputTokens);
        Assert.True(judge.Disposed);
        Assert.Equal(1, judge.Calls);
    }

    [Theory]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("1")]
    [InlineData("79228162514264337593543950335")]
    public async Task RelativeWeightsAreScaleInvariant_WithoutUnderflowOrOverflow(string weightText)
    {
        var weight = decimal.Parse(weightText, System.Globalization.CultureInfo.InvariantCulture);
        var grader = new RubricGrader
        {
            Name = "scaled weights", PassThreshold = 0.75m,
            Criteria =
            [
                new RubricCriterion { Id = "correctness", Description = "Correct", Weight = weight },
                new RubricCriterion { Id = "maintainability", Description = "Maintainable", Weight = weight },
            ],
        };
        var session = new FakeGraderSession("""
            {"criteria":[
              {"id":"correctness","score":0.9,"explanation":"good"},
              {"id":"maintainability","score":0.6,"explanation":"acceptable"}
            ]}
            """);

        var result = Assert.IsType<RubricGradeResult>(await grader.GradeAsync(Context(session)));

        Assert.Equal(0.75m, result.Score);
        Assert.Equal(GradeOutcome.Pass, result.Outcome);
    }

    [Fact]
    public async Task SuppliesOriginalTaskTranscriptAndModelProvider_SeparatelyFromSystemInstructions()
    {
        var session = new FakeGraderSession(ValidResponse);
        GraderSessionOptions? captured = null;
        var context = Context(session) with
        {
            CreateGraderSessionAsync = (options, _) =>
            {
                captured = options;
                return Task.FromResult<IGraderSession>(session);
            },
        };
        var grader = new RubricGrader
        {
            Name = "quality", Provider = "other-provider", Model = "chosen-model", PassThreshold = 0.8m,
            Criteria = [new RubricCriterion { Id = "quality", Description = "Meets requirements" }],
        };

        await grader.GradeAsync(context);

        Assert.NotNull(captured);
        Assert.Equal("other-provider", captured.Provider);
        Assert.Equal("chosen-model", captured.Model);
        Assert.Equal(context.WorkingDirectory, captured.WorkingDirectory);
        Assert.Contains("untrusted evidence", captured.SystemMessage);
        Assert.DoesNotContain(context.TaskPrompt, captured.SystemMessage);
        using var evidence = JsonDocument.Parse(session.Prompt!);
        Assert.Equal(context.TaskPrompt, evidence.RootElement.GetProperty("task").GetString());
        Assert.Equal(context.EvalResult.Transcript, evidence.RootElement.GetProperty("assistantTranscript").GetString());
    }

    public static IEnumerable<object[]> InvalidResponses => new[]
    {
        "not json", "```json\n{}\n```", "[]", "null", "{}", "{\"criteria\":null}",
        "{\"criteria\":[]}",
        """{"criteria":[{"id":"unknown","score":0.9,"explanation":"good"}]}""",
        """{"criteria":[{"id":"Quality","score":0.9,"explanation":"good"}]}""",
        """{"criteria":[{"id":"quality","score":-0.1,"explanation":"bad"}]}""",
        """{"criteria":[{"id":"quality","score":1.1,"explanation":"bad"}]}""",
        """{"criteria":[{"id":"quality","score":-1e-100,"explanation":"bad"}]}""",
        """{"criteria":[{"id":"quality","score":1.00000000000000000000000000001,"explanation":"bad"}]}""",
        """{"criteria":[{"id":"quality","score":"0.9","explanation":"bad"}]}""",
        """{"criteria":[{"id":"quality","score":null,"explanation":"bad"}]}""",
        """{"criteria":[{"id":"quality","score":1e100,"explanation":"bad"}]}""",
        """{"criteria":[{"id":"quality","score":0.9,"explanation":" "}]}""",
        """{"criteria":[{"id":"quality","score":0.9}]}""",
        """{"criteria":[{"id":"quality","score":0.9,"explanation":null}]}""",
        """{"criteria":[{"id":"quality","score":0.9,"score":0.8,"explanation":"good"}]}""",
        """{"criteria":[],"criteria":[{"id":"quality","score":0.9,"explanation":"good"}]}""",
        """{"criteria":[{"id":"quality","score":0.9,"explanation":"good","weight":99}]}""",
        """{"criteria":[{"id":"quality","score":0.9,"explanation":"good"}],"passed":true}""",
    }.Select(response => new object[] { response });

    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public async Task InvalidResponse_IsAnUnscoredErrorWithoutRetry(string response)
    {
        var session = new FakeGraderSession(response);
        var grader = SingleCriterionRubric();

        var result = Assert.IsType<RubricGradeResult>(await grader.GradeAsync(Context(session)));

        Assert.Equal(GradeOutcome.Error, result.Outcome);
        Assert.Null(result.Score);
        Assert.Empty(result.Criteria);
        Assert.NotEmpty(result.Detail);
        Assert.Equal(1, session.Calls);
        Assert.Equal(10, result.Usage.InputTokens);
        Assert.True(session.Disposed);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.0")]
    [InlineData("1")]
    [InlineData("1.00000000000000000000000000000")]
    [InlineData("8e-1")]
    [InlineData("0.0008e3")]
    public async Task AcceptsNormalizedNumericForms(string score)
    {
        var response = "{\"criteria\":[{\"id\":\"quality\",\"score\":" + score + ",\"explanation\":\"evidence\"}]}";
        var result = Assert.IsType<RubricGradeResult>(await SingleCriterionRubric().GradeAsync(Context(new FakeGraderSession(response))));

        Assert.NotEqual(GradeOutcome.Error, result.Outcome);
        Assert.NotNull(result.Score);
    }

    [Fact]
    public async Task DuplicateCriterionIds_AreRejectedEvenWhenResultCountMatches()
    {
        var session = new FakeGraderSession("""
            {"criteria":[
              {"id":"correctness","score":0.9,"explanation":"good"},
              {"id":"correctness","score":0.8,"explanation":"also good"}
            ]}
            """);

        var result = await Rubric().GradeAsync(Context(session));

        Assert.Equal(GradeOutcome.Error, result.Outcome);
        Assert.Contains("duplicate", result.Detail);
    }

    [Fact]
    public async Task ProviderFailure_PreservesUsageAndDisposesSession()
    {
        var session = new FakeGraderSession("") { Reply = _ => throw new IOException("Provider unavailable.") };

        var result = await SingleCriterionRubric().GradeAsync(Context(session));

        Assert.Equal(GradeOutcome.Error, result.Outcome);
        Assert.Contains("Provider unavailable", result.Detail);
        Assert.Equal(10, result.Usage.InputTokens);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task DisposalFailure_IsUnscoredErrorWithKnownUsage()
    {
        var session = new FakeGraderSession(ValidResponse) { DisposeError = new IOException("Cleanup failed.") };

        var result = Assert.IsType<RubricGradeResult>(await SingleCriterionRubric().GradeAsync(Context(session)));

        Assert.Equal(GradeOutcome.Error, result.Outcome);
        Assert.Null(result.Score);
        Assert.Equal(10, result.Usage.InputTokens);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task MissingTaskEvidence_DoesNotCreateOrCallAJudge()
    {
        var session = new FakeGraderSession(ValidResponse);
        var result = await SingleCriterionRubric().GradeAsync(Context(session) with { TaskPrompt = "" });

        Assert.Equal(GradeOutcome.Error, result.Outcome);
        Assert.Contains("original task prompt", result.Detail);
        Assert.Equal(0, session.Calls);
    }

    [Fact]
    public async Task OversizedResponse_IsRejectedWithoutRetry()
    {
        var session = new FakeGraderSession(new string(' ', 131073));
        var result = await SingleCriterionRubric().GradeAsync(Context(session));

        Assert.Equal(GradeOutcome.Error, result.Outcome);
        Assert.Contains("128 Ki-character limit", result.Detail);
        Assert.Equal(1, session.Calls);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task SessionCreationFailure_IsErrorWithoutFallback()
    {
        var calls = 0;
        var context = Context(new FakeGraderSession("")) with
        {
            CreateGraderSessionAsync = (_, _) => { calls++; throw new IOException("Unsupported model."); },
        };

        var result = await SingleCriterionRubric().GradeAsync(context);

        Assert.Equal(GradeOutcome.Error, result.Outcome);
        Assert.Equal("Unsupported model.", result.Detail);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Cancellation_PropagatesAndDisposesSession()
    {
        var session = new FakeGraderSession("") { Reply = _ => throw new OperationCanceledException() };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SingleCriterionRubric().GradeAsync(Context(session)));

        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task RubricFailuresAndErrors_DoNotPreventLaterRubrics()
    {
        var sessions = new Queue<FakeGraderSession>([
            new("""{"criteria":[{"id":"quality","score":0.1,"explanation":"bad"}]}"""),
            new("invalid"),
            new(ValidResponse),
        ]);
        var created = new List<FakeGraderSession>();
        var context = Context(new FakeGraderSession("")) with
        {
            CreateGraderSessionAsync = (_, _) =>
            {
                var session = sessions.Dequeue();
                created.Add(session);
                return Task.FromResult<IGraderSession>(session);
            },
        };

        var report = await GradingRunner.RunAsync(
            [SingleCriterionRubric(), SingleCriterionRubric(), SingleCriterionRubric()], context);

        Assert.Equal(new[] { GradeOutcome.Fail, GradeOutcome.Error, GradeOutcome.Pass }, report.Results.Select(result => result.Outcome));
        Assert.Equal(EvalVerdict.Error, report.Verdict);
        Assert.Equal(3, created.Count);
        Assert.All(created, session => Assert.True(session.Disposed));
    }

    private const string ValidResponse = """{"criteria":[{"id":"quality","score":0.9,"explanation":"good"}]}""";

    private static GradingContext Context(FakeGraderSession session) => new()
    {
        WorkingDirectory = Path.GetTempPath(),
        TaskPrompt = "Implement the original task.",
        EvalResult = new EvalResult { Transcript = "Agent claims task completed.", UsageSummary = new UsageSummary() },
        CreateGraderSessionAsync = (_, _) => Task.FromResult<IGraderSession>(session),
    };

    private static RubricGrader SingleCriterionRubric() => new()
    {
        Name = "quality", PassThreshold = 0.8m,
        Criteria = [new RubricCriterion { Id = "quality", Description = "Good quality" }],
    };

    private static RubricGrader Rubric(decimal threshold = 0.8m) => new()
    {
        Name = "implementation quality", PassThreshold = threshold,
        Criteria =
        [
            new RubricCriterion { Id = "correctness", Description = "Correct implementation", Weight = 3 },
            new RubricCriterion { Id = "maintainability", Description = "Maintainable code" },
        ],
    };
}
