# Roadmap

Стратегические метрики, конкурентные преимущества и порядок развития до уровня помощника №1 зафиксированы в [TOP1_PRODUCT_ROADMAP.md](TOP1_PRODUCT_ROADMAP.md). T1 production quality benchmark завершён; ближайший новый этап — M3 Push-to-Talk.

Подробное состояние уже реализованного продукта, визуальное направление и поэтапный план модульного редизайна сохранены в [PRODUCT_AND_UI_PLAN.md](PRODUCT_AND_UI_PLAN.md).

Базовые шесть продуктовых итераций представлены работающими вертикальными срезами: text pipeline, microphone, game audio, proactive policy, manual vision и opt-in TTS.

Этап 0.2 завершил инженерную подготовку релиза: knowledge DB мигрирует старую схему до v3, author/reviewer/revoke workflow проверяется CLI, release gate формирует детерминированный portable ZIP с SHA-256 manifest, а опубликованный WPF проходит автоматический startup/shutdown smoke-test.

Этап 0.3 добавил проверяемую user-scope установку portable-релиза, upgrade с backup, rollback/uninstall и повторяемый lifecycle-soak с JSON-отчётом. Это не заменяет MSIX и code signing.

Этап 0.4 реализовал общую дизайн-систему, compact/expanded overlay, модульный shell и DI feature registry. Assistant, Audio, Providers, Behavior, Privacy и Knowledge имеют независимые View/ViewModel; ключевые правила registry покрыты App-level тестами.

Этап 0.5 выполнил первую задачу полного ТЗ: добавлены provider capabilities/registry, независимые STT/Chat/Vision/TTS/Embeddings routes, primary/fallback и миграция legacy-настроек. `PerformanceProfile` больше не определяет provider mode.

Этап 0.6 добавил отдельный mock `GtaRpAssistant.MicroModelHost`: on-demand запуск через named pipe, строгий grounded JSON, одна активная задача и одна в очереди, idle TTL, package manifest contract, memory guard и состояния overlay. Настоящие веса и llama.cpp ещё не подключены.

Этап 0.7 выполнил реальный candidate benchmark: headless runner переведён на `llama-completion`, gate согласован с production memory policy, Qwen3-0.6B и SmolLM2-360M измерены и отклонены в [`ADR-0001`](./adr/ADR-0001-micro-model-candidate-benchmark.md). Release gate: 158/158 тестов, WPF smoke и 9 snapshots.

M1 добавил добровольно включаемую локальную историю в отдельной SQLite БД. M2 завершил пользовательский менеджер диалогов: список, open/new/rename/delete, retry/copy/cancel, клавиатурную отправку и безопасный Markdown. По умолчанию приложение сохраняет режим «вопрос → ответ» без данных между запусками.

T1 добавил версионированный gold-набор и блокирующий benchmark реального production pipeline. Baseline: 528 сценариев, 524 обязательных, 100% blocking pass/decision/article/citation, 0 ложных, неподтверждённых числовых и wrong-server ответов. Методика описана в [PRODUCT_QUALITY_BENCHMARK.md](PRODUCT_QUALITY_BENCHMARK.md).

Следующие приоритеты:

1. Реализовать M3 Push-to-Talk UX: hold/toggle, preview, max duration, тест микрофона и level meter.
2. Выполнить T2/T3: расширить source-reviewed core pack по реальной частоте вопросов, добавить freshness/change detection, нормализацию запросов и измеримое улучшение retrieval.
3. Собрать M4 document-oriented Knowledge UI: список источников, чтение документа/chunks, import, review и точные citations.
4. Реализовать T4: ручной текстовый ввод и управляемые context modes в expanded overlay без потери фокуса GTA.
5. Добавить контролируемый M5 `MemoryService`, candidates/confirmation, профиль и только затем M6 `ContextBuilder`/summaries.
6. Выполнить современную визуальную доводку T5 по [`docs/design`](./design/doc_design.md): reusable overlay cards, четыре сценария, no-blur fallback, accessibility и аппаратная DPI/focus QA.
7. Развивать T6 provider ecosystem и dataset/training tools после измеримого quality baseline. Не подключать собственный headless runtime до успешного повторного ADR; решение по отклонённым Qwen3-0.6B и SmolLM2-360M сохранено в [`ADR-0001`](./adr/ADR-0001-micro-model-candidate-benchmark.md).
8. Расширять реализованный `ApplicationLifecycleCoordinator` только для действительно межмодульных событий; не возвращать runtime-зависимости в shell `MainViewModel`.
9. Провести длительные тесты устройств: unplug/replug, sleep/resume, restart GTA/LM Studio, exclusive fullscreen, Windows 10/11 и несколько классов ПК.
10. Выполнить T7: installer/MSIX, code signing, безопасное обновление, rollback, first-run wizard и проверку чистой установки.
11. Выполнить T8: history/review для knowledge packs, privacy-safe feedback loop, публичный changelog и регулярный benchmark качества.

Если для запроса нет актуального проверенного official или явно маркированного community-факта, приложение должно безопасно воздерживаться от фактической игровой подсказки.
