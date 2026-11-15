using Evaluatr.Core.Evals;

namespace Evaluatr.Core.Graders;

/// <summary>The ordered grades and aggregate verdict for one eval.</summary>
public sealed record GradingReport(EvalVerdict Verdict, IReadOnlyList<GradeResult> Results);

/// <summary>Harness-neutral ordering, gating, and outcome aggregation.</summary>
public static class GradingRunner
{
    /// <summary>
    /// Runs required assertions, optional assertions, then rubrics, preserving order
    /// within each group. Only required assertions gate subsequent grading.
    /// The optional rubric executor owns any harness-specific session lifetime.
    /// </summary>
    public static async Task<GradingReport> RunAsync(
        IReadOnlyList<IGrader> graders,
        GradingContext context,
        Func<IGrader, GradingContext, CancellationToken, Task<GradeResult>>? executeRubric = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ordered = graders.OrderBy(grader => grader is AssertionGrader { Required: true }
            ? 0 : grader.Type == GraderType.Assertion ? 1 : 2);
        var results = new List<GradeResult>(graders.Count);
        var verdict = EvalVerdict.Pass;
        string? blocker = null;

        foreach (var grader in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (blocker is not null)
            {
                results.Add(new GradeResult
                {
                    Name = grader.Name,
                    Outcome = GradeOutcome.Skipped,
                    Detail = $"Blocked by required assertion '{blocker}'.",
                });
                continue;
            }

            GradeResult result;
            try
            {
                result = grader.Type == GraderType.Rubric && executeRubric is not null
                    ? await executeRubric(grader, context, cancellationToken)
                    : await grader.GradeAsync(context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new GradeResult { Name = grader.Name, Outcome = GradeOutcome.Error, Detail = ex.Message };
            }

            cancellationToken.ThrowIfCancellationRequested();
            results.Add(result);

            if (grader is AssertionGrader { Required: true } && result.Outcome != GradeOutcome.Pass)
            {
                blocker = grader.Name;
                verdict = EvalVerdict.Fail;
            }
            else if (grader.Type == GraderType.Rubric)
            {
                // Optional assertion diagnostics do not affect the verdict. Rubrics
                // remain separate: errors mean the eval could not be fully graded.
                if (result.Outcome is GradeOutcome.Error or GradeOutcome.Skipped)
                {
                    verdict = EvalVerdict.Error;
                }
                else if (result.Outcome == GradeOutcome.Fail && verdict != EvalVerdict.Error)
                {
                    verdict = EvalVerdict.Fail;
                }
            }
        }

        return new GradingReport(verdict, results);
    }
}
