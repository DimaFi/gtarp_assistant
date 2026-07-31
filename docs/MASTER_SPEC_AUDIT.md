# Аудит проекта относительно полного ТЗ

Дата аудита: 15 июля 2026 года.

Главный источник требований: [`полныое ТЗ по улучшению.md`](./полныое%20ТЗ%20по%20улучшению.md). План интерфейса в [`PRODUCT_AND_UI_PLAN.md`](./PRODUCT_AND_UI_PLAN.md) является детализацией presentation-слоя и не заменяет мастер-ТЗ.

## Краткий вывод

Проект уже имеет безопасный рабочий вертикальный срез: захват звука, VAD и сегментацию, STT, поиск по локальной базе знаний, grounded-ответ, валидацию, компактный и расширенный overlay, ручной vision и WPF release gate. Эту основу не нужно переписывать.

На момент аудита главный архитектурный разрыв состоял в том, что AI-провайдеры не являлись независимыми маршрутами. Legacy `Endpoint`/`Model` одновременно влияли на Chat, STT и Vision; локальный Chat всегда имел приоритет, cloud использовался только как fallback; `PerformanceProfile` участвовал в доступности локального Chat. Этот разрыв относительно разделов 5, 7, 10 и 46–47 мастер-ТЗ закрыт реализацией, описанной ниже.

## Результат реализации

На 15 июля 2026 года выявленный разрыв закрыт:

- provider capabilities, registry и независимые primary/fallback routes реализованы для STT, Chat, Vision, TTS и Embeddings;
- Chat, STT и Vision используют настроенный порядок; cloud/local фильтруются режимом, а не скрытым приоритетом;
- `PerformanceProfile` больше не содержит решения `PreferCloud` и не выбирает provider mode;
- legacy settings мигрируются в версию 1 с connections/routes и продолжают использовать DPAPI secret references;
- создан отдельный `GtaRpAssistant.MicroModelHost` с mock runtime, named pipe, on-demand lifecycle, one-active/one-queued policy и idle TTL;
- реализованы package manifest contract, memory guard 750/900 МБ и overlay-состояния загрузки/генерации/ошибки;
- добавлен следующий безопасный слой: конфиг трёх MicroModel-кандидатов, русский eval-набор, JSON Schema, воспроизводимый `ModelBenchmark` и блокирующие license/quality/memory gates;
- реальный candidate benchmark выполнен: Qwen3-0.6B и SmolLM2-360M отклонены, поэтому production runtime не подключён; решение сохранено в [`ADR-0001`](./adr/ADR-0001-micro-model-candidate-benchmark.md);
- повторный полный Release gate прошёл: 158/158 тестов, 0 warnings/errors, knowledge lint 0/0, WPF smoke, 9 snapshots и portable ZIP.

Настоящая модель намеренно не подключена: два реальных GGUF и pinned runtime проверены, но ни один кандидат не прошёл gate. Следующий MicroModel-этап — dataset/training tools и fine-tuning без автоматического запуска дорогого обучения.

## Карта подсистем

| Подсистема | Состояние | Что можно сохранить | Разрыв относительно ТЗ |
|---|---|---|---|
| Core pipeline | Реализовано | `AssistantSessionCoordinator`, state machine, single-flight, grounding selector, validator и provider task routes | Нет production MicroModel в runtime chain |
| Audio | Реализовано | WASAPI microphone/game capture, process loopback fallback, VAD, segmentation, bounded queues, transcript context и независимый STT route | Нужны длительные unplug/sleep/device tests |
| Knowledge | Реализовано | SQLite repository, verified facts, prepared answers, strict pack validation, official/community provenance | Нужны дальнейшие ревизии динамических community-данных, но это не блокирует provider foundation |
| Chat providers | Реализовано | Общий registry, capabilities, health, primary/fallback и равноправные local/cloud connections | Нужны реальные endpoint/device QA |
| Vision | Реализовано | Ручное подтверждение снимка, очистка PNG, unsafe-output check и независимый route | Нужна аппаратная fullscreen QA |
| TTS | Реализовано | Windows TTS, явный manual voice mode и provider route surface | Дополнительные TTS providers не обязательны для текущей версии |
| Embeddings | Контракт и route реализованы | Registry/route surface и безопасное отключение по performance policy | Нет выбранной production embeddings-модели |
| Situation classification | Заглушка | Rule-based intent detector пригоден как deterministic baseline | Нет отдельного provider route/risk evaluator |
| Overlay/UI | Реализовано | Модульный shell, tokens, compact/expanded overlay, DPI placement и MicroModel states | Будущая визуальная доводка описана по `docs/design` |
| Settings | Реализовано | Versioned JSON, DPAPI secret references, connections/routes migration и UI smoke | Нужна миграционная QA на реальных старых профилях |
| Performance | Реализовано | Деградация дорогих функций отделена от выбора local/cloud provider | Нужны weak-PC и длительные аппаратные замеры |
| Tests/release | Реализовано | Unit/integration/UI smoke, snapshots, knowledge/model validation и package pipeline | Нужен повторный полный gate после каждого среза |
| MicroModel | Foundation реализован | Host, manager, named pipe, mock runtime, queue, TTL, manifest и guard | Реальные кандидаты отклонены; нужны training tools/fine-tuning |

## Исходные конфликты и результат

Перечисленные при первичном аудите конфликты local-first, общего legacy endpoint, связанного Vision route, отсутствующего TTS route и недостаточных capabilities закрыты provider foundation и settings migration. Оставшийся блокер относится не к маршрутизации, а к качеству реальной MicroModel.

## Решение по совместимости

- Ввести общие provider contracts, capabilities, registry и task routes в Core/Providers.
- Хранить независимые routes для STT, Chat, Vision, TTS, Embeddings и Situation classification.
- Legacy поля пока не удалять: при первой загрузке преобразовать их в новые connections/routes с тем же поведением и сохранить версию схемы.
- `PerformanceProfile` может разрешать или приостанавливать дорогую функцию, но не может менять выбранный пользователем local/cloud provider.
- После provider foundation создать отдельный `GtaRpAssistant.MicroModelHost`; настоящую модель не подключать до прохождения lifecycle, TTL и memory guard тестов.

## Исполнимый порядок

1. Provider contracts, registry, route resolver и тесты.
2. Versioned settings migration и независимые настройки пяти пользовательских маршрутов.
3. Перевести Chat/STT/Vision на route resolver; добавить TTS/Embeddings route surfaces.
4. Выполнить полный release gate.
5. Добавить MicroModel contracts, package manifest и memory guard.
6. Добавить mock host, named pipe, on-demand manager, one-host guarantee и idle TTL.
7. Показать `Starting`/`Generating`/`Faulted` в overlay и повторить release gate.

## Условия завершения текущего мастер-этапа

Текущий этап закрыт только когда выполнены все 12 критериев раздела 47 мастер-ТЗ, включая независимость routes, равноправие cloud/local, mock host lifecycle, idle shutdown, memory guard, overlay state и реальный `eng/build.ps1`.
