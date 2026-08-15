namespace GtaRpAssistant.App.Shell;

public sealed class ApplicationUiState : ObservableObject
{
    private string _pipelineStatus = "Transcript → Intent → Knowledge → Router → Validator → Overlay";
    private string _appStatus = "● Инициализация";
    private bool _isPaused;
    private int _officialArticleCount;
    private int _communityArticleCount;
    private string _resourceStatus = "Ресурсы: ожидается первый замер";

    public string PipelineStatus { get => _pipelineStatus; set => Set(ref _pipelineStatus, value); }
    public string AppStatus { get => _appStatus; set => Set(ref _appStatus, value); }
    public bool IsPaused { get => _isPaused; set => Set(ref _isPaused, value); }
    public int OfficialArticleCount { get => _officialArticleCount; set { if (Set(ref _officialArticleCount, value)) Raise(nameof(TotalArticleCount)); } }
    public int CommunityArticleCount { get => _communityArticleCount; set { if (Set(ref _communityArticleCount, value)) Raise(nameof(TotalArticleCount)); } }
    public int TotalArticleCount => OfficialArticleCount + CommunityArticleCount;
    public string ResourceStatus { get => _resourceStatus; set => Set(ref _resourceStatus, value); }
}
