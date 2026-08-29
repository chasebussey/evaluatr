namespace Evaluatr.Copilot;

/// <summary>
/// Thrown when the Copilot agent returns a <c>session.error</c> event
/// during eval execution.
/// </summary>
public sealed class CopilotEvalException : Exception
{
    /// <summary>The name of the eval that produced the error.</summary>
    public string EvalName { get; }

    /// <summary>The error code reported by the Copilot runtime, if available.</summary>
    public string? ErrorCode { get; }

    /// <inheritdoc cref="CopilotEvalException"/>
    public CopilotEvalException(string evalName, string message, string? errorCode = null)
        : base($"Eval '{evalName}' failed: {message}")
    {
        EvalName = evalName;
        ErrorCode = errorCode;
    }
}
