using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GtaRpAssistant.Core;

public sealed class AssistantSessionCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan ValidatedAnswerCacheTtl = TimeSpan.FromDays(7);
    private readonly TranscriptBuffer _transcripts;
    private readonly IIntentDetector _intent;
    private readonly IKnowledgeRepository _knowledge;
    private readonly IContextSelector _contextSelector;
    private readonly IAiRouter _router;
    private readonly GroundedAnswerValidator _validator;
    private readonly IChatProviderCatalog _providers;
    private readonly IOverlayService _overlay;
    private readonly ITranscriptDeduplicator _deduplicator;
    private readonly IProactivePolicy _proactive;
    private readonly ISessionEventSink _events;
    private readonly IAssistantConversationStore _conversation;
    private readonly IUserPersonalizationContextProvider? _personalization;
    private readonly IScreenContextStore? _screenContext;
    private readonly IAnswerCache? _answerCache;
    private readonly SemaphoreSlim _singleFlight = new(1, 1);
    private readonly SessionStateMachine _stateMachine = new();
    private CancellationTokenSource _lifetime = new();
    private bool _paused;

    public AssistantSessionCoordinator(
        TranscriptBuffer transcripts,
        IIntentDetector intent,
        IKnowledgeRepository knowledge,
        IContextSelector contextSelector,
        IAiRouter router,
        GroundedAnswerValidator validator,
        IChatProviderCatalog providers,
        IOverlayService overlay,
        ITranscriptDeduplicator deduplicator,
        IProactivePolicy proactive,
        ISessionEventSink events,
        IAssistantConversationStore? conversation = null,
        IUserPersonalizationContextProvider? personalization = null,
        IScreenContextStore? screenContext = null,
        IAnswerCache? answerCache = null)
    {
        _transcripts = transcripts;
        _intent = intent;
        _knowledge = knowledge;
        _contextSelector = contextSelector;
        _router = router;
        _validator = validator;
        _providers = providers;
        _overlay = overlay;
        _deduplicator = deduplicator;
        _proactive = proactive;
        _events = events;
        _conversation = conversation ?? new InMemoryAssistantConversationStore();
        _personalization = personalization;
        _screenContext = screenContext;
        _answerCache = answerCache;
        _stateMachine.StateChanged += (_, state) => _events.Write(new(DateTimeOffset.UtcNow, "Session state changed", state));
    }

    public AssistantSessionState State => _stateMachine.State;
    public event EventHandler<AssistantSessionState>? StateChanged;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<AssistantAnswer>? AnswerProduced;

    public void Start(bool gameAvailable)
    {
        if (_stateMachine.State != AssistantSessionState.Dormant) return;
        Transition(AssistantSessionState.WaitingForGame);
        if (gameAvailable) Transition(AssistantSessionState.Listening);
    }

    public void SetGameAvailable(bool available)
    {
        if (_paused) return;
        if (available && _stateMachine.State == AssistantSessionState.WaitingForGame) Transition(AssistantSessionState.Listening);
        else if (!available && _stateMachine.State == AssistantSessionState.Listening) Transition(AssistantSessionState.WaitingForGame);
    }

    public void SetPaused(bool paused)
    {
        if (_paused == paused) return;
        _paused = paused;
        if (paused)
        {
            _lifetime.Cancel();
            if (_stateMachine.State != AssistantSessionState.Dormant && _stateMachine.State != AssistantSessionState.Paused)
                Transition(AssistantSessionState.Paused);
        }
        else
        {
            _lifetime.Dispose();
            _lifetime = new();
            if (_stateMachine.State == AssistantSessionState.Paused) Transition(AssistantSessionState.WaitingForGame);
        }
    }

    public async Task<AssistantAnswer?> ProcessAsync(AssistantProcessingRequest request, CancellationToken cancellationToken)
    {
        if (_paused) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var ct = linked.Token;
        await _singleFlight.WaitAsync(ct);
        var metrics = new RequestMetricsState(request.Entry.Id, Stopwatch.GetTimestamp());
        try
        {
            var entry = request.Entry;
            var existing = _transcripts.Snapshot();
            if (entry.Source == AudioSourceKind.GameAudio && _deduplicator.IsDuplicate(entry, existing)) return null;
            if (entry.Source == AudioSourceKind.UserMicrophone)
            {
                foreach (var duplicate in existing.Where(x => x.Source == AudioSourceKind.GameAudio && _deduplicator.IsDuplicate(entry, [x]))) _transcripts.Remove(duplicate.Id);
            }
            _transcripts.Add(entry);
            if (entry.Source == AudioSourceKind.GameAudio)
            {
                metrics.SetRoute("context-only", "game_audio_is_not_a_user_request");
                return null;
            }
            _personalization?.ApplyExplicitFeedback(entry.Text);

            if (!_proactive.CanProcess(request.Activation, entry.Text, DateTimeOffset.UtcNow, out var policyReason))
            {
                Status($"Подсказка подавлена: {policyReason}");
                return null;
            }

            EnsureListening();
            Transition(AssistantSessionState.SpeechDetected);
            Transition(AssistantSessionState.Transcribing);
            Transition(AssistantSessionState.EvaluatingIntent);
            var context = _contextSelector.Select(_transcripts.Snapshot(), entry);
            var intent = await _intent.DetectAsync(context, ct);
            _events.Write(new(DateTimeOffset.UtcNow, "Intent detected", State, intent.IntentId ?? "none"));
            if (!intent.ShouldConsiderHint && request.Activation is AssistantActivationKind.AutomaticVoice)
            {
                Transition(AssistantSessionState.Listening);
                Status($"Intent отклонён: {intent.Reason}");
                return null;
            }

            if (AssistantQuestionPolicy.TryGetBlockReason(entry.Text, out var blockedReason))
            {
                metrics.SetRoute("policy-block", "question_policy_rejected");
                _conversation.Add(new(Guid.NewGuid(), DateTimeOffset.UtcNow, ConversationRole.User, entry.Text, null, null, [], entry.Text));
                Transition(AssistantSessionState.ValidatingAnswer);
                return await PresentAsync(Abstain(blockedReason), request, entry.Text, ct);
            }

            if (ScreenQuestionClassifier.NeedsScreenContext(entry.Text) && _screenContext?.GetFresh(DateTimeOffset.UtcNow) is { } screen)
            {
                metrics.SetRoute("screen-context", "fresh_local_screen_observation");
                _conversation.Add(new(Guid.NewGuid(), DateTimeOffset.UtcNow, ConversationRole.User, entry.Text, null, null, [], "screen-context"));
                Transition(AssistantSessionState.ValidatingAnswer);
                return await PresentAsync(ScreenContextAnswerFactory.Create(screen), request, "screen-context", ct);
            }

            var currentConversation = _conversation.GetCurrent();
            var requestType = AssistantRequestClassifier.Classify(entry.Text, currentConversation);
            var situationId = currentConversation.SituationId ?? intent.IntentId;
            var relevantConversation = _conversation.GetRelevant(new(situationId, 6));
            Transition(AssistantSessionState.SearchingKnowledge);
            var searchText = requestType == AssistantRequestType.FollowUpQuestion && relevantConversation.Turns.Count > 0
                ? $"{entry.Text} {relevantConversation.Turns.LastOrDefault(x => x.Role == ConversationRole.User)?.Text}"
                : entry.Text;
            var conversationGrounding = AssistantConversationGrounding.TryCreate(entry.Text);
            var matches = conversationGrounding is null
                ? await _knowledge.SearchAsync(new(searchText, request.Server), ct)
                : [conversationGrounding];
            if (matches.Count == 0 && requestType == AssistantRequestType.FollowUpQuestion && !string.IsNullOrWhiteSpace(relevantConversation.SituationId))
            {
                var previous = await _knowledge.GetArticleAsync(relevantConversation.SituationId, ct);
                if (previous is not null) matches = [new(previous.Id, previous.Title, .75, previous.Facts, false, false)];
            }
            _events.Write(new(DateTimeOffset.UtcNow, "Knowledge results found", State, matches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            if (matches.Count == 0)
            {
                metrics.SetRoute("knowledge-miss", "verified_knowledge_not_found");
                _conversation.Add(new(Guid.NewGuid(), DateTimeOffset.UtcNow, ConversationRole.User, entry.Text, null, null, [], entry.Text));
                Transition(AssistantSessionState.ValidatingAnswer);
                if (request.Activation == AssistantActivationKind.AutomaticVoice)
                {
                    Transition(AssistantSessionState.Listening);
                    Status("Автоматическая подсказка подавлена: нет проверенной информации.");
                    return null;
                }
                return await PresentAsync(Abstain("Knowledge results not found"), request, entry.Text, ct);
            }

            var match = matches[0];
            _conversation.Add(new(Guid.NewGuid(), DateTimeOffset.UtcNow, ConversationRole.User, entry.Text, null, null, [], match.ArticleId));
            var preflight = _router.SelectBeforeProvider(new(match.HasVerifiedPreparedAnswer, HasGrounding(match)));
            var personalization = _personalization?.Build(entry.Text);
            string? cacheKey = null;
            if (preflight.RequiresProviderAvailability && requestType == AssistantRequestType.DirectKnowledgeQuestion && _answerCache is not null)
            {
                cacheKey = AnswerCacheKeyBuilder.Create(entry.Text, request.Server, match, personalization);
                metrics.CacheLookups++;
                var cached = await _answerCache.TryGetAsync(cacheKey, ct);
                if (cached is not null)
                {
                    metrics.CacheHits++;
                    metrics.SetRoute(AnswerRoute.ResponseCache.ToString(), "validated_versioned_cache_hit");
                    Transition(AssistantSessionState.ValidatingAnswer);
                    var cachedAnswer = cached.Answer with { ProviderId = "answer-cache" };
                    Status("Router: ResponseCache; Validator: cached validated answer");
                    return await PresentAsync(cachedAnswer, request, match.ArticleId, ct);
                }
            }
            ChatProviderAvailability? availability = null;
            AnswerRoute route;
            string routeReason;
            if (preflight.RequiresProviderAvailability)
            {
                metrics.ProviderAvailabilityChecks++;
                availability = await _providers.GetAvailabilityAsync(ct);
                route = _router.Select(new(match.HasVerifiedPreparedAnswer, HasGrounding(match), availability.LocalAvailable, availability.CloudAvailable, request.UserAllowsCloud, availability.Route.Count > 0));
                routeReason = DescribeRoute(route, availability, request.UserAllowsCloud);
            }
            else
            {
                route = preflight.Route!.Value;
                routeReason = preflight.Reason;
            }
            metrics.SetRoute(route.ToString(), routeReason);
            AssistantAnswer answer;
            if (route == AnswerRoute.Deterministic)
            {
                Transition(AssistantSessionState.ValidatingAnswer);
                answer = CreateDeterministicAnswer(entry.Text, match, request);
            }
            else if (route is AnswerRoute.ConfiguredChat or AnswerRoute.LocalChat or AnswerRoute.CloudChat)
            {
                Transition(AssistantSessionState.GeneratingAnswer);
                var providers = route switch
                {
                    AnswerRoute.ConfiguredChat => availability!.Route,
                    AnswerRoute.LocalChat when availability!.Local is not null => [availability.Local],
                    AnswerRoute.CloudChat when availability!.Cloud is not null => [availability.Cloud],
                    _ => [],
                };
                if (providers.Count == 0)
                {
                    Transition(AssistantSessionState.ValidatingAnswer);
                    answer = Abstain("No configured provider is available");
                }
                else
                {
                    answer = Abstain("All configured providers failed");
                    var verifiedFacts = GroundingContextSelector.Select(entry.Text, match.Facts);
                    foreach (var provider in providers)
                    {
                        try
                        {
                            var groundedRequest = new GroundedAnswerRequest(entry.Text, verifiedFacts, request.Server, FormatContext(context), requestType, relevantConversation.Turns, personalization);
                            metrics.RecordLlmCall(groundedRequest);
                            var response = await provider.CreateGroundedAnswerAsync(groundedRequest, ct);
                            var candidate = _validator.Validate(response.Json, match, request.Server, request.VoiceEnabled);
                            if (candidate.DiagnosticReason != GroundedAnswerValidator.PassedReason)
                            {
                                _events.Write(new(DateTimeOffset.UtcNow, "Provider response rejected; repairing", State, $"{provider.Id}:{candidate.DiagnosticReason}"));
                                var repairRequest = groundedRequest with { IsRepair = true, InvalidResponse = Limit(response.Json, 2000) };
                                metrics.RepairCalls++;
                                metrics.RecordLlmCall(repairRequest);
                                var repaired = await provider.CreateGroundedAnswerAsync(repairRequest, ct);
                                candidate = _validator.Validate(repaired.Json, match, request.Server, request.VoiceEnabled);
                            }
                            if (candidate.DiagnosticReason == GroundedAnswerValidator.PassedReason)
                            {
                                answer = candidate with { ProviderId = provider.Id, ModelId = (provider as IModelIdentifiedProvider)?.ModelId };
                                break;
                            }
                            _events.Write(new(DateTimeOffset.UtcNow, "Provider response rejected", State, $"{provider.Id}:{candidate.DiagnosticReason}"));
                        }
                        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                        {
                            _events.Write(new(DateTimeOffset.UtcNow, "Provider authorization failed", State, provider.Id));
                            answer = Abstain("Provider authorization failed");
                            break;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _events.Write(new(DateTimeOffset.UtcNow, "Provider unavailable", State, $"{provider.Id}:{ex.GetType().Name}"));
                        }
                    }
                    Transition(AssistantSessionState.ValidatingAnswer);
                    if (answer.Decision == AnswerDecision.Abstain)
                        answer = CreateDeterministicAnswer(entry.Text, match, request) with { ProviderId = "knowledge-fallback" };
                }
            }
            else
            {
                Transition(AssistantSessionState.ValidatingAnswer);
                answer = Abstain("Router selected abstain");
            }

            if (cacheKey is not null && _answerCache is not null && answer.Decision == AnswerDecision.Show
                && route is AnswerRoute.ConfiguredChat or AnswerRoute.LocalChat or AnswerRoute.CloudChat
                && !string.IsNullOrWhiteSpace(answer.ProviderId) && answer.ProviderId != "knowledge-fallback")
                await _answerCache.StoreAsync(cacheKey, answer, ValidatedAnswerCacheTtl, ct);

            if (answer.Decision == AnswerDecision.Abstain) _events.Write(new(DateTimeOffset.UtcNow, "Answer rejected", State, answer.DiagnosticReason));
            Status($"Router: {route}; Validator: {answer.DiagnosticReason}");
            if (request.Activation == AssistantActivationKind.AutomaticVoice && answer.Decision != AnswerDecision.Show)
            {
                Transition(AssistantSessionState.Listening);
                Status("Автоматическая подсказка подавлена: ответ не подтверждён.");
                return null;
            }
            return await PresentAsync(answer, request, match.ArticleId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Recover();
            return null;
        }
        catch (Exception ex)
        {
            Fault(ex.GetType().Name);
            return null;
        }
        finally
        {
            var completedMetrics = metrics.Complete();
            _events.Write(new(DateTimeOffset.UtcNow, "Assistant request metrics", State, JsonSerializer.Serialize(completedMetrics)));
            _singleFlight.Release();
        }
    }

    public AssistantConversationSnapshot Conversation => _conversation.GetCurrent();
    public Guid CurrentConversationId => _conversation.CurrentConversationId;
    public IReadOnlyList<AssistantConversationInfo> Conversations => _conversation.ListConversations();
    public bool OpenConversation(Guid conversationId) => _conversation.TryOpenConversation(conversationId);
    public void RenameConversation(Guid conversationId, string title) => _conversation.RenameConversation(conversationId, title);
    public void DeleteConversation(Guid conversationId) => _conversation.DeleteConversation(conversationId);
    public void ClearContext() { _transcripts.Clear(); _conversation.Clear(); _screenContext?.Clear(); }
    public void StartNewConversation() => _conversation.StartNewConversation();

    private async Task<AssistantAnswer> PresentAsync(AssistantAnswer answer, AssistantProcessingRequest request, string topic, CancellationToken ct)
    {
        Transition(AssistantSessionState.ShowingOverlay);
        _conversation.Add(new(Guid.NewGuid(), DateTimeOffset.UtcNow, ConversationRole.Assistant, answer.Message, answer.ProviderId, answer.ModelId, answer.UsedFactIds, topic));
        AnswerProduced?.Invoke(this, answer);
        await _overlay.ShowAsync(answer, ct);
        _events.Write(new(DateTimeOffset.UtcNow, "Overlay displayed", State, answer.Decision.ToString()));
        _proactive.RecordShown(request.Activation, topic, DateTimeOffset.UtcNow);
        Transition(AssistantSessionState.Cooldown);
        Transition(AssistantSessionState.Listening);
        return answer;
    }

    private void EnsureListening()
    {
        if (_stateMachine.State == AssistantSessionState.Dormant) { Transition(AssistantSessionState.WaitingForGame); Transition(AssistantSessionState.Listening); }
        else if (_stateMachine.State == AssistantSessionState.WaitingForGame) Transition(AssistantSessionState.Listening);
        else if (_stateMachine.State == AssistantSessionState.Cooldown) Transition(AssistantSessionState.Listening);
        else if (_stateMachine.State == AssistantSessionState.Faulted) Transition(AssistantSessionState.Listening);
    }

    private void Recover()
    {
        if (_paused) return;
        if (_stateMachine.State == AssistantSessionState.Faulted) Transition(AssistantSessionState.Listening);
        else if (_stateMachine.State != AssistantSessionState.Listening && !_stateMachine.TryTransitionTo(AssistantSessionState.Listening)) Fault("recovery_transition");
    }

    private void Fault(string detail)
    {
        if (_stateMachine.State != AssistantSessionState.Faulted) _stateMachine.TryTransitionTo(AssistantSessionState.Faulted);
        _events.Write(new(DateTimeOffset.UtcNow, "Session faulted", State, detail));
        Status($"Pipeline faulted: {detail}");
        if (!_paused) _stateMachine.TryTransitionTo(AssistantSessionState.Listening);
    }

    private void Transition(AssistantSessionState state)
    {
        _stateMachine.TransitionTo(state);
        StateChanged?.Invoke(this, state);
    }

    private void Status(string value) => StatusChanged?.Invoke(this, value);
    private static bool HasGrounding(KnowledgeMatch match) => match.Facts.Any(x => x.Verified) && !match.HasConflict && !match.IsOutdated;
    private static string DescribeRoute(AnswerRoute route, ChatProviderAvailability availability, bool userAllowsCloud) => route switch
    {
        AnswerRoute.ConfiguredChat => "configured_provider_route_available",
        AnswerRoute.ResponseCache => "validated_versioned_cache_hit",
        AnswerRoute.LocalChat => "local_provider_available",
        AnswerRoute.CloudChat => "cloud_provider_available_and_allowed",
        AnswerRoute.Deterministic when availability.CloudAvailable && !userAllowsCloud => "cloud_not_allowed_grounded_fallback",
        AnswerRoute.Deterministic => "no_provider_grounded_fallback",
        AnswerRoute.Abstain => "insufficient_grounding",
        _ => "router_decision",
    };

    private sealed class RequestMetricsState(Guid requestId, long startedTimestamp)
    {
        private string _route = "unresolved";
        private string _routeReason = "request_not_routed";

        public int ProviderAvailabilityChecks { get; set; }
        public int CacheLookups { get; set; }
        public int CacheHits { get; set; }
        public int LlmCalls { get; private set; }
        public int RepairCalls { get; set; }
        public int EstimatedInputTokens { get; private set; }
        public int EstimatedOutputBudgetTokens { get; private set; }

        public void SetRoute(string route, string reason)
        {
            _route = route;
            _routeReason = reason;
        }

        public void RecordLlmCall(GroundedAnswerRequest request)
        {
            LlmCalls++;
            EstimatedInputTokens += AssistantTokenEstimator.EstimateInput(request);
            EstimatedOutputBudgetTokens += AssistantTokenEstimator.EstimateOutputBudget(request.RequestType);
        }

        public AssistantRequestMetrics Complete() => new(
            requestId,
            _route,
            _routeReason,
            ProviderAvailabilityChecks,
            CacheLookups,
            CacheHits,
            LlmCalls,
            RepairCalls,
            EstimatedInputTokens,
            EstimatedOutputBudgetTokens,
            LlmCalls == 0,
            Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds);
    }

    private AssistantAnswer CreateDeterministicAnswer(string question, KnowledgeMatch match, AssistantProcessingRequest request)
    {
        var prepared = !string.IsNullOrWhiteSpace(match.PreparedAnswer);
        var selectedFacts = prepared
            ? match.Facts.Where(x => x.Verified).ToArray()
            : GroundingContextSelector.Select(question, match.Facts, maxFacts: 2, maxCharacters: 330).ToArray();
        if (selectedFacts.Length == 0)
        {
            var shortest = match.Facts.Where(x => x.Verified).OrderBy(x => x.Text.Length).FirstOrDefault();
            if (shortest is null) return Abstain("No verified facts available for deterministic answer");
            selectedFacts = [shortest];
        }

        var message = prepared
            ? match.PreparedAnswer!
            : string.Join(Environment.NewLine, selectedFacts.Select(x => x.Text));
        message = FitGroundedMessage(message, 340);
        var json = JsonSerializer.Serialize(new
        {
            decision = "show",
            title = match.Title,
            message,
            usedFactIds = selectedFacts.Select(x => x.Id),
            needsScreen = false,
            canSpeak = true,
        });
        return _validator.Validate(json, match, request.Server, request.VoiceEnabled) with
        {
            ProviderId = prepared ? "prepared-answer" : "knowledge-extractive",
        };
    }

    private static string FitGroundedMessage(string message, int maxLength)
    {
        if (message.Length <= maxLength) return message;
        var boundary = message.LastIndexOf(" ", maxLength - 1, maxLength, StringComparison.Ordinal);
        if (boundary < maxLength / 2) boundary = maxLength;
        return message[..boundary].TrimEnd(' ', ',', ';', ':', '-', '–') + "…";
    }
    private static string FormatContext(TranscriptContext context)
    {
        var result = new StringBuilder();
        foreach (var entry in context.Entries)
        {
            var source = entry.Source == AudioSourceKind.UserMicrophone ? "USER_MIC" : "GAME_AUDIO";
            result.Append('[').Append(source).Append(' ').Append(entry.StartedAt.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)).Append("] ").AppendLine(entry.Text);
        }
        return result.ToString();
    }

    private static object AbstainPayload() => new { decision = "abstain", title = GroundedAnswerValidator.SafeAbstainTitle, message = GroundedAnswerValidator.SafeAbstainMessage, usedFactIds = Array.Empty<string>(), needsScreen = false, canSpeak = false };
    private static AssistantAnswer Abstain(string reason) => new(AnswerDecision.Abstain, GroundedAnswerValidator.SafeAbstainTitle, GroundedAnswerValidator.SafeAbstainMessage, [], null, null, false, reason);
    private static string Limit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _singleFlight.WaitAsync();
        _singleFlight.Release();
        _lifetime.Dispose();
        _singleFlight.Dispose();
    }
}
