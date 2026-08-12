# ADR-0002: whisper.cpp-кандидаты отклонены, GigaAM v2 проходит quality gate

- Статус: принято
- Дата: 12 августа 2026 года
- Решение: не публиковать проверенные whisper.cpp packs; принять GigaAM v2 + sherpa-onnx как quality-победителя и направить его на hardware/final-pack gate.

## Контекст

На одном Windows x64 компьютере проверены официальный `whisper.cpp v1.9.1`, CPU-only режим с двумя потоками и multilingual модели из pinned revision `5359861c739e955e79d9a303bcbc70fb988958b1`. Consent-based русский GTA5RP dataset содержит 48 живых PCM16 mono 16 kHz WAV: игровые названия, числа, длинные и короткие вопросы, а также варианты произношения BP/DP.

Блокирующий gate: WER ≤25%, term recall ≥85%, p95 ≤5 секунд, ноль пустых/error transcript и память ≤1 100 MiB. BP и DP канонизируются раздельно: варианты `БП / Би Пи / БПишки / бонус-поинты` не считаются ошибкой для BP, варианты `ДП / Ди Пи / ДПишки / донат-поинты / донатная валюта` — для DP; смешение двух валют остаётся ошибкой.

## Результаты

| Кандидат | WER | Term recall | p95 | Peak private | Runtime failures | Итог |
|---|---:|---:|---:|---:|---:|---|
| `base-q8_0` | 49,1% | 31,9% | 1,63 с | 791 MiB | 0 | FAIL quality |
| `small-q5_1` | 32,5% | 42,7% | 6,57 с | 991 MiB | 0 | FAIL quality + latency |
| `small-q8_0` | 39,7%* | 40,3%* | 7,23 с* | 1 117 MiB | 2 memory watchdog | FAIL quality + latency + memory |
| `sherpa-onnx-nemo-ctc-giga-am-v2-russian-2025-04-19` | **7,7%** | **87,5%** | **0,43 с** | 405 MiB quality / 307 MiB cold peak | 0 | **PASS quality + reference lifecycle** |

`*` Экспериментальный q8_0 прогон включал domain prompt, который на сопоставимых исходных 40 случаях ухудшил `small-q5_1` с WER 32,9% до 39,7%. Prompt удалён из production-кода. Повторный q8_0 прогон без prompt не выполнялся: кандидат уже доказанно нарушил hard memory gate и дважды был остановлен watchdog.

Исходные отчёты находятся локально в `artifacts/stt/comparisons/final-48-no-prompt`, `artifacts/stt/comparisons/domain-prompt-v1` и `artifacts/stt/comparisons/small-q8_0-v1`. WAV, модели и runtime игнорируются Git и не входят в основной release.

## Решение и последствия

1. Ни один проверенный candidate ZIP пока не является финальным voice pack и не публикуется пользователям.
2. GigaAM v2 становится единственным quality-победителем. Штатный C# provider и reference lifecycle завершены; до публикации остаётся независимый профиль `weak-pc`.
3. Domain prompt отклонён как ухудшающий общий русский STT; скрыто подгонять benchmark словарём запрещено.
4. Безопасная BP/DP-нормализация и 48-case dataset сохраняются как обязательный контракт для следующего кандидата.
5. Приложение продолжает поддерживать внешний local/cloud STT, редактируемое подтверждение распознанного вопроса и ручной ввод.
6. Пороги не ослаблены. Scoring учитывает только явные доменные варианты и русскую словоформу `репутаций/репутации`; исходные transcript сохранены в отчёте.

Артефакты GigaAM сохранены только локально в `artifacts/stt`: archive SHA-256 `777be8717d8aaf04861823671290f7687f7579fd9ac63a2124955573f920caf5`, модель `236 457 977` байт, отчёт `comparisons/sherpa-nemo-ctc-giga-am-v2-russian/report.json`.

Production C# provider использует отдельный cancellable worker-процесс и локальный binary stdio-протокол. Повторный 48-case прогон: WER 7,7%, term recall 87,5%, p95 0,43 с, peak private 405 MiB, 0 failures. Reference lifecycle: 100/100 успешных cold start/transcribe/dispose, p95 1,96 с, peak private 307 MiB, 0 orphan processes.
