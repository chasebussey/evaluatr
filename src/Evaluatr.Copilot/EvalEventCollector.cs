using System.Text;
using Evaluatr.Core.Evals;
using GitHub.Copilot;

namespace Evaluatr.Copilot;

// Only attach this collector to the eval session, never to grader sessions.
internal sealed class EvalEventCollector
{
    private readonly object _gate = new();
    private readonly StringBuilder _transcript = new();
    private readonly List<string> _toolCalls = [];
    private long _inputTokens;
    private long _outputTokens;

    public void Record(SessionEvent evt)
    {
        lock (_gate)
        {
            switch (evt)
            {
                case AssistantMessageEvent message:
                    _transcript.Append(message.Data.Content);
                    break;
                case ToolExecutionStartEvent tool:
                    _toolCalls.Add(tool.Data.ToolName);
                    break;
                case AssistantUsageEvent usage:
                    _inputTokens += usage.Data.InputTokens ?? 0;
                    _outputTokens += usage.Data.OutputTokens ?? 0;
                    break;
            }
        }
    }

    public EvalResult Snapshot()
    {
        lock (_gate)
        {
            return new EvalResult
            {
                Transcript = _transcript.ToString(),
                ToolCalls = _toolCalls.ToArray(),
                UsageSummary = new UsageSummary
                {
                    InputTokens = (int)_inputTokens,
                    OutputTokens = (int)_outputTokens,
                },
            };
        }
    }
}
