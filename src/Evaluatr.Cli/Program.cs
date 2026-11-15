using System.CommandLine;
using Evaluatr.Cli.Commands;

RootCommand rootCommand = new("An eval runner for the dotnet ecosystem.") { RunCommand.Build() };

var parseResult = rootCommand.Parse(args);
await parseResult.InvokeAsync();
