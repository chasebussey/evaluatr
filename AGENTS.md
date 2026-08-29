# AGENTS.md

## Overview

Evaluatr is a small .NET 10 codebase for loading eval suite definitions from YAML and exposing them through a CLI.

Current behavior:
- `Evaluatr.Core` parses eval suite YAML into typed models.
- `Evaluatr.Cli` provides the `evaluatr run` command.
- Tests cover YAML parsing and CLI behavior.

## Repository Layout

- `src/Evaluatr.Core/`
  - Core domain models for evals and graders
  - YAML parsing via `YamlDotNet`
- `src/Evaluatr.Cli/`
  - Console entrypoint and CLI command handling
- `tests/Evaluatr.Core.UnitTests/`
  - Unit tests for YAML parsing
- `tests/Evaluatr.Cli.UnitTests/`
  - Unit tests for CLI behavior
- `tests/testdata/sample.yaml`
  - Canonical sample eval suite used by tests
- `.evals/`
  - Default runtime location for eval suite files

## Common Commands

Run these from the repository root.

- Restore packages:
  - `dotnet restore`
- Build everything:
  - `dotnet build`
- Run all tests:
  - `dotnet test`
- Run the CLI help:
  - `dotnet run --project src/Evaluatr.Cli/Evaluatr.Cli.csproj -- --help`
- Run the CLI against the default `.evals` directory:
  - `dotnet run --project src/Evaluatr.Cli/Evaluatr.Cli.csproj -- run`
- Run the CLI against one file:
  - `dotnet run --project src/Evaluatr.Cli/Evaluatr.Cli.csproj -- run --file tests/testdata/sample.yaml`

## Architecture Notes

### Core models
- Eval suites live in `Evaluatr.Core.Evals`.
- Graders live in `Evaluatr.Core.Graders`.
- YAML parsing is centralized in `EvalSuiteParser`.
- Grader polymorphism is currently driven by the YAML `type` field.

### CLI
- `Program.cs` delegates to `CliApplication.RunAsync`.
- `CliApplication` owns root command routing and top-level help.
- `RunCommand` owns argument parsing, default path resolution, suite loading, and user-facing error messages.

## Working Guidelines

### When changing CLI behavior
- Preserve clear stdout/stderr separation.
- Keep help text and error messages consistent with tests.
- Add or update unit tests in `tests/Evaluatr.Cli.UnitTests/` for any user-visible CLI change.

### When changing YAML/schema behavior
- Update `EvalSuiteParser` and the affected model types together.
- Extend `tests/testdata/sample.yaml` only when it improves coverage for the supported schema.
- Add or update tests in `tests/Evaluatr.Core.UnitTests/` for parser changes.

### When adding grader types
- Add the new grader model under `src/Evaluatr.Core/Graders/`.
- Register the discriminator in `EvalSuiteParser`.
- Add parser tests proving the new type deserializes correctly.

## Coding Conventions

- Target framework is `net10.0`.
- Nullable reference types are enabled; keep nullability annotations accurate.
- Prefer small, focused types and methods.
- Keep public APIs documented with XML comments when introducing new public surface area.
- Match existing naming and namespace conventions.
- Avoid introducing new packages unless necessary.

## Testing Expectations

Before finishing a change, run:
- `dotnet test`

If the change is CLI-specific, make sure the relevant CLI command is also exercised manually with `dotnet run --project ...` when useful.

## Known Project-Specific Details

- The default eval suite directory is `.evals`.
- Supported suite file extensions are `.yaml` and `.yml`.
- Directory loading is currently top-level only; it does not recurse.
- The CLI currently lists loaded suites and eval names rather than executing evals.

## Good First Places To Look

- CLI flow: `src/Evaluatr.Cli/CliApplication.cs`
- `run` command behavior: `src/Evaluatr.Cli/RunCommand.cs`
- YAML parsing: `src/Evaluatr.Core/Evals/EvalSuiteParser.cs`
- Schema models: `src/Evaluatr.Core/Evals/Types.cs`, `src/Evaluatr.Core/Graders/Types.cs`
