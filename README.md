# Evaluatr

> [!WARNING]
> Early alpha: agent execution is wired up, but assertion and rubric grading are not implemented. Both graders currently return placeholder PASS results; these do not establish task success.

Evaluatr is a .NET CLI tool for running evaluation suites against agentic developer tooling (preliminary focus on GitHub Copilot). This tool is intended to help teams
understand how changes to their outer harness impact real-world performance on engineering tasks.

---

## Overview

Evaluatr loads YAML eval suites and drives GitHub Copilot through its SDK. Currently implemented:

- Loading suites from `.evals/` or a path supplied with `--file` or `--directory` (directory loading is top-level only)
- Parsing typed eval and grader models, validating suite execution-mode consistency, and resolving the requested harness
- Sequential eval execution across three workspace modes: remote repository, local commit, and live workspace
- Copilot prompt execution, optional named-agent selection, transcript collection, and token accounting
- Console reports containing execution status, placeholder grades, and token usage

Assertion and rubric grader models and orchestration exist, including a session abstraction for isolated rubric grading, but neither grader evaluates results yet. There is no persisted or machine-readable report output.

Unit tests cover parsing, validation, CLI behavior, and Git workspace handling. They do not verify a successful authenticated Copilot run or meaningful grading.

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
        assertion: "The expected file is present."
      - name: "quality check"
        type: "rubric"
        rubric: "The output should be clear and well-structured."
```

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
