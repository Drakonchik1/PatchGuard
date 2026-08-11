using Microsoft.SemanticKernel;
using PatchGuard.Services.Ai.Tools;

namespace PatchGuard.Services.Ai;

/// <summary>
/// Builds a Semantic Kernel instance that hosts read-only council tools only.
/// Chat completion stays on <see cref="IChatCompletionProvider"/> — no SK cloud connector.
/// </summary>
public sealed class SemanticKernelToolHost
{
    private readonly CouncilReadOnlyTools _tools;
    private readonly Kernel _kernel;

    public SemanticKernelToolHost(CouncilReadOnlyTools tools)
    {
        _tools = tools;
        _kernel = Kernel.CreateBuilder().Build();
        _kernel.Plugins.AddFromObject(tools, CouncilReadOnlyTools.PluginName);
    }

    public CouncilReadOnlyTools Tools => _tools;

    public Kernel Kernel => _kernel;

    public async Task<string> InvokeAsync(
        string functionName,
        KernelArguments? arguments = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _kernel.InvokeAsync(
            CouncilReadOnlyTools.PluginName,
            functionName,
            arguments ?? [],
            cancellationToken);
        return result.GetValue<string>() ?? string.Empty;
    }
}
