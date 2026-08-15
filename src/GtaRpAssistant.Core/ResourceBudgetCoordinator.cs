namespace GtaRpAssistant.Core;

public enum AssistantWorkloadKind
{
    Chat,
    Vision,
    SpeechToText,
    TextToSpeech,
    Embeddings,
    BackgroundIndexing,
}

public enum ResourcePressureLevel { Normal, Soft, Hard }

public sealed record ResourceSnapshot(
    long? TotalRamBytes,
    long? AvailableRamBytes,
    long? TotalVramBytes,
    long? AvailableVramBytes,
    double ProcessCpuPercent,
    long ProcessWorkingSetBytes,
    bool GtaRunning,
    DateTimeOffset CapturedAt)
{
    public static ResourceSnapshot Unknown { get; } = new(null, null, null, null, 0, 0, false, DateTimeOffset.MinValue);
}

public sealed record ResourceLeaseRequest(
    AssistantWorkloadKind Workload,
    LocalAiPerformanceProfile Profile,
    bool IsLocal,
    long EstimatedRamBytes = 0,
    long EstimatedVramBytes = 0);

public sealed record ResourceLeaseResult(bool Granted, string Reason, IWorkloadLease? Lease)
{
    public static ResourceLeaseResult Denied(string reason) => new(false, reason, null);
}

public interface IWorkloadLease : IDisposable, IAsyncDisposable
{
    AssistantWorkloadKind Workload { get; }
}

public interface IResourceBudgetCoordinator
{
    ResourceSnapshot Snapshot { get; }
    ResourcePressureLevel Pressure { get; }
    void Update(ResourceSnapshot snapshot);
    ValueTask<ResourceLeaseResult> TryAcquireAsync(ResourceLeaseRequest request, CancellationToken cancellationToken);
}

public interface IHardwareTelemetry
{
    ResourceSnapshot Capture(double processCpuPercent, long processWorkingSetBytes, bool gtaRunning);
}

public sealed class ResourceBudgetCoordinator : IResourceBudgetCoordinator
{
    private const long Gib = 1024L * 1024 * 1024;
    private readonly object _sync = new();
    private readonly Dictionary<AssistantWorkloadKind, int> _active = [];
    private ResourceSnapshot _snapshot = ResourceSnapshot.Unknown;
    private ResourcePressureLevel _pressure;
    private int _healthySamples;

    public ResourceSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public ResourcePressureLevel Pressure { get { lock (_sync) return _pressure; } }

    public void Update(ResourceSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
            var observed = Classify(snapshot);
            if (observed > _pressure)
            {
                _pressure = observed;
                _healthySamples = 0;
            }
            else if (observed < _pressure)
            {
                if (++_healthySamples >= 3)
                {
                    _pressure--;
                    _healthySamples = 0;
                }
            }
            else
            {
                _healthySamples = 0;
            }
        }
    }

    public ValueTask<ResourceLeaseResult> TryAcquireAsync(ResourceLeaseRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var denial = GetDenialReason(request);
            if (denial is not null) return ValueTask.FromResult(ResourceLeaseResult.Denied(denial));
            _active[request.Workload] = Active(request.Workload) + 1;
            IWorkloadLease lease = new Lease(this, request.Workload);
            return ValueTask.FromResult(new ResourceLeaseResult(true, "resource_budget_available", lease));
        }
    }

    private string? GetDenialReason(ResourceLeaseRequest request)
    {
        if (!request.IsLocal) return null;
        if (_pressure == ResourcePressureLevel.Hard)
            return "local_ai_paused_hard_memory_pressure";
        if (_pressure == ResourcePressureLevel.Soft && request.Workload is AssistantWorkloadKind.Vision or AssistantWorkloadKind.Embeddings or AssistantWorkloadKind.BackgroundIndexing)
            return "optional_local_ai_paused_soft_memory_pressure";
        if (request.Workload is AssistantWorkloadKind.Embeddings or AssistantWorkloadKind.BackgroundIndexing && _snapshot.GtaRunning)
            return "background_ai_paused_while_gta_is_running";
        if (request.Profile is LocalAiPerformanceProfile.Compact or LocalAiPerformanceProfile.Balanced
            && request.Workload is AssistantWorkloadKind.Chat or AssistantWorkloadKind.Vision
            && (Active(AssistantWorkloadKind.Chat) > 0 || Active(AssistantWorkloadKind.Vision) > 0))
            return "chat_vision_mutual_exclusion";
        if (Active(request.Workload) > 0 && request.Workload is AssistantWorkloadKind.Chat or AssistantWorkloadKind.Vision or AssistantWorkloadKind.Embeddings or AssistantWorkloadKind.BackgroundIndexing)
            return "workload_already_active";
        if (_snapshot.AvailableRamBytes is long ram && request.EstimatedRamBytes > 0 && ram - request.EstimatedRamBytes < ReserveRam(_snapshot.GtaRunning))
            return "insufficient_ram_reserve";
        if (_snapshot.AvailableVramBytes is long vram && request.EstimatedVramBytes > 0
            && vram - request.EstimatedVramBytes < ReserveVram(_snapshot.GtaRunning, request.Profile))
            return "insufficient_vram_reserve";
        return null;
    }

    private static ResourcePressureLevel Classify(ResourceSnapshot snapshot)
    {
        if (snapshot.AvailableRamBytes is long available)
        {
            var ratio = snapshot.TotalRamBytes is > 0 ? (double)available / snapshot.TotalRamBytes.Value : 1;
            if (available < ReserveRam(snapshot.GtaRunning) || ratio < .06) return ResourcePressureLevel.Hard;
            if (available < (snapshot.GtaRunning ? 3 * Gib : 2 * Gib) || ratio < .15) return ResourcePressureLevel.Soft;
        }
        if (snapshot.AvailableVramBytes is long vram)
        {
            if (vram < Gib / 2) return ResourcePressureLevel.Hard;
            if (vram < Gib + Gib / 2) return ResourcePressureLevel.Soft;
        }
        return ResourcePressureLevel.Normal;
    }

    private static long ReserveRam(bool gtaRunning) => gtaRunning ? Gib + Gib / 2 : Gib;
    private static long ReserveVram(bool gtaRunning, LocalAiPerformanceProfile profile) => !gtaRunning
        ? Gib / 2
        : profile == LocalAiPerformanceProfile.Quality ? 4 * Gib : 2 * Gib + Gib / 2;
    private int Active(AssistantWorkloadKind workload) => _active.GetValueOrDefault(workload);

    private void Release(AssistantWorkloadKind workload)
    {
        lock (_sync)
        {
            var count = Active(workload);
            if (count <= 1) _active.Remove(workload);
            else _active[workload] = count - 1;
        }
    }

    private sealed class Lease(ResourceBudgetCoordinator owner, AssistantWorkloadKind workload) : IWorkloadLease
    {
        private int _disposed;
        public AssistantWorkloadKind Workload { get; } = workload;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Release(Workload);
        }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
