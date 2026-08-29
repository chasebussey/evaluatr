using System.Text;
using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;
using GitHub.Copilot;

namespace Evaluatr.Copilot;

/// <summary>
/// A Copilot-backed <see cref="IGraderSession"/>. Wraps a live
/// <see cref="CopilotSession"/> so rubric graders can reason with an LLM without
/// Core depending on the Copilot SDK. Accumulates token usage across every
/// <see cref="AskAsync"/> call.
/// </summary>
internal sealed class CopilotGraderSession : IGraderSession
{
    private readonly CopilotSession _session;
    private long _inputTokens;
    private long _outputTokens;

    public CopilotGraderSession(CopilotSession session)
    {
        _session = session;
    }

    /// <inheritdoc />
    public UsageSummary Usage => new()
    {
        InputTokens = (int)_inputTokens,
        OutputTokens = (int)_outputTokens,
    };

    /// <inheritdoc />
    public async Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var transcript = new StringBuilder();
        var completion = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var _ = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        _session.On<SessionEvent>(evt =>
        {
            switch (evt)
            {
                case AssistantMessageEvent msg:
                    transcript.Append(msg.Data.Content);
                    break;

                case AssistantUsageEvent usage:
                    _inputTokens += usage.Data.InputTokens ?? 0L;
                    _outputTokens += usage.Data.OutputTokens ?? 0L;
                    break;

                case SessionIdleEvent idle when idle.Data.Aborted == true:
                    completion.TrySetCanceled();
                    break;

                case SessionIdleEvent:
                    completion.TrySetResult(transcript.ToString());
                    break;

                case SessionErrorEvent err:
                    completion.TrySetException(new CopilotEvalException(
                        "grader", err.Data.Message, err.Data.ErrorCode));
                    break;
            }
        });

        await _session.SendAsync(new MessageOptions { Prompt = prompt });

        return await completion.Task;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
