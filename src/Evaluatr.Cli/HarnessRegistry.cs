using Evaluatr.Copilot;
using Evaluatr.Core.Runner;

namespace Evaluatr.Cli;

/// <summary>
/// Composition-root wiring that maps a suite's <c>harness</c> id to the
/// <see cref="IEvalRunner"/> that executes it. This is deliberately kept as simple
/// keyed lookup wiring, not domain policy: if it grows normalization, defaults, or
/// validation, promote it into a dedicated Application layer.
/// </summary>
internal static class HarnessRegistry
{
    private static readonly Dictionary<string, Func<IEvalRunner>> Runners =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["copilot"] = () => new CopilotEvalRunner(),
            // ["pi"] = () => new PiEvalRunner(),
        };

    /// <summary>
    /// Resolves the runner for <paramref name="harness"/>, returning
    /// <see langword="null"/> when no runner is registered for that id.
    /// </summary>
    public static IEvalRunner? Resolve(string harness) =>
        Runners.TryGetValue(harness, out var factory) ? factory() : null;

    /// <summary>The set of harness ids this registry can route to.</summary>
    public static IEnumerable<string> Known => Runners.Keys;
}
