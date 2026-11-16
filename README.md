# Evaluatr

> [!WARNING]
> Early alpha: assertion and rubric grading are implemented, but authenticated end-to-end execution and judge restrictions still need live-runtime validation.

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
- Weighted, multi-criterion rubric grading through provider-neutral judge sessions (Copilot is the first adapter)
- Console reports containing execution status, grading verdict, criterion scores/explanations, and token usage

There is no persisted or machine-readable report output.

Unit tests cover parsing, validation, assertion/rubric grading, grading policy, provider routing, adapter restrictions, tool-event collection, CLI behavior, and Git workspace handling. They do not verify a successful authenticated Copilot task/judge run or actual runtime enforcement of adapter restrictions.

---

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Git for repository cloning and workspace operations
- GitHub Copilot access and authentication available to the Copilot CLI used by the SDK

Task-execution sessions automatically approve Copilot tool-use permissions. Judge sessions instead expose only controlled workspace inspection tools. Only run trusted prompts and repositories; neither temporary checkouts nor adapter-level restrictions are an OS sandbox.

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
        passThreshold: 0.8
        criteria:
          - id: clarity
            description: "The output is clear and well-structured."
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

### Rubric Grading

Each rubric grader evaluates multiple weighted criteria in one fresh judge session:

```yaml
graders:
  - name: implementation quality
    type: rubric
    provider: copilot       # optional; defaults to the suite's harness
    model: gpt-4.1          # optional; defaults to the selected provider's judge model
    passThreshold: 0.8     # required, inclusive, between 0 and 1
    criteria:
      - id: correctness
        description: |
          The implementation satisfies the task requirements
          and handles relevant edge cases.
        weight: 3
      - id: test-coverage
        description: Tests cover expected behavior and meaningful failure cases.
        weight: 2
      - id: maintainability
        description: Code is readable and consistent with repository conventions.
        # weight defaults to 1
```

Criterion IDs must be nonblank and unique (case-sensitive); descriptions must be nonblank. Weights are positive decimals. Core accumulates weighted products exactly (avoiding tiny-weight underflow and large-total overflow), rounds the normalized result once to decimal precision, then computes the verdict. The formula is **Σ(weight × score) / Σ(weights)**; the aggregate is compared directly with `passThreshold`, without display rounding. For example, scores of `0.9` and `0.6` with weights `3` and `1` produce `0.825`.

The judge receives the original task prompt, assistant transcript, criterion descriptions, and inspection access to the resulting workspace. It returns a **0–1 score and evidence-based explanation for every criterion**, not an aggregate or pass/fail decision. Core rejects malformed JSON, extra/missing/duplicate IDs or properties, invalid scores, and blank explanations. An invalid response produces ERROR with no aggregate or partial criterion results. Known judge token usage is retained on errors.

Provider/model overrides are per rubric and independent of task execution. Currently only `copilot` is registered; its judge default is `gpt-4o`. Unknown providers and adapters unable to enforce inspection-only access are rejected before agent execution. Model errors are reported during judge creation/execution; explicit selections never trigger an application-level fallback. Embedded callers supply the provider-neutral session factory through `EvalRunOptions.CreateGraderSessionAsync`.

The Copilot adapter uses restricted SDK mode with fresh conversations, an explicit tool allowlist, and deny-by-default permission/tool hooks. Discovered configuration, repository instructions/hooks, skills, plugins, subagent tools, MCP tools, and cross-session context are not enabled for judges. The adapter retains the provider's normal auth data location (`COPILOT_HOME` or `~/.copilot`); credentials remain provider-owned.

Judges can only use these controlled tools:

- `evaluatr_read_file`: UTF-8 ranges of at most 16,000 characters, with offsets up to 1,000,000 characters.
- `evaluatr_list_directory`: at most 200 immediate entry names.
- `evaluatr_search_file`: literal, case-sensitive search in one UTF-8 file of at most 256 KiB, returning up to 100 match line numbers.

Paths and symbolic-link targets are confined to the eval workspace using the same boundary as file assertions. Judges cannot edit artifacts, run shell commands or tests, delegate to subagents, or access arbitrary external tools. This is not an OS sandbox or protection against concurrent filesystem replacement. Task/transcript/workspace content is treated as untrusted evidence, not judge instructions.

There are no automatic grading retries or response-repair calls. Copilot judge response waits time out after five minutes and report ERROR; caller cancellation propagates and stops the run. Judge response messages are limited to 128 Ki characters. Every rubric's session/client is disposed, including on invalid responses and provider errors.

**Breaking schema change:** legacy free-form `rubric:` is rejected with migration guidance. Move its prose into one or more `criteria` descriptions and explicitly choose `passThreshold`.

### Grading Policy and Reports

Assertions report **PASS**, **FAIL** (evaluated but did not match), or **ERROR** (could not evaluate), with explanations and no numeric scores. Rubric results have a separate optional score.

`required` defaults to `false`. Optional assertion failures and errors are diagnostic and do not block a passing verdict. Graders run after agent execution in this order, preserving YAML order within each group:

1. Required assertions
2. Optional assertions
3. Rubric graders

A required assertion FAIL or ERROR fails the eval and short-circuits grading. All remaining graders report SKIPPED and name the blocking assertion. The next eval still runs. Cancellation stops execution rather than becoming a grading error.

Execution status (`Completed`) is separate from the grading verdict (`PASS`, `FAIL`, or `ERROR`). A required assertion failure/error or rubric failure gives FAIL; any rubric error gives ERROR. Rubric FAIL/ERROR does not skip later rubrics. With no blocking checks, the verdict is PASS—even if optional assertions failed or there are no graders. This is not proof of task correctness.

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
