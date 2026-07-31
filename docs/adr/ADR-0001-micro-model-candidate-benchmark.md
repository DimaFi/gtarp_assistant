# ADR-0001: базовые MicroModel-кандидаты отклонены

- Статус: принято
- Дата: 15 июля 2026 года
- Решение: не подключать реальные веса в `MicroModelHost`; сохранить mock runtime и перейти к dataset/training tools.

## Контекст

Для optional MicroModel Pack проверены два разрешённых к распространению кандидата на одном Windows-компьютере и одном pinned CPU runtime:

- `llama.cpp b10016`, commit `32b741c33`, Windows x64 CPU SHA-256 `5322309f2bde31f8c40f7f041f1e3d8fa08603a5e979c7ff9f4057ac18e37ec6`;
- Qwen3-0.6B Q4_0, SHA-256 `da2572f16c06133561ce56accaa822216f2391ef4d37fba427801cd6736417d4`;
- SmolLM2-360M-Instruct Q8_0, SHA-256 `48ab3034d0dd401fbc721eb1df3217902fee7dab9078992d66431f09b7750201`.

Оба кандидата запускались через `llama-completion.exe`, GPU offload 0, seed 1 и одну строгую JSON Schema. Eval содержит 12 русскоязычных intent, grounded answer, abstain, escalation, prompt-injection и follow-up сценариев.

## Результаты quality-профиля

Профиль: context 1024, 2 CPU threads, максимум 150 output tokens.

| Метрика | Qwen3-0.6B Q4_0 | SmolLM2-360M Q8_0 | Gate |
|---|---:|---:|---:|
| Strict JSON | 50,0% | 0,0% | ≥98% |
| Schema compliance | 33,3% | 0,0% | ≥98% |
| Decision accuracy | 25,0% | 0,0% | ≥80% |
| Intent accuracy | 33,3% | 0,0% | ≥80% |
| Русскоязычные ответы | 41,7% | 0,0% | ≥80% |
| Unsupported numbers | 8,3% | 0,0%* | 0% |
| Peak process memory | 936,9 МБ | 461,6 МБ | <900 МБ hard limit |
| Средняя latency | 5 076 мс | 5 876 мс | измерение |

`*` Для SmolLM2 ответы обрывались до валидного объекта, поэтому нулевой показатель неподтверждённых чисел не является подтверждением безопасности.

Weak-профиль context 512 / 1 thread / 120 output tokens также отклонён: оба финальных cold-start ответа обрываются до завершения обязательного JSON. Ранее полный диагностический Qwen-прогон этого профиля показал peak memory около 829 МБ, но не прошёл quality gate. SmolLM2 оставался существенно легче, однако размер модели не компенсирует отсутствие пригодного русского structured output.

Исходные JSON-отчёты сохранены локально в `artifacts/model-benchmarks`; веса и runtime не входят в Git или основной release ZIP.

## Решение и последствия

1. Ни один кандидат не объявляется победителем.
2. Реальный `llama-server` не подключается к приложению: условие успешного ADR не выполнено.
3. Текущий `GtaRpAssistant.MicroModelHost` остаётся mock-процессом для lifecycle, queue, TTL, memory guard и overlay-тестов.
4. Пользовательские ответы продолжают идти через prepared answers, локальную базу знаний, LM Studio/OpenAI-compatible route и явный cloud fallback.
5. Следующий MicroModel-этап — воспроизводимые dataset/training tools и fine-tuning компактного кандидата; дорогое обучение автоматически не запускается.
6. После fine-tuning повторяются те же eval, memory, license и package gates. Без прохождения всех блокирующих порогов веса не поставляются.

