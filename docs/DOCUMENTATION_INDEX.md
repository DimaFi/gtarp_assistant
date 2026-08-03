# Карта документации GTA RP Assistant

Этот файл — первая точка входа для разработчика, новой AI-сессии или восстановления проекта после длительного перерыва. Документация описывает фактическое состояние кода; если возникло расхождение, сначала проверяются код и автоматические тесты, затем документ исправляется в том же изменении.

## Начать отсюда

1. [README](../README.md) — запуск, быстрый старт и возможности продукта.
2. [DEVELOPMENT_CHECKPOINT](DEVELOPMENT_CHECKPOINT.md) — последний завершённый этап, проверенный артефакт и следующий шаг.
3. [PROJECT_HANDBOOK](PROJECT_HANDBOOK.md) — устройство репозитория, runtime-потоки, данные, сборка и восстановление работы.
4. [ARCHITECTURE](ARCHITECTURE.md) — границы проектов и направление зависимостей.
5. [ASSISTANT_MEMORY_AND_CHAT_PLAN](ASSISTANT_MEMORY_AND_CHAT_PLAN.md) — состояние чата и контролируемой памяти.
6. [TOP1_PRODUCT_ROADMAP](TOP1_PRODUCT_ROADMAP.md) — измеримая стратегия развития до лучшего GTA5RP-помощника.
7. [OFFLINE_ASSISTANT_ARCHITECTURE](OFFLINE_ASSISTANT_ARCHITECTURE.md) — аудит и целевая архитектура автономного voice-сценария без LM Studio.

## Для пользователя

- [USAGE](USAGE.md) — полная инструкция по использованию.
- [INSTALLATION](INSTALLATION.md) — portable, установка, обновление, откат и нестандартные пути.
- [MANUAL_QA](MANUAL_QA.md) — ручная проверка основных сценариев.
- [PRIVACY](PRIVACY.md) — какие данные хранятся и что может уйти в облако.

## Архитектура и подсистемы

- [ARCHITECTURE](ARCHITECTURE.md) — проекты, DI и главный pipeline.
- [OFFLINE_ASSISTANT_ARCHITECTURE](OFFLINE_ASSISTANT_ARCHITECTURE.md) — реализованное/отсутствующее, voice orchestrator, runtime, ресурсы, память и этапы P0–P10.
- [AI_ROUTING](AI_ROUTING.md) — deterministic/provider/fallback/validator.
- [AUDIO_PIPELINE](AUDIO_PIPELINE.md) — microphone, game audio, VAD и STT.
- [EMBEDDED_STT](EMBEDDED_STT.md) — optional whisper.cpp pack, manifest/hash, runtime, установка и русский quality gate.
- [PERFORMANCE](PERFORMANCE.md) — профили нагрузки и деградация.
- [SECURITY](SECURITY.md) — ключи, валидация, доверие и границы процессов.
- [ANTI_CHEAT_BOUNDARIES](ANTI_CHEAT_BOUNDARIES.md) — что приложение принципиально не делает.

## База знаний

- [KNOWLEDGE_FORMAT](KNOWLEDGE_FORMAT.md) — формат статьи и факта.
- [KNOWLEDGE_AUTHORING](KNOWLEDGE_AUTHORING.md) — добавление и проверка материалов.
- [KNOWLEDGE_COVERAGE](KNOWLEDGE_COVERAGE.md) — фактическое покрытие и следующие волны.

## AI и локальные модели

- [LOCAL_AI_QUALITY_AND_CONVERSATION_PLAN](LOCAL_AI_QUALITY_AND_CONVERSATION_PLAN.md) — качество и подключение внешних моделей.
- [PRODUCT_QUALITY_BENCHMARK](PRODUCT_QUALITY_BENCHMARK.md) — gold-набор, метрики и блокирующий benchmark полного production pipeline.
- [MICRO_MODEL_BENCHMARK](MICRO_MODEL_BENCHMARK.md) — воспроизводимый benchmark.
- [ADR-0001](adr/ADR-0001-micro-model-candidate-benchmark.md) — почему встроенные Qwen/SmolLM2 не включены в продукт.

## Продукт и дизайн

- [PRODUCT_AND_UI_PLAN](PRODUCT_AND_UI_PLAN.md) — модульный UI и продуктовый план.
- [TOP1_PRODUCT_ROADMAP](TOP1_PRODUCT_ROADMAP.md) — приоритеты, метрики и критерии достижения уровня помощника №1.
- [ROADMAP](ROADMAP.md) — порядок следующих крупных этапов.
- [design/doc_design](design/doc_design.md) — визуальные референсы и правила.
- [MASTER_SPEC_AUDIT](MASTER_SPEC_AUDIT.md) — сверка с полным техническим заданием.

## Правило поддержки документации

При каждом логическом этапе обновляются:

- `README.md`, если изменился пользовательский сценарий или проверенный релиз;
- `DEVELOPMENT_CHECKPOINT.md`, если этап завершён либо изменился следующий шаг;
- профильный документ подсистемы, если изменился контракт, поток или ограничение;
- `PROJECT_HANDBOOK.md`, если изменились проекты, composition root, файлы данных, build/release или порядок восстановления;
- `USAGE.md` и `MANUAL_QA.md`, если появился новый элемент интерфейса;
- `PRIVACY.md`/`SECURITY.md`, если изменилось хранение или передача данных.

Нельзя обновлять число тестов и SHA-256 до успешного `eng/build.ps1`. Нельзя обозначать планируемую функцию как реализованную.
