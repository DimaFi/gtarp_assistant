namespace GtaRpAssistant.Core;

public enum MicroModelState
{
    NotInstalled,
    Verifying,
    Stopped,
    Starting,
    Ready,
    Generating,
    Idle,
    Stopping,
    Faulted,
    MemoryLimitExceeded,
}

public enum MicroModelTask
{
    IntentClassification,
    ArticleReranking,
    GroundedShortAnswer,
    FollowUp,
}

public enum ResourceDecision
{
    Continue,
    StopGeneration,
    TerminateAndFallback,
}

public sealed record MicroModelTranscriptEvidence(string Id, AudioSourceKind Source, string Text);

public sealed record MicroModelRequest(
    string RequestId,
    MicroModelTask Task,
    string Question,
    string Server,
    IReadOnlyList<MicroModelTranscriptEvidence> Transcript,
    IReadOnlyList<KnowledgeFact> VerifiedFacts,
    string? SituationId = null);

public sealed record MicroModelResponse(string Json);

public sealed record MicroModelStatus(
    MicroModelState State,
    int? ProcessId,
    string Message,
    DateTimeOffset UpdatedAt);

public sealed record MicroModelProcessMetrics(
    long WorkingSetBytes,
    long PrivateBytes,
    long CommittedBytes,
    double CpuPercent);

public sealed record MicroModelGenerationConfig
{
    public int ContextTokens { get; init; } = 512;
    public int MaxOutputTokens { get; init; } = 120;
    public int CpuThreads { get; init; } = 1;
    public int GpuLayers { get; init; }
}

public sealed record MicroModelPackageManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Runtime { get; init; }
    public required string ModelFile { get; init; }
    public required string ModelSha256 { get; init; }
    public required string LicenseFile { get; init; }
    public required string PromptTemplateFile { get; init; }
    public MicroModelGenerationConfig Generation { get; init; } = new();
}

public sealed record MicroModelPipeRequest(string RequestId, string Command, MicroModelRequest? Request = null);
public sealed record MicroModelPipeResponse(string RequestId, bool Success, MicroModelState State, MicroModelResponse? Response = null, string? Error = null);

public sealed class MicroModelStateChangedEventArgs(MicroModelStatus status) : EventArgs
{
    public MicroModelStatus Status { get; } = status;
}

public interface IMicroModelResourceGuard
{
    ResourceDecision Evaluate(MicroModelProcessMetrics metrics);
}

public interface IMicroModelManager : IAsyncDisposable
{
    event EventHandler<MicroModelStateChangedEventArgs>? StateChanged;
    Task<MicroModelStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task StartAsync(CancellationToken cancellationToken);
    Task<MicroModelResponse> GenerateAsync(MicroModelRequest request, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task VerifyPackageAsync(CancellationToken cancellationToken);
}

public sealed class MicroModelResourceGuard(
    long softLimitBytes = 750L * 1024 * 1024,
    long hardLimitBytes = 900L * 1024 * 1024,
    long absoluteLimitBytes = 1024L * 1024 * 1024) : IMicroModelResourceGuard
{
    public ResourceDecision Evaluate(MicroModelProcessMetrics metrics)
    {
        var maximum = Math.Max(metrics.WorkingSetBytes, Math.Max(metrics.PrivateBytes, metrics.CommittedBytes));
        if (maximum >= absoluteLimitBytes || maximum >= hardLimitBytes) return ResourceDecision.TerminateAndFallback;
        if (maximum >= softLimitBytes) return ResourceDecision.StopGeneration;
        return ResourceDecision.Continue;
    }
}
