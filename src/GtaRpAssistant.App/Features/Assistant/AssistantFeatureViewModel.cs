using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using GtaRpAssistant.Core;
using GtaRpAssistant.Infrastructure.Windows;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.App.Shell;

namespace GtaRpAssistant.App.Features;

public sealed class AssistantFeatureViewModel : FeatureViewModel
{
    private readonly TranscriptBuffer _transcripts;
    private readonly AssistantSessionCoordinator _coordinator;
    private readonly IUiDispatcher _dispatcher;
    private readonly IAppDialogService _dialogs;
    private readonly AudioSessionController _audioSession;
    private readonly AudioFeatureViewModel _audioFeature;
    private readonly OverlayService _overlay;
    private readonly VoiceInteractionCoordinator _voiceInteraction;
    private readonly RelayCommand _cancelRequestCommand;
    private readonly RelayCommand _renameConversationCommand;
    private readonly RelayCommand _deleteConversationCommand;
    private readonly RelayCommand _copyAnswerCommand;
    private readonly AsyncRelayCommand _retryQuestionCommand;
    private readonly RelayCommand _confirmVoicePreviewCommand;
    private readonly RelayCommand _cancelVoicePreviewCommand;
    private int _selectedSourceIndex;
    private string _transcriptText = "";
    private AssistantAnswer? _lastAnswer;
    private AssistantConversationInfo? _selectedConversation;
    private string _conversationTitleDraft = "";
    private CancellationTokenSource? _requestCancellation;
    private string? _lastQuestion;
    private bool _refreshingConversations;
    private bool _isBusy;
    private TimeSpan? _lastRequestDuration;

    public AssistantFeatureViewModel(
        ApplicationUiState ui,
        SettingsWorkspace workspace,
        TranscriptBuffer transcripts,
        AssistantSessionCoordinator coordinator,
        AudioSessionController audioSession,
        AudioFeatureViewModel audioFeature,
        OverlayService overlay,
        VoiceInteractionCoordinator voiceInteraction,
        SettingsSaveCoordinator save,
        IAppDialogService dialogs,
        IUiDispatcher dispatcher) : base(ui, workspace)
    {
        _transcripts = transcripts;
        _coordinator = coordinator;
        _dispatcher = dispatcher;
        _dialogs = dialogs;
        _audioSession = audioSession;
        _audioFeature = audioFeature;
        _overlay = overlay;
        _voiceInteraction = voiceInteraction;
        AddContextCommand = new RelayCommand(AddContext);
        ProcessQuestionCommand = new AsyncRelayCommand(ProcessQuestionAsync);
        StartVoiceCommand = new AsyncRelayCommand(StartVoiceAsync);
        NewConversationCommand = new RelayCommand(NewConversation, () => !IsBusy);
        _cancelRequestCommand = new RelayCommand(CancelRequest, () => IsBusy);
        _renameConversationCommand = new RelayCommand(RenameConversation, CanRenameConversation);
        _deleteConversationCommand = new RelayCommand(DeleteConversation, () => SelectedConversation is not null && !IsBusy);
        _copyAnswerCommand = new RelayCommand(CopyAnswer, HasAssistantAnswer);
        CopyMessageCommand = new RelayCommand<AssistantConversationTurn>(CopyMessage);
        _retryQuestionCommand = new AsyncRelayCommand(RetryQuestionAsync, () => !IsBusy && LastUserQuestion() is not null);
        _confirmVoicePreviewCommand = new RelayCommand(ConfirmVoicePreview, () => IsVoicePreview && !string.IsNullOrWhiteSpace(TranscriptText));
        _cancelVoicePreviewCommand = new RelayCommand(CancelVoicePreview, () => IsVoicePreview);
        CancelRequestCommand = _cancelRequestCommand;
        RenameConversationCommand = _renameConversationCommand;
        DeleteConversationCommand = _deleteConversationCommand;
        CopyAnswerCommand = _copyAnswerCommand;
        RetryQuestionCommand = _retryQuestionCommand;
        ConfirmVoicePreviewCommand = _confirmVoicePreviewCommand;
        CancelVoicePreviewCommand = _cancelVoicePreviewCommand;
        coordinator.StatusChanged += (_, status) => _dispatcher.Invoke(() => Ui.PipelineStatus = status);
        coordinator.AnswerProduced += (_, answer) => _dispatcher.Invoke(() =>
        {
            LastAnswer = answer;
            RefreshConversation();
        });
        audioSession.TranscriptRecognized += (_, text) => _dispatcher.Invoke(() => TranscriptText = text);
        voiceInteraction.StateChanged += (_, snapshot) => _dispatcher.Invoke(() =>
        {
            Raise(nameof(IsVoicePreview));
            Raise(nameof(VoicePreviewStatus));
            Raise(nameof(VoiceButtonText));
            _confirmVoicePreviewCommand.RaiseCanExecuteChanged();
            _cancelVoicePreviewCommand.RaiseCanExecuteChanged();
        });
        workspace.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(SettingsWorkspace.Settings), StringComparison.Ordinal))
                _dispatcher.Invoke(RefreshConversation);
        };
        save.SettingsSaved += (_, _) => _dispatcher.Invoke(RefreshConversation);
    }

    public int SelectedSourceIndex { get => _selectedSourceIndex; set => Set(ref _selectedSourceIndex, value); }
    public string TranscriptText
    {
        get => _transcriptText;
        set
        {
            if (Set(ref _transcriptText, value)) _confirmVoicePreviewCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsVoicePreview => _voiceInteraction.Snapshot.State == VoiceInteractionState.Preview;
    public string VoicePreviewStatus => IsVoicePreview
        ? "Проверьте распознанный текст выше. Он не будет отправлен без подтверждения."
        : string.Empty;
    public string VoiceButtonText => _voiceInteraction.Snapshot.IsActive ? "Остановить" : "Говорить";
    public string PipelineStatus => Ui.PipelineStatus;
    public AssistantAnswer? LastAnswer
    {
        get => _lastAnswer;
        private set
        {
            if (!Set(ref _lastAnswer, value)) return;
            _copyAnswerCommand.RaiseCanExecuteChanged();
        }
    }
    public SettingsEditor Settings => Workspace.Settings;
    public ObservableCollection<AssistantConversationTurn> Conversation { get; } = [];
    public ObservableCollection<AssistantConversationInfo> Conversations { get; } = [];
    public AssistantConversationInfo? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (!Set(ref _selectedConversation, value)) return;
            ConversationTitleDraft = value?.Title ?? "";
            _renameConversationCommand.RaiseCanExecuteChanged();
            _deleteConversationCommand.RaiseCanExecuteChanged();
            if (!_refreshingConversations && value is not null && value.Id != _coordinator.CurrentConversationId)
                OpenConversation(value.Id);
        }
    }
    public string ConversationTitleDraft
    {
        get => _conversationTitleDraft;
        set
        {
            if (Set(ref _conversationTitleDraft, value)) _renameConversationCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            _cancelRequestCommand.RaiseCanExecuteChanged();
            _deleteConversationCommand.RaiseCanExecuteChanged();
            _retryQuestionCommand.RaiseCanExecuteChanged();
        }
    }
    public string LastRequestDurationText => _lastRequestDuration is null ? "—" : $"{_lastRequestDuration.Value.TotalSeconds:F1} с";
    public ICommand AddContextCommand { get; }
    public ICommand ProcessQuestionCommand { get; }
    public ICommand StartVoiceCommand { get; }
    public ICommand NewConversationCommand { get; }
    public ICommand CancelRequestCommand { get; }
    public ICommand RenameConversationCommand { get; }
    public ICommand DeleteConversationCommand { get; }
    public ICommand CopyAnswerCommand { get; }
    public ICommand CopyMessageCommand { get; }
    public ICommand RetryQuestionCommand { get; }
    public ICommand ConfirmVoicePreviewCommand { get; }
    public ICommand CancelVoicePreviewCommand { get; }

    private AudioSourceKind SelectedSource => SelectedSourceIndex == 1 ? AudioSourceKind.GameAudio : AudioSourceKind.UserMicrophone;

    private void AddContext()
    {
        if (string.IsNullOrWhiteSpace(TranscriptText)) return;
        var now = DateTimeOffset.UtcNow;
        _transcripts.Add(new(Guid.NewGuid(), SelectedSource, now - TimeSpan.FromSeconds(2), now, TranscriptText.Trim(), 1));
        Ui.PipelineStatus = $"Transcript: реплика добавлена как {SelectedSource}. В памяти: {_transcripts.Snapshot().Count}.";
    }

    private async Task ProcessQuestionAsync()
    {
        if (Ui.IsPaused || IsBusy || string.IsNullOrWhiteSpace(TranscriptText)) return;
        var question = TranscriptText.Trim();
        TranscriptText = string.Empty;
        _lastQuestion = question;
        var now = DateTimeOffset.UtcNow;
        var entry = new TranscriptEntry(Guid.NewGuid(), SelectedSource, now - TimeSpan.FromSeconds(2), now, question, 1);
        _requestCancellation?.Dispose();
        _requestCancellation = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();
        IsBusy = true;
        Ui.PipelineStatus = "Ищу проверенную информацию и готовлю ответ…";
        try
        {
            await _coordinator.ProcessAsync(new(
                entry, AssistantActivationKind.ManualText, Workspace.Settings.Server.Trim(), Workspace.Settings.AllowCloud, false),
                _requestCancellation.Token);
            if (_requestCancellation.IsCancellationRequested) Ui.PipelineStatus = "Запрос отменён.";
        }
        finally
        {
            stopwatch.Stop();
            _lastRequestDuration = stopwatch.Elapsed;
            Raise(nameof(LastRequestDurationText));
            _requestCancellation.Dispose();
            _requestCancellation = null;
            IsBusy = false;
            RefreshConversation();
        }
    }

    private async Task StartVoiceAsync()
    {
        if (_voiceInteraction.Snapshot.IsActive)
        {
            _audioSession.CancelManualVoiceRequest("Голосовой вопрос остановлен пользователем.");
            await _overlay.HideAsync();
            Ui.PipelineStatus = "Голосовой вопрос остановлен.";
            return;
        }
        if (await _audioFeature.BeginManualVoiceRequestAsync(VoiceInteractionMode.Toggle))
            _ = _overlay.ShowListeningAsync(CancellationToken.None);
    }

    private void NewConversation()
    {
        if (IsBusy) return;
        _coordinator.StartNewConversation();
        LastAnswer = null;
        _lastRequestDuration = null;
        Raise(nameof(LastRequestDurationText));
        _lastQuestion = null;
        RefreshConversation();
        Ui.PipelineStatus = "Начат новый диалог. Временный audio-контекст не изменён.";
    }

    private void RefreshConversation()
    {
        Conversation.Clear();
        foreach (var turn in _coordinator.Conversation.Turns) Conversation.Add(turn);

        var currentId = _coordinator.CurrentConversationId;
        _refreshingConversations = true;
        try
        {
            Conversations.Clear();
            foreach (var conversation in _coordinator.Conversations) Conversations.Add(conversation);
            SelectedConversation = Conversations.FirstOrDefault(x => x.Id == currentId);
        }
        finally
        {
            _refreshingConversations = false;
        }
        _copyAnswerCommand.RaiseCanExecuteChanged();
        _retryQuestionCommand.RaiseCanExecuteChanged();
    }

    private void OpenConversation(Guid conversationId)
    {
        if (!_coordinator.OpenConversation(conversationId))
        {
            Ui.PipelineStatus = "Диалог больше не существует. Список обновлён.";
            RefreshConversation();
            return;
        }

        LastAnswer = null;
        _lastRequestDuration = null;
        Raise(nameof(LastRequestDurationText));
        _lastQuestion = null;
        RefreshConversation();
        Ui.PipelineStatus = "Диалог открыт.";
    }

    private bool CanRenameConversation() => SelectedConversation is not null
        && !IsBusy
        && !string.IsNullOrWhiteSpace(ConversationTitleDraft)
        && !string.Equals(SelectedConversation.Title, ConversationTitleDraft.Trim(), StringComparison.Ordinal);

    private void RenameConversation()
    {
        if (!CanRenameConversation() || SelectedConversation is null) return;
        _coordinator.RenameConversation(SelectedConversation.Id, ConversationTitleDraft);
        Ui.PipelineStatus = "Название диалога сохранено.";
        RefreshConversation();
    }

    private void DeleteConversation()
    {
        if (SelectedConversation is null || IsBusy) return;
        if (!_dialogs.Confirm(
            "Удалить диалог?",
            $"Диалог «{SelectedConversation.Title}» и все его сообщения будут удалены без возможности восстановления.",
            dangerous: true)) return;

        _coordinator.DeleteConversation(SelectedConversation.Id);
        LastAnswer = null;
        _lastRequestDuration = null;
        Raise(nameof(LastRequestDurationText));
        _lastQuestion = null;
        RefreshConversation();
        Ui.PipelineStatus = "Диалог удалён.";
    }

    private void CancelRequest()
    {
        if (!IsBusy) return;
        Ui.PipelineStatus = "Отменяю запрос…";
        _requestCancellation?.Cancel();
    }

    private void ConfirmVoicePreview()
    {
        if (!_audioSession.ConfirmManualVoiceRequest(TranscriptText)) return;
        Ui.PipelineStatus = "Голосовой вопрос подтверждён. Ищу проверенную информацию…";
    }

    private void CancelVoicePreview()
    {
        _audioSession.CancelManualVoiceRequest();
        Ui.PipelineStatus = "Голосовой вопрос отменён.";
    }

    private string? LastUserQuestion() => _lastQuestion
        ?? Conversation.LastOrDefault(x => x.Role == ConversationRole.User)?.Text;

    private async Task RetryQuestionAsync()
    {
        var question = LastUserQuestion();
        if (string.IsNullOrWhiteSpace(question) || IsBusy) return;
        TranscriptText = question;
        await ProcessQuestionAsync();
    }

    private bool HasAssistantAnswer() => LastAnswer is not null
        || Conversation.Any(x => x.Role == ConversationRole.Assistant && !string.IsNullOrWhiteSpace(x.Text));

    private void CopyAnswer()
    {
        var text = LastAnswer?.Message
            ?? Conversation.LastOrDefault(x => x.Role == ConversationRole.Assistant)?.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            System.Windows.Clipboard.SetText(text);
            Ui.PipelineStatus = "Ответ скопирован в буфер обмена.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            Ui.PipelineStatus = "Буфер обмена временно недоступен. Попробуйте ещё раз.";
        }
    }

    private void CopyMessage(AssistantConversationTurn? turn)
    {
        if (turn is null || string.IsNullOrWhiteSpace(turn.Text)) return;
        CopyText(turn.Text, "Сообщение скопировано в буфер обмена.");
    }

    private void CopyText(string text, string successStatus)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            Ui.PipelineStatus = successStatus;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            Ui.PipelineStatus = "Буфер обмена временно недоступен. Попробуйте ещё раз.";
        }
    }
}
