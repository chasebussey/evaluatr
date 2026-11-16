using GitHub.Copilot;

namespace Evaluatr.Copilot.UnitTests;

public class JudgeResponseCollectorTests
{
    [Fact]
    public async Task UsesFinalMessageNotCommentary_WhileCountingAllUsage()
    {
        var collector = new JudgeResponseCollector();
        collector.Record(Message("commentary", "I will inspect the workspace."));
        collector.Record(new AssistantUsageEvent
        {
            Data = new AssistantUsageData { Model = "judge", InputTokens = 10, OutputTokens = 5 },
        });
        collector.Record(Message("final", "{\"criteria\":[]}"));
        collector.Record(new AssistantUsageEvent
        {
            Data = new AssistantUsageData { Model = "judge", InputTokens = 3, OutputTokens = 2 },
        });
        collector.Record(new SessionIdleEvent { Data = new SessionIdleData() });

        Assert.Equal("{\"criteria\":[]}", await collector.Completion);
        Assert.Equal(13, collector.Usage.InputTokens);
        Assert.Equal(7, collector.Usage.OutputTokens);
    }

    [Fact]
    public async Task ChunksOfSameFinalMessageAreCombined()
    {
        var collector = new JudgeResponseCollector();
        collector.Record(Message("final", "{\"criteria\":"));
        collector.Record(Message("final", "[]}"));
        collector.Record(new SessionIdleEvent { Data = new SessionIdleData() });

        Assert.Equal("{\"criteria\":[]}", await collector.Completion);
    }

    [Fact]
    public async Task AbortedSessionIsCancellation()
    {
        var collector = new JudgeResponseCollector();
        collector.Record(new SessionIdleEvent { Data = new SessionIdleData { Aborted = true } });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collector.Completion);
    }

    [Fact]
    public async Task ErrorsPropagateRatherThanProducingAnEmptySuccess()
    {
        var collector = new JudgeResponseCollector();
        collector.Record(new SessionErrorEvent
        {
            Data = new SessionErrorData { Message = "Authentication failed.", ErrorType = "auth" },
        });

        var error = await Assert.ThrowsAsync<CopilotEvalException>(() => collector.Completion);
        Assert.Contains("Authentication failed", error.Message);
    }

    [Fact]
    public async Task BoundsResponseSize()
    {
        var collector = new JudgeResponseCollector();
        collector.Record(Message("final", new string('x', 131073)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => collector.Completion);
    }

    private static AssistantMessageEvent Message(string id, string content) => new()
    {
        Data = new AssistantMessageData { MessageId = id, Content = content },
    };
}
