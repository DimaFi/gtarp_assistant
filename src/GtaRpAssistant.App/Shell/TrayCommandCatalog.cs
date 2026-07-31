namespace GtaRpAssistant.App.Shell;

public enum TrayCommand
{
    Open,
    TogglePause,
    Exit,
}

public sealed record TrayCommandDefinition(TrayCommand Command, string Label);

public static class TrayCommandCatalog
{
    public static IReadOnlyList<TrayCommandDefinition> Definitions { get; } =
    [
        new(TrayCommand.Open, "Открыть"),
        new(TrayCommand.TogglePause, "Пауза"),
        new(TrayCommand.Exit, "Выход"),
    ];
}
