using System.Collections.ObjectModel;
using System.Windows.Input;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.App.Shell;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.App.Features;

public sealed record MemoryCategoryOption(UserMemoryCategory Value, string Label);

public sealed class MemoryFeatureViewModel : FeatureViewModel
{
    private readonly IUserMemoryStore _store; private readonly IAppDialogService _dialogs;
    private UserMemoryItem? _selected; private string _draft = ""; private MemoryCategoryOption _category;
    private int _detailLevel; private int _humorLevel; private int _initiativeLevel; private int _tone;
    private bool _adaptiveEnabled;

    public MemoryFeatureViewModel(ApplicationUiState ui, SettingsWorkspace workspace, IUserMemoryStore store, IAppDialogService dialogs) : base(ui, workspace)
    {
        _store = store; _dialogs = dialogs;
        Categories = [new(UserMemoryCategory.PlayStyle, "Стиль игры"), new(UserMemoryCategory.ExplainedTopic, "Уже объяснено"), new(UserMemoryCategory.FavoriteActivity, "Любимое занятие"), new(UserMemoryCategory.CommunicationPreference, "Общение"), new(UserMemoryCategory.ConfirmedFact, "Подтверждённый факт")];
        _category = Categories[0];
        SaveMemoryCommand = new RelayCommand(SaveMemory, () => Draft.Trim().Length >= 2); NewMemoryCommand = new RelayCommand(NewMemory);
        DeleteMemoryCommand = new RelayCommand(DeleteMemory, () => Selected is not null); ClearAllCommand = new RelayCommand(ClearAll, () => Memories.Count > 0);
        SavePersonalityCommand = new RelayCommand(SavePersonality); ResetPersonalityCommand = new RelayCommand(ResetPersonality);
        ClearChangesCommand = new RelayCommand(ClearChanges, () => PersonalityChanges.Count > 0); RefreshCommand = new RelayCommand(Reload); Reload();
    }

    public ObservableCollection<UserMemoryItem> Memories { get; } = [];
    public ObservableCollection<PersonalityChange> PersonalityChanges { get; } = [];
    public IReadOnlyList<MemoryCategoryOption> Categories { get; }
    public UserMemoryItem? Selected { get => _selected; set { if (!Set(ref _selected, value)) return; if (value is not null) { Draft = value.Content; Category = Categories.First(x => x.Value == value.Category); } RaiseCommands(); } }
    public string Draft { get => _draft; set { if (Set(ref _draft, value)) RaiseCommands(); } }
    public MemoryCategoryOption Category { get => _category; set => Set(ref _category, value); }
    public int DetailLevel { get => _detailLevel; set { if (Set(ref _detailLevel, Math.Clamp(value, 0, 2))) Raise(nameof(DetailLabel)); } }
    public int HumorLevel { get => _humorLevel; set { if (Set(ref _humorLevel, Math.Clamp(value, 0, 2))) Raise(nameof(HumorLabel)); } }
    public int InitiativeLevel { get => _initiativeLevel; set { if (Set(ref _initiativeLevel, Math.Clamp(value, 0, 2))) Raise(nameof(InitiativeLabel)); } }
    public int Tone { get => _tone; set { if (Set(ref _tone, Math.Clamp(value, 0, 2))) Raise(nameof(ToneLabel)); } }
    public string DetailLabel => new[] { "Кратко", "Сбалансированно", "Подробно" }[DetailLevel];
    public string HumorLabel => new[] { "Минимум", "Иногда", "Больше" }[HumorLevel];
    public string InitiativeLabel => new[] { "Только ответ", "Уместно", "Следующие шаги" }[InitiativeLevel];
    public string ToneLabel => new[] { "Нейтральный", "Мягкий", "Деловой" }[Tone];
    public bool AdaptiveEnabled { get => _adaptiveEnabled; set => Set(ref _adaptiveEnabled, value); }
    public ICommand SaveMemoryCommand { get; } public ICommand NewMemoryCommand { get; } public ICommand DeleteMemoryCommand { get; } public ICommand ClearAllCommand { get; } public ICommand SavePersonalityCommand { get; } public ICommand ResetPersonalityCommand { get; } public ICommand ClearChangesCommand { get; } public ICommand RefreshCommand { get; }

    private void Reload() { Memories.Clear(); foreach (var item in _store.List()) Memories.Add(item); PersonalityChanges.Clear(); foreach (var item in _store.ListPersonalityChanges()) PersonalityChanges.Add(item); var p = _store.GetPersonality(); DetailLevel = p.DetailLevel; HumorLevel = p.HumorLevel; InitiativeLevel = p.InitiativeLevel; Tone = p.Tone; AdaptiveEnabled = p.AdaptiveEnabled; RaiseCommands(); }
    private void SaveMemory() { Selected = _store.Upsert(Selected?.Id, Category.Value, Draft); Reload(); Ui.PipelineStatus = "User Memory сохранена локально."; }
    private void NewMemory() { Selected = null; Draft = ""; Category = Categories[0]; }
    private void DeleteMemory() { if (Selected is null || !_dialogs.Confirm("Удалить воспоминание", "Удалить выбранное воспоминание?", true)) return; _store.Delete(Selected.Id); NewMemory(); Reload(); }
    private void ClearAll() { if (!_dialogs.Confirm("Очистить User Memory", "Все воспоминания будут удалены. История чатов и Knowledge DB не изменятся.", true)) return; _store.Clear(); NewMemory(); Reload(); }
    private void SavePersonality() { _store.SavePersonality(new(DetailLevel, HumorLevel, InitiativeLevel, Tone, AdaptiveEnabled)); Ui.PipelineStatus = "Стиль сохранён. Игровые факты и grounding не изменены."; }
    private void ResetPersonality() { if (!_dialogs.Confirm("Сбросить персонализацию", "Вернуть нейтральный стиль и удалить журнал адаптации?", true)) return; _store.ResetPersonality(); Reload(); Ui.PipelineStatus = "Персонализация сброшена."; }
    private void ClearChanges() { if (!_dialogs.Confirm("Очистить журнал", "Удалить объяснения изменений стиля?")) return; _store.ClearPersonalityChanges(); Reload(); }
    private void RaiseCommands() { ((RelayCommand)SaveMemoryCommand).RaiseCanExecuteChanged(); ((RelayCommand)DeleteMemoryCommand).RaiseCanExecuteChanged(); ((RelayCommand)ClearAllCommand).RaiseCanExecuteChanged(); ((RelayCommand)ClearChangesCommand).RaiseCanExecuteChanged(); }
}
