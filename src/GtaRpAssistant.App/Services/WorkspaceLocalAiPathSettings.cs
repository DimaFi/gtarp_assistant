using GtaRpAssistant.Core;

namespace GtaRpAssistant.App.Services;

public sealed class WorkspaceLocalAiPathSettings(SettingsWorkspace workspace) : ILocalAiPathSettings
{
    public string? LmStudioCliPath => workspace.Settings.LmStudioCliPath;
    public string? LmStudioApplicationPath => workspace.Settings.LmStudioApplicationPath;
}
