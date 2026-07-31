# Production quality benchmark

Актуально на 31 июля 2026 года.

Этот benchmark проверяет не отдельную языковую модель, а реальный путь пользовательского вопроса:

```text
question
→ intent and safety policy
→ production SQLite exact/FTS retrieval
→ AssistantSessionCoordinator
→ grounded answer validation
→ answer or safe abstain
```

## Набор данных

Версионированная конфигурация находится в `ml/evaluation/product-pipeline-eval.json`.

- Все уникальные проверенные prepared-вопросы official и community knowledge автоматически превращаются в обязательные сценарии.
- Ручные сценарии покрывают медицину, достижения, работы, клубы, регистрацию транспорта, питомцев, казино и вопросы, на которые приложение обязано воздержаться от ответа.
- Опечатки, игровой сленг и транслит пока отмечены как exploratory: они видны в отчёте, но не скрывают состояние основного release gate.
- Неоднозначные prepared-вопросы не включаются автоматически: для них нужен отдельный ручной ожидаемый результат.

Каждый сценарий может фиксировать ожидаемое решение `show`/`abstain`, article ID, обязательные fact IDs и фразы, запрещённые фразы, server scope и допустимый срок актуальности.

## Метрики и обязательные пороги

- не менее 250 сценариев;
- не менее 98% успешно пройденных blocking-сценариев;
- не менее 99% правильных решений `show`/`abstain`;
- не менее 98% правильных article IDs;
- 100% фактических ответов с citation;
- 0 ложных ответов;
- 0 ответов с неподтверждёнными числами;
- 0 wrong-server ответов;
- p95 полного локального knowledge-first pipeline не более 500 мс.

Любое нарушение обязательного порога завершает `eng/build.ps1` с ошибкой.

## Запуск

Только benchmark:

```powershell
dotnet run --project tools/GtaRpAssistant.ProductBenchmark/GtaRpAssistant.ProductBenchmark.csproj -c Release -- evaluate ml/evaluation/product-pipeline-eval.json knowledge/packs/gta5rp knowledge/reference/community artifacts/product-benchmark
```

Полный release gate:

```powershell
.\eng\build.ps1 -Configuration Release -Runtime win-x64
```

Отчёты:

- `artifacts/product-benchmark/product-pipeline-report.json` — полный machine-readable результат;
- `artifacts/product-benchmark/product-pipeline-report.md` — короткий отчёт для review.

## Зафиксированный baseline

Проверенный release-прогон 31 июля 2026 года:

- 528 сценариев, из них 524 blocking;
- blocking pass rate: 100%;
- blocking decision accuracy: 100%;
- blocking article accuracy: 100%;
- blocking citation coverage: 100%;
- false answers: 0;
- unsupported-number cases: 0;
- wrong-server cases: 0;
- p95: 0,56 мс на последнем полном release-прогоне машины проверки.

Три exploratory-сценария с опечатками, сленгом и транслитом пока корректно завершаются безопасным `abstain`. Это измеримый пробел для этапов T2/T3, а не разрешение выдавать неподтверждённый ответ.

## Как поддерживать benchmark

1. Каждый исправленный дефект ответа превращать в отдельный regression case.
2. При добавлении проверенного prepared-вопроса убедиться, что автоматически созданный case проходит.
3. Не снижать пороги и не переводить сломанный обязательный сценарий в exploratory ради зелёной сборки.
4. Не записывать в dataset приватные пользовательские диалоги.
5. После изменения retrieval, routing, validator или knowledge обязательно запускать полный release gate.
