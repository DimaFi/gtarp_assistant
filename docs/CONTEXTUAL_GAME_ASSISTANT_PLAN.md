# Контекстный игровой ассистент — аудит и план внедрения

Актуально на 18 июля 2026 года.

Этот документ сверяет существующий GTA RP Assistant с ТЗ на контекстного игрового помощника. Он дополняет `ARCHITECTURE.md`, `AI_ROUTING.md`, `PRODUCT_AND_UI_PLAN.md` и `MASTER_SPEC_AUDIT.md`, но не заменяет их.

## Краткий вывод

Приложение уже работает как knowledge-first помощник: пользователь вводит вопрос или использует микрофон, запрос проходит intent, локальный SQLite-поиск, provider routing, grounded JSON validation и показывается в overlay. Без LM Studio приложение отвечает подготовленным или коротким extractive-ответом из проверенной базы.

Контекстный сценарий из нового ТЗ пока не является единым pipeline. Ручной vision, отслеживание процесса GTA, временный диалог и knowledge-поиск существуют отдельно, но обычный вопрос не собирает их через общий `ContextCollector`. Нет модели активных игровых событий и локального профиля игрока. Горячая клавиша overlay не открывает компактное поле текстового ввода: текст вводится на странице Assistant, а отдельные hotkeys запускают manual voice и manual vision.

Следовательно, следующий этап — не новый AI-клиент и не замена coordinator, а совместимый слой контекста перед существующим `AssistantSessionCoordinator`.

## Что уже реализовано и переиспользуется

| Область | Готовое основание | Текущая граница |
|---|---|---|
| Запрос и pipeline | `AssistantSessionCoordinator`, `AssistantProcessingRequest`, state machine, cancellation, single-flight | В запросе есть текст, activation, server и cloud consent, но нет составного игрового контекста |
| Текстовый ввод | `AssistantFeatureViewModel` и `AssistantView` | Работает в главном окне, не в compact overlay по горячей клавише |
| Голос | WASAPI, VAD, segmenter, bounded buffers, локальный/настроенный STT route | В LLM передаётся текст, исходное аудио не передаётся; это соответствует ТЗ |
| Intent | `RuleBasedIntentDetector`, `AssistantRequestClassifier`, `IntentDecision` | Есть deterministic baseline и `RequiresScreen`, но нет полной классификации из ТЗ, risk level и строгого JSON-контракта |
| Knowledge | SQLite v3, exact alias/prepared answer, FTS5 по статьям и фактам, server scope, expiry/conflict guard | Это рабочий lexical RAG; нет общего `KnowledgeDocument` metadata, embeddings и reranking |
| Grounding | `GroundingContextSelector`, `GroundedAnswerValidator`, официальный/community provenance | Проверяются fact ID, сервер, числа, URL, длина и запрещённая автоматизация |
| Ответ без модели | prepared answer и `knowledge-extractive` fallback | Уже отвечает на типовые вопросы при недоступных провайдерах |
| Диалог | `InMemoryAssistantConversationStore`, TTL, capacity, situation ID, follow-up | Хранятся последние релевантные turns; отдельного компактного summary пока нет |
| Model routing | независимые provider connections/routes, capabilities, health, local/cloud/custom fallback | Chat/STT/Vision/TTS разделены; встроенный MicroModel остаётся mock по ADR |
| GTA process | `GameSessionMonitor`, `GameProcessDetector` | Есть PID и main window handle; нет location/activity/task context |
| Screenshot | `WindowCaptureService` и `VisionWorkflowService` | Один ручной снимок, preview/consent, очистка памяти; не встроен в вопрос и нет downscale/redaction |
| Overlay | compact non-activating и expanded interactive overlay, `OverlayPresentation`, `Esc`, источники и feedback | Compact показывает статус/ответ, но не принимает текст и не отражает все стадии нового pipeline |
| Privacy/security | DPAPI, cloud opt-in, preview vision, безопасные логи, anti-cheat boundaries | Нужны отдельные настройки screen mode/cloud vision/history и формализованный capture indicator |
| Performance | lazy providers, bounded queues, one request, собственный process monitor | Нет общего total timeout и отдельных timeout каждого нового context provider |
| Release quality | 231 тест, strict knowledge gate, WPF smoke, 10 snapshots, custom-path install smoke | Нужны новые context/vision/event/fallback сценарии и аппаратная fullscreen QA |

## Что отсутствует

### Блокирующие пробелы для контекстного сценария

1. `ContextCollector`, объединяющий независимые источники с частичными результатами, timeout и cancellation.
2. Общие контракты `AssistantContext`, `ScreenContext`, `GameEventContext`, `UserGameProfile` и расширенный intent.
3. `ScreenRequirementClassifier` с режимами `automatic`, `always`, `never`.
4. Связь обычного вопроса с одним снимком, vision-описанием и дальнейшим knowledge/chat ответом.
5. `GameEventContextProvider` и локальное хранилище событий.
6. Локальный профиль игрока и правила запроса одного уточнения при неизвестном критичном параметре.
7. Компактный текстовый input overlay по горячей клавише.

### Важные, но не блокирующие улучшения

- metadata-rich knowledge result с category, tags, source URL, relevance и updatedAt;
- optional embeddings и reranking после стабильного lexical baseline;
- source citations как структурированная коллекция, а не одна строка `SourceTitle`;
- структурированные warnings/actions/confidence;
- отдельные stage timeouts и общий request budget;
- downscale, client-area capture и подключаемая redaction sensitive regions;
- локальное summary диалога вместо передачи только последних turns;
- настраиваемое сохранение истории; по умолчанию — выключено;
- аппаратные тесты borderless/exclusive fullscreen, DPI и двух мониторов.

## Архитектурное решение

Существующий `AssistantSessionCoordinator` сохраняется владельцем knowledge/model/validation части. Перед ним добавляется тонкий controller и агрегатор контекста:

```text
Hotkey / main page / manual voice
    → AssistantRequestController
    → ScreenRequirementClassifier
    → ContextCollector
        ├─ GameProcessContextProvider
        ├─ ScreenContextProvider (только если разрешён и нужен)
        ├─ GameEventContextProvider
        ├─ ConversationContextProvider
        └─ UserGameProfileProvider
    → existing AssistantSessionCoordinator
    → existing Knowledge / Router / Validator
    → existing OverlayService
```

Каждый context provider возвращает собственный результат и диагностику. Ошибка или timeout vision не отменяет knowledge-поиск. Общий collector не содержит WPF, Win32, SQLite или HTTP-код.

## Совместимость контрактов

Нельзя сразу заменять `AssistantProcessingRequest`, `IntentDecision`, `KnowledgeMatch` и `AssistantAnswer`: они используются Core, Providers, App и тестами. Новый слой вводится адаптерами.

Планируемые Core-типы:

- `AssistantRequest` — исходный текст/voice transcript, activation, server и privacy flags;
- `AssistantContext` — immutable snapshot собранного контекста и ошибок отдельных источников;
- `ScreenContext` — availability, description, visible UI, location hints и confidence;
- `GameEventContext` — id, окно активности, этап, ограничения, награды, location и participation;
- `UserGameProfile` — сервер, уровень, доступы, стиль и приоритет с датой подтверждения;
- `IntentClassification` — тип, needs flags, risk и confidence;
- `KnowledgeDocument` / `KnowledgeSearchResult` — metadata projection поверх текущего `KnowledgeMatch`;
- `ModelRequest` / `ModelResponse` — структурированный envelope вокруг текущего grounded request/response;
- `AssistantClarification` и `AssistantError` — типизированные результаты, адаптируемые в текущий `AssistantAnswer`.

## Этапы внедрения

### Этап C1 — контракты и пустой context foundation

Добавить Core-контракты, `IContextProvider<T>`, `IContextCollector`, `IScreenRequirementClassifier`, timeout options и deterministic classifier. Реализовать collector с независимыми provider tasks, общим deadline, частичными результатами и cancellation. Подключить адаптер текущего conversation snapshot и game process info, но не менять пользовательский поток и не запускать capture автоматически.

Планируемые файлы:

- добавить `src/GtaRpAssistant.Core/ContextContracts.cs`;
- добавить `src/GtaRpAssistant.Core/ContextCollection.cs`;
- добавить `src/GtaRpAssistant.Core/ScreenRequirementClassifier.cs`;
- добавить `tests/GtaRpAssistant.Core.Tests/ContextCollectionTests.cs`;
- точечно расширить `src/GtaRpAssistant.Core/Interfaces.cs`;
- зарегистрировать foundation в `src/GtaRpAssistant.App/App.xaml.cs` без включения нового поведения.

Критерии завершения:

- контракты не зависят от WPF/Windows/SQLite;
- automatic classifier не требует снимок для обычных mechanic/economy вопросов;
- timeout одного provider возвращает partial context;
- отмена завершает все незавершённые операции;
- текущие 231 тест и новые unit-тесты проходят;
- внешний пользовательский сценарий не меняется.

### Этап C2 — единый ручной request controller и input overlay

Добавить `AssistantRequestController`, через который проходят main-page text, manual voice и hotkey input. Compact hotkey открывает минимальный ввод; Enter отправляет, Escape закрывает. Один active request, повторное нажатие отменяет или возвращает focus согласно UI-контракту.

Планируемые изменения:

- новый controller в App;
- input state в `OverlayPresentation` или отдельный reusable `OverlayInputCard`;
- подключение в `ApplicationLifecycleCoordinator` и `AssistantFeatureViewModel`;
- UI Automation для hotkey → input → answer и cancellation.

### Этап C3 — screen decision и vision в основном pipeline

Подключить режимы `automatic/always/never`. При необходимости сделать ровно один снимок окна GTA, показать capture indicator и preview/consent перед cloud vision. Добавить downscale, client-area crop и memory-only lifecycle. Результат vision становится недоверенным `ScreenContext`, а не источником игровых правил.

При timeout/ошибке vision pipeline продолжает работать по вопросу и knowledge.

### Этап C4 — игровые события и профиль пользователя

Добавить versioned локальный JSON/SQLite store событий и профиль пользователя с `confirmedAt`. События фильтруются по текущему времени и серверу. Неизвестный параметр, меняющий рекомендацию, приводит максимум к одному короткому clarification.

### Этап C5 — metadata RAG и citations

Проецировать текущие статьи в `KnowledgeDocument`, добавить category/tags/source URL и relevance. Сохранить exact/prepared/FTS как обязательный baseline. Затем опционально добавить embedding route и reranker; их недоступность не ухудшает lexical поиск.

### Этап C6 — structured contextual model request

Передавать модели ограниченный `AssistantContext`, не огромный текст. Расширить JSON Schema warnings/actions/sources/confidence с обратным адаптером в `AssistantAnswer`. Сохранить проверку allowed fact IDs и неподтверждённых чисел во всех строках.

### Этап C7 — производительность, приватность и аппаратная QA

Добавить отдельные timeout settings, total deadline, cache policy и экран настроек privacy. Проверить Windows 10/11, RTX 3060/16 GB, borderless/exclusive fullscreen, DPI 100/150/200%, два монитора, cloud disabled, provider timeout и GTA under load.

Современная визуальная доводка выполняется через существующую дизайн-систему и план UI-7, без специальных GTA-условий в XAML.

## Настройки, которые потребуются

```text
screenAnalysisMode: automatic | always | never
allowScreenCapture: false
allowCloudVision: false
saveScreenshots: false
saveConversationHistory: false
showCaptureIndicator: true

classificationTimeoutMs: 3000
screenCaptureTimeoutMs: 3000
screenAnalysisTimeoutMs: 15000
knowledgeSearchTimeoutMs: 5000
generationTimeoutMs: 30000
totalRequestTimeoutMs: 45000
```

Без миграции эти значения должны иметь privacy-safe defaults. `allowCloudVision` не выводится из общего `AllowCloud` автоматически.

## Риски и меры

| Риск | Мера |
|---|---|
| Излишний capture и нагрузка | `automatic` classifier, one-shot capture, no background loop |
| Снимок содержит чужие окна | захват найденного GTA window/client area, preview и redaction до отправки |
| Vision выдумывает игровое правило | маркировать как untrusted screen description; правила только из verified knowledge |
| Событие или профиль устарели | `updatedAt`, `validUntil`/`confirmedAt`, server filter и clarification |
| Новый orchestrator дублирует coordinator | controller собирает контекст, существующий coordinator сохраняет routing/validation |
| Timeout одного источника срывает ответ | partial context и общий deadline |
| Новая схема ломает providers | versioned envelope и адаптер к текущему `GroundedAnswerRequest` |
| Overlay начинает мешать GTA | compact non-activating по умолчанию; input/expanded только по явному действию |
| Функция воспринимается как автоматизация | никаких SendInput, memory injection, aiming, actions или anti-cheat обхода |

## Полная готовность контекстного сценария

Функция считается полностью готовой только когда hotkey открывает компактный ввод, controller классифицирует запрос, collector безопасно собирает разрешённые источники, automatic mode при необходимости делает один подтверждённый снимок, события и профиль учитываются, knowledge возвращает citations, ответ проходит grounding validation и отображается в overlay при любых отказах отдельных providers.

На текущем срезе это ещё не выполнено полностью. Готовы knowledge-first вопросы, voice/manual vision, routing, validation и overlay; не готовы единый context collector, integrated screen decision, game events, user profile и hotkey text input.

## Результат текущего логического этапа

Этап анализа завершён этим документом. Код контекстного pipeline намеренно не менялся до фиксации контрактов и границ. Следующий рекомендуемый и ограниченный срез — C1: контракты и context foundation без изменения поведения приложения.
