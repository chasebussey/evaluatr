# Evaluatr

> [!WARNING]
> This project is currently in-progress, with core functionality yet unimplemented.

Evaluatr is a .NET CLI tool for running evaluation suites against agentic developer tooling (preliminary focus on GitHub Copilot). This tool is intended to help teams
understand how changes to their outer harness impact real-world performance on engineering tasks.

---

## Overview

Evaluatr loads eval suites in YAML and run them against an AI agent. It handles:

- Loading eval suites from a default directory (`.evals/`) or a specified path with `--file` or `--directory`
- Validating suite structure and execution mode at load time
- Orchestrating eval execution across three workspace modes (remote repo, local commit, live workspace)
- Running graders (assertion and rubric) against agent output and reporting results

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

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

```bash
# Run all suites in the default .evals/ directory
evaluatr run

# Run a specific file
evaluatr run --file path/to/suite.yaml

# Run from a different directory
evaluatr run --directory path/to/evals/

# Allow a dirty working tree (live workspace suites only)
evaluatr run --allow-dirty

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

Evaluatr will clone the referenced repository to a temp directory and execute the eval suite from that directory. Specify a `baseCommit` to check out a specific commit.

#### Commit

If no `repo` is specified, but `baseCommit` is, Evaluatr will create a throwaway worktree at `baseCommit`, then execute evals in that worktree.

#### Live Workspace

With neither `repo` nor `baseCommit` specified, Evaluatr runs in the current working directory. If the working tree is dirty, a warning will be printed:

```
The working directory '/path/to/repo' has uncommitted changes.
Live-workspace evals run in place and may modify tracked and untracked files.
Proceed against the dirty tree? [y/N]
```

Pass `--allow-dirty` to bypass that warning.

---

## License

MIT — see [LICENSE](LICENSE).
