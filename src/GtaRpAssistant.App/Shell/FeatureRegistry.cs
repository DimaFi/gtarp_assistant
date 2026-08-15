using System.Windows;
using GtaRpAssistant.App.Features;
using GtaRpAssistant.App.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GtaRpAssistant.App.Shell;

public interface IShellFeature
{
    string Id { get; }
    string Title { get; }
    string Symbol { get; }
    int Order { get; }
    object Content { get; }
}

public sealed record ShellFeature(string Id, string Title, string Symbol, int Order, object Content) : IShellFeature;

public sealed class FeatureRegistry
{
    public FeatureRegistry(IEnumerable<IShellFeature> features)
    {
        Features = features.OrderBy(x => x.Order).ToArray();
        if (Features.Count == 0) throw new InvalidOperationException("At least one shell feature is required.");
        if (Features.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Features.Count)
            throw new InvalidOperationException("Shell feature IDs must be unique.");
    }

    public IReadOnlyList<IShellFeature> Features { get; }
}

public static class FeatureRegistration
{
    public static IServiceCollection AddFeatureModules(this IServiceCollection services)
    {
        services.AddSingleton<ApplicationUiState>();
        services.AddSingleton<SettingsWorkspace>();
        services.AddSingleton<AudioDeviceSelectionState>();
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<SettingsSaveCoordinator>();

        Add<AssistantFeatureViewModel, AssistantView>(services, "assistant", "Чат", "✦", 10);
        Add<AudioFeatureViewModel, AudioView>(services, "audio", "Аудио", "◉", 20);
        Add<ProvidersFeatureViewModel, ProvidersView>(services, "providers", "AI и модели", "AI", 30);
        Add<BehaviorFeatureViewModel, BehaviorView>(services, "behavior", "Поведение", "⌁", 40);
        Add<PrivacyFeatureViewModel, PrivacyView>(services, "privacy", "Приватность", "◇", 50);
        Add<MemoryFeatureViewModel, MemoryView>(services, "memory", "Память", "◈", 55);
        Add<KnowledgeFeatureViewModel, KnowledgeView>(services, "knowledge", "База знаний", "▤", 60);
        Add<AboutFeatureViewModel, AboutView>(services, "about", "О приложении", "i", 70);
        services.AddSingleton<FeatureRegistry>();
        return services;
    }

    private static void Add<TViewModel, TView>(
        IServiceCollection services, string id, string title, string symbol, int order)
        where TViewModel : class
        where TView : FrameworkElement
    {
        services.AddSingleton<TViewModel>();
        services.AddSingleton<TView>();
        services.AddSingleton<IShellFeature>(sp =>
        {
            var view = sp.GetRequiredService<TView>();
            view.DataContext = sp.GetRequiredService<TViewModel>();
            return new ShellFeature(id, title, symbol, order, view);
        });
    }
}
