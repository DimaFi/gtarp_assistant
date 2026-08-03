using System.IO;
using System.Text.Json;
using GtaRpAssistant.App;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.App.Tests;

public sealed class ProviderSettingsMigrationTests
{
    [Fact]
    public void VoiceAutoSubmit_RoundTripsThroughEditor()
    {
        var original = ProviderSettingsMigration.Migrate(new AppSettings(VoiceAutoSubmit: true));

        var updated = SettingsEditor.From(original).ToSettings(null, null, original);

        Assert.True(updated.VoiceAutoSubmit);
    }

    [Fact]
    public void LegacySettings_CreateIndependentRoutesAndConnections()
    {
        var migrated = ProviderSettingsMigration.Migrate(new AppSettings(
            AllowCloud: true,
            CloudEndpoint: "https://api.example.test/v1",
            CloudModel: "cloud-chat",
            VisionEnabled: true,
            VisionModel: "vision-model",
            VoiceMode: 1));

        Assert.Equal(ProviderSettingsMigration.CurrentVersion, migrated.ProviderSettingsVersion);
        Assert.NotSame(migrated.ProviderRouting!.SpeechToText, migrated.ProviderRouting.Chat);
        Assert.Equal(ProviderSettingsMigration.LocalSttId, migrated.ProviderRouting.SpeechToText.PrimaryProviderId);
        Assert.Equal(ProviderSettingsMigration.LocalChatId, migrated.ProviderRouting.Chat.PrimaryProviderId);
        Assert.Equal([ProviderSettingsMigration.CloudChatId], migrated.ProviderRouting.Chat.FallbackProviderIds);
        Assert.Equal(ProviderSettingsMigration.LocalVisionId, migrated.ProviderRouting.Vision.PrimaryProviderId);
        Assert.Equal(ProviderSettingsMigration.WindowsTtsId, migrated.ProviderRouting.TextToSpeech.PrimaryProviderId);
        Assert.Contains(migrated.ProviderConnections!, connection => connection.Id == ProviderSettingsMigration.CloudVisionId && !connection.IsLocal);
    }

    [Fact]
    public void Editor_ChangesOneTaskModeWithoutChangingOtherRoutesOrPerformance()
    {
        var original = ProviderSettingsMigration.Migrate(new AppSettings(PerformanceProfile: (int)PerformanceProfile.LocalHybrid));
        var editor = SettingsEditor.From(original);
        editor.ChatProviderMode = (int)ProviderSelectionMode.Cloud;

        var updated = editor.ToSettings(null, null, original);

        Assert.Equal(ProviderSelectionMode.Cloud, updated.ProviderRouting!.Chat.Mode);
        Assert.Equal(original.ProviderRouting!.SpeechToText, updated.ProviderRouting.SpeechToText);
        Assert.Equal((int)PerformanceProfile.LocalHybrid, updated.PerformanceProfile);
    }

    [Fact]
    public void Editor_RefreshesLegacyConnectionWithoutDiscardingIndependentModes()
    {
        var original = ProviderSettingsMigration.Migrate(new AppSettings());
        var editor = SettingsEditor.From(original);
        editor.Endpoint = "http://127.0.0.1:8080/v1";
        editor.Model = "new-chat-model";
        editor.ChatProviderMode = (int)ProviderSelectionMode.Local;
        editor.SttProviderMode = (int)ProviderSelectionMode.Disabled;

        var updated = editor.ToSettings(null, null, original);
        var chat = Assert.Single(updated.ProviderConnections!, connection => connection.Id == ProviderSettingsMigration.LocalChatId);

        Assert.Equal("http://127.0.0.1:8080/v1", chat.BaseUri.AbsoluteUri.TrimEnd('/'));
        Assert.Equal("new-chat-model", chat.ModelId);
        Assert.Equal(ProviderSelectionMode.Local, updated.ProviderRouting!.Chat.Mode);
        Assert.Equal(ProviderSelectionMode.Disabled, updated.ProviderRouting.SpeechToText.Mode);
    }

    [Fact]
    public async Task SettingsService_LoadPersistsVersionedMigration()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gta-rp-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new AppSettings()));
            var service = new SettingsService(directory);

            await service.LoadAsync(default);

            Assert.Equal(ProviderSettingsMigration.CurrentVersion, service.Current.ProviderSettingsVersion);
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(ProviderSettingsMigration.CurrentVersion, document.RootElement.GetProperty("ProviderSettingsVersion").GetInt32());
            Assert.True(document.RootElement.TryGetProperty("ProviderRouting", out _));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SettingsService_RoundTripsNonStandardLmStudioPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gta-rp-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var expected = new AppSettings(
                LmStudioCliPath: @"D:\AI Tools\LM Studio CLI\lms.exe",
                LmStudioApplicationPath: @"E:\Portable Apps\LM Studio\LM Studio.exe");
            var writer = new SettingsService(directory);
            await writer.SaveAsync(expected, default);

            var reader = new SettingsService(directory);
            await reader.LoadAsync(default);

            Assert.Equal(expected.LmStudioCliPath, reader.Current.LmStudioCliPath);
            Assert.Equal(expected.LmStudioApplicationPath, reader.Current.LmStudioApplicationPath);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SettingsService_LongTermConversationIsOptInAndRoundTrips()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gta-rp-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            Assert.False(new AppSettings().EnableLongTermConversation);

            var writer = new SettingsService(directory);
            await writer.SaveAsync(new AppSettings(EnableLongTermConversation: true), default);
            var reader = new SettingsService(directory);
            await reader.LoadAsync(default);

            Assert.True(reader.Current.EnableLongTermConversation);
            var editor = SettingsEditor.From(reader.Current);
            Assert.True(editor.EnableLongTermConversation);
            Assert.True(editor.ToSettings(null, null, reader.Current).EnableLongTermConversation);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task SettingsService_RecoversCorruptJsonAndKeepsBackup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "gta-rp-settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "settings.json"), "{ not-json");
            var service = new SettingsService(directory);

            await service.LoadAsync(default);

            Assert.Equal(ProviderSettingsMigration.CurrentVersion, service.Current.ProviderSettingsVersion);
            Assert.Single(Directory.GetFiles(directory, "settings.invalid-*.json"));
            using var recovered = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "settings.json")));
            Assert.Equal(ProviderSettingsMigration.CurrentVersion, recovered.RootElement.GetProperty("ProviderSettingsVersion").GetInt32());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
