# GTA RP Assistant — compact project handoff

Актуальность: 12 августа 2026 года. Текущий исходный код имеет приоритет над историческими планами. Начинать работу следует с этого файла, затем с `DEVELOPMENT_CHECKPOINT.md` и только после этого — с профильной документации подсистемы.

## 1. Назначение продукта

GTA RP Assistant — внешнее Windows-приложение для игроков GTA5RP. Оно отвечает на игровые вопросы по локальной проверяемой базе знаний, поддерживает текстовый и голосовой ввод, показывает компактный overlay поверх игры и опционально использует локальные или облачные AI-провайдеры.

Основные сценарии:

- быстрый вопрос → grounded-ответ без обязательной LLM и интернета;
- PTT/toggle-голос → локальная транскрибация → редактируемый preview → ответ;
- краткий текущий диалог или opt-in долговременная история;
- ручной подтверждённый снимок окна GTA → Vision-анализ → безопасная карточка;
- настройка LM Studio, произвольных путей, моделей, микрофона, overlay и privacy.

Ключевые требования: offline-first; полезность без Model Pack/LM Studio/cloud; достоверность важнее попытки ответить; официальный и player-confirmed контент имеют разное происхождение; слабые ПК должны получать контролируемую деградацию; никакого внедрения в GTA, чтения памяти, генерации ввода, автокликеров или скрытого захвата.

## 2. Что уже реализовано

| Подсистема | Статус | Основные файлы и символы |
|---|---|---|
| UI | Работает, визуальная доводка продолжается | `GtaRpAssistant.App`; `MainWindow.xaml`, `Features/*`, `FeatureRegistry`; дизайн-система в `DesignSystem/Tokens`, `Styles`, `Controls`; reusable `FeaturePageHeader`, `FeatureSection`, `MetricCard`, `MarkdownTextBlock` |
| Чат | Работает | `Features/Assistant/AssistantView.xaml`, `AssistantFeatureViewModel`; welcome-state, bubbles, composer, Enter/Shift+Enter, retry/copy/cancel, список диалогов |
| Overlay | Работает | `OverlayService`, `OverlayWindow`, `ExpandedOverlayWindow`, `OverlayPresentation`; compact/expanded, drag, сохранение позиции, pin/hide; зелёно-белый voice orb и сине-белый assistant orb |
| Локальная база знаний | Работает | `GtaRpAssistant.Knowledge`; `KnowledgePackLoader`, `KnowledgePackValidator`, `CommunityReferenceLoader`, `SqliteKnowledgeRepository`; данные в `knowledge/packs/gta5rp` и `knowledge/reference/community` |
| Поиск и grounding | Работает | `SqliteKnowledgeRepository.SearchAsync`, `RuleBasedIntentDetector`, `ContextSelector`, `AssistantConversationGrounding`, `GroundedAnswerValidator`; exact/prepared/FTS, русская нормализация, server scope, conflict/outdated checks |
| Основной answer pipeline | Работает | `AssistantSessionCoordinator.ProcessAsync`; single-flight, retrieval, deterministic answer, provider route, validation, abstain, overlay, conversation store |
| Локальная LLM | Работает через внешний LM Studio | `LocalAiEngineManager`, `LmStudioEngineAdapter`, `LocalAiCapabilityTester`, `ChatProviderCatalog`; discovery/start/download/load/unload, capability gate, выбор установленной chat-модели, custom CLI/app/GGUF path |
| Автонастройка LM Studio | Реализована в текущем worktree | `LmStudioBootstrapInstaller`, `ILocalAiBootstrapInstaller`; официальный headless installer, выбранный пользователем каталог, bounded download, validation, timeout/cancel/kill-tree |
| Provider routing | Работает | `ProviderFoundation.cs`, `ProviderRegistry`, `ProviderRouteResolver`, `ProviderSettingsMigration`; независимые STT/Chat/Vision/TTS/Embeddings routes и fallback, cloud только после opt-in |
| Голосовой control plane | Работает | `VoiceInteractionCoordinator`, `VoiceInteractionStateMachine`, `AudioSessionController`; toggle/hold, cancel, max duration, editable preview, confirm, auto-submit opt-in, state events |
| Микрофон и game audio | Работает | `WasapiMicrophoneCaptureService`, `WasapiGameAudioCaptureService`, `ProcessLoopbackCaptureService`, `AudioRingBuffer`, `EnergyAudioSegmenter`; device selection/test/level, VAD, bounded channels, PID loopback и обозначенный system fallback |
| Восстановление микрофона | Работает, нужна hardware-матрица | `AudioSessionController.RecoverMicrophoneAsync`, `MicrophoneRecoveryPolicy`, `GlobalVoiceHotkeyHook`; повторный поиск именно выбранного устройства без скрытой подмены |
| Транскрибация | Runtime готов; публичный quality gate не закрыт | `SpeechToTextProviderCatalog`, `WhisperCppSpeechToTextProvider`, `EmbeddedSttPackLocator`; optional whisper.cpp pack, hash manifest, lazy loopback server, single request, TTL, timeout/cancel/memory kill и external fallback |
| Запись STT-датасета | Работает как developer tool | `GtaRpAssistant.SttBenchmark`, `SttDatasetRecorder`; интерактивная запись PCM16 mono 16 kHz WAV с выбранного микрофона, только локально и с явным действием |
| Screenshot/Vision | Ручной сценарий работает | `WindowCaptureService`, `VisionWorkflowService`, `VisionPreviewWindow`, `VisionConsentCard`; снимок только найденного окна GTA, preview/Confirm/Cancel, local/cloud route, очистка PNG из памяти после запроса |
| OCR/автоматическое понимание экрана | Не реализовано | Есть только ручной provider-based Vision; нет OCR, frame diff, known-screen recognition или постоянного наблюдения |
| TTS | Работает, opt-in | `WindowsTextToSpeechService`; Windows TTS, голос/устройство настраиваются, новый ручной вопрос останавливает текущую речь |
| Диалоги | Работают | `ConfigurableAssistantConversationStore`, `InMemoryAssistantConversationStore`, `SqliteAssistantConversationStore`; по умолчанию временно, долговременное хранение только по галочке в отдельном `assistant-data.db` |
| Настройки и секреты | Работают | `AppSettings`, `SettingsService`, `SettingsEditor`, `SettingsSaveCoordinator`, `ProviderSettingsMigration`, `DpapiSecretStore`; atomic save, recovery invalid JSON, DPAPI CurrentUser, ручные пути и независимые режимы подсистем |
| GTA lifecycle | Работает | `GameProcessDetector`, `GameSessionMonitor`, `ApplicationLifecycleCoordinator`; обнаружение GTA/RAGE MP, перепривязка process loopback, tray/startup lifecycle |
| Производительность | Частично централизована | `PerformanceController`, `ProcessPerformanceMonitor`, `MicroModelResourceGuard`; профили и деградация существуют, но общего workload broker для App/STT/Chat/Vision/TTS ещё нет |
| Установка и запуск | Portable и user-scope scripts работают | `eng/package.ps1`, `install.ps1`, `rollback.ps1`, `uninstall.ps1`; self-contained/framework-dependent win-x64, SHA-256, staging, rollback, нестандартный install path |
| Диагностика и QA | Работают | fatal/startup reports в `App.xaml.cs`, rotating logger, `eng/smoke.ps1`, `soak.ps1`, UI snapshots, product/model/STT benchmarks, unit/integration suites |
| MicroModelHost | Только mock | `GtaRpAssistant.MicroModelHost/MockMicroModelRuntime.cs`, `MicroModelManager`; безопасный fallback и lifecycle foundation без production-модели |

## 3. Реально существующая архитектура

| Компонент | Ответственность | Основные файлы | Зависимости |
|---|---|---|---|
| `GtaRpAssistant.Core` | Доменные модели, интерфейсы, orchestration, state machines, routing/grounding contracts | `AssistantSessionCoordinator.cs`, `DecisionServices.cs`, `Interfaces.cs`, `ProviderFoundation.cs`, `LocalAiConversation.cs` | Не зависит от WPF, SQLite, Windows или provider implementations |
| `GtaRpAssistant.Knowledge` | Загрузка/валидация pack, governance, SQLite/FTS retrieval | `KnowledgePackLoader.cs`, `SqliteKnowledgeRepository.cs`, `KnowledgeGovernance.cs` | Core, Microsoft.Data.Sqlite |
| `GtaRpAssistant.Providers` | OpenAI-compatible Chat/STT/Vision, registry и capability checks | `ProviderRegistry.cs`, `OpenAiCompatible*Provider.cs`, `LocalAiCapabilityTester.cs` | Core, HTTP |
| `GtaRpAssistant.Infrastructure.Windows` | WASAPI, game/window capture, LM Studio, whisper.cpp, DPAPI, processes, performance | `Wasapi*`, `WindowCaptureService.cs`, `LocalAiEngineManager.cs`, `EmbeddedSttPack.cs`, `WhisperCppSpeechToTextProvider.cs` | Core, NAudio, System.Drawing, Windows APIs |
| `GtaRpAssistant.LocalData` | Opt-in SQLite conversation history | `SqliteAssistantConversationStore.cs` | Core, Microsoft.Data.Sqlite |
| `GtaRpAssistant.App` | WPF/MVVM UI, DI composition, feature modules, settings, tray, voice/vision/application coordination | `App.xaml.cs`, `MainViewModel.cs`, `Features/*`, `Shell/*`, `AudioSessionController.cs`, `VisionWorkflowService.cs` | Все production-библиотеки выше |
| `GtaRpAssistant.MicroModelHost` | Отдельный named-pipe host и mock runtime | `Program.cs`, `MockMicroModelRuntime.cs` | Core; реальная модель отсутствует |
| `tools/*` | Knowledge validation, product/model/STT benchmarks, local-AI diagnostics | Соответствующие `Program.cs` и runners | Production libraries по назначению |
| `eng/*` | Build, package, smoke, snapshots, installation, rollback, model/STT preparation | `build.ps1`, `package.ps1`, `smoke.ps1`, `compare-stt-candidates.ps1` | .NET SDK, PowerShell; optional внешние packs |

Composition root — `App.ConfigureServices()` в `src/GtaRpAssistant.App/App.xaml.cs`. Основной поток: manual/voice transcript → `AssistantSessionCoordinator` → intent/retrieval/context → deterministic или configured Chat route → validator → conversation store/overlay. Capture, STT, TTS и Vision управляются отдельными app/infrastructure-сервисами и не должны переноситься в coordinator или `MainWindow`.

## 4. Важные решения

- Offline-first означает, что локальная knowledge-first цепочка и ручной текст обязаны работать без LM Studio, Model Pack и cloud. LLM улучшает формулировку/диалог, но не является источником игровых фактов.
- Маршрут ответа фактически knowledge-first: prepared/deterministic → configured Chat route → grounded validator → abstain. Неподтверждённые числа, wrong-server и запрещённая автоматизация блокируются.
- Chat, STT, Vision, TTS и Embeddings — разные capabilities и routes. Qwen/LM Studio chat-модель нельзя использовать как `whisper-1`; migration v2 удаляет такой старый маршрут.
- Cloud всегда явный opt-in. Game audio и screenshot имеют дополнительные consent/privacy границы. Отказ или отсутствие provider понижает функциональность, но не ломает базовый сценарий.
- Официальные статьи и сведения игроков хранятся отдельно. Community data разрешены, когда официальных данных нет, но должны маркироваться как player-confirmed/приблизительные и не становиться «официальными».
- Embedded STT вынесен в отдельный hash-verified pack и не входит в основной ZIP до русского quality ADR. Runtime слушает случайный loopback-порт, не использует shell/GPU по умолчанию и уничтожает дерево процесса при hard limit/cancel/timeout.
- `base-q8_0` быстрее/легче `small-q5_1` на техническом smoke, но победитель намеренно не выбран без одинакового 40-case русского датасета. Технический lifecycle не равен quality result.
- Собственная MicroModel не выпущена: Qwen3-0.6B Q4_0 и SmolLM2-360M Q8_0 отклонены по quality/memory gate. `MicroModelHost` остаётся mock до нового успешного ADR; fine-tuning не запускается автоматически.
- LM Studio — текущий основной проверенный внешний local-AI adapter. Выбор модели сохраняется только после capability-test; при ошибке остаётся предыдущий рабочий route. Balanced профиль CPU-first из-за конкуренции GTA/OBS за VRAM, Quality допускает auto GPU offload.
- История разговора по умолчанию временная. Долговременное общение — отдельная opt-in настройка и отдельная SQLite DB; knowledge, chat history и будущая user memory не объединяются.
- Voice transcript по умолчанию показывается для редактирования/подтверждения. Auto-submit — отдельный opt-in. Hold-hook read-only и не создаёт игровой/клавиатурный ввод.
- Vision только вручную: capture выбранного окна → preview → consent → provider. Снимок не сохраняется, а результат не считается источником игровых правил.
- UI развивается поверх WPF/MVVM и общей дизайн-системы, без переписывания рабочего pipeline или разрастания `MainWindow`. Цель — компактный ChatGPT/Gemini-подобный чат и Discord-подобный полупрозрачный overlay; blur должен иметь no-blur fallback.
- Anti-cheat boundary жёсткий: никакой инъекции, чтения памяти, управления персонажем, автокликов, макросов или скрытого постоянного анализа.

## 5. Известные проблемы

| Проблема | Причина / что пробовали | Текущий статус |
|---|---|---|
| Embedded STT нельзя считать production-ready | Runtime/hash/lifecycle прошли; нет полного живого 40-case русского WAV-набора и сравнительного WER/term-recall gate | Открыто; pack не включать в основной публичный ZIP |
| Нет подтверждённой работы voice vertical на всех целевых ПК | Нужны физические unplug/replug, stuck-key, sleep/resume, Windows 10/11 и weak-PC измерения | Открыто; автоматические тесты есть, hardware matrix нет |
| Нет OCR и автономного понимания игрового экрана | Реализован только ручной screenshot + external/local Vision provider; Windows OCR варианты ещё не прошли capability/quality/resource gate | Открыто |
| Нет единого resource broker | App, STT, LM Studio/Vision/TTS и MicroModel контролируются разными механизмами | Открыто; особенно важно для слабых ПК |
| Реальная встроенная MicroModel отсутствует | Два малых кандидата провалили benchmark; ранее `llama-cli` давал интерактивный exit 130, benchmark переведён на `llama-completion --single-turn` | Решение принято: mock/fallback до нового ADR |
| Код `0xe0434352` появлялся при старте WPF | Это оболочка unhandled .NET exception; найден read-only binding без `OneWay` | Исправлено; при повторении читать inner exception из fatal/startup report, а не диагностировать по коду |
| Старый микрофонный route давал странные ответы вместо транскрипта | Qwen chat endpoint был ошибочно назначен STT-моделью `whisper-1` | Исправлено migration v2; STT и Chat разделены |
| Full-suite иногда нестабилен на deadline-тесте | При параллельном запуске семи test assemblies `Deadline_CancelsRequest` один раз через 150 мс остался в `Arming`; тест использует реальный таймер 20 мс | Открытая test-flake: изолированный повтор прошёл 10/10; перед релизом заменить ожидание на детерминированную синхронизацию или устойчивый polling |
| Текущий worktree содержит незакоммиченные продуктовые и tooling-изменения | UI/voice/LM Studio/Codex workspace развивались после последнего commit | Перед новой веткой сначала проверить `git status`, не потерять и не перезаписать изменения |

## 6. Что ещё не сделано

### Важно

1. Записать полный consent-based 40-case русский GTA5RP STT dataset; выполнить `compare-stt-candidates.ps1 -RunLifecycle`; оформить ADR: победитель или отказ от обоих packs.
2. Провести P0 hardware matrix: weak PC, Windows 10/11, unplug/replug, stuck hotkey, sleep/resume, clean-profile offline voice E2E; измерить latency/RAM/CPU и отсутствие скрытой сети.
3. Проверить и закрепить полный current worktree release gate, portable ZIP и пользовательский first-run сценарий на чистом ПК.
4. Расширять и измерять coverage частых GTA5RP-вопросов; поддерживать freshness/server scope и product benchmark, а не просто накапливать статьи.
5. Спроектировать/реализовать offline OCR только после capability, privacy, GTA UI quality и weak-PC benchmark. Отсутствие OCR не должно блокировать manual Vision/text.

### Желательно

- завершить единый минималистичный component catalog, loading/empty/error/success states, accessibility, reduced motion, no-blur fallback и визуальные snapshots размеров/тем;
- добавить document-oriented Knowledge UI: источники, документы, chunks/facts, точные citations, import preview/validation, enable/reindex/review/rollback;
- унифицировать resource budgeting для STT/Chat/Vision/TTS/background workload;
- расширить adapters через общий контракт (Ollama/direct llama.cpp), не меняя Core pipeline;
- first-run wizard и diagnostics export без transcript/screenshots/keys;
- подписанный installer/MSIX и проверяемые обновления с rollback.

### Идеи на будущее

- отдельная opt-in память профиля игрока с просмотром/редактированием/удалением/экспортом;
- opt-in адаптивный характер, который меняет стиль и инициативность, но не факты, citations или safety;
- frame diff/known-screen recognition и разрешённый временный screen context после OCR gate;
- community contribution/review workflow, coverage/freshness dashboard и feedback loop;
- dataset/fine-tuning tools и повторный MicroModel ADR, если baseline-модели не проходят gate.

## 7. Пользовательские требования

- Интерфейс должен быть понятен без технических знаний: основные действия автоматизированы, advanced options скрыты, пути можно менять для нестандартных установок и любых дисков.
- Главный UI — компактный современный мобильный ChatGPT/Gemini-подобный чат. Игровой UI — перемещаемый минимальный orb/overlay, который расширяется только по действию; его можно закрепить или скрыть.
- Цветовая семантика voice overlay: зелёно-белый — говорит пользователь, сине-белый — думает/отвечает ассистент; рядом видны transcript и ответ.
- По умолчанию — «вопрос → ответ» без долговременной истории; долгосрочное общение и будущая персонализация включаются отдельно.
- Приложение обязано отвечать на обычные реплики и вопросы о себе, не требуя игровую статью, но игровые факты должны оставаться grounded.
- Автономность приоритетна: текст и knowledge работают всегда; voice должен получить отдельный локальный pack; cloud не включается автоматически.
- Целевой базовый профиль — Windows x64, включая слабые ПК без дискретной GPU. Тяжёлые функции отключаются/понижаются, а GPU offload не должен конфликтовать с GTA/OBS.
- Допустимы .NET 8/WPF, SQLite, Windows APIs, NAudio, optional hash-verified packs и внешние OpenAI-compatible providers. Python, LM Studio, Ollama, Docker и cloud не могут быть обязательны для базового приложения.
- Все данные, модели и runtime должны поддерживать пользовательские пути. Основной release — portable/self-contained; установка не должна требовать фиксированного `C:` или стандартного каталога.

## 8. Команды

Из корня `E:\Code\LAB_AI (GTA5RP)`:

```powershell
# Разработка
dotnet restore
dotnet build -c Release
dotnet run --project src/GtaRpAssistant.App -c Release

# Тесты
dotnet test -c Release

# Полный release gate и win-x64 package
.\eng\build.ps1 -Configuration Release -Runtime win-x64

# WPF smoke и lifecycle soak готового exe
.\eng\smoke.ps1 -Executable .\artifacts\publish\win-x64\GtaRpAssistant.App.exe
.\eng\soak.ps1 -Executable .\artifacts\publish\win-x64\GtaRpAssistant.App.exe -Iterations 10

# Установка portable package в произвольный каталог
.\eng\install.ps1 -Package <zip> -InstallRoot <directory> -StartAfterInstall
.\eng\rollback.ps1 -InstallRoot <directory>
.\eng\uninstall.ps1 -InstallRoot <directory>

# STT pack и диагностика
.\eng\install-stt-pack.ps1 -Package <stt-zip> -Destination <directory> -ExpectedSha256 <sha256>
dotnet run --project tools/GtaRpAssistant.SttBenchmark -- devices
dotnet run --project tools/GtaRpAssistant.SttBenchmark -- record ml/evaluation/stt-russian-gta5rp-v1.json
.\eng\compare-stt-candidates.ps1 -RunLifecycle

# Проверка локального Qdrant handoff-memory для Codex
python .\eng\bootstrap-qdrant-memory.py all
```

Диагностика startup: смотреть privacy-safe fatal/startup reports в data directory приложения. Для нестандартного тестового профиля используется `GTA_RP_ASSISTANT_DATA_DIR`; automation/smoke использует `GTA_RP_AUTOMATION_MODE=1`.

## 9. Устаревшая информация

- «Все тесты — 153/158» — исторические числа; использовать только результат нового запуска текущего worktree.
- План немедленно встроить Qwen3-0.6B/SmolLM2 runtime устарел: обе модели отклонены, production host остаётся mock.
- `llama-cli` как benchmark runner устарел; корректный headless путь — `llama-completion --single-turn`.
- Утверждение, что hold-to-talk и microphone recovery отсутствуют, устарело: текущий код содержит `GlobalVoiceHotkeyHook`, `EndManualVoiceRequest` и recovery policy.
- Утверждение, что UI — одно монолитное окно, устарело: feature registry, отдельные Views/ViewModels и общая дизайн-система уже есть. Визуальная доводка при этом не завершена.
- Старый маршрут Qwen/LM Studio → `whisper-1` недействителен и автоматически мигрируется.
- LM Studio больше не привязан только к стандартному пути: есть ручные CLI/app paths, обнаружение и импорт GGUF; текущий worktree также содержит bootstrap installer в выбранную папку.
- Постоянная история чата не является default: она opt-in. Не путать её с будущей долговременной памятью игрока.
- Vision не является автоматическим OCR или постоянным screen assistant: это только ручной capture с preview/consent.
- Старые планы обязательного Docker/Qdrant относятся к окружению разработки Codex, а не к runtime GTA RP Assistant. Продукт не зависит от Docker или Qdrant.

# INFORMATION WORTH SAVING TO LONG-TERM MEMORY

1. Composition root: `src/GtaRpAssistant.App/App.xaml.cs`, `App.ConfigureServices`; orchestration не переносится в `MainWindow`/`MainViewModel`.
2. Answer pipeline: `AssistantSessionCoordinator` — single-flight transcript → retrieval → deterministic/configured Chat → grounding validator → overlay/conversation.
3. Базовый продукт knowledge-first и offline-first; он обязан работать без LM Studio, Model Pack, Qdrant и cloud.
4. Официальные и player-confirmed данные имеют отдельное provenance; community facts нельзя выдавать за официальные.
5. Chat/STT/Vision/TTS/Embeddings — независимые capabilities/routes; Qwen chat нельзя назначать `whisper-1`.
6. Embedded whisper.cpp pack технически работает, но публичный русский 40-case quality ADR ещё не закрыт; pack не включать в основной ZIP.
7. Qwen3-0.6B Q4_0 и SmolLM2-360M Q8_0 отклонены; `MicroModelHost` остаётся mock до нового успешного ADR.
8. LM Studio — текущий основной внешний local-AI adapter; модель активируется только после capability-test, Balanced профиль CPU-first.
9. Голос по умолчанию требует редактируемый preview/confirm; auto-submit и долговременная история — отдельные opt-in настройки.
10. Vision только вручную: окно GTA → preview → consent → provider; PNG очищается, результат не является источником игровых правил.
11. OCR, frame diff и автоматическое понимание экрана ещё не реализованы.
12. Anti-cheat boundary запрещает injection, memory reading, synthetic input, macros, autoclick и скрытый постоянный capture.
13. Код Windows `0xe0434352` — оболочка unhandled .NET exception; причина берётся из inner exception/fatal report.
14. Knowledge DB, conversation DB и будущая user memory должны оставаться раздельными.
15. Перед продолжением обязательно сохранить текущий dirty worktree и запускать актуальные тесты; исторические test counts неавторитетны.
