# AGENTS.md

## Overview

Evaluatr is an early-alpha .NET 10 CLI for running YAML eval suites against agentic developer tooling, currently GitHub Copilot.

Current behavior:
- `Evaluatr.Core` provides typed suite/grader models, YAML parsing, mode validation, and Git workspace utilities.
- `Evaluatr.Cli` loads suites, resolves harnesses, handles dirty-tree confirmation, and runs evals sequentially with console reports.
- `Evaluatr.Copilot` drives SDK sessions in temporary clones, detached worktrees, or the live workspace and collects transcripts, tool names at execution start, and token usage.
- Deterministic assertion grading supports `file-exists`, `file-contains`, `transcript-contains`, and `tool-called`. Rubric evaluation is unimplemented and reports ERROR.
- Tests cover parsing, validation, assertion grading/policy, tool-event collection, CLI behavior, and Git workspace handling; successful authenticated Copilot execution is not covered.

## Repository Layout

- `src/Evaluatr.Core/`
  - Core domain models for evals and graders
  - YAML parsing via `YamlDotNet`, suite mode validation, and Git checkout/workspace helpers
- `src/Evaluatr.Cli/`
  - Console entrypoint, command handling, reporting, and harness registry
- `src/Evaluatr.Copilot/`
  - Copilot SDK execution and grader-session adapter
- `tests/Evaluatr.Core.UnitTests/`
  - Unit tests for YAML parsing, validation, assertion checks, grading policy, and Git workspace/worktree behavior
- `tests/Evaluatr.Cli.UnitTests/`
  - Unit tests for CLI behavior
- `tests/Evaluatr.Copilot.UnitTests/`
  - Runner workspace-guard and tool-event collection tests, not successful end-to-end Copilot execution
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
- YAML `type` selects the grader; assertion `check` selects a typed `AssertionCheck`.
- Parser-only wire definitions map flat YAML to domain types; unknown/duplicate keys and irrelevant check fields are rejected. Legacy `assertion:` definitions are rejected with a migration message.

### CLI
- `Program.cs` builds and invokes a `System.CommandLine` root command.
- `Commands/RunCommand.cs` owns options, path resolution, suite loading, mode/harness preflight, dirty-tree confirmation, sequential execution, and report output.
- `HarnessRegistry` maps harness ids to runners; currently only `copilot` is registered.
- The project is packaged as a .NET tool named `evaluatr` (`Evaluatr.Cli`, version `0.0.1-alpha`).

### Execution and grading
- Core's `IEvalRunner` and `EvalRunOptions` keep the runner boundary harness-neutral.
- `CopilotEvalRunner` selects a temporary repository clone, detached local worktree, or live working directory per eval.
- Copilot tool permissions are auto-approved. Temporary checkouts are not security sandboxes; prompts and repositories must be trusted.
- Core's `GradingRunner` owns stable ordering: required assertions, optional assertions, then rubrics.
- `required` defaults to false. Required FAIL/ERROR fails the eval and marks all remaining graders SKIPPED; optional FAIL/ERROR is non-blocking. Subsequent evals continue after grading failures.
- Assertions have PASS/FAIL/ERROR/SKIPPED outcomes and explanations, never scores. Only `RubricGradeResult` carries an optional score. Unimplemented rubrics return ERROR rather than a placeholder PASS.
- `EvalReport.Verdict` (`EvalVerdict`) is separate from `EvalRunStatus`. Required assertion FAIL/ERROR and rubric FAIL give Fail; rubric ERROR gives Error. Optional-only failures or no graders can still yield Pass.
- CLI exits nonzero when any eval verdict is Fail/Error; `Program.cs` propagates the command exit code.
- File checks enforce workspace-relative paths and inspect symlink targets at runtime. This does not provide race-proof containment against concurrent filesystem mutation.
- Rubric session infrastructure remains harness-owned; auth fail-fast/circuit breaking is a follow-up, not grading policy.
- Reports are console-only; there is no persisted or machine-readable report output.

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
- Add the wire mapping and field validation in `EvalSuiteParser`.
- For a new assertion check, add a typed `AssertionCheck` with configuration validation and deterministic evaluation.
- Add parser, execution, error/cancellation, and policy tests as applicable.

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
- All evals within a suite must use the same execution mode. Repo-mode evals may independently specify `baseCommit`.
- Commit and live-workspace modes require the current directory to be inside a Git working tree.
- Live-workspace evals share the working directory and do not roll back changes. Unless dirty execution is allowed, the runner checks cleanliness before each eval; an earlier eval's changes can block the next one.
- `--allow-dirty` skips the initial dirty-tree prompt and permits dirty-tree execution.
- Running evals requires Git and working Copilot access/authentication. Unit-test success alone does not validate that runtime setup.

## Good First Places To Look

- CLI entrypoint and harness resolution: `src/Evaluatr.Cli/Program.cs`, `src/Evaluatr.Cli/HarnessRegistry.cs`
- `run` command behavior: `src/Evaluatr.Cli/Commands/RunCommand.cs`
- Copilot execution: `src/Evaluatr.Copilot/CopilotEvalRunner.cs`
- Suite mode validation: `src/Evaluatr.Core/Evals/EvalSuiteValidator.cs`
- YAML parsing: `src/Evaluatr.Core/Evals/EvalSuiteParser.cs`
- Schema models: `src/Evaluatr.Core/Evals/Types.cs`, `src/Evaluatr.Core/Graders/Types.cs`
- Assertion checks and workspace boundary: `src/Evaluatr.Core/Graders/AssertionCheck.cs`, `src/Evaluatr.Core/Graders/WorkspaceFile.cs`
- Grading order and gating: `src/Evaluatr.Core/Graders/GradingRunner.cs`
