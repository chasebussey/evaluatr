using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;
using GitHub.Copilot;

namespace Evaluatr.Copilot;

/// <summary>A single-use judge conversation owning both its session and client.</summary>
internal sealed class CopilotGraderSession(CopilotSession session, CopilotClient client) : IGraderSession
{
    private readonly JudgeResponseCollector _collector = new();
    private int _asked;

    public UsageSummary Usage => _collector.Usage;

    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _asked, 1) != 0)
        {
            throw new InvalidOperationException("A judge session may only be asked once.");
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var subscription = session.On<SessionEvent>(_collector.Record);
        try
        {
            await session.SendAsync(new MessageOptions { Prompt = prompt }, deadline.Token);
            return await _collector.Completion.WaitAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Judge session exceeded the five-minute response timeout.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await session.DisposeAsync();
        }
        finally
        {
            await client.DisposeAsync();
        }
    }
}
