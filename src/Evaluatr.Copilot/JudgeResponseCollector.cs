using System.Text;
using Evaluatr.Core.Evals;
using GitHub.Copilot;

namespace Evaluatr.Copilot;

// Judge replies use the final assistant message, not concatenated commentary from
// earlier tool-use turns. Usage still includes the entire conversation.
internal sealed class JudgeResponseCollector
{
    private readonly object _gate = new();
    private readonly StringBuilder _response = new();
    private readonly TaskCompletionSource<string> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _messageId;
    private long _inputTokens;
    private long _outputTokens;

    public Task<string> Completion => _completion.Task;

    public UsageSummary Usage
    {
        get
        {
            lock (_gate)
            {
                return new UsageSummary { InputTokens = (int)_inputTokens, OutputTokens = (int)_outputTokens };
            }
        }
    }

    public void Record(SessionEvent evt)
    {
        lock (_gate)
        {
            switch (evt)
            {
                case AssistantMessageEvent message:
                    if (_messageId != message.Data.MessageId)
                    {
                        _response.Clear();
                        _messageId = message.Data.MessageId;
                    }
                    if (_response.Length + message.Data.Content.Length > 131072)
                    {
                        _completion.TrySetException(new InvalidOperationException("Judge response exceeds the 128 Ki-character limit."));
                        break;
                    }
                    _response.Append(message.Data.Content);
                    break;
                case AssistantUsageEvent usage:
                    _inputTokens += usage.Data.InputTokens ?? 0;
                    _outputTokens += usage.Data.OutputTokens ?? 0;
                    break;
                case SessionIdleEvent idle when idle.Data.Aborted == true:
                    _completion.TrySetCanceled();
                    break;
                case SessionIdleEvent:
                    _completion.TrySetResult(_response.ToString());
                    break;
                case SessionErrorEvent error:
                    _completion.TrySetException(new CopilotEvalException("judge", error.Data.Message, error.Data.ErrorCode));
                    break;
            }
        }
    }
}
