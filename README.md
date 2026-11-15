# Evaluatr

> [!WARNING]
> Early alpha: deterministic assertion grading is implemented, but rubric grading is not. Rubric graders report ERROR until implemented.

Evaluatr is a .NET CLI tool for running evaluation suites against agentic developer tooling (preliminary focus on GitHub Copilot). This tool is intended to help teams
understand how changes to their outer harness impact real-world performance on engineering tasks.

---

## Overview

Evaluatr loads YAML eval suites and drives GitHub Copilot through its SDK. Currently implemented:

- Loading suites from `.evals/` or a path supplied with `--file` or `--directory` (directory loading is top-level only)
- Parsing typed eval and grader models, validating suite execution-mode consistency, and resolving the requested harness
- Sequential eval execution across three workspace modes: remote repository, local commit, and live workspace
- Copilot prompt execution, optional named-agent selection, transcript and tool-call collection, and token accounting
- Deterministic file, transcript, and tool-call assertions, with optional required-check gating
- Console reports containing execution status, grading verdict, individual outcomes, and token usage

Rubric session infrastructure exists, but rubric evaluation remains unimplemented. There is no persisted or machine-readable report output.

Unit tests cover parsing, validation, assertion grading, grading policy, tool-event collection, CLI behavior, and Git workspace handling. They do not verify a successful authenticated Copilot run.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git for repository cloning and workspace operations
- GitHub Copilot access and authentication available to the Copilot CLI used by the SDK

Copilot tool-use permission requests are automatically approved. Only run trusted prompts and repositories; a temporary clone or worktree is not a security sandbox.

### Build

```bash
dotnet restore
dotnet build
```

### Run Tests

```bash
dotnet test
```

---

## Usage

Run from source with:

```bash
dotnet run --project src/Evaluatr.Cli/Evaluatr.Cli.csproj -- --help
dotnet run --project src/Evaluatr.Cli/Evaluatr.Cli.csproj -- run
```

The CLI is also configured as a .NET tool (`Evaluatr.Cli`, version `0.0.1-alpha`). To install a local build:

```bash
dotnet pack src/Evaluatr.Cli/Evaluatr.Cli.csproj -o ./artifacts
dotnet tool install --global Evaluatr.Cli --version 0.0.1-alpha --add-source ./artifacts
```

Once installed:

```bash
# Run all suites in the default .evals/ directory
evaluatr run

# Run a specific file
evaluatr run --file path/to/suite.yaml

# Run from a different directory
evaluatr run --directory path/to/evals/

# Allow a dirty working tree (live workspace suites only)
evaluatr run --allow-dirty

# Include loading and mode-resolution diagnostics
evaluatr run --verbose

# Show help
evaluatr --help
```

---

## Eval Suite Format

Eval suites are YAML files placed in `.evals/` (or a path you specify).

```yaml
name: "some eval suite"
harness: "copilot" # copilot is the only supported harness at this time
evals:
  - name: "example task"
    description: "A short description of what this eval tests."
    repo: "owner/repo" # optional: GitHub repo slug to clone
    baseCommit: "abc1234" # optional: commit to check out
    agent: "default" # optional: named agent to select
    prompt: |
      The prompt sent to the agent.
    graders:
      - name: "file exists"
        type: "assertion"
        check: file-exists
        path: src/output.cs
        required: true
      - name: "quality check"
        type: "rubric"
        rubric: "The output should be clear and well-structured."
```

### Assertion Checks

Each assertion grader contains exactly one check:

```yaml
graders:
  - name: output exists
    type: assertion
    check: file-exists
    path: src/output.cs
    required: true

  - name: output has expected content
    type: assertion
    check: file-contains
    path: src/output.cs
    text: "public class Output"

  - name: agent explains verification
    type: assertion
    check: transcript-contains
    text: "Tests passed"

  - name: agent used the shell
    type: assertion
    check: tool-called
    tool: bash
```

- `file-exists` passes only for a file, not a directory.
- `file-contains` reads UTF-8 and matches literal, case-sensitive text. Invalid UTF-8 is an ERROR.
- `transcript-contains` matches literal, case-sensitive assistant message text, not tool output.
- `tool-called` matches the exact SDK tool name at execution start at least once, regardless of the tool's eventual outcome. Mentions in text and grader-session tool calls do not count. Use the harness's actual tool name.
- `text` must not be empty; `tool` must not be blank. Unknown checks, unknown/duplicate YAML keys, and fields irrelevant to the selected check are rejected.

File paths use `/` separators and must be workspace-relative. Absolute paths and traversal outside the workspace are rejected. Runtime checks also reject symbolic links that lead outside the workspace, including intermediate directory links. This boundary is not an OS sandbox and does not protect against another process concurrently replacing paths during inspection.

**Breaking schema change:** free-form `assertion:` is no longer supported. Replace it with `check:` and the applicable `path`, `text`, or `tool` fields; legacy definitions are rejected with a migration message.

### Grading Policy and Reports

Assertions report **PASS**, **FAIL** (evaluated but did not match), or **ERROR** (could not evaluate), with explanations and no numeric scores. Rubric results have a separate optional score.

`required` defaults to `false`. Optional assertion failures and errors are diagnostic and do not block a passing verdict. Graders run after agent execution in this order, preserving YAML order within each group:

1. Required assertions
2. Optional assertions
3. Rubric graders

A required assertion FAIL or ERROR fails the eval and short-circuits grading. All remaining graders report SKIPPED and name the blocking assertion. The next eval still runs. Cancellation stops execution rather than becoming a grading error.

Execution status (`Completed`) is separate from the grading verdict (`PASS`, `FAIL`, or `ERROR`). A required assertion failure/error or rubric failure gives FAIL; a rubric error gives ERROR. With no blocking checks, the verdict is PASS—even if optional assertions failed or there are no graders. This is not proof of task correctness. Unimplemented rubric graders currently report ERROR, never a placeholder PASS.

The CLI runs subsequent evals after grading failures/errors and exits nonzero if any eval's verdict is FAIL or ERROR. Invalid suite configuration is rejected before agent execution. Harness authentication fail-fast/circuit breaking is a follow-up, not part of grading policy.

### Execution Modes

Evaluatr infers an execution mode from the fields present across all evals in a suite. All evals in a suite must use the same mode.

| Mode                     | Condition                                   | Execution                             |
| ------------------------ | ------------------------------------------- | ------------------------------------- |
| **Repo suite**           | every eval has `repo`                       | independent clone of the repository   |
| **Commit suite**         | no `repo`; every eval task has `baseCommit` | throwaway worktree per eval task      |
| **Live workspace suite** | no `repo` and no `baseCommit`               | runs in the current working directory |

#### Repo

Each eval gets an independent temporary clone of its referenced repository, removed after the run. Specify a `baseCommit` to check out a specific commit; repo-mode evals may use different repositories and may independently omit or specify `baseCommit`.

#### Commit

If no `repo` is specified, but every eval has `baseCommit`, Evaluatr creates a throwaway detached worktree per eval at its requested commit and removes it after the run. The current working directory must be inside a Git working tree.

#### Live Workspace

With neither `repo` nor `baseCommit` specified, evals share the current working directory, which must be inside a Git working tree. Changes are not rolled back. If the working tree is dirty before execution, the CLI prompts once:

```
The working directory '/path/to/repo' has uncommitted changes.
Live-workspace evals run in place and may modify tracked and untracked files.
Proceed against the dirty tree? [y/N]
```

Pass `--allow-dirty` to skip confirmation and permit dirty-tree execution. Otherwise only `y` or `yes` proceeds. The runner also checks cleanliness before each live-workspace eval, so changes from an earlier eval can prevent a later one from running unless dirty-tree execution was allowed.

---

## License

MIT — see [LICENSE](LICENSE).
