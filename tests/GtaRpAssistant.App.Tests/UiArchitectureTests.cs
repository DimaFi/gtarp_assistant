using GtaRpAssistant.App;
using GtaRpAssistant.App.DesignSystem.Controls;
using GtaRpAssistant.App.Features;
using GtaRpAssistant.App.Services;
using GtaRpAssistant.App.Shell;
using GtaRpAssistant.Core;
using System.IO;
using System.Xml.Linq;

namespace GtaRpAssistant.App.Tests;

public sealed class UiArchitectureTests
{
    [Fact]
    public void ColorThemes_ExposeTheSameSemanticTokens()
    {
        var appRoot = FindAppRoot();
        var light = ReadResourceKeys(Path.Combine(appRoot, "DesignSystem", "Tokens", "Colors.xaml"));
        var gray = ReadResourceKeys(Path.Combine(appRoot, "DesignSystem", "Tokens", "GrayColors.xaml"));

        Assert.Equal(light, gray);
    }

    [Fact]
    public void ActionButtons_HaveBehaviorAndAccessibleIdentity()
    {
        var appRoot = FindAppRoot();
        var failures = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(file => XDocument.Load(file).Descendants().Where(x => x.Name.LocalName == "Button")
                .Select(button => (file, button)))
            .Where(x => !HasAttribute(x.button, "Command", "Click", "IsCancel", "IsDefault"))
            .Select(x => $"{Path.GetRelativePath(appRoot, x.file)}: {x.button.Attribute("Content")?.Value ?? "<template>"}")
            .ToArray();

        Assert.True(failures.Length == 0, "Buttons without behavior: " + string.Join(", ", failures));
    }

    [Fact]
    public void FeaturePageComponents_ExposeStableDependencyPropertyContracts()
    {
        Assert.Equal(typeof(string), FeaturePageHeader.TitleProperty.PropertyType);
        Assert.Equal(typeof(string), FeaturePageHeader.DescriptionProperty.PropertyType);
        Assert.Equal(typeof(System.Windows.Input.ICommand), FeaturePageHeader.ActionCommandProperty.PropertyType);
        Assert.Equal(typeof(string), FeatureSection.TitleProperty.PropertyType);
        Assert.Equal(typeof(string), FeatureSection.DescriptionProperty.PropertyType);
        Assert.Equal(typeof(string), MetricCard.ValueProperty.PropertyType);
        Assert.Equal(typeof(System.Windows.Media.Brush), MetricCard.AccentBrushProperty.PropertyType);
    }

    [Fact]
    public void FeatureRegistry_OrdersModulesByExplicitOrder()
    {
        var registry = new FeatureRegistry([
            new ShellFeature("second", "Second", "2", 20, new object()),
            new ShellFeature("first", "First", "1", 10, new object()),
        ]);

        Assert.Equal(["first", "second"], registry.Features.Select(x => x.Id));
    }

    [Fact]
    public void FeatureRegistry_RejectsDuplicateIdsIgnoringCase()
    {
        Assert.Throws<InvalidOperationException>(() => new FeatureRegistry([
            new ShellFeature("audio", "Audio", "A", 10, new object()),
            new ShellFeature("AUDIO", "Duplicate", "D", 20, new object()),
        ]));
    }

    [Fact]
    public void FeatureRegistry_RequiresAtLeastOneModule() =>
        Assert.Throws<InvalidOperationException>(() => new FeatureRegistry([]));

    [Fact]
    public void MainViewModel_DependsOnlyOnShellStateRegistryAndLifecycleCoordinator()
    {
        var constructor = Assert.Single(typeof(MainViewModel).GetConstructors());
        Assert.Equal(
            [typeof(ApplicationUiState), typeof(FeatureRegistry), typeof(ApplicationLifecycleCoordinator)],
            constructor.GetParameters().Select(x => x.ParameterType));
    }

    [Theory]
    [InlineData(1, GlobalHotkeyAction.ToggleOverlay)]
    [InlineData(2, GlobalHotkeyAction.TogglePause)]
    [InlineData(3, GlobalHotkeyAction.ManualVoice)]
    [InlineData(4, GlobalHotkeyAction.ManualVision)]
    [InlineData(99, GlobalHotkeyAction.None)]
    public void GlobalHotkeyMap_UsesStableRegistrationContract(int registrationId, GlobalHotkeyAction expected) =>
        Assert.Equal(expected, GlobalHotkeyMap.FromRegistrationId(registrationId));

    [Fact]
    public void TrayCommandCatalog_HasStableUniqueOrder()
    {
        Assert.Equal(
            [TrayCommand.Open, TrayCommand.TogglePause, TrayCommand.Exit],
            TrayCommandCatalog.Definitions.Select(x => x.Command));
        Assert.Equal(TrayCommandCatalog.Definitions.Count, TrayCommandCatalog.Definitions.Select(x => x.Label).Distinct().Count());
    }

    [Theory]
    [InlineData("TopLeft", 24, 60)]
    [InlineData("TopRight", 676, 60)]
    [InlineData("BottomLeft", 24, 576)]
    [InlineData("BottomRight", 676, 576)]
    public void OverlayPlacement_UsesRequestedWorkingAreaCorner(string position, double expectedX, double expectedY)
    {
        var point = OverlayPlacement.Calculate(new(0, 0, 1100, 800), new(400, 200), position);
        Assert.Equal(expectedX, point.X);
        Assert.Equal(expectedY, point.Y);
    }

    [Fact]
    public void OverlayPlacement_ClampsOversizedOverlayInsideWorkingArea()
    {
        var point = OverlayPlacement.Calculate(new(100, 50, 300, 200), new(500, 400), "BottomRight");
        Assert.Equal(new System.Windows.Point(100, 50), point);
    }

    [Fact]
    public void OverlayPlacement_ClampsDraggedPositionInsideWorkingArea()
    {
        var point = OverlayPlacement.Clamp(new(100, 50, 900, 600), new(390, 180), new(5000, -200));

        Assert.Equal(610, point.X);
        Assert.Equal(50, point.Y);
    }

    [Fact]
    public void ApplicationUiState_RecalculatesKnowledgeTotal()
    {
        var state = new ApplicationUiState { OfficialArticleCount = 37, CommunityArticleCount = 415 };
        Assert.Equal(452, state.TotalArticleCount);
    }

    [Fact]
    public void AboutDiagnostics_RefreshesCountsAndNeverIncludesSecrets()
    {
        var ui = new ApplicationUiState();
        var workspace = new SettingsWorkspace
        {
            ApiKey = "local-secret",
            CloudApiKey = "cloud-secret",
        };
        using var viewModel = new AboutFeatureViewModel(ui, workspace);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        ui.OfficialArticleCount = 74;
        ui.CommunityArticleCount = 415;

        Assert.Equal("489", viewModel.KnowledgeCount);
        Assert.DoesNotContain('+', viewModel.DisplayVersion);
        Assert.StartsWith(viewModel.DisplayVersion, viewModel.Version, StringComparison.Ordinal);
        Assert.Contains(nameof(AboutFeatureViewModel.KnowledgeCount), changed);
        Assert.DoesNotContain("local-secret", viewModel.DiagnosticSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("cloud-secret", viewModel.DiagnosticSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayPresentation_DetectsCommunityAnswer()
    {
        var answer = new AssistantAnswer(AnswerDecision.Show, "Награда", "По данным игроков: 25 BP", [], "Community", DateTimeOffset.UtcNow, false, "test");
        var presentation = OverlayPresentationFactory.Create(answer);
        Assert.True(presentation.IsCommunity);
        Assert.Equal(OverlayTone.Success, presentation.Tone);
        Assert.Equal(OverlayActivity.Answering, presentation.Activity);
    }

    [Theory]
    [InlineData(MicroModelState.Starting, "Запуск", OverlayTone.Neutral, OverlayActivity.Thinking)]
    [InlineData(MicroModelState.Generating, "Формирование ответа", OverlayTone.Neutral, OverlayActivity.Thinking)]
    [InlineData(MicroModelState.MemoryLimitExceeded, "Лимит памяти", OverlayTone.Warning, OverlayActivity.None)]
    public void OverlayPresentation_MapsMicroModelLifecycle(
        MicroModelState state,
        string expectedStatus,
        OverlayTone expectedTone,
        OverlayActivity expectedActivity)
    {
        var presentation = OverlayPresentationFactory.Create(new MicroModelStatus(state, 123, "Состояние модели", DateTimeOffset.UtcNow));
        Assert.Equal(expectedStatus, presentation.Status);
        Assert.Equal(expectedTone, presentation.Tone);
        Assert.Equal(expectedActivity, presentation.Activity);
        Assert.Contains("MicroModel", presentation.Title);
    }

    [Fact]
    public void OverlayPresentation_CreatesPrivacySafeListeningState()
    {
        var presentation = OverlayPresentationFactory.CreateListening();

        Assert.Equal(OverlayActivity.Listening, presentation.Activity);
        Assert.Equal(OverlayTone.Neutral, presentation.Tone);
        Assert.Contains("20", presentation.Message);
        Assert.Contains("не сохраняется", presentation.Updated, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindAppRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "GtaRpAssistant.App");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate src/GtaRpAssistant.App.");
    }

    private static string[] ReadResourceKeys(string path) => XDocument.Load(path).Root!
        .Elements()
        .Select(x => x.Attributes().FirstOrDefault(a => a.Name.LocalName == "Key")?.Value)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Order(StringComparer.Ordinal)
        .ToArray()!;

    private static bool HasAttribute(XElement element, params string[] names) =>
        element.Attributes().Any(a => names.Contains(a.Name.LocalName, StringComparer.Ordinal));
}
