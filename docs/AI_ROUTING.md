# AI routing

Порядок маршрутизации:

```text
verified prepared answer
→ explicit assumption guidance for supported broad goals/follow-ups
→ sufficient verified grounding + healthy local provider
→ sufficient verified grounding + healthy allowed cloud provider
→ abstain
```

Health provider кешируется на 30 секунд. `CloudLite` не загружает и не использует локальную chat-модель. Облачный provider недоступен маршрутизатору без `AllowCloud`.

Перед provider call `AssistantContextBuilder` формирует bounded request: verified facts имеют приоритет, transcript/history/memory получают независимые лимиты, обычный output ограничен 300 токенами, problem solving — 450. Prepared answer и versioned answer-cache hit завершаются до provider health-check.

Долгий диалог использует in-memory structured session state и bounded rolling summary вместо полной истории. Эти производные от частного разговора поля передаются только локальному provider; cloud получает прежний явно разрешённый текущий request context, но не session summary/state и не User Memory.

Модели получают отдельно `VERIFIED_FACTS` и недоверенный transcript context. Ответ обязан быть structured JSON и проходит `GroundedAnswerValidator`. Валидатор отклоняет неизвестные fact IDs, неподтверждённые числа/URL, устаревшие или конфликтующие источники и предложения автоматизации.

Автоматическая подсказка с `abstain` не показывается. Ручной запрос получает честное сообщение о недостатке данных.

Knowledge miss больше не означает обязательный отказ для поддерживаемых широких целей. `AssistantInferenceGrounding` может явно предположить стартовые условия пользователя и дать общий план без provider call. Предположения всегда называются предположениями и касаются цели/стратегии; цены, правила, уровни доступа и наказания по-прежнему нельзя додумывать. Follow-up вроде «что тебе нужно?» использует сохранённую цель предыдущего вопроса и предлагает продолжить с допущениями, а уточнения делает необязательными.
