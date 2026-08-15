# Local AI intelligence — аудит и первый вертикальный срез

Актуально на 15 августа 2026 года.

## Фактический baseline

В приложение не встроена одна обязательная Qwen. Chat подключается через общий `IChatProvider`: LM Studio, OpenAI-compatible endpoint или разрешённый cloud route. На машине аудита LM Studio CLI 1.3.3/commit `9902c3a`, server включён на `127.0.0.1:54321`, но модель не загружена. Установлена `qwen/qwen3-vl-4b`: GGUF, Q4_K_M, 4B, 3 333 641 502 байта, tool-use/vision metadata и максимальный model context 262 144. Безопасная CPU-first оценка при context 4096/parallel 1 — около 3,10 GiB total memory с низкой уверенностью LM Studio. Это оценка загрузки, не измеренный runtime peak.

Текстовый Balanced-кандидат каталога — Qwen3 4B Instruct 2507 Q4_K_M; фактически на машине он не установлен. Ранее Qwen3-0.6B Q4_0 запускалась через `llama-completion.exe` CPU-only и была отклонена: quality gate не пройден, weak-profile peak около 829 МБ. Поэтому называть 0.6B текущим «мозгом» продукта неверно.

Во время аудита GTA5 была запущена: working set около 11,6 ГБ, NVIDIA RTX 4070 Ti SUPER имела 16 376 MiB total, 10 722 MiB used и 5 340 MiB free. Реальный LLM latency/tokens-per-second намеренно не измерялись: загрузка модели могла ухудшить текущую игровую сессию. Модельный context 262k не используется целиком: runtime-профиль оценивается с 4096, а `AssistantContextBuilder` ограничивает полезный input примерно 1600 токенами.

## Аудит pipeline

| Область | Текущее состояние |
|---|---|
| System prompt | Один strict JSON prompt в `OpenAiCompatibleChatProvider`; до этого среза пустые `VERIFIED_FACTS` всегда требовали abstain. |
| История | RAM или opt-in SQLite `assistant-data.db`; разговоры можно создавать, открывать, переименовывать и удалять. |
| Recent context | До 6 релевантных turns, character budgets и общий token estimate. |
| Summary | RAM-only deterministic rolling summary старых обменов; не LLM-summary и не сохраняется между запусками. |
| Session state | Goal, situation, open question, recent article/fact IDs в RAM. |
| Long-term memory | SQLite `user-memory.db`, ручные подтверждённые записи и явная адаптация стиля; автоматического candidate/confirm flow ещё нет. |
| Knowledge/RAG | Production runtime использует SQLite exact/prepared/FTS5; optional local embedding rerank только для неоднозначного FTS. Qdrant относится к памяти Codex workspace и не является runtime-зависимостью продукта. |
| Knowledge data | GTA5RP official/community packs; последний зафиксированный полный срез — 48 статей/226 фактов. Текущий gate блокируется истёкшими `validUntil`; даты нельзя продлевать без source review. |
| Voice | Manual PTT/hold/toggle → WASAPI capture → выбранный STT (embedded pack или отдельный endpoint) → editable preview/submit → тот же coordinator. |
| TTS | Windows TTS получает ответ только после явно начатого voice request; game-audio не озвучивается. |
| Intent | `RuleBasedIntentDetector` для voice/proactive gate и `AssistantRequestClassifier` для request type; отдельной intent-модели нет. |
| Tools | Модель отмечена как tool-use capable, но production tool runtime/function calling ещё не подключён. |
| Агенты/роутеры | Нескольких LLM-агентов нет. Есть deterministic preflight/router, configured provider fallback, validator и resource control plane. |
| Quality checks | Product pipeline benchmark, micro-model benchmark, semantic relevance, STT benchmark, capability tester, unit/integration/release gates. |

## Почему ответы были хуже желаемого

Главный дефект был архитектурным, а не только модельным: `knowledge miss` завершал ручной запрос до provider discovery, strict prompt запрещал любой содержательный ответ без GTA-фактов, а вопрос с `?` почти всегда классифицировался как knowledge question. Поэтому модель вообще не получала многие бытовые вопросы и не могла показать разговорные способности. Память была доступна только после успешного knowledge selection. Кроме того, summary сейчас механически склеивает старые вопросы/ответы и не выделяет цели, решения и нерешённые вопросы; long-term memory не имеет безопасного candidate/confirm flow.

## Реализованный Stage 1

Pipeline теперь различает `grounded_knowledge` и `open_conversation`.

```text
manual request → request type → SQLite RAG
  ├─ match: strict facts-only generation/validation
  └─ miss + conversation: bounded history + summary + memory → local LLM → conversation validator
```

Игровые правила, цены, награды и серверные сведения по-прежнему требуют verified facts. Обычный разговор допускает reasoning и уточнение, но запрещает fact IDs, URL, игровую автоматизацию и категоричные обвинения; изменчивые или GTA-факты должны быть обозначены как требующие базы/экрана/tool. Automatic voice при knowledge miss не будит модель.

Добавлен `ml/evaluation/conversation-model-eval.json`: 12 русских сценариев с casual chat, reasoning, follow-up, ambiguity, memory context, GTA grounding, abstain, escalation, intent и prompt injection. Он использует существующий ModelBenchmark CLI и одинаково применим к GGUF-кандидатам. Добавлена review-first JSON Schema для будущих SFT/LoRA записей; факты с высокой изменчивостью должны оставаться в RAG.

## Измеримый результат и следующий шаг

До изменения 7 conversation/follow-up примеров нового набора завершались `knowledge miss`/abstain до вызова LLM. После изменения соответствующий pipeline regression проходит `open_conversation`, передаёт bounded conversation context и принимает безопасный ответ без фиктивных fact IDs. Это pipeline delta, не заявление о качестве реальной Qwen: реальный модельный прогон отложен до остановки GTA.

Phase 5 controlled memory candidates реализована без дополнительной модели: только явные пользовательские предпочтения создают bounded RAM-кандидат на 24 часа, чувствительные данные блокируются, а SQLite-запись появляется лишь после подтверждения в UI. Memory retrieval больше не расходует prompt на нерелевантные записи и сохраняет relevance order.

Основной target уточнён как 32 ГБ RAM и RTX 3060/4060/4060 Ti. Balanced автоматически предпочитает текстовую 4B Q4 instruct-модель вместо VLM, оставляет GTA минимум 2,5 ГБ VRAM и не выполняет Chat/Vision одновременно. Следующий рациональный шаг — прогон conversation benchmark на Qwen3 4B Instruct 2507 и установленной Qwen3-VL 4B после остановки GTA, затем ограниченный read-only Tool Runtime. Маленькая router-модель и LoRA пока не оправданы.
