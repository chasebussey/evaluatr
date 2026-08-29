using Evaluatr.Core.Evals;

namespace Evaluatr.Core.Runner;

/// <summary>
/// Executes a single <see cref="Eval"/> against an agent and returns the result.
/// </summary>
public interface IEvalRunner
{
    /// <summary>
    /// Runs the given eval end to end — checkout, prompt execution, and grading —
    /// and returns a combined <see cref="EvalReport"/>.
    /// </summary>
    /// <param name="eval">The eval definition to execute.</param>
    /// <param name="options">Runtime options such as the GitHub token and target model.</param>
    /// <param name="cancellationToken">Token used to cancel a running eval.</param>
    /// <returns>
    /// An <see cref="EvalReport"/> containing the grade results and combined token usage.
    /// </returns>
    Task<EvalReport> RunAsync(
        Eval eval,
        EvalRunOptions options,
        CancellationToken cancellationToken = default);
}
