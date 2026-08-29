namespace Evaluatr.Cli.UnitTests;

/// <summary>
/// Creates a temporary directory populated with one or more YAML eval suite files
/// for use in tests, and deletes the directory on disposal.
/// </summary>
internal sealed class TempSuiteDirectory : IDisposable
{
    /// <summary>The path to the temporary directory.</summary>
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());

    /// <summary>
    /// Initialises a new instance, creates the directory, and writes one file per entry
    /// in <paramref name="suites"/>. Each entry is a <c>(filename, yaml)</c> pair;
    /// when <paramref name="suites"/> is omitted two minimal suites are written.
    /// </summary>
    public TempSuiteDirectory(IEnumerable<(string FileName, string Yaml)>? suites = null)
    {
        Directory.CreateDirectory(Path);

        foreach (var (fileName, yaml) in suites ?? DefaultSuites)
            File.WriteAllText(System.IO.Path.Combine(Path, fileName), yaml);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }

    /// <summary>Two minimal, fully-valid eval suites written by default.</summary>
    public static IEnumerable<(string FileName, string Yaml)> DefaultSuites =>
    [
        ("suite-a.yaml", MakeSuiteYaml("suite a", "task a")),
        ("suite-b.yaml", MakeSuiteYaml("suite b", "task b")),
    ];

    /// <summary>Builds a minimal valid YAML suite string.</summary>
    public static string MakeSuiteYaml(string suiteName, string evalName) => $"""
        name: "{suiteName}"
        harness: "copilot"
        evals:
          - name: "{evalName}"
            description: "a temporary task used in unit tests"
            repo: "test/repo"
            baseCommit: "abc123"
            prompt: "Do something."
            agent: "default"
            graders: []
        """;
}
