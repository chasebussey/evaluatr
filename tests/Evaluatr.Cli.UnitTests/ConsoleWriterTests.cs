using System.CommandLine;
using AwesomeAssertions;

namespace Evaluatr.Cli.UnitTests;

public class ConsoleWriterTests
{
    private static (ConsoleWriter Writer, StringWriter Out, StringWriter Error) Create(bool verbose)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var config = new InvocationConfiguration { Output = stdout, Error = stderr };
        return (new ConsoleWriter(config, verbose), stdout, stderr);
    }

    [Fact]
    public void WriteLine_AlwaysWritesToOutput()
    {
        var (writer, stdout, stderr) = Create(verbose: false);

        writer.WriteLine("hello");

        stdout.ToString().Should().Contain("hello");
        stderr.ToString().Should().BeEmpty();
    }

    [Fact]
    public void WriteError_WritesToErrorNotOutput()
    {
        var (writer, stdout, stderr) = Create(verbose: false);

        writer.WriteError("boom");

        stderr.ToString().Should().Contain("boom");
        stdout.ToString().Should().BeEmpty();
    }

    [Fact]
    public void WriteVerbose_WhenVerbose_WritesToOutput()
    {
        var (writer, stdout, _) = Create(verbose: true);

        writer.WriteVerbose("details");

        stdout.ToString().Should().Contain("details");
    }

    [Fact]
    public void WriteVerbose_WhenNotVerbose_WritesNothing()
    {
        var (writer, stdout, _) = Create(verbose: false);

        writer.WriteVerbose("details");

        stdout.ToString().Should().BeEmpty();
    }
}
