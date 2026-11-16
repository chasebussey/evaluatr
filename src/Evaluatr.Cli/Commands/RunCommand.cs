using System.CommandLine;
using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;
using Evaluatr.Core.Runner;

namespace Evaluatr.Cli.Commands;

public static class RunCommand
{
    private static readonly Option<string?> DirectoryOption = new("--directory", "-d")
    {
        Description = "Directory containing eval suites as .yaml or .yml files. Defaults to ./.evals",
    };

    private static readonly Option<string?> FileOption = new("--file", "-f")
    {
        Description = "A .yaml or .yml file containing an eval suite to run.",
    };

    private static readonly Option<bool> VerboseOption = new("--verbose", "-v");

    private static readonly Option<bool> AllowDirtyOption = new("--allow-dirty")
    {
        Description =
            "Permit live-workspace suites to run against a working tree with uncommitted " +
            "changes without prompting for confirmation.",
    };

    /// <summary>
    /// Builds the <c>run</c> command with its options and action wired up.
    /// </summary>
    /// <param name="resolveRunner">
    /// Resolves the eval runner for a suite's <c>harness</c> id, returning
    /// <see langword="null"/> for an unknown harness. Injectable so the CLI is
    /// unit-testable; defaults to <see cref="HarnessRegistry.Resolve"/>.
    /// </param>
    /// <param name="standardInput">
    /// The reader used for the interactive dirty-tree confirmation prompt. Injectable so
    /// the prompt can be driven in tests; defaults to <see cref="Console.In"/>.
    /// </param>
    /// <param name="resolveProvider">Resolves judge provider adapters; defaults to the registered providers.</param>
    public static Command Build(
        Func<string, IEvalRunner?>? resolveRunner = null,
        TextReader? standardInput = null,
        Func<string, IAgentProvider?>? resolveProvider = null)
    {
        resolveRunner ??= HarnessRegistry.Resolve;
        resolveProvider ??= AgentProviderRegistry.Resolve;

        var cmd = new Command("run", "Execute eval suites.")
        {
            DirectoryOption,
            FileOption,
            VerboseOption,
            AllowDirtyOption,
        };
        cmd.SetAction((parseResult, cancellationToken) =>
            Run(parseResult, resolveRunner, resolveProvider, standardInput, cancellationToken));
        return cmd;
    }

    /// <summary>
    /// Loads the requested eval suites, validates each suite's execution mode, resolves the
    /// runner for each suite's <c>harness</c>, resolves the dirty-tree decision for
    /// live-workspace suites, then runs every eval via the resolved
    /// <see cref="IEvalRunner"/> and surfaces the grade results.
    /// </summary>
    private static async Task<int> Run(
        ParseResult parseResult,
        Func<string, IEvalRunner?> resolveRunner,
        Func<string, IAgentProvider?> resolveProvider,
        TextReader? standardInput = null,
        CancellationToken cancellationToken = default)
    {
        var targetDir = parseResult.GetValue(DirectoryOption);
        var targetFile = parseResult.GetValue(FileOption);
        var verbose = parseResult.GetValue(VerboseOption);
        var allowDirty = parseResult.GetValue(AllowDirtyOption);

        var output = new ConsoleWriter(parseResult.InvocationConfiguration, verbose);

        if (targetDir is not null && targetFile is not null)
        {
            output.WriteError("Only one of --directory and --file may be defined.");
            return 1;
        }

        if (targetDir is null && targetFile is null)
        {
            targetDir = Path.Combine(Directory.GetCurrentDirectory(), ".evals");
        }

        var suitePath = targetDir ?? targetFile;
        output.WriteVerbose($"Loading suites from {suitePath}");
        List<EvalSuite> evalSuites;
        try
        {
            evalSuites = LoadSuites(suitePath!);
        }
        catch (EvalSuiteValidationException ex)
        {
            output.WriteError(ex.Message);
            return 1;
        }

        output.WriteVerbose($"Loaded {evalSuites.Count} eval suite(s).");

        // Determine (and validate) each suite's execution mode and resolve its runner up
        // front, before any checkout or Copilot work, so authoring mistakes fail fast.
        var planned = new List<(EvalSuite Suite, SuiteExecutionMode Mode, IEvalRunner Runner,
            Func<GraderSessionOptions, CancellationToken, Task<IGraderSession>> JudgeFactory)>(evalSuites.Count);
        foreach (var suite in evalSuites)
        {
            SuiteExecutionMode mode;
            try
            {
                mode = EvalSuiteValidator.DetermineMode(suite);
            }
            catch (EvalSuiteValidationException ex)
            {
                output.WriteError(ex.Message);
                return 1;
            }

            var runner = resolveRunner(suite.Harness);
            if (runner is null)
            {
                output.WriteError(
                    $"Suite '{suite.Name}' requests unknown harness '{suite.Harness}'. " +
                    $"Known harnesses: {string.Join(", ", HarnessRegistry.Known)}.");
                return 1;
            }

            try
            {
                var judgeFactory = CreateJudgeSessionFactory(suite, resolveProvider);
                planned.Add((suite, mode, runner, judgeFactory));
            }
            catch (EvalSuiteValidationException ex)
            {
                output.WriteError(ex.Message);
                return 1;
            }

            output.WriteVerbose($"Suite '{suite.Name}' resolved to {mode} mode on harness '{suite.Harness}'.");
        }

        // Live-workspace suites run in place and may mutate the working tree. If the tree
        // is dirty and the user hasn't already opted in with --allow-dirty, confirm once,
        // up front, before running anything.
        if (!allowDirty && planned.Exists(p => p.Mode == SuiteExecutionMode.LiveWorkspace))
        {
            var decision = await ResolveDirtyDecisionAsync(output, standardInput, cancellationToken);
            if (decision is null)
            {
                return 1;
            }

            allowDirty = decision.Value;
        }

        var options = new EvalRunOptions
        {
            // null = the process current working directory; live-workspace and commit
            // suites resolve against it, repo suites clone to a temp directory.
            WorkingDirectory = null,
            AllowDirty = allowDirty,
        };

        var hasBlockingResult = false;
        foreach (var (suite, mode, runner, judgeFactory) in planned)
        {
            output.WriteLine($"Suite: {suite.Name} ({mode})");
            var suiteOptions = options with { CreateGraderSessionAsync = judgeFactory };

            foreach (var eval in suite.Evals)
            {
                var report = await runner.RunAsync(eval, suiteOptions, cancellationToken);
                WriteReport(output, report);
                hasBlockingResult |= report.Verdict != EvalVerdict.Pass;
            }
        }

        return hasBlockingResult ? 1 : 0;
    }

    private static Func<GraderSessionOptions, CancellationToken, Task<IGraderSession>> CreateJudgeSessionFactory(
        EvalSuite suite, Func<string, IAgentProvider?> resolveProvider)
    {
        var providers = new Dictionary<string, IAgentProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var rubric in suite.Evals.SelectMany(eval => eval.Graders).OfType<RubricGrader>())
        {
            var id = rubric.Provider ?? suite.Harness;
            if (providers.ContainsKey(id)) continue;

            var provider = resolveProvider(id)
                ?? throw new EvalSuiteValidationException($"Rubric '{rubric.Name}' requests unknown judge provider '{id}'.");
            if (!provider.SupportsInspectionOnlySessions)
            {
                throw new EvalSuiteValidationException($"Judge provider '{id}' cannot enforce inspection-only workspace access.");
            }

            providers.Add(id, provider);
        }

        return (request, token) => providers[request.Provider ?? suite.Harness].CreateGraderSessionAsync(request, token);
    }

    /// <summary>
    /// Resolves whether a dirty working tree may be used for live-workspace suites.
    /// Returns <see langword="true"/>/<see langword="false"/> for the allow-dirty value to
    /// use, or <see langword="null"/> when the run should abort.
    /// </summary>
    private static async Task<bool?> ResolveDirtyDecisionAsync(
        ConsoleWriter output,
        TextReader? standardInput,
        CancellationToken cancellationToken)
    {
        var workingDirectory = Directory.GetCurrentDirectory();

        // Only prompt when we can confirm the tree is actually dirty. If the directory
        // isn't a git work tree the runner enforces that invariant and reports it.
        if (!await GitWorkspace.IsInsideWorkTreeAsync(workingDirectory, cancellationToken))
        {
            return false;
        }

        if (!await GitWorkspace.IsDirtyAsync(workingDirectory, cancellationToken))
        {
            return false;
        }

        var input = standardInput ?? Console.In;
        output.WriteLine(
            $"The working directory '{workingDirectory}' has uncommitted changes.");
        output.WriteLine(
            "Live-workspace evals run in place and may modify tracked and untracked files.");
        output.WriteLine("Proceed against the dirty tree? [y/N]");

        var response = input.ReadLine();
        if (IsAffirmative(response))
        {
            return true;
        }

        output.WriteError(
            "Aborted: the working tree has uncommitted changes. Commit or stash them, or " +
            "pass --allow-dirty to run anyway.");
        return null;
    }

    /// <summary>Determines whether a prompt response is an affirmative yes.</summary>
    private static bool IsAffirmative(string? response)
    {
        var normalized = response?.Trim();
        return string.Equals(normalized, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Writes a human-readable summary of a single eval report.</summary>
    private static void WriteReport(ConsoleWriter output, EvalReport report)
    {
        output.WriteLine($"  Eval: {report.EvalName} — {report.Status} / {report.Verdict.ToString().ToUpperInvariant()}");

        if (report.StatusDetail is not null)
        {
            output.WriteLine($"    {report.StatusDetail}");
        }

        if (report.GradeResults.Count == 0)
        {
            output.WriteLine("    (no graders)");
        }

        foreach (var grade in report.GradeResults)
        {
            var outcome = grade.Outcome.ToString().ToUpperInvariant();
            var score = grade is RubricGradeResult { Score: { } value } ? FormattableString.Invariant($" (score: {value:G})") : "";
            output.WriteLine($"    [{outcome}] {grade.Name}{score}");
            output.WriteLine($"      {grade.Detail}");

            if (grade is RubricGradeResult rubric)
            {
                foreach (var criterion in rubric.Criteria)
                {
                    output.WriteLine(FormattableString.Invariant($"      {criterion.Id}: {criterion.Score:G} (weight: {criterion.Weight:G}) — {criterion.Explanation}"));
                }
            }
        }

        var usage = report.UsageSummary;
        output.WriteLine($"    Tokens: {usage.InputTokens} in / {usage.OutputTokens} out");
    }

    /// <summary>
    /// Loads eval suites from <paramref name="path"/>, which may be either a single
    /// <c>.yaml</c>/<c>.yml</c> file or a directory containing such files.
    /// </summary>
    private static List<EvalSuite> LoadSuites(string path)
    {
        // A single file was requested.
        if (File.Exists(path))
        {
            return [EvalSuiteParser.ParseFile(path)];
        }

        // Otherwise treat the path as a directory of suite files (top-level only).
        if (!Directory.Exists(path)) throw new FileNotFoundException($"No such file or directory: {path}", path);
        var files = Directory
                    .EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                                || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.Ordinal);

        return [.. files.Select(EvalSuiteParser.ParseFile)];
    }
}
