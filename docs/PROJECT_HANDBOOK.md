# Инженерный справочник GTA RP Assistant

Актуально на 3 августа 2026 года. Это руководство предназначено для восстановления разработки другим человеком или новой AI-сессией без скрытого контекста.

## 1. Назначение и неизменяемые границы

GTA RP Assistant — внешнее Windows-приложение для ответов по GTA5RP из проверяемой базы знаний. Оно принимает ручной текст или голос, при необходимости использует внешний AI provider и показывает ответ в основном окне/оверлее.

Приложение не внедряется в GTA, не читает память процесса, не создаёт игровой ввод, не нажимает клавиши вместо игрока и не автоматизирует игровые действия. Эти ограничения важнее удобства отдельной функции.

Основной принцип ответа: сначала проверенные данные, затем необязательная модель только для формулировки, после неё обязательная проверка grounding. При недостатке данных — честный `abstain`.

## 2. Карта репозитория

```text
src/
  GtaRpAssistant.Core/                  доменные модели, порты, pipeline и политики
  GtaRpAssistant.Knowledge/             SQLite/FTS knowledge repository
  GtaRpAssistant.Providers/             OpenAI-compatible и provider adapters
  GtaRpAssistant.Infrastructure.Windows/ Windows audio, process, hotkeys, capture
  GtaRpAssistant.LocalData/             assistant-data.db и история диалогов
  GtaRpAssistant.MicroModelHost/         отдельный optional mock host
  GtaRpAssistant.App/                   WPF, DI composition root и feature modules
tests/                                  семь автоматических test projects
tools/                                  knowledge, product/model/STT benchmarks, Local AI check
knowledge/packs/                        версионированные статьи и факты
knowledge/reference/community/          player-confirmed lookup-данные
eng/                                    build, package, smoke, install и E2E scripts
docs/                                   документация, планы, ADR и QA
artifacts/                              генерируемые publish/release/reports/snapshots
```

Направление зависимостей — внутрь. `Core` не зависит от WPF, SQLite, Windows или конкретного provider. `App.xaml.cs` — composition root. Не переносить runtime-логику обратно в `MainWindow`/`MainViewModel`.

## 3. Главный runtime-поток

```text
manual text / microphone
→ TranscriptBuffer
→ AssistantSessionCoordinator (single-flight + cancellation)
→ RuleBasedIntentDetector
→ ContextSelector
→ IKnowledgeRepository (exact/prepared/FTS)
→ AiRouter
→ prepared/extractive answer или configured provider chain
→ GroundedAnswerValidator
→ IAssistantConversationStore
→ main chat + OverlayService
```

`AssistantSessionCoordinator` владеет state machine и единственным активным запросом. Game audio может добавлять временный контекст, но не активирует подсказку. Любой provider response считается недоверенным до валидации fact IDs, чисел, URL, server scope и запретов автоматизации.

## 4. Чат и история

`IAssistantConversationStore` предоставляет current/list/open/new/rename/delete/clear. Production использует `ConfigurableAssistantConversationStore`:

- `EnableLongTermConversation=false` — `InMemoryAssistantConversationStore`, данные исчезают после выхода;
- `true` — лениво создаваемый `SqliteAssistantConversationStore` в `assistant-data.db`.

SQLite хранит conversations, messages, provider/model IDs, used fact IDs и situation ID. Используются WAL, foreign keys, транзакции и индексы. Повреждённый JSON одного сообщения не ломает загрузку; повреждённая БД переносится в `.corrupt-*` перед созданием чистой.

Интерфейс M2 умеет создавать, открывать, переименовывать и удалять диалоги, повторять последний вопрос, копировать ответ и отменять активный запрос. Enter отправляет, Shift+Enter добавляет строку. Markdown отображается нативным безопасным подмножеством без WebView, HTML и активных ссылок.

Это ещё не долговременная семантическая память. `MemoryService`, профиль, candidates, summaries и `ContextBuilder` относятся к следующим этапам и не должны имитироваться бесконечной передачей истории модели.

## 5. Файлы пользовательских данных

Путь по умолчанию: `%LocalAppData%\GtaRpAssistant`. Для тестов/portable-профиля путь меняется переменной `GTA_RP_ASSISTANT_DATA_DIR`.

```text
settings.json             несекретные настройки и provider routes
knowledge.db              индекс проверенной игровой базы
assistant-data.db         opt-in история пользователя
assistant-data.db-wal/shm служебные SQLite-файлы во время работы
secrets/                  DPAPI CurrentUser API keys
logs/                     privacy-safe rotating logs
model-packs/stt/           optional embedded STT pack; можно заменить custom path
startup-error.txt         ошибка раннего запуска
fatal-error.txt           необработанная критическая ошибка
```

Knowledge и пользовательская история принципиально разделены. Не помещать transcript, профиль или диалоги в `knowledge.db`. Не помещать API keys в `settings.json` или логи.

## 6. Provider routing и модели

STT, Chat, Vision, TTS и Embeddings имеют независимые маршруты и режимы. `PerformanceProfile` ограничивает ресурсы, но не подменяет выбор local/cloud. Cloud недоступен без явного opt-in.

LM Studio — внешний backend, а не обязательная часть приложения. Пользователь может указать нестандартные пути к `lms.exe`/`LM Studio.exe`, выбрать установленную chat-модель или импортировать GGUF. Новая модель становится активной только после capability-test; прежний маршрут сохраняется при провале.

Embedded STT — отдельный `whisper.cpp` provider в `Infrastructure.Windows`, а не часть LM Studio. `EmbeddedSttPackLocator` проверяет manifest/size/SHA; `WhisperCppSpeechToTextProvider` владеет loopback process, single request, timeout/cancel/memory watchdog и idle unload. Pack строится/устанавливается отдельными `eng/build-stt-pack.ps1` и `eng/install-stt-pack.ps1`, не входит в основной ZIP до PASS русского gate. Подробности: `EMBEDDED_STT.md`.

Комплектный `MicroModelHost` остаётся mock fallback. Реальные Qwen3-0.6B и SmolLM2-360M отклонены ADR-0001 по quality/memory gate. Не добавлять веса или реальный headless runtime без нового успешного ADR.

## 7. База знаний

Source JSON находится в `knowledge/packs/gta5rp`; player-confirmed данные — отдельно в `knowledge/reference/community`. Официальный и community provenance нельзя смешивать. Community-ответ обязан явно сообщать, что сведения получены от игроков.

При изменении knowledge:

1. соблюдать `KNOWLEDGE_FORMAT.md` и `KNOWLEDGE_AUTHORING.md`;
2. не придумывать источник, дату, server scope или число;
3. обновить coverage;
4. запустить strict knowledge gate через полный build;
5. добавить regression-вопрос, если исправлялся реальный неправильный ответ.

## 8. WPF и модульный UI

Feature modules регистрируются в `Shell/FeatureRegistry.cs`. Каждая страница имеет View, ViewModel и корневой AutomationId. Общие цвета, typography, buttons, inputs и surfaces находятся в `DesignSystem`.

Основные модули: Assistant, Audio, Providers, Behavior, Privacy, Knowledge, About. Overlay и vision preview — отдельные окна. Новая функция не должна раздувать `MainViewModel`; межмодульную orchestration выполняет `ApplicationLifecycleCoordinator`.

При изменении UI обязательны: клавиатурная доступность, AutomationId, minimum-size layout, WPF smoke и визуальный просмотр соответствующего PNG из `artifacts/ui-snapshots`.

## 9. Сборка и тестирование

Быстрая проверка:

```powershell
dotnet restore GtaRpAssistant.sln
dotnet build GtaRpAssistant.sln -c Release --no-restore
dotnet test GtaRpAssistant.sln -c Release --no-build
```

Единственный release gate:

```powershell
.\eng\build.ps1 -Configuration Release -Runtime win-x64
```

Он выполняет build без warnings, все тесты, governance/knowledge validation, model config validation, блокирующий benchmark полного production pipeline, self-contained publish, WPF smoke, UI snapshots, custom-path install smoke и создаёт ZIP/manifest/SHA-256. Артефакт считается готовым только после полного выхода 0.

Если упаковка не может очистить `artifacts/publish`, проверить запущенный `GtaRpAssistant.App.exe` именно из этой папки. Не завершать произвольные экземпляры приложения по одному имени — сверять полный `Path`.

## 10. Диагностика

1. Сначала открыть `startup-error.txt`/`fatal-error.txt` в папке данных.
2. Затем проверить последние файлы `logs/`.
3. Воспроизвести с отдельным `GTA_RP_ASSISTANT_DATA_DIR`, чтобы не менять профиль пользователя.
4. Проверить ручной текст без модели; затем knowledge; затем provider health/capability.
5. Для Local AI использовать `eng/local-ai-e2e.ps1` и отчёты `artifacts/local-ai-e2e`.

Код `0xe0434352` — оболочка необработанного .NET-исключения, а не причина. Нужен текст внутреннего exception из startup/fatal report.

## 11. Как безопасно продолжить работу

Новая сессия должна:

1. прочитать `DOCUMENTATION_INDEX.md`, этот файл и верхний active checkpoint;
2. проверить незавершённые процессы и состояние файлов, не предполагая наличие чистого Git worktree;
3. запустить релевантные тесты до изменения;
4. выбрать один логический этап и не переписывать уже работающие подсистемы;
5. после изменения пройти профильные тесты, затем полный release gate;
6. визуально проверить изменённые страницы;
7. обновить документацию и только затем зафиксировать новый checksum.

Запрещено отмечать этап завершённым только по успешной компиляции. Нужны тесты, smoke и честно записанные ограничения.

## 12. Текущая точка и следующие этапы

На текущей точке завершены M1 (opt-in SQLite history), M2 (полный менеджер диалогов и безопасное отображение сообщений), T1 (версионированный production pipeline benchmark), архитектурный аудит автономного режима и основной P0.1 toggle/preview voice control plane. T1 проверяет 528 сценариев через реальный coordinator и SQLite retrieval; методика и baseline находятся в `PRODUCT_QUALITY_BENCHMARK.md`. Аудит, resource budgets и путь P0–P10 находятся в `OFFLINE_ASSISTANT_ARCHITECTURE.md`.

Программная часть P0.1b завершена. Для P0.2 реализованы optional `whisper.cpp` runtime, строгий pack manifest/hash, отдельные build/install-скрипты, custom path, watchdog, cancellation и fallback; production pack ждёт русский comparative quality gate и ADR по [`EMBEDDED_STT.md`](EMBEDDED_STT.md). Перед публичным релизом отдельно выполняется P0.1b hardware-матрица. После PASS P0.2 проверяется полный offline voice knowledge vertical. Современная визуальная доводка продолжается по `PRODUCT_AND_UI_PLAN.md` и `docs/design`, не блокируя автономный сценарий.
