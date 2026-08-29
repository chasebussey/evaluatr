namespace Evaluatr.Core.Runner;

/// <summary>
/// Neutral options bag for executing an eval. Fields are interpreted by the
/// selected harness: a Copilot runner reads <see cref="GitHubToken"/> and
/// <see cref="Model"/> as a GitHub token and Copilot model; another harness may
/// map or ignore them.
/// </summary>
public record EvalRunOptions
{
    /// <summary>
    /// Auth token, interpreted by the selected harness. A Copilot runner uses it
    /// as a GitHub token (personal access token, Actions <c>GITHUB_TOKEN</c>, or
    /// OAuth app token). When <see langword="null"/>, the harness falls back to its
    /// own default auth chain.
    /// </summary>
    public string? GitHubToken { get; init; }

    /// <summary>
    /// Model hint, interpreted by the selected harness. A Copilot runner uses it as
    /// the Copilot model (e.g. <c>gpt-4o</c>, <c>claude-sonnet-4-5</c>). When
    /// <see langword="null"/> (the default), the harness uses its own default model.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// The working directory for live-workspace evals (evals with no <c>repo</c>
    /// and no <c>baseCommit</c>). When <see langword="null"/>, the process current
    /// working directory is used. Repo evals run in a temp clone and commit evals
    /// run in a throwaway worktree, so this only affects live-workspace evals and
    /// the work-tree lookup for commit evals.
    /// </summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// When <see langword="true"/>, permits a live-workspace eval to run against a
    /// working tree with uncommitted changes. When <see langword="false"/> (the
    /// default), a dirty tree causes the runner to throw a
    /// <see cref="WorkspaceInvariantException"/>. Prompting for this decision is a
    /// CLI concern; the runner only enforces the invariant.
    /// </summary>
    public bool AllowDirty { get; init; }
}
