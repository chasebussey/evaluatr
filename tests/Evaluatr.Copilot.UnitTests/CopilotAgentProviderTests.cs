using System.Text.Json;
using Evaluatr.Core.Evals;
using Evaluatr.Core.Graders;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.AI;

namespace Evaluatr.Copilot.UnitTests;

// PermissionDecision is the experimental return type required by the SDK callback.
#pragma warning disable GHCP001
public class CopilotAgentProviderTests
{
    [Fact]
    public void UsesRestrictedDefaultsAndOnlyControlledTools()
    {
        var client = CopilotAgentProvider.BuildClientOptions("token");
        var session = CopilotAgentProvider.BuildSessionConfig(Options());

        Assert.Equal(CopilotClientMode.Empty, client.Mode);
        Assert.Equal("token", client.GitHubToken);
        Assert.False(string.IsNullOrWhiteSpace(client.BaseDirectory));
        Assert.Equal("chosen-model", session.Model);
        Assert.Equal(SystemMessageMode.Replace, session.SystemMessage!.Mode);
        Assert.Equal("trusted grading instructions", session.SystemMessage.Content);
        Assert.Equal(new[] { "evaluatr_read_file", "evaluatr_list_directory", "evaluatr_search_file" }, session.AvailableTools);
        Assert.Equal(session.AvailableTools, session.Tools!.Select(tool => tool.Name));
        Assert.False(session.EnableConfigDiscovery);
        Assert.False(session.EnableFileHooks);
        Assert.False(session.EnableSkills);
        Assert.True(session.SkipCustomInstructions);
        Assert.False(session.EnableOnDemandInstructionDiscovery);
        Assert.False(session.EnableHostGitOperations);
        Assert.False(session.EnableSessionStore);
        Assert.True(session.SkipEmbeddingRetrieval);
        Assert.False(session.InfiniteSessions!.Enabled);
        Assert.False(session.Memory!.Enabled);
        Assert.Empty(session.McpServers!);
        Assert.Empty(session.CustomAgents!);
        Assert.Empty(session.PluginDirectories!);
    }

    [Fact]
    public void OmittedModelUsesDocumentedProviderDefault()
    {
        Assert.Equal("gpt-4o", CopilotAgentProvider.BuildSessionConfig(Options() with { Model = null }).Model);
    }

    [Fact]
    public async Task HooksDenyShellWritesDelegationAndUnexpectedTools()
    {
        var config = CopilotAgentProvider.BuildSessionConfig(Options());
        foreach (var name in new[] { "bash", "powershell", "edit", "task", "web_fetch", "read_file", "unknown", "EVALUATR_READ_FILE" })
        {
            var result = await config.Hooks!.OnPreToolUse!(new PreToolUseHookInput
            {
                ToolName = name, SessionId = "judge", Timestamp = DateTimeOffset.UnixEpoch,
                WorkingDirectory = Path.GetTempPath(), ToolArgs = JsonSerializer.SerializeToElement(new { }),
            }, default!);
            Assert.Equal("deny", result!.PermissionDecision);
        }

        var allowed = await config.Hooks!.OnPreToolUse!(new PreToolUseHookInput
        {
            ToolName = "evaluatr_read_file", SessionId = "judge", Timestamp = DateTimeOffset.UnixEpoch,
            WorkingDirectory = Path.GetTempPath(), ToolArgs = JsonSerializer.SerializeToElement(new { path = "file.txt" }),
        }, default!);
        Assert.Equal("ask", allowed!.PermissionDecision);
    }

    [Fact]
    public async Task PermissionsOnlyApproveControlledCustomTools_NotNativeReadsOrManagedRequests()
    {
        var config = CopilotAgentProvider.BuildSessionConfig(Options());
        var custom = new PermissionRequestCustomTool { ToolName = "evaluatr_read_file", ToolCallId = "call-1", ToolDescription = "Read evidence" };
        var unknown = new PermissionRequestCustomTool { ToolName = "untrusted-tool", ToolCallId = "call-2", ToolDescription = "Untrusted" };
        var managed = new PermissionRequestCustomTool
        {
            ToolName = "evaluatr_read_file", ToolCallId = "call-3", ManagedApprovalRequired = true, ToolDescription = "Read evidence",
        };
        var read = new PermissionRequestRead { Path = "/outside", ToolCallId = "call-4", Intention = "Read outside" };

        Assert.IsType<PermissionDecisionApproveOnce>(await config.OnPermissionRequest!(custom, default!));
        Assert.IsType<PermissionDecisionReject>(await config.OnPermissionRequest!(unknown, default!));
        Assert.IsType<PermissionDecisionReject>(await config.OnPermissionRequest!(managed, default!));
        Assert.IsType<PermissionDecisionReject>(await config.OnPermissionRequest!(read, default!));
    }

    [Fact]
    public async Task RegisteredToolEnforcesWorkspaceBounds_AndHasNoCancellationTokenArgument()
    {
        var directory = Path.Combine(Path.GetTempPath(), "evaluatr-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "file.txt"), "evidence");
        try
        {
            var config = CopilotAgentProvider.BuildSessionConfig(Options() with { WorkingDirectory = directory });
            var read = Assert.IsAssignableFrom<AIFunction>(config.Tools!.Single(tool => tool.Name == "evaluatr_read_file"));
            Assert.DoesNotContain("cancellationToken", read.JsonSchema.ToString());

            var result = await read.InvokeAsync(new AIFunctionArguments { ["path"] = "file.txt" });
            var payload = Assert.IsType<JsonElement>(result).Deserialize<WorkspaceReadResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.Equal("evidence", payload!.Content);
            await Assert.ThrowsAsync<EvalSuiteValidationException>(() => read.InvokeAsync(
                new AIFunctionArguments { ["path"] = "../outside" }).AsTask());
            Assert.Equal("evidence", File.ReadAllText(Path.Combine(directory, "file.txt")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static GraderSessionOptions Options() => new()
    {
        Model = "chosen-model", WorkingDirectory = Path.GetTempPath(), SystemMessage = "trusted grading instructions",
    };
}
#pragma warning restore GHCP001
