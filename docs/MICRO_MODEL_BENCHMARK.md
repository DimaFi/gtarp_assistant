# MicroModel: candidate benchmark и лицензионный gate

Дата ревизии: 15 июля 2026 года.

Этап выполнен на двух настоящих GGUF-файлах. Оба базовых кандидата отклонены; решение и реальные метрики зафиксированы в [`adr/ADR-0001-micro-model-candidate-benchmark.md`](./adr/ADR-0001-micro-model-candidate-benchmark.md). Веса не входят в репозиторий или основной ZIP и не скачиваются приложением автоматически.

## Что добавлено

- конфиг кандидатов: [`ml/configs/micro-model-candidates.json`](../ml/configs/micro-model-candidates.json);
- русскоязычный eval-набор: [`ml/evaluation/micro-model-eval.json`](../ml/evaluation/micro-model-eval.json);
- строгая JSON Schema: [`ml/evaluation/micro-model-response.schema.json`](../ml/evaluation/micro-model-response.schema.json);
- CLI: [`tools/GtaRpAssistant.ModelBenchmark`](../tools/GtaRpAssistant.ModelBenchmark);
- автоматическая валидация каталога и eval-набора в `eng/build.ps1`;
- unit-тесты правил каталога, context budget, grounding, чисел, лицензии, памяти и ранжирования.

## Кандидаты

| Кандидат | Почему в списке | Лицензия и поставка | Статус |
|---|---|---|---|
| Qwen3-0.6B | 600M, instruction/post-trained, 100+ языков, есть quantization ecosystem | Apache-2.0; GGUF и runtime проверены по SHA-256 | отклонён: quality и hard-memory gate |
| Gemma 3 270M IT | минимальный размер, официальный card заявляет 140+ языков | отдельные Gemma Terms: при распространении нужны копия условий, use restrictions и Notice | условный, не проходит release gate без отдельного одобрения |
| SmolLM2-360M-Instruct | компактная Apache-2.0 контрольная точка | Apache-2.0, официальный Q8_0 проверен по SHA-256 | отклонён: русский structured-output gate |

Первичные источники:

- [Qwen3-0.6B model card](https://huggingface.co/Qwen/Qwen3-0.6B) и [Apache-2.0 license](https://huggingface.co/Qwen/Qwen3-0.6B/blob/main/LICENSE);
- [Gemma 3 270M IT model card](https://huggingface.co/google/gemma-3-270m-it) и [Gemma Terms](https://ai.google.dev/gemma/terms);
- [SmolLM2-360M-Instruct model card](https://huggingface.co/HuggingFaceTB/SmolLM2-360M-Instruct);
- [официальный llama.cpp](https://github.com/ggml-org/llama.cpp) и [JSON Schema/GBNF guide](https://github.com/ggml-org/llama.cpp/blob/master/grammars/README.md).

Это техническая предварительная проверка, а не юридическое заключение. Для дистрибутива фиксируется точная ревизия исходной модели, runtime, GGUF-конвертации, полный текст лицензии, notices и SHA-256.

## Команды

Проверка метаданных без модели:

```powershell
dotnet run --project tools/GtaRpAssistant.ModelBenchmark -c Release -- validate ml/configs/micro-model-candidates.json ml/evaluation/micro-model-eval.json
```

Полный benchmark локального GGUF через заранее установленный `llama-completion.exe`:

```powershell
dotnet run --project tools/GtaRpAssistant.ModelBenchmark -c Release -- benchmark-model <model.gguf> <llama-completion.exe> qwen3-0.6b --context 1024 --threads 2 --output 150
```

Дополнительные команды: `evaluate-model`, `memory-test`, `cold-start-test`, `compare-models`. Они не скачивают runtime или модель. Отчёты сохраняются в `artifacts/model-benchmarks` и содержат SHA-256 модели, сведения о машине, latency, time-to-first-output, peak working set/private bytes и quality metrics.

## Release gate

Модель блокируется, если выполняется хотя бы одно условие:

- license review не `approved` или распространение не разрешено;
- максимум working set/private/committed memory достигает 900 МБ;
- strict JSON или schema compliance ниже 98%;
- обнаружен выдуманный fact ID или число без опоры на факт;
- decision/intent accuracy ниже порога;
- русскоязычные ответы ниже 80%, обнаружен wrong-server ответ или runtime failure;
- нет intent cases, prompt-injection, abstain и escalation проверок.

Ранжирование сначала учитывает прохождение gate, затем intent/decision accuracy, schema compliance, hallucination rate, память и latency. Маленький файл сам по себе не делает модель подходящей.

## Результат и следующий исполнимый шаг

1. Базовые Qwen3-0.6B Q4_0 и SmolLM2-360M Q8_0 проверены и отклонены.
2. Реальный runtime не подключается; mock fallback сохраняется.
3. Подготовить dataset builder, training config, LoRA/QLoRA, export/quantization и license report без автоматического запуска обучения.
4. После fine-tuning повторить этот benchmark и только при полном PASS оформлять новый ADR поставки.

Для локальных прогонов предусмотрен отдельный, не входящий в release gate скрипт `eng/prepare-model-benchmark.ps1`. Он поддерживает `-Candidate qwen3-0.6b`, `-Candidate smollm2-360m-instruct` и `-Candidate all`, загружает pinned официальный Windows CPU runtime и модели только в `artifacts/model-benchmarks/assets`, проверяет опубликованные SHA-256 и записывает `provenance.json`.
