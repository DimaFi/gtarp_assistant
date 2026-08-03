# GTA RP Assistant — точка продолжения

## ACTIVE CHECKPOINT — P0.2 embedded STT foundation реализован, quality gate открыт

Актуально на 3 августа 2026 года. Это единственная активная точка продолжения; разделы ниже сохранены как исторический журнал и не определяют следующий этап.

Завершено:

- M1: opt-in SQLite history в отдельном `assistant-data.db`; по умолчанию остаётся временный вопрос–ответ;
- M2: список диалогов, create/open/rename/delete с подтверждением, восстановление current conversation;
- retry последнего вопроса, копирование ответа и cancellation активного pipeline;
- Enter отправляет вопрос, Shift+Enter добавляет строку;
- безопасный нативный Markdown без WebView, HTML execution и активных ссылок;
- адаптивная двухколоночная страница Assistant и AutomationId для новых действий;
- версионированный dataset `ml/evaluation/product-pipeline-eval.json` и CLI `GtaRpAssistant.ProductBenchmark`;
- проверка реального `AssistantSessionCoordinator`, SQLite retrieval, safety policy, grounded answer и abstain;
- метрики decision/article/citation, false answer/abstain, unsupported numbers, wrong server и latency;
- исправлены ложные совпадения по общим словам, добавлены лёгкая русская нормализация и блокировка запросов запрещённой автоматизации/непроверяемых прогнозов;
- benchmark включён в обязательный `eng/build.ps1`, отчёты сохраняются в `artifacts/product-benchmark`;
- единая документация: `DOCUMENTATION_INDEX.md`, `PROJECT_HANDBOOK.md` и `PRODUCT_QUALITY_BENCHMARK.md`.
- завершён аудит voice, STT, provider routing, runtime, ресурсов, памяти, Vision и knowledge lifecycle;
- зафиксирована целевая автономная архитектура без обязательных LM Studio, Ollama, Python или cloud;
- добавлены `VoiceInteractionCoordinator`, отдельный `SpeechToTextProviderCatalog` и проверяемые состояния manual voice;
- toggle-hotkey повторным нажатием отменяет capture/STT/ожидание preview;
- распознанный текст по умолчанию не отправляется автоматически: его можно изменить и подтвердить в основном окне или expanded overlay;
- auto-submit оставлен отдельной выключенной по умолчанию настройкой;
- добавлены уровень сигнала и отдельный трёхсекундный тест выбранного микрофона;
- voice preview включён в обязательный UI automation/snapshot gate;
- исправлено startup-падение read-only WPF binding: индикатор уровня использует явный `OneWay`.
- добавлен выбор toggle/hold для `Ctrl+Alt+A`; настройка применяется после сохранения без перезапуска;
- hold-to-talk использует изолированный read-only key-up hook, не блокирует и не создаёт клавиатурный ввод;
- отпускание клавиши немедленно завершает активный речевой сегмент, а key repeat не создаёт повторные запросы;
- `RegisterHotKey` теперь использует `MOD_NOREPEAT` и сообщает, какая именно команда конфликтует;
- неожиданный обрыв WASAPI отменяет текущий voice request и до 10 раз ожидает возвращения именно выбранного микрофона;
- автоматическое восстановление не переключает пользователя скрытно на другое аудиоустройство и отменяется при остановке сессии.
- добавлен optional embedded `whisper.cpp` STT provider, который не зависит от LM Studio, Python или cloud;
- `stt-pack.json` проверяет schema/runtime/architecture/HTTPS sources, безопасные относительные пути, размеры и SHA-256 всех runtime/model/license файлов;
- runtime запускается только при первой транскрибации на случайном `127.0.0.1` порту, повторно использует загруженную модель и выгружается после idle TTL;
- один request gate, startup/request timeout, hard memory watchdog и cancellation завершают всё дерево native-процесса и оставляют прежний provider fallback;
- путь STT-пака можно изменить на странице **Аудио**; пустое поле использует `%LOCALAPPDATA%\GtaRpAssistant\model-packs\stt`;
- добавлены pinned build/install scripts для отдельного STT ZIP; веса остаются в `artifacts/stt`, не входят в Git и основной portable ZIP;
- добавлен `GtaRpAssistant.SttBenchmark` с WER, GTA-term recall, error/empty, p95 latency и peak memory gate;
- официальный `whisper.cpp v1.9.1` и multilingual `base-q8_0` прошли hash check, custom-path runtime smoke, повторное использование PID и cancel-kill;
- сборщик поддерживает два pinned-кандидата: `base-q8_0` и `small-q5_1`; оба веса проверяются по точному размеру и SHA-256;
- добавлен закрытый манифест из 40 русских GTA5RP-фраз, интерактивная локальная запись с явным действием пользователя, выбор любого активного microphone device ID и защита от неявной перезаписи;
- benchmark теперь валидирует gate, обязательные поля и уникальность case IDs; отдельная lifecycle-команда проверяет start/transcribe/dispose, память, p95 и отсутствие orphan process;
- каждый quality-отчёт содержит SHA-256, gate и полное определение датасета; compare-команда перепроверяет transcript-level и aggregate metrics и отклоняет разные/повреждённые отчёты;
- `eng/compare-stt-candidates.ps1` выполняет preflight всех WAV, одинаковый прогон двух паков, формирует единое решение и запускает 100 lifecycle-циклов только для кандидата после PASS;
- три lifecycle-цикла `base-q8_0` прошли за 2,66–3,08 с при peak private 773 MiB; `small-q5_1` — за 8,04–8,32 с при 992 MiB; orphan processes: 0;
- отдельный ZIP `small-q5_1` прошёл hash-verified установку и реальный запуск из нестандартного пути с пробелами; это техническая проверка runtime, не русского качества.
- реализован следующий независимый UI-7 срез: общие `FeaturePageHeader`, `FeatureSection` и `MetricCard` подключены к дизайн-системе;
- **О приложении**, **База знаний** и **Приватность** переведены на общие компоненты с сохранением bindings и AutomationId;
- исправлено переполнение карточки версии: краткая версия отделена от полного диагностического informational version;
- обновлённые About/Knowledge/Privacy snapshots просмотрены вручную; layout и доступные действия сохранены.

Проверка текущего среза:

- release build: 0 ошибок, 0 предупреждений;
- 290 тестов: Core 85, Providers 20, Knowledge 68, Integration 49, App 51, ModelBenchmark 13, ProductBenchmark 4;
- governance/knowledge gate: 0 ошибок, 0 предупреждений; 48 статей и 226 фактов;
- production benchmark: 528 сценариев, 524 blocking, 100% blocking pass/decision/article/citation, 0 false answers, unsupported numbers и wrong-server, p95 0,82 мс;
- WPF smoke, keyboard/minimum-layout, 11 snapshots и custom-path install smoke прошли;
- portable ZIP: `artifacts/release/GtaRpAssistant-0.2.0-win-x64.zip`;
- SHA-256: `f675de743187336e57c10a97b1bba8acc29047ccd03255c30c73edbfb5377593`.

Текущий логический срез завершён на границе, не требующей имитации данных: два кандидата собраны, технически сравнены, а весь comparative/lifecycle gate автоматизирован и защищён fingerprint-проверкой. Production quality gate остаётся открытым только до записи живой русской речи. Дальше нужен полный 40-case набор WAV и запуск одной команды `eng/compare-stt-candidates.ps1 -RunLifecycle`; затем weak-PC профиль и ADR с победителем либо отказом от обоих. До успешного ADR STT ZIP не публикуется как рекомендуемый и не включается в основной релиз. Подробности и команды сохранены в `EMBEDDED_STT.md`. До публичного релиза P0.1b также требует отдельную hardware-матрицу: физический unplug/replug, stuck-key, sleep/resume и Windows 10/11.

Перед продолжением прочитать `DOCUMENTATION_INDEX.md`, `PROJECT_HANDBOOK.md`, `OFFLINE_ASSISTANT_ARCHITECTURE.md` и `ASSISTANT_MEMORY_AND_CHAT_PLAN.md`. Не подключать реальный MicroModel runtime без нового успешного ADR. Не смешивать пользовательскую память с `knowledge.db`.

---

## Исторический журнал — полезные текстовые ответы и official knowledge v8

- Устранён ложный `abstain`: разные FTS-результаты больше не считаются противоречием только из-за разных фактов.
- При одинаковом точном запросе официальный источник имеет приоритет над community-справкой и не образует ложный конфликт.
- Если AI-провайдер не настроен или все провайдеры недоступны, приложение формирует короткий проверенный ответ прямо из базы знаний.
- Длинные community-ответы безопасно сокращаются до лимита валидатора без потери provenance-префикса.
- Полный production pipeline без AI отвечает на восемь типовых текстовых вопросов: медицина в игре, достижения, дальнобойщик, Merryweather, регистрация транспорта, питомцы и казино.
- Official pack v8: 48 статей, 226 фактов, 103 prepared answers из 44 страниц; добавлены питомцы, казино и достижения.
- Все 231 тест прошли: Core 73, Providers 20, App 30, Integration 29, Knowledge 66, ModelBenchmark 13.
- Strict knowledge gate: 0 ошибок и 0 предупреждений; WPF smoke, 10 snapshots и установка в нестандартный путь прошли.
- Portable ZIP: `artifacts/release/GtaRpAssistant-0.2.0-win-x64.zip`.
- SHA-256: `621dd501de5bc4865466d81550a0688635e6e2eff54199de734688d9f6776094`.

Следующий этап: аудит и первый логический срез контекстного игрового ассистента описываются в `docs/CONTEXTUAL_GAME_ASSISTANT_PLAN.md`.

## Последний завершённый этап — минималистичная оболочка и диагностика

Эта секция актуальнее расположенных ниже исторических UI-7 записей.

- Основное окно стало компактнее: один заголовок вместо двух строк, уменьшенные внешние отступы, навигация шириной 184 DIP, мягкое выделение активного пункта без яркой рамки и сокращённый footer.
- Обычные карточки больше не получают видимую рамку автоматически. Raised, warning и overlay-поверхности сохраняют границы там, где они помогают отделить состояние или интерактивную область.
- Добавлена модульная страница `О приложении` с версией, платформой, runtime, количеством статей, состоянием cloud, выбранной Local AI моделью/endpoint и папкой данных.
- Диагностическая сводка намеренно не содержит API-ключи, transcript, prompt или историю диалога. Это закреплено unit-тестом.
- Исправлено падение WPF при первом показе окна: read-only поля диагностики теперь используют явную `OneWay` binding. Именно этот дефект мог показывать системное окно `0xe0434352` в предыдущей сборке.
- Ошибка раннего запуска теперь записывается в `startup-error.txt` внутри папки данных, чтобы вместо неизвестного CLR-исключения оставалась пригодная для разбора причина.
- Добавлен единый fatal-error boundary для UI thread, AppDomain и фоновых Task: ранний сбой сохраняет отчёт, показывает понятное WPF-сообщение и завершает повреждённый процесс без зависшего экземпляра.
- README синхронизирован с фактическим gate: 226 тестов, 10 UI snapshots и актуальный checksum portable-сборки.
- Knowledge summary обновляется после инициализации каталога и показывает компактные значения: всего, official и community.
- UI automation расширена страницей `about`; обязательный snapshot gate теперь содержит 10 изображений.
- Визуально проверены `assistant`, `providers`, `about`, compact/expanded overlay и vision consent. Основные действия и статусы читаются при минимальном размере окна.

### Проверка текущего среза

- Release build: 0 ошибок, 0 предупреждений.
- Все 226 тестов прошли: Core 71, Providers 20, App 30, Integration 28, Knowledge 64, ModelBenchmark 13.
- Governance lint: 0 ошибок, 0 предупреждений.
- Strict knowledge pack: 44 статьи, 202 факта.
- WPF smoke: exit code 0 в изолированном профиле.
- UI snapshots: 10/10.
- Установка и удаление из нестандартного пути с пробелами: успешно.
- Lifecycle soak: 0/10 ошибок, max peak working set 219,35 МБ; отчёт `artifacts/reports/lifecycle-soak.json`.
- Portable ZIP: `artifacts/release/GtaRpAssistant-0.2.0-win-x64.zip`.
- SHA-256: `bbc3ab39e1d795fbfa5e4f6a80b6d6840c86b2ea638b02bfca4ca064fc3cc89f`.

### GitHub

- Целевой репозиторий: `https://github.com/DimaFi/gtarp_assistant.git`.
- Локальная папка `.git` существует, но пуста, поэтому она не содержит историю, ветку или remote и сейчас не является Git-репозиторием.
- GitHub CLI `gh` на ПК не установлен; `winget` в текущем сеансе также не запускается. Намеренно не выполнялись `git init`, force-push или загрузка всего дерева без сверки с удалённой историей.
- Для безопасного продолжения установить GitHub CLI, выполнить `gh auth login`, затем сначала получить удалённую историю и только после сравнения сформировать небольшие профессиональные коммиты.

### Следующие действия

1. Продолжить минималистичный UI-7: выделить reusable `Icon`, `EmptyState`, `ErrorState` и page-status компоненты, затем постепенно заменить локальные варианты на feature pages.
2. Добавить компактный сворачиваемый navigation rail и проверить его при DPI 100/150/200%, не уменьшая доступность подписей и горячих клавиш.
3. Проверить полный `lms import --copy` на отдельной безопасной копии GGUF; пользовательский единственный файл модели не использовать.
4. Провести длительные сценарии: 30 минут idle, restart runtime, sleep/resume, audio unplug/replug, недоступный endpoint, cloud-only и GTA-under-load.
5. Провести аппаратную UI-матрицу Windows 10/11, второй монитор и borderless GTA.
6. Подготовить подписанный installer и механизм обновления. Git metadata восстанавливать отдельно, не блокируя продуктовую работу.

Актуально на 18 июля 2026 года. Это основная точка входа для следующей сессии разработки.

## Последний завершённый этап — начало современного UI-7

- Git больше не считается блокером развития по решению владельца продукта; восстановление metadata и коммиты остаются отдельной фоновой задачей.
- Добавлен переиспользуемый `DesignSystem/Controls/OverlayCardShell`: единый шаблон поверхности, тени, радиуса, padding и semantic accent.
- `OverlayTone.Neutral`, `Success` и `Warning` теперь автоматически задают цвет боковой полосы и status text через ресурсы дизайн-системы.
- Compact и expanded overlay используют один компонент; локальная дублированная логика выбора brush удалена из обоих code-behind.
- Добавлены общие стили `Badge.Source` и `Badge.Community`, а expanded background вынесен в токен `Brush.OverlayExpanded`.
- UI Automation отдельно создаёт warning presentation и проверяет, что применён фактический `Brush.Warning`.
- Новые compact/expanded snapshots просмотрены: контент читается, semantic-полоса не перекрывает текст и корректно масштабирует карточку.
- На первом semantic-shell срезе проходили 224 теста, WPF smoke, 9 snapshot gate и custom-path install/uninstall smoke.
- SHA-256 промежуточного semantic-shell среза: `26648de4e50898a18ecc72085fa5985d85284e497524b82f06aaf310cf2049b6`; актуальный указан ниже.

### UI-7 Listening/Thinking и QuickActions

- Добавлен reusable `OverlayStatusPill` с semantic dot, текстом и activity-маркером без тяжёлой анимации.
- `Ctrl+Alt+A` теперь после успешного запуска аудиосессии показывает non-activating Listening-pill на 20 секунд; следующая карточка автоматически отменяет предыдущий индикатор.
- `MicroModelState.Starting` и `Generating` отображаются как `OverlayActivity.Thinking`; обычные ответы и ошибки остаются статическими.
- Activity-layout сжимает compact window примерно до 250 DIP и скрывает title, message, source и действия, оставляя только pill.
- Footer expanded overlay вынесен в reusable `QuickActionsBar`; сохранены все события и AutomationId.
- Smoke проверяет Listening activity, компактную ширину с допустимым DPI-rounding, скрытие полного заголовка, warning brush и QuickActions automation contract.
- Полный release gate прошёл: 225 тестов, WPF smoke, 9 snapshots, custom-path install/uninstall smoke.
- Lifecycle soak нового publish: 0/10 ошибок, max peak working set 213,86 МБ.
- SHA-256 промежуточного Listening/Thinking среза: `d403dfff183060cbe0c4a18a83cff2e27c2c9cd5015d9daf183e64deaf634eaa`; актуальный указан ниже.

### UI-7 Vision consent card

- Содержимое `VisionPreviewWindow` вынесено в reusable `VisionConsentCard`; окно отвечает только за modal result и загрузку изображения в память.
- Карточка явно показывает destination/endpoint и отдельно сообщает, что анализ не начинается до подтверждения.
- Preview, destination, Confirm и Cancel получили сохранённые/новые стабильные AutomationId.
- `Confirm` является default-кнопкой для `Enter`, `Cancel` — cancel-кнопкой для `Esc`; smoke проверяет обе настройки.
- Footer сообщает, что кадр не сохраняется на диск и удаляется из памяти после ответа.
- Новый vision snapshot просмотрен: destination читается, preview не перекрывается, кнопки помещаются при текущем минимальном размере.
- WPF smoke, 9 snapshots и custom-path install/uninstall smoke повторно прошли.
- Актуальный portable SHA-256: `dbc999382f0953e6f67fd81e6c48150f6717d98a87dcac79ef7f9e605d30e4cb`.

## Ранее завершённый этап — Local AI

Завершены стабилизация реального Local AI и подключение произвольных моделей LM Studio.

### Реальный Local AI

- После отключения питания восстановлен headless runtime LM Studio: удалён только устаревший PID-lock, официально установлен `llmster 0.0.19-2`, daemon и API на `127.0.0.1:1234` успешно запущены.
- LM Studio `0.4.19` видит нестандартную директорию моделей `E:\Download_models\models`.
- Загруженная `qwen/qwen3-vl-4b` Q4_K_M работает через реальный код провайдера приложения.
- Исправлена несовместимость LM Studio 0.4.19: вместо устаревшего `response_format=json_object` приложение отправляет строгую `json_schema`; для старых OpenAI-compatible endpoint оставлен один контролируемый compatibility-retry.
- Добавлен воспроизводимый инструмент `tools/GtaRpAssistant.LocalAiCheck`.
- Четыре последовательных живых capability-прогона прошли полностью: endpoint, модель, русский язык, strict JSON, grounding, безопасный abstain, follow-up и контекст. Средняя задержка была примерно `1,3–1,4 с`.

### Безопасность ответов и fallback

- Модель обязана использовать точные `usedFactIds`, если подтверждённые факты прямо отвечают на вопрос.
- Ответ без проверенных фактов допускается только в канонической безопасной форме; слухи и игровые утверждения модели не попадают пользователю.
- Capability gate теперь проверяет наличие всех полей JSON Schema и прогоняет результат через production validator.
- Исправлена пустая проверка follow-up fact IDs.
- Добавлены тесты отсутствующего провайдера и перехода с недоступного primary на следующий configured fallback.

### Любая установленная модель LM Studio

- Исправлен дефект, при котором кнопка загрузки всегда подменяла ручной Model ID моделью из встроенного каталога.
- Настройки больше не перезаписывают ручной выбор при открытии страницы или при наличии нескольких загруженных моделей.
- Установленные модели и рекомендуемые для скачивания модели разделены в UI.
- API-поле `type` теперь разбирается отдельно от `format`: embedding-модели, включая Nomic Embed, не попадают в список chat-моделей.
- «Настроить автоматически» сначала использует уже установленную LLM и не начинает лишнее скачивание.
- Кнопка «Использовать выбранную» загружает выбранную LLM, запускает capability-test и только после успеха сохраняет её как действующий Local Chat route. При провале предыдущий маршрут не меняется.
- Добавлен поиск нестандартной установки LM Studio через `%USERPROFILE%\.lmstudio\.internal\app-install-location.json`; ручные пути к `lms.exe` и `LM Studio.exe` сохранены.

### Импорт GGUF и выбор папки

- В UI добавлены «Выбрать GGUF-файл» и «Найти GGUF в папке».
- Сканирование выполняется только внутри явно выбранной папки, не следует junction/reparse point, поддерживает отмену и не импортирует несколько моделей молча.
- Перед импортом проверяются расширение и сигнатура `GGUF`; `mmproj`, повреждённые и sharded-файлы отклоняются с понятной ошибкой.
- Импорт выполняется официальной командой LM Studio в два шага:
  1. `lms import <path> --copy --yes --user-repo local-imports/<slug-hash> --dry-run`;
  2. `lms import <path> --copy --yes --user-repo local-imports/<slug-hash>`.
- `--copy` обязателен: исходный пользовательский файл не перемещается и не удаляется.
- Unicode-пути и пути с пробелами передаются через `ProcessStartInfo.ArgumentList`; timeout, cancellation и завершение дерева процессов сохранены.
- После импорта новый model key определяется сравнением `lms ls --llm --json`, затем модель автоматически загружается и проходит тот же capability gate.
- На реальном пользовательском GGUF выполнен только безопасный dry-run; копирование не запускалось, исходный файл не изменялся.

Официальные опорные документы: [LM Studio import](https://lmstudio.ai/docs/app/advanced/import-model), [CLI и список моделей](https://lmstudio.ai/docs/cli), [structured output](https://lmstudio.ai/docs/developer/openai-compat/structured-output).

## Проверка на этой точке

- Полный `eng/build.ps1 -Configuration Release -Runtime win-x64 -SoakIterations 10` — успешно.
- Release build — `0` ошибок, `0` предупреждений.
- Все `225` тестов прошли:
  - Core: 71;
  - Providers: 20;
  - App: 29;
  - Integration: 28;
  - Knowledge: 64;
  - ModelBenchmark: 13.
- Governance lint — `0` ошибок, `0` предупреждений.
- Strict knowledge pack — 44 статьи, 202 факта, валиден.
- WPF smoke — успешно, exit code 0, изолированный профиль.
- Двухпроцессный реальный UI E2E LM Studio/Qwen — успешно. Отчёты: `artifacts/local-ai-e2e/20260718-102430` и `artifacts/local-ai-e2e/20260718-103017`.
- Offline `lms import --dry-run` с локальным repository ID — успешно, исходная модель не изменена.
- Фактическая загрузка Qwen подтверждена с context 4096, parallel 1 и TTL 300000 мс.
- Install/uninstall smoke из нестандартного пути с пробелами — успешно.
- Lifecycle soak — 10/10, максимальный peak working set 211,79 МБ; отчёт `artifacts/reports/lifecycle-soak.json`.
- Portable ZIP: `artifacts/release/GtaRpAssistant-0.2.0-win-x64.zip`.
- SHA-256 после текущего UI-среза: `dbc999382f0953e6f67fd81e6c48150f6717d98a87dcac79ef7f9e605d30e4cb`.

## Основные изменённые части

- `src/GtaRpAssistant.Providers/OpenAiCompatibleChatProvider.cs`
- `src/GtaRpAssistant.Providers/LocalAiCapabilityTester.cs`
- `src/GtaRpAssistant.Core/DecisionServices.cs`
- `src/GtaRpAssistant.Core/AssistantSessionCoordinator.cs`
- `src/GtaRpAssistant.Core/LocalAiManagement.cs`
- `src/GtaRpAssistant.Infrastructure.Windows/LocalAiEngineManager.cs`
- `src/GtaRpAssistant.App/Features/FeatureViewModels.cs`
- `src/GtaRpAssistant.App/Features/Providers/ProvidersView.xaml`
- `src/GtaRpAssistant.App/Services/AppDialogService.cs`
- `src/GtaRpAssistant.App/Services/LocalModelFileDiscovery.cs`
- `src/GtaRpAssistant.App/Services/UiAutomationScenarioService.cs`
- `src/GtaRpAssistant.App/DesignSystem/Controls/OverlayCardShell.cs`
- `src/GtaRpAssistant.App/DesignSystem/Controls/OverlayStatusPill.cs`
- `src/GtaRpAssistant.App/DesignSystem/Controls/QuickActionsBar.xaml`
- `src/GtaRpAssistant.App/DesignSystem/Controls/VisionConsentCard.xaml`
- `src/GtaRpAssistant.App/VisionPreviewWindow.xaml`
- `src/GtaRpAssistant.App/DesignSystem/Tokens/Colors.xaml`
- `src/GtaRpAssistant.App/DesignSystem/Styles/Surfaces.xaml`
- `src/GtaRpAssistant.App/OverlayWindow.xaml`
- `src/GtaRpAssistant.App/ExpandedOverlayWindow.xaml`
- `tools/GtaRpAssistant.LocalAiCheck/`
- `eng/local-ai-e2e.ps1`
- соответствующие Core, Provider, App и Integration tests.

## Завершённый логический этап

Этап стабилизации Local AI, свободного выбора модели LM Studio, безопасного импорта GGUF и релизной проверки закрыт. Приложение компилируется, проходит все тесты и WPF smoke. Реальный автоматизированный UI E2E подтвердил, что Qwen виден в списке, Nomic Embed отфильтрован, кнопка «Использовать выбранную» проводит capability-test, а выбранная модель и Local Chat route восстанавливаются во втором процессе.

Исправлен offline-import: без `--user-repo` LM Studio пыталась обратиться к Hugging Face даже при локальном dry-run. Теперь создаётся стабильный безопасный `local-imports/<slug-hash>`, поэтому dry-run работает без сети. Полное копирование намеренно не запускалось на единственном пользовательском файле модели.

Профиль загрузки унифицирован с CLI LM Studio: реальная загрузка и estimate используют одинаковые context, parallel и GPU policy; TTL применяется к загрузке. Полный релизный pipeline и документация завершены.

## Следующие действия — не начаты

В следующей сессии выполнять именно в таком порядке:

1. Обновить feature pages shell, добавив единые icon/status/empty/error компоненты и страницу диагностики/«О приложении».
2. Проверить полное копирование импорта на отдельном тестовом GGUF или безопасной копии; не использовать единственный экземпляр пользовательской модели. Dry-run, offline repository ID, аргументы, timeout и cancellation уже проверены.
3. Провести длительные эксплуатационные сценарии: 30-минутный idle, restart runtime, sleep/resume, audio unplug/replug, недоступный endpoint, cloud-only и GTA-under-load.
4. Провести аппаратную UI-матрицу Windows 10/11, DPI 100/150/200%, второй монитор и borderless GTA.
5. Подготовить подписанный installer/автообновление; Ollama и безопасное удаление моделей остаются опциональными backend-этапами.
6. Git metadata и профессиональные коммиты восстановить отдельно, не задерживая функциональное развитие.

## Что не делать при возобновлении

- Не удалять и не перемещать файлы из `E:\Download_models\models` вручную.
- Не включать cloud без явного согласия пользователя.
- Не считать mock `MicroModelHost` реальной встроенной моделью: отклонённые 0.6B/360M кандидаты по-прежнему не входят в релиз.
- Не смешивать следующий UI-срез с изменениями Core/Local AI: каждое визуальное улучшение должно отдельно проходить smoke/snapshot gate.
