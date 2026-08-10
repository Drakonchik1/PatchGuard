using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PatchGuard.Data.Entities;
using PatchGuard.Services.Ai;

namespace PatchGuard.ViewModels;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private readonly ICouncilEvaluationService _evaluationService;

    public SettingsViewModel(ICouncilEvaluationService evaluationService)
    {
        _evaluationService = evaluationService;
    }

    public ObservableCollection<CouncilEvaluationRecord> RecentSessions { get; } = [];

    [ObservableProperty]
    private bool _hasRecentSessions;

    public string Title => "Settings";

    public void OnNavigatedTo()
    {
        _ = LoadRecentSessionsAsync();
    }

    private async Task LoadRecentSessionsAsync()
    {
        RecentSessions.Clear();

        var records = await _evaluationService.GetRecentAsync();
        foreach (var record in records)
        {
            RecentSessions.Add(record);
        }

        HasRecentSessions = RecentSessions.Count > 0;
    }
}
