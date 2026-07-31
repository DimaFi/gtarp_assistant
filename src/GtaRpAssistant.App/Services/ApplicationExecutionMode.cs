namespace GtaRpAssistant.App.Services;

public sealed record ApplicationExecutionMode(bool IsAutomation)
{
    public static ApplicationExecutionMode FromEnvironment() =>
        new(string.Equals(Environment.GetEnvironmentVariable("GTA_RP_AUTOMATION_MODE"), "1", StringComparison.Ordinal));
}
