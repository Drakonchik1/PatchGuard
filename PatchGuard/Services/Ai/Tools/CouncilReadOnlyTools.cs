using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;
using PatchGuard.Models;
using PatchGuard.Services.Hardware;

namespace PatchGuard.Services.Ai.Tools;

/// <summary>
/// Semantic Kernel plugins used by the Phase 3 council graph.
/// All functions are read-only and stateless (safe as singleton).
/// </summary>
public sealed class CouncilReadOnlyTools
{
    public const string PluginName = "CouncilTools";
    public const string QueryKnowledgeBaseName = "query_knowledge_base";
    public const string GetLocalStatusName = "get_local_status";

    private const int MaxOutputChars = 3500;

    private readonly IKnowledgeRetrievalService _knowledge;
    private readonly IHardwareMonitorService _hardware;

    public CouncilReadOnlyTools(
        IKnowledgeRetrievalService knowledge,
        IHardwareMonitorService hardware)
    {
        _knowledge = knowledge;
        _hardware = hardware;
    }

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
    [Description("Capture aggregate local hardware metrics and summarise sanitized finding categories. Read-only. Omits device names and free-text titles.")]
    public string GetLocalStatus(
        [Description("JSON array of {module,severity} with already-sanitized module categories.")]
        string findingsSummaryJson = "[]")
    {
        HardwareSnapshot snapshot;
        try
        {
            snapshot = _hardware.Capture();
        }
        catch
        {
            snapshot = new HardwareSnapshot { MonitorUnavailable = true };
        }

        object findingsSummary;
        try
        {
            findingsSummary = JsonSerializer.Deserialize<JsonElement>(
                string.IsNullOrWhiteSpace(findingsSummaryJson) ? "[]" : findingsSummaryJson);
        }
        catch
        {
            findingsSummary = Array.Empty<object>();
        }

        // Privacy: numeric aggregates only — no CPU/GPU product names or wall-clock identity.
        var payload = new
        {
            cpu = new
            {
                loadPercent = snapshot.CpuLoadPercent,
                tempC = snapshot.CpuTemperatureC
            },
            gpu = new
            {
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
            findings = findingsSummary
        };

        return Cap(JsonSerializer.Serialize(payload));
    }

    /// <summary>Build a sanitized findings summary for <see cref="GetLocalStatus"/> (no titles/details).</summary>
    public static string BuildFindingsSummaryJson(IReadOnlyList<Finding> findings, int max = 8)
    {
        var items = findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.ModuleName, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(f => new
            {
                module = ExternalDiagnosticSanitizer.SanitizeCategory(f.ModuleName),
                severity = f.Severity.ToString()
            })
            .ToList();

        return JsonSerializer.Serialize(items);
    }

    private static string Cap(string text) =>
        text.Length <= MaxOutputChars ? text : text[..MaxOutputChars] + "…";

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "…";
}
