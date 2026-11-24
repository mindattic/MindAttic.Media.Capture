using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MindAttic.Capture.Hosting;

namespace MindAttic.Capture.Agent;

/// <summary>
/// <c>capture mcp</c>: the capture agent as a stdio MCP server, exposing preview_new_set, capture_web,
/// capture_screen, add_file, show and list_sets (see <see cref="CaptureTools"/>). Each tool is a thin wrapper over
/// the same logic the CLI commands use (<see cref="SetCommands"/>, <see cref="WebCommand"/>,
/// <see cref="ScreenCommand"/>, <c>PreviewSetStore</c>), so there is nothing to keep in sync between the two.
/// stdout is the protocol wire; every log line goes to stderr, same as the rest of this agent.
/// </summary>
internal static class McpCommand
{
    public static async Task<int> RunAsync(AgentArgs args, ICaptureLog log)
    {
        var builder = Host.CreateApplicationBuilder([]);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services
            .AddMcpServer(o => o.ServerInfo = new()
            {
                Name = "mindattic-capture",
                Version = typeof(McpCommand).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            })
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        await builder.Build().RunAsync();
        return 0;
    }
}
