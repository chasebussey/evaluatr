namespace Evaluatr.Cli.UnitTests;

/// <summary>
/// Creates a temporary YAML eval suite file for use in tests and deletes it on disposal.
/// </summary>
internal sealed class TempSuiteFile : IDisposable
{
    /// <summary>The path to the temporary file.</summary>
    public string Path { get; } = System.IO.Path.ChangeExtension(System.IO.Path.GetTempFileName(), ".yaml");

    /// <summary>
    /// Initialises a new instance and writes <paramref name="yaml"/> (or <see cref="MinimalYaml"/>)
    /// to the temporary file.
    /// </summary>
    public TempSuiteFile(string? yaml = null) => File.WriteAllText(Path, yaml ?? MinimalYaml);

    public void Dispose()
    {
        if (File.Exists(Path))
            File.Delete(Path);
    }

    /// <summary>
    /// A minimal, fully-valid eval suite that satisfies every <c>required</c> field.
    /// </summary>
    public const string MinimalYaml = """
        name: "temp eval suite"
        harness: "copilot"
        evals:
          - name: "temp eval task"
            description: "a temporary task used in unit tests"
            repo: "test/repo"
            baseCommit: "abc123"
            prompt: "Do something."
            agent: "default"
            graders: []
        """;
}
