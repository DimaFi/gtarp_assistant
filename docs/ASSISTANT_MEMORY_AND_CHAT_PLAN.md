# Чат и локальная память ассистента — аудит и план

Актуально на 3 августа 2026 года.

Статус: **M1, основной срез M2 и программная часть M3 завершены и прошли Release-gate**. Для M3 остаётся ручная hardware-матрица и будущий встроенный STT pack.

Документ фиксирует границу между обычным режимом «вопрос → ответ» и добровольно включаемым долгосрочным общением. База знаний GTA5RP и пользовательская память остаются разными подсистемами и разными файлами данных.

## Продуктовое решение

По умолчанию `Долгосрочное общение` выключено.

- В обычном режиме вопросы, ответы и follow-up доступны в пределах текущего запуска, но после закрытия приложения история не восстанавливается.
- После включения галочки диалоги и сообщения сохраняются локально в SQLite и текущий диалог восстанавливается после перезапуска.
- Выключение галочки прекращает использование и запись долгосрочной истории, но не удаляет уже сохранённые данные. Удаление будет отдельным подтверждаемым действием на странице памяти.
- Долговременные воспоминания и профиль пользователя не создаются автоматически на этом этапе. Это отдельные будущие слои с кандидатами, подтверждением и защитой от секретов.
- Адаптивный характер включается отдельной галочкой и не включается автоматически вместе с историей. Он меняет только стиль ответа, но не игровые факты, grounding или safety policy.

## Аудит существующего проекта

### Уже реализовано

- WPF/.NET 8, модульный feature registry и общая дизайн-система.
- Поле ручного вопроса, список сообщений текущего диалога и визуальное разделение ролей.
- Ручной voice hotkey, WASAPI capture, VAD, STT routes и отображение распознанного текста.
- `AssistantSessionCoordinator` с single-flight, cancellation, intent, knowledge search, provider fallback и grounded validator.
- Ограниченная `InMemoryAssistantConversationStore`: capacity, idle TTL, situation ID и релевантные последние turns.
- Отдельная `assistant-data.db`: conversations, messages, provider/model/fact metadata, WAL, индексы, транзакции и восстановление current conversation.
- Policy-aware переключатель: постоянное хранилище создаётся только после явного включения `Долгосрочного общения`.
- Core API для list/open/rename/delete/new conversation; текущий сохранённый диалог отображается после запуска.
- Минималистичный менеджер диалогов: list/open/new/rename/delete, retry/copy/cancel и адаптивная раскладка.
- Enter/Shift+Enter, provider/model/source/latency metadata и безопасный нативный Markdown.
- LM Studio и произвольные OpenAI-compatible routes, `/v1/models`, capability-test, выбор/импорт локальной модели.
- Отдельная SQLite knowledge DB, official/community provenance и knowledge-first ответ без модели.
- Compact/expanded overlay, ручной vision consent, безопасные логи и DPAPI secrets.

### Реализовано частично

- Полный чат готов в основном окне; отдельный ввод текста прямо в compact overlay пока не добавлен.
- Голосовой UX объединяет toggle/hold, cancellation, editable preview, auto-submit opt-in, тест/уровень микрофона и ограниченное восстановление выбранного устройства.
- Knowledge UI показывает состояние каталога, но не является document reader/import manager.
- Conversation context ограничен последними turns, но нет summary старой части.
- Настройки приватности очищают временный контекст, но нет отдельного управления сохранённой историей и памятью.

### Отсутствует

- `MemoryService`, memory candidates, deduplication, confirmation и relevance search.
- User profile, episodic memory и `ContextBuilder` с разделением provenance.
- Страница «Память ассистента».
- Читаемый document source viewer.
- Situation modes и автоматическое summary длинных разговоров.

### Хрупкие места

- Current turns ограничены и одновременно служат UI-историей и модельным контекстом. В будущем полный журнал и ограниченный model context должны быть разными проекциями.
- Общий `AllowCloud` пока не описывает, какие части пользовательской памяти разрешено отправлять конкретной модели.

## Архитектура

```text
AssistantSessionCoordinator
    → IAssistantConversationStore
        → режим выключен: InMemoryAssistantConversationStore
        → режим включён: SqliteAssistantConversationStore

knowledge.db       — только проверенные сведения об игре
assistant-data.db  — только пользовательские диалоги и будущая память
settings.json      — opt-in и несекретные настройки
secrets/           — DPAPI, отдельно от обоих SQLite-файлов
```

UI и coordinator не обращаются к SQLite напрямую. Переключатель реализуется через policy-aware adapter, который выбирает временное или постоянное хранилище на основании актуальных настроек.

## Этапы

### M1 — локальная история диалогов с opt-in — завершён

- [x] расширить Core-контракт списком диалогов, current ID, open, rename и delete;
- [x] сохранить совместимый in-memory implementation;
- [x] добавить отдельный `GtaRpAssistant.LocalData` с SQLite schema v1;
- [x] хранить conversations/messages/model/fact metadata;
- [x] использовать WAL, foreign keys, транзакции и индексы;
- [x] восстанавливать current conversation после перезапуска;
- [x] переживать повреждённый JSON metadata отдельного сообщения;
- [x] при повреждённой БД сохранить `.corrupt-*` копию и создать чистую;
- [x] добавить `EnableLongTermConversation=false` и галочку в Privacy;
- [x] переключать stores без изменения coordinator и provider routes.

### M2 — полноценный chat manager — основной срез завершён

- [x] список прошлых диалогов;
- [x] create/open/rename/delete с подтверждением;
- [x] copy/retry/cancel;
- [x] Enter и Shift+Enter;
- [x] Markdown renderer с безопасным набором элементов;
- [x] model/latency/status/source details;
- [ ] compact overlay input — отложен до отдельной overlay UX-итерации, чтобы основной оверлей не забирал фокус GTA.

### M3 — Push-to-Talk UX

- [x] hold mode через проверенный key-up hook;
- [x] toggle mode и повторная отмена;
- [x] editable preview/confirm или отдельный auto-send opt-in;
- [x] max duration и explicit cancellation states;
- [x] тест микрофона и level meter;
- [x] unplug/replug recovery и hotkey conflict detection на уровне реализации и автоматических тестов;
- [ ] ручная hardware-матрица unplug/replug, stuck-key, sleep/resume и Windows 10/11;
- optional whisper.cpp adapter без тяжёлой модели в комплекте.

### M4 — document-oriented Knowledge UI

- source list, raw document reader и chunks;
- import `.txt`, `.md`, `.json`;
- enable/disable, server/category metadata и reindex;
- source/chunk citations в сообщениях.

### M5 — контролируемая долговременная память — базовый ручной срез завершён

- [x] отдельный `user-memory.db` с подтверждёнными ручными записями, не смешанный с chat history и `knowledge.db`;
- [x] локальный CRUD: посмотреть, добавить/изменить, удалить и очистить всё;
- [x] категории play style / explained topic / favorite activity / communication preference / confirmed fact;
- [ ] candidates, `memory_sources`, summaries и автоматическое извлечение с подтверждением;
- candidate/confirmed/rejected/outdated/deleted;
- secret filter и запрет автоматического сохранения чувствительных данных;
- deduplication и ручное подтверждение;
- страница просмотра/edit/delete/export/import.

### M6 — ContextBuilder и персонализация — базовый срез завершён

- [x] единый `AssistantContextBuilder` с Balanced input target 1600 tokens и раздельными hard budgets для facts/transcript/turns/memory;
- [x] request-level output cap 300 tokens, для problem solving 450; профиль модели может дополнительно понизить лимит;
- [x] текущий вопрос не дублируется внутри untrusted transcript context;
- [x] bounded deterministic rolling summary старой части диалога и structured session situation state в RAM;
- [x] session summary/state передаются только локальному provider и не считаются verified knowledge;

- [x] recent turns + до 8 релевантных подтверждённых memories + verified knowledge;
- [x] память передаётся только локальному AI provider; cloud routes её не получают;
- [x] bounded deterministic summary старой части диалога; расширенное memory ranking остаётся будущим этапом;
- FTS relevance × confidence × importance × recency;
- provenance: память никогда не считается официальным правилом;
- debug view выбранных memories/facts;
- situation modes и context budgets.

### M7 — прозрачный адаптивный характер — основной безопасный срез завершён

- [x] отдельный `PersonalityProfile`: тон, подробность, юмор и инициативность;
- [x] ручной UI и нормализация диапазонов параметров;
- [x] personality context не считается knowledge и не меняет verified facts/validator;
- [x] opt-in адаптация только по явным просьбам пользователя, без анализа game audio и скрытого психологического профилирования;
- [x] explanation log без сохранения исходного текста реплики;
- [x] ручная настройка, полный reset и очистка explanation log;
- [x] regression: personalization прикрепляется после Knowledge selection и сохраняет verified fact IDs;
- [ ] pin отдельных черт и export профиля.

## Критерии M1

- при выключенной галочке после перезапуска начинается пустой временный диалог;
- при включённой галочке сообщения и current conversation восстанавливаются;
- новый диалог не удаляет старый;
- диалог можно открыть, переименовать и удалить через Core API;
- knowledge DB не содержит пользовательских сообщений;
- все записи выполняются транзакционно;
- corrupt metadata одной строки не ломает загрузку;
- текущие provider/model/knowledge сценарии не меняются;
- unit/integration tests и полный release gate проходят.

## Ограничение текущего этапа

Автоматическое извлечение memory candidates и summary старой части диалога пока не выполняется. Подтверждённая User Memory управляется вручную; адаптация PersonalityProfile реагирует только на ограниченный набор явных просьб после opt-in.

## Проверка M1

- 237 тестов: Core 75, Providers 20, Knowledge 66, Integration 32, App 31, ModelBenchmark 13;
- сборка без ошибок и предупреждений;
- governance lint и knowledge validation прошли;
- WPF smoke, нестандартный путь установки и 10 UI snapshots прошли;
- portable ZIP: `artifacts/release/GtaRpAssistant-0.2.0-win-x64.zip`;
- SHA-256: `daa4f845513e9dc0cec37b0290eac7d7f1c0471746276a91931d8c2d1408ef24`.

## Проверка M2

- 238 тестов: Core 75, Providers 20, Knowledge 66, Integration 32, App 32, ModelBenchmark 13;
- безопасный Markdown покрыт отдельным WPF unit-тестом;
- keyboard/minimum-layout, WPF smoke, custom-path install и 10 snapshots прошли;
- portable ZIP и актуальный SHA-256 указаны в `README.md` и верхнем active checkpoint.
