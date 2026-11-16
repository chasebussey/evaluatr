using System.CommandLine;
using Evaluatr.Cli.Commands;
using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;
using Evaluatr.Core.Runner;

namespace Evaluatr.Cli.UnitTests;

public class RubricCliTests
{
    private const string SuiteYaml = """
        name: rubric suite
        harness: executor
        evals:
          - name: task
            description: test task
            repo: owner/repo
            prompt: original task
            graders:
              - name: default judge
                type: rubric
                passThreshold: 0.8
                criteria:
                  - id: quality
                    description: Quality
                    weight: 3
              - name: alternate judge
                type: rubric
                provider: alternate
                model: chosen-model
                passThreshold: 0.8
                criteria:
                  - id: quality
                    description: Quality
        """;

    [Theory]
    [InlineData("0.9", "PASS", 0)]
    [InlineData("0.7", "FAIL", 1)]
    public async Task RoutesDefaultAndOverrideProvidersAndModels_AndReportsCriterionScores(string score, string outcome, int expectedExit)
    {
        var reply = "{\"criteria\":[{\"id\":\"quality\",\"score\":" + score + ",\"explanation\":\"Evidence supports this score.\"}]}";
        var primary = new Provider(reply);
        var alternate = new Provider(reply);
        var runner = new ScoringRunner();
        using var suite = new TempSuiteFile(SuiteYaml);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var command = RunCommand.Build(_ => runner, resolveProvider: id => id switch
        {
            "executor" => primary, "alternate" => alternate, _ => null,
        });

        var exit = await command.Parse(["--file", suite.Path]).InvokeAsync(
            new InvocationConfiguration { Output = stdout, Error = stderr });

        Assert.Equal(expectedExit, exit);
        Assert.Empty(stderr.ToString());
        Assert.Equal(1, runner.Calls);
        Assert.Null(Assert.Single(primary.Options).Model);
        Assert.Equal("chosen-model", Assert.Single(alternate.Options).Model);
        Assert.Equal("alternate", alternate.Options[0].Provider);
        Assert.All(primary.Sessions.Concat(alternate.Sessions), session => Assert.True(session.Disposed));
        Assert.Contains($"[{outcome}] default judge", stdout.ToString());
        Assert.Contains($"quality: {score} (weight: 3)", stdout.ToString());
        Assert.Contains("Evidence supports this score.", stdout.ToString());
        Assert.Contains("Tokens: 16 in / 9 out", stdout.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOrUnsafeJudgeProvider_IsRejectedBeforeAgentExecution(bool unsafeProvider)
    {
        var provider = new Provider("unused") { SupportsInspectionOnlySessions = !unsafeProvider };
        var runner = new ScoringRunner();
        using var suite = new TempSuiteFile(SuiteYaml);
        var stderr = new StringWriter();
        var command = RunCommand.Build(_ => runner, resolveProvider: id =>
            id == "executor" || unsafeProvider ? provider : null);

        var exit = await command.Parse(["--file", suite.Path]).InvokeAsync(
            new InvocationConfiguration { Output = new StringWriter(), Error = stderr });

        Assert.Equal(1, exit);
        Assert.Equal(0, runner.Calls);
        Assert.Empty(provider.Sessions);
        Assert.Contains(unsafeProvider ? "cannot enforce inspection-only" : "unknown judge provider", stderr.ToString());
    }

    [Fact]
    public async Task LegacyRubricIsRejectedWithMigrationGuidance()
    {
        var yaml = SuiteYaml.Replace("passThreshold: 0.8", "rubric: legacy prose");
        using var suite = new TempSuiteFile(yaml);
        var runner = new ScoringRunner();
        var stderr = new StringWriter();
        var command = RunCommand.Build(_ => runner);

        var exit = await command.Parse(["--file", suite.Path]).InvokeAsync(
            new InvocationConfiguration { Output = new StringWriter(), Error = stderr });

        Assert.Equal(1, exit);
        Assert.Equal(0, runner.Calls);
        Assert.Contains("legacy 'rubric:'", stderr.ToString());
        Assert.Contains("criteria:", stderr.ToString());
    }

    [Fact]
    public async Task JudgeModelFailure_IsErrorWithoutFallback_AndLaterRubricStillRuns()
    {
        var calls = new List<string?>();
        var provider = new Provider("""{"criteria":[{"id":"quality","score":0.9,"explanation":"good"}]}""")
        {
            OnCreate = options =>
            {
                calls.Add(options.Model);
                if (options.Model == "unsupported") throw new IOException("Unsupported model.");
            },
        };
        // Put the unsupported model on the first rubric, followed by a valid judge.
        var yaml = SuiteYaml.Replace("name: default judge", "name: default judge\n        model: unsupported");
        using var suite = new TempSuiteFile(yaml);
        var stdout = new StringWriter();
        var command = RunCommand.Build(_ => new ScoringRunner(), resolveProvider: _ => provider);

        var exit = await command.Parse(["--file", suite.Path]).InvokeAsync(
            new InvocationConfiguration { Output = stdout, Error = new StringWriter() });

        Assert.Equal(1, exit);
        Assert.Equal(new[] { "unsupported", "chosen-model" }, calls);
        Assert.Contains("[ERROR] default judge", stdout.ToString());
        Assert.Contains("[PASS] alternate judge", stdout.ToString());
    }

    private sealed class Provider(string reply) : IAgentProvider
    {
        public bool SupportsInspectionOnlySessions { get; init; } = true;
        public Action<GraderSessionOptions>? OnCreate { get; init; }
        public List<GraderSessionOptions> Options { get; } = [];
        public List<Session> Sessions { get; } = [];

        public Task<IGraderSession> CreateGraderSessionAsync(GraderSessionOptions options, CancellationToken cancellationToken = default)
        {
            OnCreate?.Invoke(options);
            Options.Add(options);
            var session = new Session(reply);
            Sessions.Add(session);
            return Task.FromResult<IGraderSession>(session);
        }
    }

    private sealed class Session(string reply) : IGraderSession
    {
        public bool Disposed { get; private set; }
        public UsageSummary Usage { get; } = new() { InputTokens = 7, OutputTokens = 4 };
        public Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default) => Task.FromResult(reply);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class ScoringRunner : IEvalRunner
    {
        public int Calls { get; private set; }

        public async Task<EvalReport> RunAsync(Eval eval, EvalRunOptions options, CancellationToken cancellationToken = default)
        {
            Calls++;
            var result = new EvalResult { Transcript = "completed", UsageSummary = new UsageSummary { InputTokens = 2, OutputTokens = 1 } };
            var grading = await GradingRunner.RunAsync(eval.Graders, new GradingContext
            {
                TaskPrompt = eval.Prompt, EvalResult = result, WorkingDirectory = Path.GetTempPath(),
                CreateGraderSessionAsync = options.CreateGraderSessionAsync,
            }, cancellationToken);

            return new EvalReport
            {
                EvalName = eval.Name, EvalResult = result, Verdict = grading.Verdict, GradeResults = grading.Results,
                UsageSummary = grading.Results.Aggregate(result.UsageSummary, (usage, grade) => usage.Add(grade.Usage)),
            };
        }
    }
}
