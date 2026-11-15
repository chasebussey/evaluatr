using Evaluatr.Core.Graders;
using GitHub.Copilot;

namespace Evaluatr.Copilot.UnitTests;

public class EvalEventCollectorTests
{
    [Fact]
    public async Task ToolStart_IsCapturedWithoutRequiringSuccessfulCompletion()
    {
        var collector = new EvalEventCollector();
        collector.Record(new ToolExecutionStartEvent
        {
            Data = new ToolExecutionStartData { ToolName = "bash", ToolCallId = "call-1" },
        });
        var snapshot = collector.Snapshot();
        var grader = new AssertionGrader { Name = "used bash", Check = new ToolCalledCheck("bash") };

        var grade = await grader.GradeAsync(new GradingContext
        {
            WorkingDirectory = Path.GetTempPath(), EvalResult = snapshot,
        });

        Assert.Equal(new[] { "bash" }, snapshot.ToolCalls);
        Assert.Equal(GradeOutcome.Pass, grade.Outcome);
    }

    [Fact]
    public void CollectsTranscriptAndUsage_WithoutTreatingMentionsAsCalls()
    {
        var collector = new EvalEventCollector();
        collector.Record(new AssistantMessageEvent
        {
            Data = new AssistantMessageData { Content = "I used bash.", MessageId = "message-1" },
        });
        collector.Record(new AssistantUsageEvent
        {
            Data = new AssistantUsageData { Model = "test", InputTokens = 10, OutputTokens = 5 },
        });
        collector.Record(new AssistantUsageEvent
        {
            Data = new AssistantUsageData { Model = "test", InputTokens = 3, OutputTokens = 2 },
        });

        var snapshot = collector.Snapshot();

        Assert.Equal("I used bash.", snapshot.Transcript);
        Assert.Empty(snapshot.ToolCalls);
        Assert.Equal(13, snapshot.UsageSummary.InputTokens);
        Assert.Equal(7, snapshot.UsageSummary.OutputTokens);
    }

    [Fact]
    public void Snapshots_AreIndependentAndCollectorsDoNotLeakBetweenSessions()
    {
        var eval = new EvalEventCollector();
        var grader = new EvalEventCollector();
        var snapshot = eval.Snapshot();
        eval.Record(new ToolExecutionStartEvent
        {
            Data = new ToolExecutionStartData { ToolName = "bash", ToolCallId = "eval-1" },
        });
        grader.Record(new ToolExecutionStartEvent
        {
            Data = new ToolExecutionStartData { ToolName = "read_file", ToolCallId = "grader-1" },
        });

        Assert.Empty(snapshot.ToolCalls);
        Assert.Equal(new[] { "bash" }, eval.Snapshot().ToolCalls);
        Assert.Equal(new[] { "read_file" }, grader.Snapshot().ToolCalls);
    }
}
