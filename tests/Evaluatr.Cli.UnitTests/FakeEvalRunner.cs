using Evaluatr.Core.Evals;
using Evaluatr.Core.Runner;

namespace Evaluatr.Cli.UnitTests;

/// <summary>
/// A test double for <see cref="IEvalRunner"/> that records the evals it was asked
/// to run and returns a canned <see cref="EvalReport"/> without touching git or Copilot.
/// </summary>
internal sealed class FakeEvalRunner : IEvalRunner
{
    public List<Eval> RanEvals { get; } = [];
    public List<EvalRunOptions> ReceivedOptions { get; } = [];

    public Task<EvalReport> RunAsync(
        Eval eval,
        EvalRunOptions options,
        CancellationToken cancellationToken = default)
    {
        RanEvals.Add(eval);
        ReceivedOptions.Add(options);

        return Task.FromResult(new EvalReport
        {
            EvalName = eval.Name,
            Status = EvalRunStatus.Completed,
        });
    }
}
