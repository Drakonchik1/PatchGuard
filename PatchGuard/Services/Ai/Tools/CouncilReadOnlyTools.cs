using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using PatchGuard.Models;
using PatchGuard.Services.Hardware;

namespace PatchGuard.Services.Ai.Tools;

/// <summary>
/// Semantic Kernel plugins used by the Phase 3 council graph.
/// All functions are read-only — no optimizer, registry, or service mutations.
/// </summary>
public sealed class CouncilReadOnlyTools
{
    public const string PluginName = "CouncilTools";
    public const string QueryKnowledgeBaseName = "query_knowledge_base";
    public const string GetLocalStatusName = "get_local_status";

    private const int MaxOutputChars = 3500;
    private const int MaxFindingsInStatus = 8;

    private readonly IKnowledgeRetrievalService _knowledge;
    private readonly IHardwareMonitorService _hardware;

    private IReadOnlyList<Finding> _findings = [];

    public CouncilReadOnlyTools(
        IKnowledgeRetrievalService knowledge,
        IHardwareMonitorService hardware)
    {
        _knowledge = knowledge;
        _hardware = hardware;
    }

    /// <summary>Bind the current scan findings before invoking tools.</summary>
    public void SetFindings(IReadOnlyList<Finding> findings) =>
        _findings = findings ?? [];

    [KernelFunction(QueryKnowledgeBaseName)]
    [Description("Re-query the local PatchGuard playbook knowledge base. Does not leave the machine.")]
    public async Task<string> QueryKnowledgeBaseAsync(
        [Description("Short search query derived from scan findings.")] string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "{\"hits\":[],\"note\":\"empty query\"}";
        }

        var safeQuery = Trim(query.Trim(), 200);
        IReadOnlyList<KnowledgeHit> hits;
        try
        {
            hits = await _knowledge.RetrieveAsync(safeQuery, topK: 3, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return "{\"hits\":[],\"note\":\"retrieval failed\"}";
        }

        var payload = new
        {
            query = safeQuery,
            hits = hits.Select(hit => new
            {
                playbookId = hit.Chunk.PlaybookId,
                title = hit.Chunk.Title,
                score = Math.Round(hit.Score, 3),
                excerpt = Trim(hit.Chunk.Content, 280)
            }).ToList()
        };

        return Cap(JsonSerializer.Serialize(payload));
    }

    [KernelFunction(GetLocalStatusName)]
    [Description("Capture a safe local hardware snapshot and summarise current scan findings. Read-only.")]
    public string GetLocalStatus()
    {
        HardwareSnapshot snapshot;
        try
        {
            snapshot = _hardware.Capture();
        }
        catch
        {
            snapshot = new HardwareSnapshot
            {
                MonitorUnavailable = true,
                StatusMessage = "Hardware capture failed."
            };
        }

        var topFindings = _findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.ModuleName, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFindingsInStatus)
            .Select(f => new
            {
                module = ExternalDiagnosticSanitizer.SanitizeCategory(f.ModuleName),
                severity = f.Severity.ToString()
            })
            .ToList();

        var payload = new
        {
            capturedAt = snapshot.CapturedAt.ToString("O"),
            cpu = new
            {
                name = Trim(snapshot.CpuName, 80),
                loadPercent = snapshot.CpuLoadPercent,
                tempC = snapshot.CpuTemperatureC
            },
            gpu = new
            {
                name = Trim(snapshot.GpuName, 80),
                loadPercent = snapshot.GpuLoadPercent,
                tempC = snapshot.GpuTemperatureC
            },
            ram = new
            {
                usedGb = snapshot.RamUsedGb,
                totalGb = snapshot.RamTotalGb,
                loadPercent = snapshot.RamLoadPercent
            },
            sensorsLimited = snapshot.SensorsLimited,
            monitorUnavailable = snapshot.MonitorUnavailable,
            findings = topFindings
        };

        return Cap(JsonSerializer.Serialize(payload));
    }

    private static string Cap(string text) =>
        text.Length <= MaxOutputChars ? text : text[..MaxOutputChars] + "…";

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
