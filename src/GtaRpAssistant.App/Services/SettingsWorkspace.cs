namespace GtaRpAssistant.App.Services;

public sealed class SettingsWorkspace : ObservableObject
{
    private SettingsEditor _settings = new();
    private string _apiKey = string.Empty;
    private string _cloudApiKey = string.Empty;

    public SettingsEditor Settings { get => _settings; set => Set(ref _settings, value); }
    public string ApiKey { get => _apiKey; set => Set(ref _apiKey, value); }
    public string CloudApiKey { get => _cloudApiKey; set => Set(ref _cloudApiKey, value); }

    public void Apply(LoadedSettings loaded)
    {
        Settings = loaded.Editor;
        ApiKey = loaded.ApiKey;
        CloudApiKey = loaded.CloudApiKey;
    }
}
