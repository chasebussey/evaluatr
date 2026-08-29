using System.CommandLine;

namespace Evaluatr.Cli;

public sealed class ConsoleWriter
{
    private readonly TextWriter _out;
    private readonly TextWriter _error;
    private readonly bool _verbose;

    /// <summary>
    /// Creates a writer over the streams supplied by System.CommandLine's
    /// <see cref="InvocationConfiguration"/>. These default to the console during normal
    /// invocation, and can be pointed at a <see cref="StringWriter"/> in tests without
    /// touching the process-global console streams.
    /// </summary>
    public ConsoleWriter(InvocationConfiguration configuration, bool verbose)
    {
        _out = configuration.Output;
        _error = configuration.Error;
        _verbose = verbose;
    }

    public void WriteLine(string message) => _out.WriteLine(message);
    public void WriteError(string message) => _error.WriteLine(message);
    public void WriteVerbose(string message) { if (_verbose) _out.WriteLine(message); }
}