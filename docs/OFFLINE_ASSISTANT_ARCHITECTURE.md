# Архитектура автономного GTA RP Assistant

Актуально на 31 июля 2026 года.

Статус: архитектурный аудит завершён. Документ является главным планом автономного сценария без LM Studio. Детальные документы существующих подсистем сохраняют силу, если не противоречат зафиксированным здесь границам.

## 1. Целевой пользовательский результат

На чистом поддерживаемом Windows-ПК пользователь должен получить следующий путь:

```text
запуск portable-приложения
→ удержание или переключение Push-to-Talk
→ локальная запись и VAD
→ локальное STT
→ preview распознанного текста
→ подтверждение или исправление
→ локальный поиск по knowledge packs
→ optional локальная AI-формулировка
→ grounded validation
→ overlay
→ локальный Windows TTS
```

LM Studio, Ollama, облачный API и встроенная chat-модель не являются обязательными. Если STT или AI pack отсутствует, ручной текст, knowledge search, sources и overlay продолжают работать.

## 2. Результат аудита

### Реализовано и проверено

| Область | Фактическое состояние |
|---|---|
| Text-to-answer | `AssistantSessionCoordinator` выполняет intent, knowledge retrieval, routing, validation, conversation и overlay |
| Knowledge-first fallback | Проверенные prepared/extractive ответы работают без LLM |
| Grounding | Проверяются fact IDs, числа, URL, server scope, conflicts и freshness |
| Knowledge storage | Отдельная SQLite БД, official/community provenance, migrations и governance |
| Conversation history | Временное хранилище по умолчанию и opt-in `assistant-data.db` |
| Audio capture | WASAPI microphone, отдельный game-audio path, ring buffers и bounded channels |
| Segmentation | Energy detector, pre/post-roll, silence close и ограничение 20 секунд |
| STT routing | Primary/fallback OpenAI-compatible providers и privacy policy |
| TTS | Локальный Windows `System.Speech`, выбор голоса/выхода и остановка |
| Vision consent | Ручной снимок выбранного окна, preview, подтверждение и очистка буфера |
| Local AI management | LM Studio adapter, нестандартные пути, GGUF import, load/unload и capability-test |
| Runtime isolation foundation | `MicroModelHost`, named pipe, TTL, очередь, cancellation и memory guard |
| Performance degradation | Мониторинг собственного процесса и отключение game-audio/proactivity при нагрузке |
| Quality gate | 249 тестов и production benchmark полного answer pipeline |

### Реализовано частично

| Область | Чего не хватает |
|---|---|
| Central orchestration | Coordinator начинается с готового transcript; capture, STT, preview, TTS и Vision управляются разными сервисами |
| Push-to-Talk | Hotkey открывает 20-секундное окно manual activation, но нет hold/release, toggle state, cancel и preview gate |
| VAD | Есть adaptive energy threshold, но нет полноценного speech/noise classifier и пользовательской калибровки |
| Device lifecycle | Есть ручное обновление списка и game-process rebind, но нет автоматического microphone unplug/replug recovery |
| Resource policy | Есть частные guards для WPF и MicroModel, но нет общего бюджета RAM/VRAM/CPU и workload leases |
| Hardware profiles | Есть generation/performance presets, но нет hardware probe и единого профиля STT/Chat/Vision/TTS |
| Knowledge packs | Есть manifest/version/scope/source/review, но нет checksum, signature, dependencies, min app version и rollback UI |
| Diagnostics | Есть privacy-safe summary и release tools, но нет единого пользовательского «Проверить систему» |
| Query normalization | Есть lowercase/`ё→е`, stopwords и лёгкий suffix matching; нет раскладки, транслита, typo correction и slang aliases |
| Screen context | Есть только ручной provider-based Vision; OCR, frame diff и UI-state recognition отсутствуют |
| Memory | История диалога есть; session memory, confirmed profile и memory candidates отсутствуют |

### Существует только как план или контракт

- `LocalAiEngineKind.Ollama` и `LocalAiEngineKind.LlamaCpp` перечислены, но adapters не зарегистрированы.
- `ProviderKind.MicroModel` и MicroModel tasks определены, но manager не включён в основной answer route.
- GPU/VRAM budgets и автоматический hardware profile описаны концептуально, но не измеряются.
- Knowledge pack signature, dependency resolution и rollback не реализованы.
- OCR/classifier/event pipeline, session context store и controlled memory описаны только в документации.

### Отсутствует

- встроенный локальный STT runtime/model pack;
- реальный встроенный chat runtime, прошедший новый ADR;
- hold-to-talk keyboard lifecycle;
- transcript preview/edit/confirm/cancel;
- единый resource budget coordinator;
- OCR и дешёвое распознавание известных экранов;
- confirmed player profile и export/delete UI;
- signed installer/updater и code signing;
- аппаратная матрица Windows 10/11 на трёх классах ПК.

## 3. Обнаруженные архитектурные проблемы

### 3.1 Голосовой control plane раздроблен

`AudioSessionController` одновременно захватывает аудио, строит provider route, выполняет STT и сразу вызывает answer coordinator. `ApplicationLifecycleCoordinator` запускает manual voice и TTS, а `AudioFeatureViewModel` напрямую запускает/останавливает аудиосессию.

Последствия:

- preview невозможно вставить без изменения нескольких слоёв;
- hold/release и cancellation не имеют единого владельца;
- встроенный STT легко превратится во второй параллельный путь;
- UI знает слишком много о runtime lifecycle.

### 3.2 Provider route собирается трижды

Chat использует `ChatProviderCatalog`, а Audio и Vision отдельно повторяют построение registry, чтение secrets и privacy filtering. Это создаёт риск разного fallback и cloud policy.

### 3.3 Resource policy не является общей

`ProcessPerformanceMonitor` видит только WPF-процесс. `MicroModelResourceGuard` видит только host-процесс. STT, LM Studio, Vision и TTS не участвуют в общем бюджете и не резервируют ресурс перед запуском.

### 3.4 Declarative engine support шире реальной

Enums и UI допускают Ollama/llama.cpp, но composition root регистрирует только `LmStudioEngineAdapter`. UI должен показывать только реально зарегистрированные engines и точный статус optional pack.

### 3.5 Current transcript context не является session memory

`TranscriptBuffer` и последние conversation turns пригодны для bounded context, но не должны расширяться до профиля игрока. Для session state и permanent profile нужны отдельные контракты и provenance.

## 4. Что переиспользуется

Не создаются новые аналоги следующих компонентов:

- `AssistantSessionCoordinator` остаётся владельцем validated answer pipeline;
- `IAssistantConversationStore` остаётся единственной точкой истории диалогов;
- `IKnowledgeRepository` и `GroundedAnswerValidator` остаются источником и gate игровых фактов;
- `IAiProviderRegistry`/`IProviderRouteResolver` остаются основой provider selection;
- `IAudioCaptureService`, `EnergyAudioSegmenter` и WASAPI services переиспользуются;
- `ITextToSpeechService` остаётся локальным TTS fallback;
- `MicroModelManager`/named pipe lifecycle переиспользуются при будущем успешном runtime ADR;
- `OverlayService` получает состояния, но не управляет runtime;
- `ApplicationLifecycleCoordinator` остаётся shell-level coordinator, а не превращается в answer engine.

## 5. Целевая архитектура

```text
WPF / Hotkeys
    → VoiceInteractionCoordinator
        → Audio capture + VAD
        → SpeechToTextProviderCatalog
        → TranscriptPreviewPolicy
        → AssistantSessionCoordinator
            → QueryNormalizer
            → Intent
            → Knowledge
            → optional Chat route
            → GroundedAnswerValidator
            → Conversation store
        → OverlayService
        → TTS policy/service

Manual Vision
    → ScreenContextPipeline
        → frame change
        → OCR
        → known-screen classifier
        → optional Vision provider
        → structured ScreenContext
        → AssistantSessionCoordinator

All heavy workloads
    → ResourceBudgetCoordinator
        → leases, priorities, cancellation, degradation, idle unload
```

### 5.1 `VoiceInteractionCoordinator`

Практический application service, который будет владеть одной голосовой интеракцией:

- `BeginHold`, `EndHold`, `Toggle`, `Cancel`;
- capture/VAD/max duration;
- STT task и cancellation;
- preview/confirm/edit;
- остановка текущего TTS новым вопросом;
- вызов существующего `AssistantSessionCoordinator` только после подтверждения;
- единый поток состояний для UI и overlay.

UI вызывает только commands coordinator и отображает immutable snapshot.

### 5.2 `SpeechToTextProviderCatalog`

Выделяется из текущего `AudioSessionController`:

- строит route из versioned settings;
- применяет local/cloud privacy;
- кеширует health;
- предоставляет единый ordered список providers;
- допускает встроенный provider без HTTP;
- не владеет capture или UI.

После этого текущий OpenAI-compatible STT и будущий встроенный STT используют один контракт.

### 5.3 `QueryNormalizer`

Чистый Core pipeline:

```text
Unicode cleanup
→ keyboard layout candidate
→ transliteration candidate
→ typo correction
→ GTA5RP dictionary/slang aliases
→ light morphology
→ ranked normalized variants
```

Исходный текст всегда сохраняется. Нормализованный вариант помогает retrieval, но не заменяет пользовательский текст в истории.

### 5.4 `ResourceBudgetCoordinator`

Не заменяет OS scheduler и не обещает точную VRAM без доступного telemetry adapter. Он:

- выдаёт workload lease: `Stt`, `Chat`, `Vision`, `Ocr`, `Tts`;
- имеет приоритет `ManualVoice > ManualText > TTS > ManualVision > Background`;
- допускает только одну тяжёлую generative workload;
- отменяет устаревший Vision при новом голосовом запросе;
- запрещает queue/context growth после soft limit;
- завершает worker и возвращает fallback после hard limit;
- инициирует idle unload;
- публикует privacy-safe metrics.

### 5.5 Local runtime

LM Studio остаётся optional adapter. Встроенный runtime входит в продукт только как отдельный process/pack:

- pinned runtime и model revision;
- SHA-256, license, manifest и compatibility version;
- loopback или named-pipe transport;
- watchdog и process-tree termination;
- capability benchmark до активации;
- atomic activation/rollback;
- отсутствие обязательной загрузки при старте.

MicroModelHost нельзя переключать с mock на произвольную модель без нового успешного ADR. Для STT создаётся отдельный provider/runtime path: провал chat-моделей не блокирует автономное распознавание речи.

## 6. Данные и память

```text
knowledge.db
    official/community facts only

assistant-data.db
    conversations/messages
    future confirmed profile/memory tables

in-memory SessionContextStore
    server, activity, goal, recent screen events, TTL

settings.json
    opt-ins, routes, budgets, non-secret profile settings

secrets/
    DPAPI CurrentUser only
```

Уровни:

1. request context — уничтожается после запроса;
2. session context — in-memory, TTL, очищается по завершению GTA/сессии;
3. conversation history — только по существующему opt-in;
4. player profile — только explicit request или подтверждённый candidate.

Memory никогда не становится official knowledge. Каждый profile/memory item имеет category, value, source, confidence, confirmation state, created/confirmed/updated timestamps. Неподтверждённое не передаётся модели как факт.

## 7. Голосовые состояния

```text
Idle
→ Arming
→ Listening
→ SpeechDetected
→ Transcribing
→ Preview
→ Submitting
→ AnswerReady
→ Speaking
→ Idle
```

Из любого активного состояния разрешены `Cancel → Idle` и `Faulted → Idle`. Новый manual voice request останавливает TTS. В auto-send режиме `Preview` остаётся наблюдаемым состоянием с короткой отменой; по умолчанию безопаснее explicit confirmation.

## 8. Screen context

Текущий ручной preview сохраняется. Развитие:

1. capture только выбранного окна/области;
2. perceptual frame hash и skip неизменившегося кадра;
3. локальный OCR;
4. known-screen templates/classifier;
5. structured `ScreenContext` с TTL и confidence;
6. Vision model только после ручного действия или разрешённого события.

Screen context не является источником игровых правил. Числа/текст интерфейса маркируются как наблюдение, а ответы о механике всё равно grounded в knowledge.

## 9. Начальные resource budgets

Это цели для benchmark, а не уже достигнутые гарантии.

| Профиль | Base app | STT workload | Chat/Vision | Общий soft/hard RAM | Правило |
|---|---:|---:|---:|---:|---|
| Слабый | 200/300 МБ | 450/650 МБ | по умолчанию unloaded | 1,0/1,4 ГБ | одна локальная модель; knowledge fallback |
| Средний | 250/400 МБ | 600/900 МБ | до 1,8/2,5 ГБ | 2,8/3,8 ГБ | STT и Chat не infer одновременно |
| Мощный | 300/500 МБ | 900/1 200 МБ | до 4/6 ГБ | 6/8 ГБ | Vision отменяется голосом |

Формат `soft/hard`. Для weak profile GPU offload выключен по умолчанию. VRAM budgets появляются только после надёжного telemetry adapter; до этого используются model estimates и conservative presets.

Автовыбор профиля является рекомендацией. Пользователь может изменить профиль и полностью отключить STT, Chat, Vision, TTS или background context.

## 10. Этапы реализации

### P0.1 — единый voice interaction control plane

- Цель: убрать прямой путь `AudioSessionController → answer coordinator`.
- Пользовательский результат: manual voice имеет наблюдаемые состояния, отмену и transcript preview перед отправкой.
- Объём: `VoiceInteractionCoordinator`, STT catalog, state snapshot, hold/toggle commands и текущий provider fallback.
- Компоненты: Core state/contracts, App coordinator/Audio feature/overlay, Providers route catalog.
- Готовность: text path не меняется; voice transcript не отправляется до confirm; cancel прекращает capture/STT; новый вопрос останавливает TTS.
- Тесты: state transitions, hold/toggle/cancel, preview confirm/edit, provider failure, no-device, cloud blocked.
- Benchmark: latency от release до preview и от confirm до knowledge answer.
- Риск: WPF hotkey не передаёт key-up. Mitigation — toggle реализуется первым, hold добавляется через keyboard hook только после isolated test.
- Fallback: текущий текстовый ввод и текущий STT route.
- Зависимости: существующие capture/STT/coordinator.
- Не входит: встроенная STT-модель, OCR, chat runtime.

### P0.2 — встроенный STT pack

- Цель: распознавание русского вопроса без LM Studio и сети.
- Пользовательский результат: offline PTT на чистом Windows-ПК после установки optional/default STT pack.
- Объём: pinned runtime/model, manifest/license/SHA, local provider, watchdog, lazy load и idle unload.
- Компоненты: Infrastructure.Windows, Providers, packaging, diagnostics.
- Готовность: capability-test русского GTA5RP набора, cancellation, Unicode paths, CPU-only weak profile.
- Тесты: package validation, corrupted pack, process crash, timeout, cancellation, fallback.
- Benchmark: WER/term accuracy, cold/warm latency, RAM/CPU, 100 lifecycle cycles.
- Риск: размер pack и CPU latency.
- Fallback: manual text; external local/cloud STT только при настройке.
- Зависимости: P0.1 и выбранный лицензируемый STT candidate.
- Не входит: chat LLM.

### P0.3 — автономный voice knowledge vertical

- Цель: подтвердить полный offline путь.
- Пользовательский результат: PTT → STT → knowledge → overlay → Windows TTS без сторонних программ.
- Объём: first-run readiness, default local route, system diagnostic и E2E harness.
- Готовность: чистая VM без сети и LM Studio проходит сценарий.
- Тесты: installer/portable, microphone unavailable, no model pack, TTS unavailable, fallback.
- Benchmark: end-to-answer p95 ≤4 с для prepared answers на рекомендованном weak profile.
- Риск: hardware/device variability.
- Fallback: typed knowledge path.
- Зависимости: P0.1/P0.2.
- Не входит: embedded chat LLM.

### P1 — optional embedded chat runtime

- Только после dataset/fine-tuning и нового PASS ADR.
- Knowledge остаётся источником истины.
- Провал runtime всегда возвращает deterministic knowledge answer.

### P2 — язык реальных запросов

- Раскладка, транслит, опечатки, сленг и aliases.
- Каждый дефект становится blocking regression case после review.

### P3 — полный resource manager и hardware profiles

- Workload leases, hardware probe, RAM/VRAM telemetry, idle unload и degradation matrix.

### P4 — Knowledge Center и signed packs

- Documents/facts/sources, enable/disable/import/update/rollback, checksum/signature/dependencies.

### P5 — временный игровой контекст

- Отдельный in-memory `SessionContextStore` с TTL и явным provenance.

### P6 — OCR и Vision

- Frame diff → OCR → known screens → optional VLM.

### P7 — controlled memory

- Confirmed profile, candidates, view/edit/delete/export и category opt-outs.

### P8 — proactive assistant

- Activity levels, cooldown, deduplication, reasons и resource-aware suppression.

### P9 — публичный продукт

- Signed installer, updater/rollback, first-run wizard, system check и hardware QA.

### P10 — визуальная доводка

- Component catalog, accessibility, reduced motion, no-blur fallback и polished overlay.

## 11. Выбранная ближайшая точка

Следующий исполнимый этап — **P0.1: единый voice interaction control plane**.

Причина выбора:

- answer orchestrator уже существует и прошёл production benchmark;
- локальный TTS уже существует;
- главный разрыв находится между hotkey/capture/STT и готовым transcript;
- подключение встроенного STT до устранения этого разрыва создаст дублирующий pipeline;
- P0.1 можно проверить существующими providers без выбора или скачивания модели;
- этап даёт немедленный UX-результат и является обязательной зависимостью автономного STT.

## 12. Намеренно не реализуется в P0.1

- автоматическое обучение модели;
- реальный MicroModel runtime без PASS ADR;
- постоянный анализ экрана;
- запись аудио/скриншотов на диск;
- автоматическое сохранение player profile;
- управление GTA, чтение памяти, ввод или автокликеры;
- обязательный cloud или сторонний runtime.

