# Smart RP Assistant — архитектурный аудит и целевая система

Статус: проектный документ, 15 августа 2026 года. Он описывает развитие текущего приложения, а не переписывание с нуля. Источник истины — текущий код; существующие специализированные документы сохраняют силу в своих областях.

## 1. Резюме решения

RP Assistant должен быть не «GPT‑5VP с длинным prompt», а локальной knowledge-first системой. Тяжёлая модель — последний, а не первый этап ответа.

Целевой горячий путь:

```text
запрос
  → нормализация и дешёвая классификация
  → точный ответ / prepared answer / response cache
  → SQLite FTS5 + фильтры сервера и актуальности
  → экстрактивный ответ из проверенных фактов
  → только при реальной необходимости: короткий запрос к GPT-5VP
  → validator → ответ → кэш
```

GPT‑5VP считается изначально выбранной тяжёлой локальной chat/vision-моделью. Она не должна постоянно держать vision-контекст, получать всю историю или вызываться для ответа, уже существующего в локальной БД. При запущенной GTA приоритет получают FPS игры и резерв VRAM; модель выгружается по idle TTL или работает с консервативным offload.

Наибольший прирост «интеллекта на единицу ресурса» дадут не более крупная модель и не постоянный vision, а:

1. качественные prepared answers и метаданные знаний;
2. явный каскад маршрутизации с быстрым выходом до provider health-check;
3. малый Context Builder с бюджетом по секциям;
4. session summary и релевантная, а не полная память;
5. метрики cache-hit, LLM-avoidance, prompt tokens и p95 latency;
6. OCR/event detection перед VLM.

## 2. Проверенное текущее состояние

### 2.1 Уже реализовано и пригодно для переиспользования

| Подсистема | Фактическая реализация | Решение |
|---|---|---|
| Основной coordinator | `AssistantSessionCoordinator` | Расширять, затем постепенно разнести policy/context orchestration по сервисам |
| Intent baseline | `RuleBasedIntentDetector`, `AssistantRequestClassifier` | Оставить первым дешёвым слоем |
| Контекст transcript | `ContextSelector`, лимит 2000 символов | Переиспользовать внутри нового Context Builder |
| Knowledge | `SqliteKnowledgeRepository`, exact aliases, prepared answers, SQLite FTS5 | Сделать главным маршрутом |
| Grounding | `GroundingContextSelector`: до 6 фактов и 1600 символов | Оставить hard budget; сделать профильным |
| Ответ без LLM | prepared/extractive deterministic answer и безопасный abstain | Расширить покрытие |
| Валидация | `GroundedAnswerValidator`, fact IDs, server scope, числа, URL, conflict/outdated guards | Сохранить обязательным после генерации |
| Providers | OpenAI-compatible Chat/Vision/STT, LM Studio, `/v1/models`, capability test | Сохранить через adapters/providers |
| Local AI lifecycle | `LocalAiEngineManager`, LM Studio discovery/load/unload/estimate | Расширить resource leases и telemetry |
| История | in-memory или opt-in `SqliteAssistantConversationStore` | Разделить журнал UI и model-context projection |
| User memory | отдельный `SqliteUserMemoryStore`, ручное управление, local-only injection | Добавить candidates, provenance и retrieval |
| Screen | capture, grid diff, Tesseract OCR, TTL store, explicit screen answers | Использовать как дешёвый event/OCR слой |
| Vision | ручной preview/consent, отдельный provider route | Оставить on-demand; не делать постоянным |
| Voice | WASAPI, bounded buffers, STT routes, hotkeys, preview/cancel | Переиспользовать |
| Privacy | local-by-default, DPAPI secrets, отдельные consent boundaries | Сохранить как архитектурный инвариант |
| Ограничение нагрузки | single-flight chat, bounded audio, `PerformanceController`, model idle TTL | Расширить; текущий контроллер слишком грубый |
| Проверка продукта | unit/integration, production knowledge benchmark, smoke и model/STT gates | Добавить измерения маршрутов и токенов |

### 2.2 Текущий реальный pipeline

`AssistantSessionCoordinator.ProcessAsync` уже выполняет дедупликацию, intent, screen fast-path, retrieval, routing, provider generation, repair, validation и показ ответа. Knowledge-first режим работает без LM Studio.

Однако есть архитектурная потеря времени: после получения knowledge match coordinator запрашивает `ChatProviderCatalog.GetAvailabilityAsync` до выбора route. Даже prepared answer может дождаться health-check провайдеров при холодном/просроченном 30-секундном кэше. Это противоречит цели «БД отвечает первой».

Текущий model request уже ограничен:

- последние 6 conversation turns;
- до 6 проверенных facts / 1600 символов;
- transcript context около 2000 символов;
- `max_tokens` обычно 420, для problem solving — 700;
- temperature 0 и structured JSON;
- user memory передаётся только локальному provider.

Это хорошая основа, но prompt всё ещё содержит многословную постоянную схему, всегда сериализует много пустых полей, а невалидный ответ способен удвоить стоимость вторым repair-вызовом.

## 3. Основные проблемы

### P0 — влияют на каждый ответ

1. **Provider availability проверяется слишком рано.** Deterministic route обязан завершаться без health-check и без загрузки GPT‑5VP.
2. **Нет явного response/query cache.** Повторные и близкие вопросы снова проходят retrieval и иногда генерацию.
3. **Нет общего Context Builder.** Ограничения существуют, но распределены между transcript selector, grounding selector, conversation store и provider serializer.
4. **Маршрутизация знает наличие prepared answer, но не учитывает уверенность retrieval, стоимость модели, состояние GTA, pressure VRAM/RAM и latency SLO.**
5. **Нет измерения prompt/completion tokens и доли ответов без модели.** Оптимизацию нельзя доказать.

### P1 — ограничивают качество диалога

6. UI history и model context концептуально близки; нет summary старой части разговора.
7. Follow-up опирается на последние turns и `SituationId`, но не на компактное состояние задачи: цель, ограничения, уже выполненные шаги, открытый вопрос.
8. User memory в основном ручная; нет безопасного candidate workflow, deduplication и relevance scoring.
9. Lexical RAG силён на известных формулировках, но не имеет опционального semantic fallback/reranker.
10. Общий coordinator становится местом всех решений и будет труден для тестирования при добавлении tools/web/vision.

### P2 — ресурсы и автономность

11. `PerformanceController` смотрит только CPU процесса и working set, пороги 15%/200 MB; он не знает FPS, системную RAM, VRAM, GPU contention, model residency и очереди.
12. Vision и OCR имеют правильные privacy boundaries, но ещё нет полноценной taxonomy событий/known screens.
13. Нет общего tool contract, time budget, cancellation policy и audit trail.
14. Web intelligence отсутствует как контролируемый freshness fallback.

## 4. Целевая архитектура

```text
Text / PTT / Voice / Explicit Screenshot
                    │
          Interaction Manager
                    │
        Query Normalizer + Fast Router
          │         │          │
   screen fast   exact/cache   conversational
          │         │          │
          └──── Context Planner ┘
                    │
         Assistant Orchestrator
          │       │       │
     Knowledge   Tools   GPT-5VP gateway
       │          │       │
 SQLite FTS5   OCR/Web   bounded prompt
 prepared/cache  adapters  structured JSON
          └───────┬───────┘
              Validator
                    │
          Overlay / Voice / History

Side planes:
  ResourceBudgetCoordinator ─ leases, telemetry, degradation
  MemoryService             ─ session/profile/candidates
  Observability             ─ latency, tokens, route, cache, load
  PrivacyPolicy             ─ local/cloud/screen/memory consent
```

### 4.1 Границы модулей

- `Core` содержит contracts, policy и чистые decision services; не зависит от SQLite, WPF, Windows или LM Studio.
- `Knowledge` хранит и ищет проверенные знания; не знает о UI и модели.
- `LocalData` хранит диалоги, memory candidates, profile и cache metadata.
- `Providers` сериализует минимальные provider requests и нормализует ответы.
- `Infrastructure.Windows` даёт hardware telemetry, capture, audio и runtime adapters.
- `App` связывает use cases и отображает состояние, но не принимает архитектурных решений.
- GTA-специфика помещается в `IGameAdapter`/`Gta5RpAdapter`: словарь, server scope, screen recognizers, knowledge pack.

## 5. Каскад маршрутизации

### 5.1 Маршруты

| Route | Когда | LLM | Цель p95 |
|---|---|---:|---:|
| `ScreenObservation` | явный вопрос и свежий OCR | нет | <100 ms после OCR |
| `ExactAnswer` | exact alias / verified prepared answer | нет | <50 ms warm |
| `ResponseCache` | совпали normalized query, server, knowledge version, policy/profile | нет | <20 ms |
| `ExtractiveKnowledge` | уверенный FTS result и 1–2 самодостаточных факта | нет | <100 ms |
| `GroundedCompose` | нужно объединить факты/контекст | GPT‑5VP text | <2.5 s warm target |
| `Clarify` | неоднозначность дешевле генерации | нет или короткая LLM | <150 ms |
| `VisionOnDemand` | OCR недостаточен и пользователь запросил экран | GPT‑5VP vision | отдельный SLO |
| `WebFreshness` | запрос требует свежести, локальные данные устарели, есть consent | tool + optional compose | best effort |
| `Abstain` | нет достаточного ground truth | нет | <100 ms |

### 5.2 Обязательный порядок

1. Normalize: регистр, пробелы, известные GTA-термины, server scope; не использовать LLM.
2. Safety/scope checks.
3. Fresh screen deterministic path.
4. Exact/prepared lookup.
5. Versioned response cache.
6. FTS5 retrieval и confidence/margin gate.
7. Если факты можно показать без перефразирования — extractive answer.
8. Только теперь получить provider availability и resource lease.
9. Если GPT‑5VP запрещена/не помещается/занята — deterministic fallback.
10. Для сложного вопроса собрать bounded context и вызвать модель один раз.
11. Repair разрешён только для schema errors и с более коротким prompt; максимум один retry.

### 5.3 Не использовать LLM для

- exact FAQ, цен и правил с готовым проверенным ответом;
- определения свежести/конфликта/server scope;
- очистки строки, hotkey, lifecycle и permissions;
- простого OCR-ответа «что написано»;
- cache lookup, ranking baseline и дедупликации;
- resource scheduling;
- удаления/редактирования памяти;
- вычислений по структурированным локальным таблицам.

## 6. Локальная БД, retrieval и кэш

### 6.1 SQLite остаётся обязательным baseline

SQLite/FTS5 уже обеспечивает быстрый, дешёвый и предсказуемый путь. Qdrant не должен быть runtime-зависимостью приложения только потому, что он уже используется как память Codex workspace.

Хранение:

- `knowledge.db`: articles, facts, aliases, prepared answers, sources, revisions, server scope, FTS indexes;
- `assistant-data.db`: opt-in conversation journal и summaries;
- `user-memory.db`: подтверждённая память, candidates, sources, profile;
- отдельный небольшой response cache допустим в `assistant-data.db` или собственном `answer-cache.db`.

### 6.2 Versioned response cache

Ключ:

```text
hash(normalized_query, server_scope, knowledge_revision,
     response_policy_version, language, profile_class)
```

В value: финальный validated answer, usedFactIds, source revisions, route, created/expires. Cache не хранит screen answers и персонализированный текст без включения memory/profile revision в ключ. Изменение article revision инвалидирует связанные entries. Для prepared answers допустим долгий TTL; для web-derived — короткий TTL и source timestamp.

### 6.3 Semantic retrieval — только опциональный второй слой

Embeddings полезны при низком FTS score или малом margin между кандидатами. Они не нужны на каждом запросе. Начальный вариант:

1. FTS top 8;
2. metadata/server/freshness filter;
3. дешёвый lexical rerank;
4. semantic rerank top 8 только если профиль и budget разрешают;
5. итоговые top 1–3 articles и не более 6 facts.

Для небольшой базы embeddings можно хранить локально и грузить лениво. Qdrant оправдан при десятках тысяч chunks, сложных фильтрах или нескольких knowledge packs; до этого SQLite + vector extension/flat local index проще и дешевле.

## 7. Context Builder и токен-бюджет

Новый `IContextBuilder` получает `ContextPlan`, а не «всё доступное». Бюджет задаётся одновременно в токенах и символах; перед отправкой используется tokenizer конкретной модели, при его отсутствии — консервативная оценка.

Статус: первый production-вариант реализован как `IAssistantContextBuilder`/`AssistantContextBuilder`. Balanced target — 1600 input tokens; facts до 6/1200 символов, transcript 450, conversation 600, user memory 240; output 300 либо 450 для problem solving. Текущая оценка консервативна (три UTF-16 символа на токен плюс 700 tokens policy/schema reserve); model-specific tokenizer остаётся будущим улучшением.

Рекомендуемый warm profile для GPT‑5VP text:

| Секция | Мягкий бюджет |
|---|---:|
| Сжатая system policy | 250–400 tokens |
| Текущий вопрос | 64–160 |
| Verified facts | 250–500 |
| Conversation summary | 100–220 |
| Последние turns | 200–450 |
| Relevant user memory | 0–120 |
| Screen observation | 0–120 |
| Output/schema instruction | 120–220 |
| Итого input target | 800–1,600 |
| Output target | 120–300, problem solving до 450 |

Текущие 420/700 output tokens следует понизить по умолчанию и повышать только для `ProblemSolving`. Пустые массивы и поля не должны сериализоваться. Полную JSON Schema можно заменить provider-native cached grammar или компактным schema ID там, где runtime это поддерживает; fallback остаётся совместимым.

Hard rules:

- ни один раздел не «заимствует» весь остаток бюджета;
- facts имеют приоритет над историей и стилем;
- память не считается knowledge;
- старые turns заменяются summary;
- screen text помечается untrusted observation;
- если context не помещается, система уменьшает turns → memory → secondary facts, а не обрезает текущий вопрос и главный факт.

## 8. Память

### 8.1 Уровни

| Уровень | Хранение | TTL/контроль | В prompt |
|---|---|---|---|
| Request | память процесса | один запрос | всегда только релевантное |
| Short-term | последние 4–6 turns | текущий диалог | ограниченно |
| Session | structured state + rolling summary | до конца игровой сессии | по intent |
| Long-term | `user-memory.db` | opt-in, CRUD пользователем | local-only, top 0–3 |
| Knowledge | `knowledge.db` | pack governance | как verified facts |

`SessionState` должен хранить не transcript, а `currentGoal`, `situation`, `constraints`, `completedSteps`, `openQuestion`, `lastGroundedArticleIds`, timestamps и provenance.

### 8.2 Запись долговременной памяти

Автоматически нельзя сразу сохранять утверждение модели. Pipeline: candidate extraction → deterministic PII/category policy → deduplication → UI confirmation или строго разрешённая auto-category → confirmed memory. Каждая запись содержит source turn, confidence, confirmation, timestamps и revision.

### 8.3 UI памяти

Показывать раздельно:

- RAM процесса и модели;
- VRAM модели/vision и резерв игры;
- размер SQLite knowledge/history/user-memory на диске;
- ориентировочный model context в tokens;
- количество подтверждённых memories и candidates.

Единое число «память ассистента 4 GB» вводит в заблуждение и не используется.

## 9. GPT‑5VP и vision

GPT‑5VP по умолчанию используется как text generator только после knowledge gate. Vision-часть не активируется автоматически из-за того, что модель её поддерживает.

Vision ladder:

1. window presence / known region;
2. уменьшенный frame diff;
3. known-screen/event classifier;
4. OCR нужных ROI;
5. deterministic interpretation;
6. единичный VLM screenshot только по запросу/согласию;
7. continuous VLM — не целевой режим для обычного игрового ПК.

Перед VLM кадр уменьшается до минимального разрешения, достаточного для задачи; вырезаются только нужные ROI, если вопрос локален. Нельзя прикладывать серию кадров и полный transcript без отдельного плана. Image bytes очищаются после запроса, как в текущем workflow.

## 10. Tools и web

Tool contract:

```text
ToolRequest(name, arguments, deadline, privacyClass, requiresConfirmation)
ToolResult(status, structuredData, sources, observedAt, expiresAt, diagnostic)
```

Сначала поддерживаются только read-only tools: knowledge lookup, calculator для локальных таблиц, current screen observation и явно разрешённый web search. Tool selection по возможности deterministic. LLM tool calling применяется для неоднозначной композиции, но allowlist, argument validation, deadline, cancellation и максимум вызовов задаются orchestrator.

Web используется лишь когда вопрос явно требует свежести или локальный документ помечен outdated. Результат не становится knowledge автоматически. Он получает источник, дату, TTL и видимую пользователю маркировку; сохранение в pack — отдельный governance workflow.

## 11. Voice

Существующий bounded voice pipeline сохраняется. Для низкой задержки:

- push-to-talk — основной режим;
- STT запускается отдельно от GPT‑5VP;
- partial transcript не отправляется chat-модели;
- после финального transcript сначала выполняется exact/cache lookup;
- TTS получает короткий `speechSummary`, а не полный structured answer;
- при pressure отключаются game-audio STT, TTS и background processing раньше ручного текста.

## 12. Resource Budget Coordinator

Новый coordinator выдаёт workload lease для `Chat`, `Vision`, `STT`, `Embeddings`, `TTS`, `BackgroundIndexing`. Он не обещает точность ОС, а принимает решение по telemetry и conservative estimates.

Входы:

- system available/committed RAM;
- process working set и queue depth;
- GPU dedicated used/free, если telemetry доступна;
- GTA detected/running и optional frame-time signal;
- model estimated residency, context length, GPU layers;
- workload priority и deadline.

Порядок деградации при игре:

1. остановить background indexing;
2. не загружать embeddings;
3. отключить proactive/background OCR frequency;
4. запретить VLM и предложить OCR/manual text;
5. уменьшить context/output и GPU offload GPT‑5VP;
6. выгрузить GPT‑5VP по idle TTL;
7. сохранить exact/FTS/manual text работоспособными всегда.

Нужны soft/hard thresholds и hysteresis, чтобы компоненты не загружались/выгружались каждую секунду. Один chat request и один STT остаются верхней границей; vision не выполняется параллельно с chat на ограниченном GPU.

## 13. Профили производительности

| Профиль | GPT‑5VP | Context target | Vision | Embeddings | Поведение |
|---|---|---:|---|---|---|
| Compact | unloaded by default, CPU/low offload | 1k input | manual disabled/OCR | off | exact/FTS/extractive |
| Balanced | lazy, short TTL | 1.6k | manual on-demand | lazy fallback | рекомендуемый игровой |
| Quality | resident if lease allows | 2.5k | on-demand ROI | enabled | richer composition |
| Companion | only high-end and explicit | 4k cap | event-triggered, not continuous | enabled | session memory/proactivity |

Название профиля не выбирает cloud/local. Эти настройки независимы.

## 14. Ориентиры hardware tiers

Цифры — стартовые safety envelopes, затем калибруются benchmark на целевом ПК.

| VRAM / RAM | Безопасный режим рядом с GTA | Модель/quantization | Возможности |
|---|---|---|---|
| 6–8 GB / 16 GB | Compact, GPU reserve 2.5–4 GB | 3–4B Q4, преимущественно CPU/shared offload; GPT‑5VP может не помещаться | SQLite/FTS, OCR, STT по очереди, text LLM on-demand |
| 10–12 GB / 32 GB | Balanced, reserve 4–5 GB | 4–7B Q4 с ограниченным offload | warm text, lazy embeddings, manual vision после освобождения ресурсов |
| 16 GB / 32–64 GB | Balanced/Quality, reserve игре минимум 5–7 GB | 4–8B Q4/Q5; GPT‑5VP с динамическим offload | быстрый grounded chat, OCR, STT; vision только по запросу и не параллельно |
| 24+ GB / 64 GB | Quality/Companion, reserve 7–10 GB | 8–14B Q4/Q5 либо более качественный VLM | richer context, event vision, semantic rerank, но без постоянного full-frame VLM |

Для ключевого ПК 16 GB VRAM нельзя планировать 8.2 GB игре + 5 GB LLM + 1.8 GB vision + 0.6 GB speech как устойчивую одновременную загрузку: пики и драйверный overhead легко исчерпают VRAM. Рекомендуемый принцип — игра получает не менее 6 GB свободного/доступного запаса после типичной загрузки; chat и vision взаимоисключаются, STT по возможности CPU. Конкретный offload определяется capability benchmark, а не жёсткой цифрой.

## 15. UI управления AI

Страница «AI и модели» должна показывать:

- активный route и почему текущий ответ пошёл через БД/кэш/модель;
- модель, quantization, context cap, idle unload;
- текущую/пиковую RAM и VRAM раздельно, reserve игры;
- переключатели Chat, Vision, Embeddings, Speech, Web;
- режим `Экономить ресурсы во время GTA`;
- counters: доля ответов без модели, cache hit, средний input/output tokens;
- capability-test с результатом, а не только «модель доступна».

Страница «Память» показывает дисковые данные и контекст отдельно, позволяет просматривать/удалять memory, очищать history/cache, отключать категории и auto-candidates.

Debug panel (opt-in) показывает `route`, selected facts/memories, budgets, provider/model, validation outcome и timings без исходного аудио, screenshots и secrets.

## 16. Privacy model

Инварианты:

- local-by-default;
- history и long-term memory — opt-in;
- cloud chat consent не означает consent на screen, audio или memory;
- memory не отправляется cloud provider;
- screenshot требует явного preview/consent, если не гарантирован локальный deterministic OCR path;
- web query показывает, какой текст уйдёт наружу;
- cache/history/memory удаляются независимо;
- диагностические события не содержат prompt, screenshot, transcript целиком или secret.

## 17. Метрики и целевые gates

### Latency

- exact/cache p95 < 50 ms;
- FTS/extractive p95 < 120 ms на production pack;
- warm GPT first-token p95 < 1.5 s и complete p95 < 3 s на референсном 16 GB ПК;
- cold load измеряется отдельно;
- OCR ROI p95 < 500 ms; VLM screenshot p95 < 5 s.

### Efficiency

- не менее 70% production knowledge questions без chat LLM; после расширения prepared answers — 85%;
- cache hit на повторном eval-наборе > 40%;
- median input < 1200 tokens, p95 < 2000 для Balanced;
- median output < 220 tokens;
- repair rate < 2%, unnecessary provider health-check < 1% deterministic requests;
- zero overlapping Chat+Vision GPU leases в Compact/Balanced.

### Quality

- текущий verified knowledge benchmark не регрессирует;
- grounded factual accuracy ≥ 98% на покрытых вопросах;
- false-memory-as-rule = 0;
- wrong-server = 0;
- tool selection ≥ 95% на routing eval;
- abstain precision/recall измеряются отдельно.

### Game impact

- median FPS loss < 3%, p95 frame-time regression < 8% в Balanced на эталонной сцене;
- hard VRAM pressure не возникает;
- при pressure assistant деградирует без падения и без потери manual exact/FTS path.

## 18. Реалистичность

### Хорошо реализуемо сейчас

- ранний deterministic exit, query/response cache, token telemetry;
- единый Context Builder и budgets;
- rolling session summary;
- resource leases на основе RAM/process/model estimates;
- OCR/event-first screen context;
- UI прозрачности маршрутов и ресурсов.

### Реализуемо с компромиссами

- semantic reranking: качество зависит от русской embedding-модели и RAM;
- GPU telemetry/FPS correlation: драйверы и API дают неполные данные;
- автоматические memory candidates: требуется confirmation UX;
- быстрый GPT‑5VP рядом с GTA: зависит от реального размера/quantization/offload.

### Слишком тяжело для обычного игрового ПК

- постоянный full-frame VLM;
- одновременная резидентность большой GPT‑5VP, отдельной VLM, GPU STT и игры;
- длинный непрерывный transcript в prompt;
- multi-agent reasoning на каждый вопрос.

### Позже

- proactive companion после доказанной precision;
- расширяемые game adapters;
- web ingestion governance;
- Qdrant runtime только после доказанной необходимости масштаба.

## 19. Риски и защиты

| Риск | Защита |
|---|---|
| FTS возвращает близкий, но неверный документ | score/margin gate, server filters, clarify |
| Кэш устарел | knowledge revision + fact dependency invalidation |
| GPT‑5VP съедает VRAM игры | lease, reserve, lazy load, idle unload, mutual exclusion with vision |
| Summary искажает разговор | structured state + source turn IDs, не считать knowledge |
| Memory загрязняет правила | отдельный provenance и validator |
| Repair удваивает latency | compact schema, repair-rate gate, максимум один retry |
| Resource oscillation | soft/hard limits, hysteresis, cooldown |
| Web приносит слухи | source/date/TTL, не импортировать автоматически |
| Архитектурный overengineering | вертикальные этапы, interfaces только на реальных границах |

## 20. Приоритет реализации

Первым этапом должен быть **Fast Knowledge Path & Budget Telemetry**, а не новая память, web или vision. Он даёт измеримый выигрыш на всех уже поддерживаемых вопросах и уменьшает нагрузку GPT‑5VP без ухудшения качества.

Статус Phase 1A–1B: ранний preflight route, request-level telemetry, versioned response cache и агрегаты product benchmark реализованы. Prepared answers и cache hits больше не инициируют проверку каталога/health локальной модели. Persistent SQLite cache активируется только вместе с opt-in постоянной историей; иначе используется bounded in-memory cache. Confidence/cost-aware extractive route остаётся частью будущего Knowledge Intelligence.

Конкретно следующий этап:

1. перенести deterministic route до `GetAvailabilityAsync`;
2. добавить versioned response cache;
3. ввести `RouteDecision` с reason/confidence/cost;
4. собирать timings, route, cache hit, prompt/output token estimates;
5. добавить eval на отсутствие provider вызова для exact/prepared/extractive queries;
6. установить Balanced budgets: 1,600 input target, 300 output default, 450 problem solving;
7. подтвердить отсутствие регрессии production benchmark и измерить долю LLM-avoidance.

После этого — Context Builder/session summary, затем Resource Coordinator. Полный порядок и критерии находятся в `SMART_ASSISTANT_ROADMAP.md`.

## 21. Краткое резюме

**Что уже есть:** зрелый SQLite/FTS knowledge-first pipeline, deterministic answers, grounding/validator, conversation/history, ручная memory, voice, OCR/screen, on-demand vision, LM Studio lifecycle и тестовые gates.

**Что изменить:** сделать ранний выход до provider health-check, добавить cache/telemetry, собрать контекст централизованно, учитывать resource cost в router и разделить journal от model context.

**Целевая архитектура:** deterministic cascade + bounded orchestrator + optional GPT‑5VP + независимые Memory/Knowledge/Perception/Tools и общий Resource Budget Coordinator.

**Главный прирост интеллекта:** лучшие знания, retrieval, follow-up state и контекстная дисциплина, а не увеличение prompt или постоянная работа VLM.

**Самые тяжёлые компоненты:** resident GPT‑5VP, vision и GPU STT; они должны работать лениво и по очереди.

**На 8/12/16/24 GB VRAM:** от knowledge/extractive с on-demand 3–4B до bounded 8–14B и event vision; непрерывный VLM не рекомендуется ни на одном обычном игровом профиле.

**Первый этап:** Fast Knowledge Path & Budget Telemetry.

**Следующий конкретный результат:** типовой вопрос по базе отвечает за десятки миллисекунд без загрузки GPT‑5VP, а UI и benchmark показывают route, latency, token estimate и сэкономленный model call.
