# Состояние продукта и план модульного UI

> Этот файл детализирует UI и overlay. Общий порядок архитектурных работ задаёт [`полныое ТЗ по улучшению.md`](./полныое%20ТЗ%20по%20улучшению.md), а фактические расхождения с кодом зафиксированы в [`MASTER_SPEC_AUDIT.md`](./MASTER_SPEC_AUDIT.md).

Актуально на 03.08.2026.

Текущий Local AI/conversation цикл и точная граница его готовности зафиксированы в [`LOCAL_AI_QUALITY_AND_CONVERSATION_PLAN.md`](./LOCAL_AI_QUALITY_AND_CONVERSATION_PLAN.md).

Этот документ фиксирует уже реализованное состояние GTA RP Assistant и целевой план развития интерфейса. Главная цель следующего этапа — сделать приложение визуально спокойным, современным и модульным, чтобы новые разделы, карточки и действия добавлялись без разрастания одного окна и одного ViewModel.

### Текущий прогресс редизайна

- Добавлены общие `FeaturePageHeader`, `FeatureSection` и `MetricCard`: заголовки, секции и показатели feature pages теперь собираются из единых компонентов и токенов дизайн-системы.
- Светлая и серая палитры имеют одинаковый набор семантических токенов; выбор темы сохраняется в настройках, а автоматический тест блокирует расхождение контрактов палитр.
- Страницы **О приложении**, **База знаний** и **Приватность** переведены на новые компоненты без изменения команд, bindings и стабильных `AutomationId`.
- Карточка версии показывает короткую версию без переполнения, а полный informational version с commit SHA остаётся в диагностике.
- Контракты dependency properties защищены App-тестом; обновлённые страницы прошли WPF smoke, minimum-layout и визуальную проверку 11 snapshots.
- Начат UI-7: добавлен переиспользуемый `OverlayCardShell`, который централизует прозрачную поверхность, тень, радиус и semantic accent для `Neutral`, `Success` и `Warning`.
- Compact и expanded overlay переведены на общий shell; дублированный выбор status brush удалён из code-behind, source/community badge вынесены в общие стили.
- Automation smoke отдельно проверяет warning tone и фактический `Brush.Warning`; обновлённые compact/expanded snapshots визуально подтверждены.
- Добавлен reusable `OverlayStatusPill`: manual voice показывает минимальный Listening-индикатор, а Starting/Generating MicroModel — Thinking без раскрытия полной карточки.
- Footer expanded overlay вынесен в отдельный `QuickActionsBar`; события источника, feedback, DND и collapse сохраняют прежний контракт и AutomationId.
- Vision preview переведён на reusable `VisionConsentCard`: отдельное назначение endpoint/provider, preview только выбранного кадра, понятные гарантии удаления и keyboard-контракт `Enter`/`Esc`.
- Этап UI-1 реализован: общие colors/metrics/typography tokens и стили кнопок, полей, карточек, вкладок и overlay surfaces подключены через `App.xaml`.
- `MainWindow`, compact overlay и vision preview используют одну дизайн-систему вместо локально заданных цветов и отступов.
- Начаты UI-3/UI-4: добавлена отдельная `OverlayPresentation`, compact non-activating поверхность и expanded interactive окно.
- Compact → expanded открывается только явной кнопкой; expanded поддерживает `Esc`, сворачивание обратно и отдельные действия с источником/feedback/DND.
- Модульный shell реализован: Assistant, Audio, AI Providers, Behavior, Privacy и Knowledge вынесены в отдельные View/UserControl и feature ViewModel, а `MainWindow` выбирает их через `DataTemplate`.
- Smoke-test последовательно создаёт все feature pages, поэтому проверяет navigation templates и общие ресурсы, а не только стартовое окно.
- Внедрён `IAppDialogService`: окна и ViewModel больше не вызывают `MessageBox` напрямую.
- Загрузка official/community packs, индексация SQLite и runtime-счётчики вынесены в `KnowledgeCatalogService`.
- Load/save секретов, применение runtime-настроек, startup и GTA monitoring вынесены в `SettingsApplicationService`.
- Проверка endpoint теперь принадлежит Providers feature, а DND/proactive-команды — Behavior feature.
- Assistant feature теперь владеет текстом вопроса, выбором источника, добавлением transcript-контекста и запуском knowledge pipeline.
- Audio feature владеет каталогом устройств, listening session, game-audio rebind и применением performance actions.
- Privacy feature владеет TTS, выбором voice/output и очисткой временных audio/transcript-буферов.
- Корневой `MainViewModel` сокращён до shell-навигации и делегирования команд; lifecycle/hotkeys/cross-feature coordination находятся в `ApplicationLifecycleCoordinator`.
- DI feature registry реализован: каждый модуль регистрирует metadata, View и ViewModel через `AddFeatureModules`, а shell получает `IEnumerable<IShellFeature>`.
- Feature ViewModel больше не зависят от `MainViewModel`; общий статус, настройки и audio selection передаются через небольшие shared-state services.
- `MainWindow` не перечисляет feature DataTemplate: registry отдаёт готовую View с DataContext, поэтому новая страница не требует изменения shell XAML или корневого ViewModel.
- Добавлены App-level тесты порядка registry, уникальности ID, обязательного наличия модулей, knowledge totals и community overlay marker.
- Все feature pages, основные shell-действия, compact/expanded overlay и vision confirmation получили стабильные `AutomationId`.
- WPF smoke теперь переключает каждый зарегистрированный модуль и проверяет выбранное состояние, фактический layout, корневой `AutomationId`, наличие actionable-элементов и отсутствие дублирующихся ID.
- Добавлен автоматический capture gate: опубликованное приложение создаёт PNG-снимки всех шести модулей в `artifacts/ui-snapshots`; снимки проверяются по именам и минимальному размеру и используются для визуальной доводки.
- По результатам первой автоматической визуальной проверки исправлены ширина прокручиваемых модулей и перенос длинного текста Knowledge.
- Cross-feature lifecycle вынесен в `ApplicationLifecycleCoordinator`: он владеет инициализацией, GTA/performance событиями, overlay/TTS и hotkey-сценариями; `MainViewModel` оставлен только shell-навигацией и делегированием команд.
- Архитектурный тест фиксирует узкую границу зависимостей `MainViewModel`, чтобы runtime-сервисы не вернулись в shell незаметно.
- Интерактивный WPF smoke проверяет compact → expanded → compact → hidden, focus-контракт обоих overlay, community badge и реальные кнопки по `AutomationId`.
- Vision smoke открывает модальный preview с тестовым изображением и отдельно проверяет Cancel и Confirm; изображение не отправляется в provider и не сохраняется приложением.
- Snapshot gate расширен до девяти рендеров: шесть feature pages, compact overlay, expanded overlay и vision preview.
- Smoke/capture используют уникальный временный data directory, не читают реальный профиль, не регистрируют глобальные hotkeys, не запускают GTA-monitoring и не меняют Windows Startup.
- Settings smoke меняет endpoint через UI Automation `InvokePattern`, сохраняет настройки, проверяет JSON и DPAPI round-trip и подтверждает отсутствие секрета в JSON.
- Маршрутизация четырёх глобальных hotkeys вынесена в стабильный `GlobalHotkeyMap` и покрыта тестами без запуска микрофона или vision capture.
- Tray menu строится из типизированного `TrayCommandCatalog`; smoke сверяет фактически созданные пункты, порядок, подписи и команды без выполнения Exit.
- Providers smoke проверяет validation/error state для некорректного абсолютного URI без сетевого запроса, затем выполняет успешный изолированный save round-trip.
- Keyboard smoke проверяет focus chain Navigation Assistant → Audio и Assistant Source → Question → Add context → Ask через настоящий WPF focus traversal.
- Все feature pages повторно проходят layout smoke при минимальном размере окна 900×620.
- Проект явно использует `ApplicationHighDpiMode=PerMonitorV2`; расчёт позиции overlay вынесен в тестируемую геометрию и ограничивает окно рабочей областью даже для слишком больших размеров.
- Следующий этап — аппаратная QA-матрица Windows 10/11, DPI 100/150/200%, второй монитор и focus поверх borderless GTA.
- Release по умолчанию self-contained; gate дополнительно устанавливает и запускает пакет из нестандартного пути с пробелами. В Local AI доступны ручные пути к CLI/desktop runtime без жёсткой привязки к диску C:.

## 1. Что уже сделано

### Основа приложения

- Windows-приложение на .NET 8 и WPF с dependency injection.
- Работа из системного tray, сворачивание без завершения процесса и штатное освобождение сервисов.
- Глобальные горячие клавиши для оверлея, паузы, ручного голосового вопроса и vision.
- Обнаружение запуска, закрытия и перезапуска GTA/RAGE MP.
- Настройки в `%LocalAppData%\GtaRpAssistant`, API-ключи защищены DPAPI CurrentUser.
- Профили производительности и контролируемая деградация ресурсоёмких функций.

### Ввод и ответы

- Ручные текстовые вопросы.
- Захват микрофона через WASAPI.
- Захват звука игры с предпочтением process-specific loopback и безопасным fallback.
- Временный transcript-контекст без обязательной записи аудио на диск.
- Ручной vision только после превью и подтверждения пользователя.
- TTS выключен по умолчанию и разрешён только для ручного голосового сценария.
- Проактивные подсказки с cooldown, DND и полным отключением.

### AI routing и безопасность

- Цепочка Transcript → Intent → Knowledge → Router → Validator → Overlay.
- Детерминированные prepared answers без LLM, когда найден точный проверенный ответ.
- Локальная модель имеет приоритет; cloud fallback работает только с разрешения пользователя.
- Ответ отклоняется при конфликтном, устаревшем или неподтверждённом grounding.
- Валидатор запрещает неподтверждённые числа, URL и советы по игровой автоматизации.
- Данные игроков всегда маркируются «По данным игроков:»; валидатор восстанавливает пометку, даже если модель её убрала.

### База знаний

- Официальный source-reviewed pack: 44 компактные статьи, 202 атомарных факта и 90 prepared answers из 41 официальной страницы.
- Отдельный community-confirmed справочник: 445 компактных lookup-записей.
- В community-слое сохранены достижения, фарм BP, рецепты, игровая медицинская шпаргалка, экономика, гаражи, навыки, календарные ориентиры, дрессировка, шар предсказаний и клубные справки.
- В runtime доступно 489 небольших поисковых статей; в LLM передаётся только релевантная статья и ограниченный набор фактов.
- SQLite schema v3, нормализованный server scope и FTS5-поиск.
- CLI для validate, lint, inspect и проверки официальных источников.

### Релиз и качество

- Release-сборка без предупреждений.
- 226 unit/integration тестов, включая provider route/settings migration, privacy-safe diagnostics, MicroModel lifecycle/TTL/queue/memory guard, benchmark catalog/evaluation gates, Local AI model selection/import и точный поиск по community-справкам.
- Автоматический WPF startup/navigation/layout smoke-test и PNG snapshot gate для всех feature pages.
- Страница «Память» входит в обязательный snapshot gate; четыре personality controls показаны snap-slider в одну строку на обычной ширине, а экран разговора однозначно называется «Чат» и отделяет историю/переименование от основного действия.
- Portable `win-x64` ZIP, manifest и SHA-256.
- User-scope install, upgrade с backup, rollback, uninstall и lifecycle soak.
- Ручная QA-матрица для Windows 10/11, DPI, нескольких мониторов, аудио, hotkeys и overlay.

## 2. Исходное состояние UI до модульного редизайна

Ниже сохранён исходный baseline, по которому планировался этап 0.4. Он описывает проблемы старого инженерного MVP, а не текущее состояние после выполненного редизайна:

- `App.xaml` содержит только один общий accent brush;
- цвета, отступы и стили повторяются непосредственно в `MainWindow`, `OverlayWindow` и `VisionPreviewWindow`;
- `MainWindow` объединяет симулятор и длинную форму всех настроек;
- `MainViewModel` координирует почти все функции приложения и будет быстро расти при добавлении экранов;
- элементы настроек нельзя переиспользовать как готовые секции или карточки;
- оверлей имеет один фиксированный вид шириной 430 px и сразу показывает все действия;
- компактное уведомление и интерактивная панель не разделены по правилам получения фокуса;
- состояния loading, listening, thinking, answer, warning и error не имеют единой визуальной модели;
- нет общего набора дизайн-токенов, типографики, иконок, анимаций и accessibility-правил.

Функциональность не нужно переписывать. Следующий этап должен заменить presentation-слой постепенно, сохраняя Core, Knowledge, Providers и Windows infrastructure.

## 3. Визуальное направление

Интерфейс должен напоминать качественный игровой/голосовой оверлей по ощущению, а не копировать Discord буквально.

Основные принципы:

1. Минимум визуального шума. На экране остаётся только информация, необходимая в текущий момент.
2. Тёмная нейтральная основа с одним фиолетово-индиговым accent и отдельными semantic colors.
3. Полупрозрачность используется локально: фон оверлея, верхняя панель и всплывающие поверхности. Текстовые поля и длинные настройки остаются достаточно непрозрачными для чтения.
4. Мягкие границы, радиус 12–16 px, лёгкая тень и спокойные переходы 120–180 мс.
5. Иерархия строится отступами и типографикой, а не большим количеством рамок.
6. Компактный оверлей не забирает фокус у GTA и не мешает управлению.
7. Расширение происходит только по явному действию: горячая клавиша, «Подробнее» или открытие поля ввода.
8. Все важные состояния различимы не только цветом, но и иконкой/текстом.
9. Поддерживаются DPI 100–200%, несколько мониторов, клавиатурная навигация и reduced motion.

### Базовые дизайн-токены

Начальные значения уточняются после визуального прототипа:

| Группа | Предлагаемое значение |
|---|---|
| Фон приложения | `#0E1015` |
| Основная поверхность | `#171A21` |
| Поднятая поверхность | `#1D212B` |
| Accent | `#7C6CF2` |
| Основной текст | `#F4F5F7` |
| Вторичный текст | `#A8AEBD` |
| Success / Warning / Error | `#63D69A` / `#F2C66D` / `#F07C82` |
| Сетка отступов | 4, 8, 12, 16, 24, 32 px |
| Радиусы | 8 px для controls, 12 px для cards, 16 px для overlay |
| Compact overlay opacity | примерно 84–90% |
| Expanded overlay opacity | примерно 94–97% |
| Анимации | 120–180 мс, без пружинящего движения |

Blur/Acrylic следует включать через Windows composition/DWM только там, где он стабилен. Для unsupported Windows, remote desktop и режима экономии ресурсов обязателен простой полупрозрачный fallback. Дорогой blur всей поверхности поверх игры не является обязательным.

## 4. Целевая модель оверлея

Оверлей разделяется на две поверхности с разным поведением окна. Это надёжнее, чем динамически превращать одно `WS_EX_NOACTIVATE` окно в обычное интерактивное окно.

### Compact overlay

- Ширина примерно 360–420 px, высота зависит от короткого ответа.
- Не активирует окно и не забирает keyboard focus.
- Показывает status icon, короткий заголовок, 2–4 строки ответа и источник/маркер данных игроков.
- Второстепенные действия скрыты; остаются только ненавязчивые affordances раскрытия и закрытия.
- Автоматически исчезает, но таймер приостанавливается при наведении, если это не ухудшает управление игрой.
- Для listening/thinking используется компактный индикатор без скачков размера.

### Expanded overlay

- Открывается явно и может иметь ширину примерно 520–640 px.
- Получает фокус только после действия пользователя.
- Содержит полный ответ, источник и дату, follow-up input, быстрые вопросы, feedback и DND.
- Длинное содержимое прокручивается внутри панели, а сама панель не выходит за рабочую область экрана.
- Поддерживает возврат в compact state и закрытие по `Esc`.
- В перспективе может показывать модульные карточки: рецепт, достижение, таблицу наград, список шагов или предупреждение.

### Состояния

```text
Hidden
  → Listening
  → Thinking
  → CompactAnswer / CompactWarning
  → ExpandedAnswer
  → Hidden
```

`Listening` и `Thinking` не должны открывать большую панель. `ExpandedAnswer` никогда не появляется автоматически во время управления GTA.

## 5. Модульная UI-архитектура

Первая версия модульности должна быть compile-time и DI-based. Загрузка произвольных сторонних DLL пока не нужна: она добавит риски безопасности, совместимости и обновления.

Предлагаемая структура:

```text
src/GtaRpAssistant.App/
  DesignSystem/
    Tokens/
      Colors.xaml
      Spacing.xaml
      Typography.xaml
      Motion.xaml
    Styles/
      Buttons.xaml
      Inputs.xaml
      Navigation.xaml
      Cards.xaml
      Overlay.xaml
    Icons/
  Shell/
    MainShellWindow.xaml
    MainShellViewModel.cs
    NavigationItem.cs
  Features/
    Assistant/
      AssistantView.xaml
      AssistantViewModel.cs
    Audio/
      AudioSettingsView.xaml
      AudioSettingsViewModel.cs
    AiProviders/
      ProviderSettingsView.xaml
      ProviderSettingsViewModel.cs
    Privacy/
      PrivacySettingsView.xaml
      PrivacySettingsViewModel.cs
    Knowledge/
      KnowledgeStatusView.xaml
      KnowledgeStatusViewModel.cs
    About/
  Overlay/
    OverlayToastWindow.xaml
    OverlayPanelWindow.xaml
    OverlayViewModel.cs
    OverlayPresentation.cs
    OverlayState.cs
  Components/
    StatusChip.xaml
    SettingCard.xaml
    SectionHeader.xaml
    SourceBadge.xaml
    EmptyState.xaml
    InlineMessage.xaml
```

### Правила модульности

- Shell отвечает только за окно, навигацию и размещение текущего feature view.
- Каждый feature имеет собственные View/ViewModel и команды.
- Общие visual resources не объявляются внутри feature-окон.
- Все reusable controls используют DynamicResource и дизайн-токены.
- View выбирается для ViewModel через DataTemplate, а feature регистрируется через DI/feature registry.
- ViewModel не показывает `MessageBox` напрямую; диалоги, toast и navigation доступны через интерфейсы presentation services.
- Code-behind разрешён только для поведения, связанного с Win32/window lifecycle, focus и чисто визуальными эффектами.
- Сервисы Core не должны зависеть от WPF types.
- Overlay получает готовую `OverlayPresentation`, а не самостоятельно интерпретирует доменную модель.
- Новый элемент должен добавляться как control/card или feature module без изменения `MainWindow.xaml`.

### Базовые переиспользуемые элементы

- Primary, secondary, ghost и icon buttons.
- Text field, password field, select, toggle row и slider.
- SettingCard и SettingsSection с title, description и validation message.
- Navigation rail/sidebar с active state и компактным режимом.
- StatusChip для GTA, микрофона, local AI, cloud permission и pause.
- AnswerCard, SourceBadge и CommunityBadge.
- QuickActionChip для follow-up вопросов.
- Loading/Listening indicator, EmptyState, ErrorState и InlineMessage.
- DialogHost для подтверждения vision и потенциально опасных действий.

## 6. Главное окно после редизайна

Вместо двух больших вкладок используется shell с узкой левой навигацией:

1. **Ассистент** — поле вопроса, состояние pipeline, последние локальные ответы текущей сессии и быстрые действия.
2. **Аудио** — микрофон, game audio, устройства и тест уровня сигнала.
3. **AI и модели** — local endpoint, модели и отдельно оформленный opt-in cloud fallback.
4. **Поведение** — hotkeys, оверлей, позиция, время показа, proactive mode и DND.
5. **Приватность** — что хранится, что может уйти в cloud, очистка временного контекста.
6. **База знаний** — число официальных/community статей, дата актуальности и состояние источников.
7. **О приложении** — версия, диагностика, путь к логам и release information.

На широком окне navigation rail раскрывается до sidebar. На минимальной ширине остаются иконки и tooltip. Настройки сохраняются по явной кнопке либо через безопасный auto-save только после внедрения validation/rollback.

## 7. План реализации

### Этап UI-0 — визуальная спецификация

Результат:

- wireframes compact и expanded overlay;
- wireframes главного shell и трёх ключевых страниц;
- утверждённые цвета, типографика, радиусы и состояния;
- решение по blur/fallback и правилам focus.

Критерий готовности: все обязательные состояния можно проверить на макетах до изменения рабочей логики.

### Этап UI-1 — дизайн-система без изменения поведения

Работы:

- создать merged resource dictionaries;
- перенести цвета, размеры, отступы и typography из окон в tokens;
- реализовать базовые button/input/card/status styles;
- добавить light-independent semantic resources и high-contrast fallback;
- заменить локальные стили текущих окон общими ресурсами.

Критерий готовности: ни один основной цвет или размер control не дублируется в feature XAML; существующие сценарии и тесты не изменены.

### Этап UI-2 — модульный shell

Работы:

- создать `MainShellWindow` и feature registry;
- разделить `MainViewModel` на shell и feature ViewModel;
- перенести настройки в отдельные Audio, Providers, Behavior и Privacy modules;
- заменить прямые `MessageBox` на dialog service;
- сохранить текущий settings round-trip и DPAPI behavior.

Критерий готовности: новый экран добавляется регистрацией feature и DataTemplate, без редактирования layout shell.

### Этап UI-3 — compact overlay

Работы:

- ввести `OverlayState` и `OverlayPresentation`;
- создать отдельное non-activating toast window;
- добавить единые состояния listening/thinking/answer/warning/error;
- реализовать безопасное позиционирование, DPI и несколько мониторов;
- добавить короткие fade/resize transitions с reduced-motion fallback.

Критерий готовности: compact overlay не забирает focus у GTA, не перекрывает критичную область и не выходит за working area.

### Этап UI-4 — expanded overlay

Работы:

- создать отдельную интерактивную panel window;
- раскрывать её только по hotkey или явному действию;
- добавить полный ответ, источник, follow-up input и quick actions;
- реализовать `Esc`, возврат в compact mode и scroll для длинного содержимого;
- подготовить DataTemplates для recipe, achievement, BP и generic answer cards.

Критерий готовности: пользователь может перейти compact → expanded → compact/hidden без потери ответа и без неожиданного захвата фокуса.

### Этап UI-5 — доводка всех сценариев

Работы:

- обновить vision confirmation, tray и empty/error states;
- добавить skeleton/progress только там, где ожидание заметно;
- проверить keyboard navigation, screen reader labels и contrast;
- проверить локализацию RU/EN без обрезания текста;
- добавить UI automation для shell navigation, overlay states, settings и focus.

Критерий готовности: автоматические тесты плюс ручная матрица Windows 10/11, DPI 100/150/200%, два монитора, borderless и fullscreen.

### Этап UI-6 — стабилизация и выпуск

Работы:

- измерить CPU/GPU/working set compact и expanded overlay;
- проверить 30-минутный idle и lifecycle soak;
- сравнить fallback без blur с composition mode;
- провести migration check старых настроек;
- обновить screenshots, README, manual QA и release notes.

Критерий готовности: новый UI не ухудшает release gate, privacy guarantees, startup time и стабильность аудио.

## 8. Ближайший исполнимый backlog

UI-этапы 1–6 в основном реализованы и защищены smoke/snapshot gate. Текущий приоритет по мастер-ТЗ — не дальнейшая косметическая перестройка shell, а provider foundation и MicroModel skeleton:

1. Ввести provider capabilities, registry и независимые STT/Chat/Vision/TTS/Embeddings routes.
2. Отделить provider selection от `PerformanceProfile` и мигрировать legacy-настройки.
3. Перевести действующие Chat/STT/Vision workflows на новые routes.
4. Создать mock `GtaRpAssistant.MicroModelHost`, named-pipe lifecycle, idle TTL и memory guard.
5. Добавить состояния MicroModel в существующую модель overlay.
6. После каждого архитектурного среза выполнять полный release gate.

Статус на 15 июля 2026 года: пункты 1–6 выполнены. Qwen3-0.6B и SmolLM2-360M проверены реальным benchmark и отклонены; подробности в [`MICRO_MODEL_BENCHMARK.md`](./MICRO_MODEL_BENCHMARK.md) и [`ADR-0001`](./adr/ADR-0001-micro-model-candidate-benchmark.md). `MicroModelHost` остаётся mock-процессом, реальные веса не подключаются.

Исторический порядок UI-редизайна, уже использованный для реализации:

Рекомендуемый порядок ближайших задач:

1. Создать XAML resource dictionaries с токенами и подключить их в `App.xaml`.
2. Собрать `Button`, `IconButton`, `SettingCard`, `StatusChip` и `SourceBadge`.
3. Перестилизовать существующий overlay на токенах без изменения его API.
4. Ввести `OverlayPresentation` и unit-тесты mapping доменного ответа в UI state.
5. Разделить compact toast и expanded panel.
6. Создать shell/navigation и перенести страницу «Ассистент».
7. Последовательно вынести Audio, Providers, Behavior, Privacy и Knowledge.
8. Удалить старый монолитный layout после feature parity.
9. Расширять UI automation на tray, hotkeys, сохранение настроек, overlay focus и vision confirmation; базовая shell navigation/layout automation и snapshot gate уже реализованы.

Такой порядок даёт видимый результат рано, но не заставляет одновременно переписывать всю работающую логику.

## 9. Параллельный продуктовый roadmap

После или параллельно с безопасными UI-этапами:

1. Расширять официальный GTA5RP knowledge pack по волнам из `KNOWLEDGE_COVERAGE.md`.
2. Ввести историю ревизий и повторную проверку динамических community-данных.
3. Провести длительные аппаратные тесты Windows 10/11, audio unplug/replug, sleep/resume и restart GTA/LM Studio.
4. Подготовить MSIX/installer, code signing и безопасное автоматическое обновление.
5. Добавить диагностический экран без секретов и персональных transcript.
6. Рассмотреть дополнительные карточки knowledge только через стабильный `OverlayPresentation`, без специальных условий в окне.

## 10. Definition of Done для нового UI-модуля

Новый модуль считается готовым, если:

- он зарегистрирован через DI/feature registry;
- использует общие tokens и reusable controls;
- не добавляет бизнес-логику в code-behind;
- имеет loading, empty, validation и error states;
- управляется клавиатурой и корректно масштабируется;
- не выводит секреты и не ослабляет cloud opt-in;
- имеет unit-тест ViewModel/state mapping;
- включён в UI automation или ручную QA-матрицу;
- проходит полный `eng/build.ps1` и WPF smoke-test.

## 11. Решения, которые нельзя потерять

- Оверлей не должен имитировать внутриигровой HUD и не должен автоматизировать действия игрока.
- Compact overlay всегда non-activating; expanded overlay открывается только пользователем.
- Красивый blur не важнее читаемости, производительности и совместимости.
- Community-данные всегда визуально и текстово отличаются от официальных источников.
- Модульность сначала внутренняя и compile-time; внешний plugin API проектируется только при реальной необходимости.
- Редизайн выполняется поэтапно с feature parity, без одновременной замены проверенных Core/Knowledge/Audio подсистем.

## 12. Будущий этап UI-7 — современная визуальная доводка по `docs/design`

Источники направления: [`design/doc_design.md`](./design/doc_design.md), [`design/сценарии.md`](./design/сценарии.md) и четыре сценарных макета `design/1.png`–`design/4.png`. Этот этап начинается отдельным срезом после текущего технического аудита; он не переписывает Core, Knowledge, Audio или provider routing.

### Визуальная система

- Закрепить точные токены: `#0E1015` background, `#171A21` surface, `#7C6CF2` accent, `#F2C66D` warning, Segoe UI Variable, сетку 4 px, controls 8 px, cards 12 px и overlay 16 px.
- Добавить локальный Acrylic/Mica/blur только для overlay surfaces с непрозрачным fallback для слабого ПК, Remote Desktop, Windows 10 и performance degradation.
- Унифицировать мягкую боковую semantic-полоску, header с типом ответа, source/confidence footer, состояния hover/focus/pressed/disabled и reduced-motion переходы 120–180 мс.
- Не переносить ошибки сгенерированных макетов: смешанный язык, выдуманные факты, нечитаемый текст и игровые инструкции без grounding не являются частью дизайна.

### Переиспользуемые компоненты

- `OverlayCardShell`, `RuleWarningCard`, `AiAnswerCard`, `OverlayStatusPill`, `QuickActionsBar`.
- `ExpandedAnswerPanel`, `RelatedFactCard`, `TranscriptContextCard` с виртуализацией/ограничением высоты.
- `VisionConsentCard` с thumbnail, явными Confirm/Cancel, описанием destination/provider и гарантированным удалением изображения.
- Компоненты получают готовую `OverlayPresentation`; окна не содержат специальных условий по типам GTA5RP-ответов.

### Четыре обязательных сценария

1. Compact rule warning: жёлтая semantic-полоса, максимум 2–4 строки, официальный/community source, non-activating, auto-dismiss 7–10 секунд.
2. Manual AI answer: фиолетовый accent, listening/thinking pill, краткий нумерованный ответ и quick actions только после явного раскрытия.
3. Expanded context: правая панель 520–640 px, отдельные answer/fact/context cards, внутренний scroll, `Esc` и возврат в compact без потери ответа.
4. Vision consent: preview до отправки, понятный scope снимка, Confirm/Cancel с клавиатуры, результат в обычной answer card.

### Порядок и критерии готовности

1. Снять baseline девяти существующих UI snapshots и составить token/component inventory.
2. Обновить resource dictionaries и reusable controls без изменения поведения.
3. По одному заменить compact, expanded и vision surfaces, затем shell feature pages.
4. Для каждого среза добавить snapshot, UI Automation, focus/DPI/contrast/reduced-motion и no-blur fallback проверки.
5. Завершить аппаратной QA Windows 10/11, DPI 100/150/200%, два монитора и borderless GTA; полный `eng/build.ps1` обязан остаться зелёным.
