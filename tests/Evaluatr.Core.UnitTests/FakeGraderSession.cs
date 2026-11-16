using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;

namespace Evaluatr.Core.UnitTests;

internal sealed class FakeGraderSession(string response) : IGraderSession
{
    public bool Disposed { get; private set; }
    public string? Prompt { get; private set; }
    public int Calls { get; private set; }
    public Func<CancellationToken, Task<string>>? Reply { get; init; }
    public Exception? DisposeError { get; init; }
    public UsageSummary Usage { get; } = new() { InputTokens = 10, OutputTokens = 5 };

    public Task<string> AskAsync(string prompt, CancellationToken cancellationToken = default)
    {
        Calls++;
        Prompt = prompt;
        return Reply?.Invoke(cancellationToken) ?? Task.FromResult(response);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return DisposeError is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeError);
    }
}
