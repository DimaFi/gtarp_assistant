# Smart RP Assistant — последовательный roadmap

Статус: активный план, 15 августа 2026 года. Первый срез Phase 1 реализован; каждый следующий срез выполняется отдельной задачей с измеримым пользовательским результатом.

## Принципы порядка

1. Сначала убрать лишние обращения к GPT‑5VP и научиться измерять стоимость.
2. Затем улучшить bounded context и продолжительность диалога.
3. После этого построить реальный resource control plane.
4. Улучшать retrieval до tools/web/vision, потому что локальные знания дешевле и надёжнее.
5. Vision, proactivity и web включать только после privacy, quality и performance gates.

## Phase 1 — Fast Knowledge Path & Budget Telemetry

**Прогресс.** Срезы 1A–1B завершены: `AiRouter` выполняет preflight до discovery провайдеров; verified prepared answer и недостаточный grounding не вызывают `GetAvailabilityAsync`. Введены `AiRouteDecision`, `AssistantRequestMetrics`, консервативный `AssistantTokenEstimator` и versioned answer cache. Повторный direct knowledge request получает только ранее validated answer до provider discovery; ключ автоматически меняется вместе с server/facts/personalization. По умолчанию кэш bounded in-memory, SQLite включается только вместе с opt-in постоянной историей. Product benchmark выводит route, LLM-avoidance, cache-hit, provider/model calls и token estimates. Следующий этап — Phase 2 Context Builder.

**Цель.** Типовые вопросы получают ответ из SQLite/prepared/cache без health-check, загрузки или вызова GPT‑5VP.

**Затрагивает.** `AssistantSessionCoordinator`, `DecisionServices`, `SqliteKnowledgeRepository`, `ChatProviderCatalog`, provider request telemetry, product benchmark.

**Новые компоненты.** `RouteDecision`, `IAnswerCache`, `SqliteAnswerCache`, `AssistantRequestMetrics`, `TokenEstimator`.

**Зависимости.** Текущие knowledge revisions, validator, settings/profile fingerprint.

**Риски.** Устаревший cache; слишком агрессивный extractive route; изменение production answers.

**Ожидаемая нагрузка.** Незначительная RAM/CPU, небольшой рост диска; резкое снижение model calls.

**Критерии готовности.** Exact/prepared/extractive запрос не вызывает `GetAvailabilityAsync`; cache инвалидируется revision; ≥70% knowledge eval без LLM; exact/cache p95 <50 ms; все текущие gates зелёные.

**Тесты.** Unit route matrix, fake provider call count, cache invalidation, server/profile isolation, concurrency; production pipeline benchmark с новыми counters.

**Пользователь увидит.** Мгновенные ответы на известные вопросы и диагностическую метку «Локальная база / кэш / модель».

## Phase 2 — Bounded Context Builder & Conversation State

**Прогресс.** Срезы 2A–2B завершены: `AssistantContextBuilder` централизует budgets verified facts/transcript/conversation/user memory, исключает дублирование текущего вопроса и отдаёт request-level output cap 300/450 tokens. `InMemoryAssistantSessionContextStore` детерминированно поддерживает goal/situation/open question/recent article+fact IDs и rolling summary старых обменов. Summary ограничен отдельным бюджетом и передаётся только локальному provider; cloud route его не получает. Следующий этап — Phase 3 Resource Budget Coordinator.

**Цель.** Естественные follow-up без передачи длинной истории.

**Затрагивает.** `AssistantSessionCoordinator`, `LocalAiConversation`, conversation stores, `OpenAiCompatibleChatProvider`.

**Новые компоненты.** `IContextBuilder`, `ContextPlan`, `ContextBudget`, `SessionSituationState`, `ConversationSummarizer`.

**Зависимости.** Phase 1 telemetry и token estimator.

**Риски.** Потеря важной детали при summary; summary hallucination.

**Ожидаемая нагрузка.** До нескольких KB session RAM; summary создаётся редко и предпочтительно deterministic/structured.

**Критерии готовности.** Balanced median input <1200 tokens, p95 <2000; follow-up accuracy не хуже baseline; полный журнал не сериализуется в model request.

**Тесты.** Длинные диалоги, pronoun/follow-up, смена темы, budget trimming order, restart с opt-in history.

**Пользователь увидит.** Ассистент помнит текущую задачу и не «забывает», о чём шла речь, оставаясь быстрым.

## Phase 3 — Resource Budget Coordinator

**Прогресс.** Срезы 3A–3B реализованы: единый `ResourceBudgetCoordinator` выдаёт disposable leases для Chat, Vision, STT, TTS, Embeddings и BackgroundIndexing; Windows sampler раз в пять секунд передаёт системную RAM, working set, CPU и признак запущенной GTA. Введены soft/hard pressure, трёхзамерный hysteresis, RAM/VRAM reserve checks при наличии данных и взаимное исключение локальных Chat/Vision в Compact/Balanced. Chat, manual Vision, STT, TTS и загрузка локальной модели подключены к leases; exact/FTS остаются вне control plane. UI показывает resource status и аппаратные Compact/Balanced/Quality envelopes. NVIDIA VRAM читается через bounded/cached `nvidia-smi`; AMD/Intel остаются честно unavailable. Chat/manual load/Vision получают idle TTL. Следующий этап — Phase 4 Knowledge Intelligence.

**Цель.** Гарантировать приоритет GTA и предсказуемую деградацию AI.

**Затрагивает.** `PerformanceController`, `ProcessPerformanceMonitor`, `LocalAiEngineManager`, Chat/Vision/STT coordinators, settings UI.

**Новые компоненты.** `IHardwareTelemetry`, `ResourceSnapshot`, `IWorkloadLease`, `ResourceBudgetCoordinator`, `DegradationPolicy`.

**Зависимости.** Phase 1 latency metrics; runtime model estimates.

**Риски.** Неточная GPU telemetry; oscillation; разные драйверы.

**Ожидаемая нагрузка.** Low-frequency sampling, <1% CPU target; отсутствие busy waiting.

**Критерии готовности.** Soft/hard RAM policies; GPU reserve при наличии telemetry; hysteresis; Chat/Vision mutual exclusion в Balanced; manual knowledge path всегда доступен.

**Тесты.** Synthetic pressure, GTA start/stop, lease cancellation, idle unload, telemetry unavailable, soak.

**Пользователь увидит.** Профили Compact/Balanced/Quality и понятные причины временного отключения тяжёлой функции.

## Phase 4 — Knowledge Intelligence

**Прогресс.** Срезы 4A–4B реализованы: каждый exact/alias/FTS match получает diagnostics, а неоднозначный FTS при наличии Embeddings lease может использовать зарегистрированный локальный OpenAI-compatible adapter. Вызов batch-ограничен, документы имеют bounded RAM-cache, endpoint обязан быть loopback, а сбой сохраняет исходный FTS order. `SemanticRerankPolicy` по-прежнему может переставить только существующие статьи и не меняет verified facts. Добавлен offline paraphrase dataset/gate: semantic top-1 не может регрессировать относительно lexical baseline или выбирать forbidden article. Без указанной embedding-модели baseline остаётся SQLite-only и не потребляет дополнительную память. Phase 4 завершена.

**Цель.** Повысить recall локальной базы, не превращая embeddings в обязательную нагрузку.

**Затрагивает.** Knowledge schema/migrator/loader, `SqliteKnowledgeRepository`, pack tool, Knowledge UI, benchmarks.

**Новые компоненты.** `KnowledgeDocument`, relevance diagnostics, optional `ISemanticReranker`, embedding index adapter.

**Зависимости.** Phase 3 leases для embeddings.

**Риски.** Semantic false positives; рост pack; несовместимость revisions.

**Ожидаемая нагрузка.** FTS остаётся baseline; embeddings грузятся только при low-confidence route.

**Критерии готовности.** Улучшение recall на paraphrase set без снижения precision/wrong-server; fallback работает без embedding pack.

**Тесты.** Exact/FTS regression, paraphrases, ambiguous queries, outdated/conflicts, server filters, missing/corrupt embedding index.

**Пользователь увидит.** База понимает больше естественных формулировок, сохраняя ссылки и проверенность.

## Phase 5 — Controlled Memory

**Цель.** Ассистент запоминает полезные предпочтения, но пользователь видит и контролирует каждую долгосрочную запись.

**Затрагивает.** `UserMemory`, `SqliteUserMemoryStore`, personalization provider, Memory UI, privacy docs.

**Новые компоненты.** `MemoryCandidate`, `MemorySource`, candidate extractor, deduplicator, relevance scorer, confirmation queue.

**Зависимости.** Phase 2 context plan; local-only policy.

**Риски.** Ложная память, PII, раздражающие подтверждения.

**Ожидаемая нагрузка.** Небольшая SQLite/FTS; extraction после ответа или idle, не в latency-critical path.

**Критерии готовности.** Memory-as-rule = 0; local-only transport tests; CRUD/categories/clear; top 0–3 memories в пределах 120 tokens.

**Тесты.** Candidate confirmation/rejection, duplicates, edits, deletion, cloud exclusion, prompt injection in memory.

**Пользователь увидит.** Очередь «Можно запомнить», категории, причины выбора воспоминания и раздельные размеры данных.

## Phase 6 — Tool Orchestrator

**Цель.** Выполнять ограниченные read-only задачи без превращения LLM в бесконтрольного агента.

**Затрагивает.** Coordinator, router, Core interfaces, calculator/knowledge/screen adapters, diagnostics UI.

**Новые компоненты.** `ITool`, `ToolRequest/Result`, `ToolRegistry`, `ToolPolicy`, `AssistantOrchestrator`.

**Зависимости.** Phase 1 route decision, Phase 2 context builder, Phase 3 deadlines/leases.

**Риски.** Лишние tool calls, prompt injection, зависшие операции.

**Ожидаемая нагрузка.** Максимум 1–2 tool calls для обычного запроса; strict deadlines/cancellation.

**Критерии готовности.** Tool selection ≥95% eval; invalid arguments отклоняются; no-write default; audit events без чувствительных данных.

**Тесты.** Allowlist, schema validation, timeout/cancel, malicious OCR/transcript, unnecessary-call metric.

**Пользователь увидит.** Более точные вычисления и ответы с объяснением использованного локального инструмента.

## Phase 7 — Web Freshness

**Цель.** Находить свежие обновления только когда локальной базы недостаточно или она устарела.

**Затрагивает.** Tool orchestrator, privacy settings, source model, answer citations, cache.

**Новые компоненты.** `IWebSearchTool`, freshness classifier, outbound preview/policy, web result cache.

**Зависимости.** Phase 6.

**Риски.** Утечка текста, недостоверные источники, сеть/latency.

**Ожидаемая нагрузка.** Ноль по умолчанию; best-effort network on explicit/freshness route.

**Критерии готовности.** Явный opt-in; видимый outbound query; source/date/TTL; web result не становится official knowledge автоматически.

**Тесты.** Offline fallback, consent, stale sources, cancellation, cache expiry, source rendering.

**Пользователь увидит.** Маркированный свежий ответ со ссылками либо честное сообщение, что сеть отключена.

## Phase 8 — Vision on Demand, OCR First

**Уточнённый пользовательский сценарий.** Цель похожа на Gemini-style interaction, но без постоянного дорогого видеопотока: `Ctrl+Alt+A` для голоса, `Ctrl+Alt+S` для подтверждённого снимка GTA и кнопка **Фото** для локального анализа выбранного PNG/JPEG. Вложение уже использует общий Vision preview, сначала пробует локальный OCR и запрещает cloud fallback. Ручной screen/photo workflow также использует VLM только при недостаточном OCR. Постоянный event/video Vision допускается только отдельным opt-in профилем после hardware benchmark.

**Цель.** Понимать текущий экран с минимальной GPU-нагрузкой.

**Затрагивает.** `ScreenContextController`, `TesseractScreenOcr`, `VisionWorkflowService`, screen store, game adapter.

**Новые компоненты.** ROI planner, known-screen recognizers, screen event taxonomy, vision lease.

**Зависимости.** Phase 3 resource manager, Phase 6 tools.

**Риски.** OCR errors, sensitive capture, VLM contention with GTA.

**Ожидаемая нагрузка.** Frame diff low-rate; OCR только при событии; VLM один кадр/ROI по запросу.

**Критерии готовности.** OCR ROI p95 <500 ms; image cleared; no Chat/Vision parallel lease in Balanced; GTA UI dataset gate.

**Тесты.** Known screens, rus/eng OCR, stale TTL, DPI/resolution, consent, malicious on-screen instructions.

**Пользователь увидит.** Быстрые ответы на текст интерфейса и отдельную кнопку глубокого анализа экрана.

## Phase 9 — Voice Companion

**Цель.** Стабильный полный цикл речь → дешёвый route → ответ → короткая озвучка.

**Затрагивает.** Voice coordinator, STT catalog, transcript/session context, TTS and overlay.

**Новые компоненты.** Speech answer projection, interruption/barge-in policy, latency spans.

**Зависимости.** Phases 1–3.

**Риски.** STT ошибки, echo, конкуренция CPU, длинная озвучка.

**Ожидаемая нагрузка.** Один STT; TTS только короткого summary; game audio STT первым отключается при pressure.

**Критерии готовности.** Voice end-to-end p95 budget на reference hardware; cancel/recovery; GTA term accuracy gate; no orphan processes.

**Тесты.** Toggle/hold, device removal, silence, duplicate game audio, interruption, pressure degradation.

**Пользователь увидит.** Быстрый разговор с редактируемым transcript и возможностью перебить/отменить.

## Phase 10 — Adaptive Context and Cautious Proactivity

**Цель.** Предлагать помощь только при высокой уверенности и низкой стоимости.

**Затрагивает.** `ProactivePolicy`, screen events, session state, resource coordinator, UI controls.

**Новые компоненты.** opportunity scorer, cooldown/history, per-category opt-in, feedback capture.

**Зависимости.** Phases 2, 3, 8, 9.

**Риски.** Раздражающие/ложные подсказки, постоянная нагрузка.

**Ожидаемая нагрузка.** Deterministic event scoring; GPT‑5VP не вызывается для rejected candidates.

**Критерии готовности.** Precision выше согласованного порога (начально 90% на curated events); жёсткие cooldown; один клик отключения; FPS gate.

**Тесты.** Repeated events, cooldown, quiet mode, pressure, false positives, consent persistence.

**Пользователь увидит.** Редкие уместные подсказки, а не постоянные комментарии.

## Phase 11 — Hardware Scaling and Release Gates

**Цель.** Превратить профили и hardware tiers в воспроизводимые рекомендации.

**Затрагивает.** model/product benchmark tools, installer, Local AI UI, docs, release pipeline.

**Новые компоненты.** hardware calibration wizard, benchmark result store, compatibility matrix.

**Зависимости.** Все предыдущие performance-sensitive phases.

**Риски.** Переобучение рекомендаций под одну машину; устаревшие модели/драйверы.

**Ожидаемая нагрузка.** Benchmark запускается явно, не в фоне игры.

**Критерии готовности.** Профили проверены минимум на 8/12/16/24 GB tiers; release gate включает token, latency, RAM/VRAM и game-impact metrics.

**Тесты.** Clean install, offline, nonstandard paths, driver/telemetry unavailable, model load/unload, soak.

**Пользователь увидит.** Рекомендованный профиль с объяснением и безопасной кнопкой применения.

## Первая отдельная задача после утверждения плана

Название: **Fast Knowledge Path: ранний deterministic exit и измерение сэкономленных GPT‑5VP вызовов**.

Scope:

1. Ввести `RouteDecision` и чистую policy-функцию.
2. Выбирать deterministic answer до `ChatProviderCatalog.GetAvailabilityAsync`.
3. Добавить fake provider test, доказывающий ноль health/chat calls.
4. Добавить timing spans и counters `route`, `provider_health_calls`, `llm_calls`, `estimated_input_tokens`, `estimated_output_tokens`.
5. Расширить production benchmark сравнением baseline/new route.
6. Не добавлять в этой задаче embeddings, web, новую memory или vision.

Definition of done: все прежние ответы и validation gates сохранены; для exact/prepared набора GPT‑5VP не загружается и не проверяется; отчёт показывает p50/p95 и долю avoided calls.

После выполнения этой задачи следует отдельно утвердить Phase 1 cache slice. Автоматически переходить к следующему этапу не нужно.
