using Evaluatr.Core.Graders;
using Evaluatr.Copilot;

namespace Evaluatr.Cli;

/// <summary>Composition-root registration of judge provider adapters, independent of eval runners.</summary>
internal static class AgentProviderRegistry
{
    public static IAgentProvider? Resolve(string provider) =>
        string.Equals(provider, "copilot", StringComparison.OrdinalIgnoreCase) ? new CopilotAgentProvider() : null;
}
