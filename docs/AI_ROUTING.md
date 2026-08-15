# AI routing

Порядок маршрутизации:

```text
verified prepared answer
→ sufficient verified grounding + healthy local provider
→ sufficient verified grounding + healthy allowed cloud provider
→ abstain
```

Health provider кешируется на 30 секунд. `CloudLite` не загружает и не использует локальную chat-модель. Облачный provider недоступен маршрутизатору без `AllowCloud`.

Перед provider call `AssistantContextBuilder` формирует bounded request: verified facts имеют приоритет, transcript/history/memory получают независимые лимиты, обычный output ограничен 300 токенами, problem solving — 450. Prepared answer и versioned answer-cache hit завершаются до provider health-check.

Модели получают отдельно `VERIFIED_FACTS` и недоверенный transcript context. Ответ обязан быть structured JSON и проходит `GroundedAnswerValidator`. Валидатор отклоняет неизвестные fact IDs, неподтверждённые числа/URL, устаревшие или конфликтующие источники и предложения автоматизации.

Автоматическая подсказка с `abstain` не показывается. Ручной запрос получает честное сообщение о недостатке данных.
