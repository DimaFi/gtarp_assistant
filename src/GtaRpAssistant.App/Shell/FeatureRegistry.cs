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
    object GetContent();
}

public sealed class ShellFeature : IShellFeature
{
    private readonly Lazy<object> _content;

    public ShellFeature(string id, string title, string symbol, int order, object content)
        : this(id, title, symbol, order, () => content) { }

    public ShellFeature(string id, string title, string symbol, int order, Func<object> contentFactory)
    {
        Id = id;
        Title = title;
        Symbol = symbol;
        Order = order;
        _content = new(contentFactory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Id { get; }
    public string Title { get; }
    public string Symbol { get; }
    public int Order { get; }
    public object GetContent() => _content.Value;
}

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

        Add<AssistantFeatureViewModel, AssistantView>(services, "assistant", "Чат", "\uE8BD", 10);
        Add<AudioFeatureViewModel, AudioView>(services, "audio", "Аудио", "\uE720", 20);
        Add<ProvidersFeatureViewModel, ProvidersView>(services, "providers", "AI и модели", "\uE950", 30);
        Add<BehaviorFeatureViewModel, BehaviorView>(services, "behavior", "Поведение", "\uE713", 40);
        Add<PrivacyFeatureViewModel, PrivacyView>(services, "privacy", "Приватность", "\uEA18", 50);
        Add<MemoryFeatureViewModel, MemoryView>(services, "memory", "Память", "\uE81C", 55);
        Add<KnowledgeFeatureViewModel, KnowledgeView>(services, "knowledge", "База знаний", "\uE82D", 60);
        Add<AboutFeatureViewModel, AboutView>(services, "about", "О приложении", "\uE946", 70);
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
        services.AddSingleton<IShellFeature>(sp => new ShellFeature(id, title, symbol, order, () =>
        {
            var view = sp.GetRequiredService<TView>();
            view.DataContext = sp.GetRequiredService<TViewModel>();
            return view;
        }));
    }
}
