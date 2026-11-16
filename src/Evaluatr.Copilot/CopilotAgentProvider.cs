using Evaluatr.Core.Graders;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace Evaluatr.Copilot;

/// <summary>Creates fresh, inspection-only Copilot judge sessions.</summary>
public sealed class CopilotAgentProvider : IAgentProvider
{
    private static readonly string[] InspectionTools =
        ["evaluatr_read_file", "evaluatr_list_directory", "evaluatr_search_file"];
    private readonly string? _gitHubToken;

    /// <summary>Creates an adapter using the supplied token or Copilot's own auth chain.</summary>
    public CopilotAgentProvider(string? gitHubToken = null) => _gitHubToken = gitHubToken;

    /// <inheritdoc />
    public bool SupportsInspectionOnlySessions => true;

    /// <inheritdoc />
    public async Task<IGraderSession> CreateGraderSessionAsync(GraderSessionOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = new CopilotClient(BuildClientOptions(_gitHubToken));
        try
        {
            await client.StartAsync(cancellationToken);
            var session = await client.CreateSessionAsync(BuildSessionConfig(options), cancellationToken);
            return new CopilotGraderSession(session, client);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    internal static CopilotClientOptions BuildClientOptions(string? token) => new()
    {
        Mode = CopilotClientMode.Empty,
        // Keep the provider's normal data/auth location, independent of the
        // requested workspace. Disable ambient config and cross-session context.
        BaseDirectory = Environment.GetEnvironmentVariable("COPILOT_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot"),
        GitHubToken = token,
    };

    // The SDK's required permission callback returns its experimental RPC decision type.
#pragma warning disable GHCP001
    internal static SessionConfig BuildSessionConfig(GraderSessionOptions options)
    {
        var inspection = new WorkspaceInspection(options.WorkingDirectory);
        return new SessionConfig
        {
            Model = options.Model ?? "gpt-4o",
            WorkingDirectory = options.WorkingDirectory,
            SystemMessage = new SystemMessageConfig { Mode = SystemMessageMode.Replace, Content = options.SystemMessage },
            AvailableTools = InspectionTools.ToList(),
            Tools =
            [
                AIFunctionFactory.Create(inspection.ReadAsync, "evaluatr_read_file", "Read a workspace-relative UTF-8 file range; offset is zero-based characters. No writes or shell execution."),
                AIFunctionFactory.Create(inspection.List, "evaluatr_list_directory", "List immediate entry names in a workspace-relative directory; use '.' for the workspace root."),
                AIFunctionFactory.Create(inspection.SearchAsync, "evaluatr_search_file", "Find literal, case-sensitive text in one workspace-relative UTF-8 file; returns line numbers."),
            ],
            OnPermissionRequest = (request, _) => Task.FromResult(
                request is PermissionRequestCustomTool custom && IsInspectionTool(custom.ToolName) && request.ManagedApprovalRequired != true
                    ? PermissionDecision.ApproveOnce()
                    : PermissionDecision.Reject("Judge sessions may only use the controlled workspace inspection tools.")),
            Hooks = new SessionHooks
            {
                OnPreToolUse = (input, _) => Task.FromResult<PreToolUseHookOutput?>(new PreToolUseHookOutput
                {
                    // Known tools still pass through the permission callback so
                    // a managed approval requirement cannot be bypassed here.
                    PermissionDecision = IsInspectionTool(input.ToolName) ? "ask" : "deny",
                    PermissionDecisionReason = "Only controlled workspace inspection tools are permitted for judges.",
                }),
            },
            EnableConfigDiscovery = false,
            EnableFileHooks = false,
            EnableSkills = false,
            SkipCustomInstructions = true,
            EnableOnDemandInstructionDiscovery = false,
            EnableHostGitOperations = false,
            EnableSessionStore = false,
            SkipEmbeddingRetrieval = true,
            McpServers = new Dictionary<string, McpServerConfig>(),
            CustomAgents = [],
            PluginDirectories = [],
            SkillDirectories = [],
            InstructionDirectories = [],
            InfiniteSessions = new InfiniteSessionConfig { Enabled = false },
            Memory = new MemoryConfiguration { Enabled = false },
        };
    }

#pragma warning restore GHCP001

    private static bool IsInspectionTool(string name) => InspectionTools.Contains(name, StringComparer.Ordinal);
}
