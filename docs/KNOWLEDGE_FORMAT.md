# Knowledge format

Pack содержит `manifest.json`, JSON-статьи и словари. Loader проверяет уникальность article/fact ID, project, server scope, aliases и согласованность verified-статусов. Demo-статьи обязательно имеют `demo: true`, `verified: false`.

SQLite хранит server scope в нормализованной таблице и сравнивает сервер точным значением. Поиск выполняет prepared question, exact alias и FTS5. Близкие top-results с различающимися verified facts помечаются как конфликт и не могут сформировать подтверждённый ответ.

Реальная verified-статья должна иметь источник, дату обновления, reviewer, версию и при необходимости `validUntil`. Непроверенные, устаревшие и конфликтующие данные приводят к `abstain`.

Данные, подтверждённые владельцем продукта как найденные игроками, хранятся отдельно в `knowledge/reference/community`. Они не входят в официальный manifest, source review и статистику покрытия Wiki. При запуске `CommunityReferenceLoader` превращает каждую строку в небольшую поисковую статью. Текст каждого факта и prepared answer обязательно начинается с «По данным игроков:», поэтому приложение может использовать сведения без ложной ссылки на официальный источник. Для динамических цен, наград и механик также применяется короткий `validUntil`; по истечении срока требуется повторная проверка.

Пустые значения в community-таблицах означают «нет данных». Loader не должен подставлять предполагаемые числа, инструменты или требования навыка. Медицинская шпаргалка относится только к игровой механике и не является медицинской рекомендацией.

База SQLite имеет версию схемы в `PRAGMA user_version`. Миграция v2 сохраняет старые статьи, нормализует legacy `server_scope` и добавляет `source_url`, `article_version`, `verified_by`. Миграция v3 добавляет FTS5 по атомарным фактам, поэтому запрос может найти статью по детали, отсутствующей в заголовке.

В LLM передаётся не весь pack, а одна найденная статья: максимум 6 наиболее релевантных проверенных фактов и 1600 символов. Transcript ограничен отдельно. Prepared answer возвращается без вызова LLM.

CLI поддерживает структурную проверку, governance lint и машинно-читаемую инспекцию:

```powershell
dotnet run --project tools/GtaRpAssistant.KnowledgePackTool -- validate knowledge/packs/gta5rp --strict
dotnet run --project tools/GtaRpAssistant.KnowledgePackTool -- lint knowledge/packs/gta5rp
dotnet run --project tools/GtaRpAssistant.KnowledgePackTool -- inspect knowledge/packs/gta5rp --json
dotnet run --project tools/GtaRpAssistant.KnowledgePackTool -- check-sources knowledge/packs/gta5rp
```

Полный процесс подготовки и отзыва проверенных статей описан в [KNOWLEDGE_AUTHORING.md](KNOWLEDGE_AUTHORING.md).
Текущее и планируемое покрытие официальной wiki описано в [KNOWLEDGE_COVERAGE.md](KNOWLEDGE_COVERAGE.md).
Состав и правила использования сведений игроков описаны в [`knowledge/reference/community/README.md`](../knowledge/reference/community/README.md).
