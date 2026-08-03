# GTA RP Assistant

## Как включить

1. Скачайте portable ZIP из GitHub Releases и распакуйте его.
2. Запустите `GtaRpAssistant.App.exe`.
3. Откройте раздел **Ассистент**, введите вопрос и нажмите **Получить ответ** — LM Studio для локальной базы знаний не требуется.
4. По умолчанию приложение работает в режиме «вопрос → ответ» без истории между запусками. Чтобы продолжать диалог после перезапуска, откройте **Приватность**, включите **Долгосрочное общение** и сохраните настройки. История хранится только локально в `assistant-data.db`.
5. Для локальной AI-модели откройте **AI и модели**. Можно запустить **Настроить автоматически**, выбрать любую уже установленную chat-модель LM Studio или безопасно импортировать собственный GGUF-файл/папку. Активной модель становится только после capability-test; прежний рабочий маршрут сохраняется при ошибке.

Подробная постоянно поддерживаемая инструкция: **[Руководство пользователя](docs/USAGE.md)**. Установка и обновление: [docs/INSTALLATION.md](docs/INSTALLATION.md). Для разработчика и новой AI-сессии: [карта документации](docs/DOCUMENTATION_INDEX.md), [инженерный справочник](docs/PROJECT_HANDBOOK.md) и [активная точка продолжения](docs/DEVELOPMENT_CHECKPOINT.md). Измеримый план развития продукта: [roadmap до помощника №1](docs/TOP1_PRODUCT_ROADMAP.md), текущая методика и baseline: [production quality benchmark](docs/PRODUCT_QUALITY_BENCHMARK.md).

Лёгкий внешний Windows-помощник для GTA 5 RP. Приложение не внедряется в GTA, не читает память игры, не создаёт ввод и не автоматизирует действия.

Рабочая цепочка:

```text
Microphone / manual text
→ transcript context
→ local intent rules
→ SQLite exact/FTS knowledge search
→ independent task route / deterministic grounded answer
→ grounded answer validator
→ optional local conversation store
→ non-activating overlay
```

Game audio может пополнять контекст, но никогда не активирует подсказку.

## Быстрый старт

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
dotnet run --project src/GtaRpAssistant.App -c Release
```

Полная release-проверка и создание portable ZIP:

```powershell
.\eng\build.ps1
```

Скрипт фиксирует .NET SDK через `global.json`, выполняет build/tests, строгую проверку knowledge pack и блокирующий benchmark полного production pipeline, по умолчанию публикует self-contained `win-x64` вместе с отдельным mock `MicroModelHost`, запускает WPF navigation/overlay/vision smoke-test, проверяет установку в нестандартный каталог с пробелами, создаёт одиннадцать PNG-снимков feature pages и вспомогательных окон и формирует ZIP, manifest файлов и `.sha256` в `artifacts/release`.

Для десяти повторных startup/shutdown циклов и JSON-отчёта:

```powershell
.\eng\build.ps1 -SoakIterations 10
```

Release-папка также содержит user-scope скрипты install/upgrade, rollback и uninstall. Порядок использования и границы доверия описаны в [docs/INSTALLATION.md](docs/INSTALLATION.md).

Для работы аудио выберите микрофон и настройте OpenAI-compatible STT endpoint. LM Studio по умолчанию ожидается на `http://127.0.0.1:1234/v1`, но не является обязательным. STT, Chat, Vision, TTS и Embeddings имеют независимые режимы `Disabled/Cloud/Local/Automatic/Custom`; профиль производительности ограничивает дорогие функции, но не выбирает local/cloud.

## Реализовано

- WPF/MVVM shell, DI, tray, сохраняемые настройки и privacy-safe rotating logs;
- единый `AssistantSessionCoordinator`, state machine, cancellation и single-flight;
- монитор запуска/закрытия/перезапуска GTA с перепривязкой process loopback;
- WASAPI microphone, process-specific/system loopback, VAD, bounded segmentation и OpenAI-compatible STT;
- единый manual-voice control plane: toggle/hold, cancel, max duration, редактируемый preview, opt-in автоотправка, уровень/тест микрофона, точная диагностика hotkey и ограниченное unplug/replug recovery;
- SQLite/FTS5 knowledge packs, exact/prepared answers, server scope, conflict/outdated checks;
- provider capabilities/registry и независимые primary/fallback routes для STT, Chat, Vision, TTS и Embeddings;
- версионированная миграция старых endpoint/model-настроек без потери явного cloud opt-in;
- deterministic → configured Chat route → grounded validator → abstain routing;
- validator fact IDs, чисел, URL и запрещённой автоматизации;
- proactive modes, cooldown, пауза и «Не беспокоить»;
- DPI/multi-monitor overlay с feedback/DND actions;
- ручной vision только после превью и подтверждения, без сохранения снимка;
- Windows TTS, выключенный по умолчанию и разрешённый только после ручного голосового hotkey;
- DPAPI CurrentUser для API-ключей;
- автоматический центр Local AI: discovery LM Studio/CLI/API, `llmster` startup, каталог моделей, download/load/unload, оценка RAM/VRAM и capability-test;
- ручные пути к `lms.exe` и `LM Studio.exe` для portable/нестандартной установки с возвратом к автоматическому поиску;
- выбор любой установленной instruct/chat-модели LM Studio с сохранением между запусками и фильтрацией embedding-моделей;
- безопасный импорт одиночного GGUF или файла из выбранной папки: проверка сигнатуры, offline dry-run, `--copy`, локальный repository ID, timeout/cancellation и capability gate;
- менеджер диалогов: temporary/opt-in SQLite history, create/open/rename/delete, retry/copy/cancel, Enter/Shift+Enter и безопасный нативный Markdown;
- compact overlay с краткими шагами и expanded overlay с планом, причинами, уточнениями и provider/model;
- отдельный on-demand `MicroModelHost` с named pipe, строгим mock JSON, one-active/one-queued policy, idle TTL и memory guard 750/900 МБ; настоящая модель пока не подключена;
- воспроизводимый [`ModelBenchmark`](./docs/MICRO_MODEL_BENCHMARK.md): реальные Qwen3/SmolLM2 прогоны, strict JSON/grounding/license/memory gate и [`ADR-0001`](./docs/adr/ADR-0001-micro-model-candidate-benchmark.md), отклонивший оба базовых кандидата без включения весов в приложение.

## Горячие клавиши

- `Ctrl+Alt+Q` — показать или скрыть помощника.
- `Ctrl+Alt+A` — ручной голосовой вопрос; в разделе **Аудио** выбирается toggle или удержание; только этот сценарий может включить TTS.
- `Ctrl+Alt+S` — один снимок обнаруженного окна GTA с обязательным превью.
- `Ctrl+Alt+P` — полная пауза.

## Данные и приватность

Обычные настройки, базы и логи находятся в `%LocalAppData%\GtaRpAssistant`. Аудиофайлы и снимки экрана на диск не записываются. Долгосрочная история выключена по умолчанию; после opt-in сообщения сохраняются только локально в отдельном `assistant-data.db`. API-ключи отсутствуют в `settings.json` и защищены DPAPI CurrentUser. Облачные запросы запрещены до явного разрешения пользователя.

## Проверка

Release-сборка проходит без предупреждений. Набор содержит 270 unit/integration тестов для Core, voice interaction, hold/release, STT privacy routing, UI registry, безопасного Markdown, shell boundaries, privacy-safe diagnostics, hotkey/tray routing и DPI-aware overlay geometry, knowledge search и миграций SQLite, временной и opt-in постоянной истории диалогов, conversation/follow-up/repair, local AI management, нестандартных путей, provider routes, process loopback, MicroModel lifecycle/TTL/queue/memory guard, product/model benchmark gates и защитных ограничений. Блокирующий production benchmark проверяет 528 вопросов через реальный coordinator и SQLite retrieval: 524 обязательных сценария прошли на 100%, ложных ответов, неподтверждённых чисел и wrong-server ответов нет. Опубликованное приложение дополнительно проходит изолированный startup/navigation/keyboard/settings/tray/overlay/vision/voice-preview smoke-test, установку из нестандартного каталога, gate из 11 snapshots и 10 startup/shutdown циклов. Реальный двухпроцессный UI E2E LM Studio 0.4.19 с Qwen проверил выбор модели, capability gate и сохранение маршрута после перезапуска; отчёты находятся в `artifacts/local-ai-e2e`.

Текущий проверенный portable-релиз: `artifacts/release/GtaRpAssistant-0.2.0-win-x64.zip`, SHA-256 `9041a0b0d4f72ac7467a0e42d6a64c04158111f8342ffc11ac7d2a972bf3200e`.

## Ограничения

- Комплектный source-reviewed pack содержит компактный срез официальной GTA5RP Wiki: 48 статей и 226 фактов из 44 официальных страниц, включая базовые механики, работы, имущество, телефон, питомцев, казино, достижения, спорт, Car Meet и мотоклубы. Полное покрытие 130 URL ещё не завершено; список волн находится в `docs/KNOWLEDGE_COVERAGE.md`.
- Отдельный community-confirmed каталог содержит 445 коротких lookup-записей из предоставленных игроками достижений, таблиц и игровых справок. В него входят примерный календарь событий, советы по интерфейсу, актуальная поправка по доходу дальнобойщика, дрессировка питомцев, шар предсказаний и клубы. Каждый такой ответ начинается с «По данным игроков:» и не смешивается с официальной Wiki.
- Автоматическая сверка выполнена по официальным страницам, но не заменяет human review владельцем продукта; просроченные, конфликтующие или отозванные данные приводят к безопасному `abstain`.
- Снимок делается с видимой области окна. Exclusive fullscreen, перекрытое или защищённое окно может дать чёрное/неполное изображение.
- Реальные cloud/LM Studio/STT/vision ответы требуют совместимого настроенного endpoint.
- Автоматический мастер зависит от исправного LM Studio 0.4.x/`llmster`; при ошибке daemon приложение продолжает работать без модели и показывает диагностику.
- `MicroModelHost` пока использует только mock runtime: реальный benchmark выполнен, но обе базовые модели не прошли quality/memory gate, поэтому GGUF/llama.cpp намеренно не подключены.
- Современная визуальная доводка уже начата: compact/expanded/vision поверхности и минималистичная shell-навигация используют общую дизайн-систему. Оставшиеся UI-7 задачи зафиксированы в [`PRODUCT_AND_UI_PLAN.md`](./docs/PRODUCT_AND_UI_PLAN.md).
- Self-contained portable `win-x64` ZIP и безопасные user-scope install/rollback/uninstall-скрипты подготовлены; MSIX, code signing и автоматическое обновление пока не подготовлены.
