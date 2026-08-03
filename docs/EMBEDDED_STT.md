# Встроенный автономный STT

## Статус

Процессный и упаковочный фундамент P0.2 реализован 3 августа 2026 года. Приложение умеет обнаружить отдельный STT-пак, проверить его манифест, размеры и SHA-256 каждого файла, лениво запустить `whisper-server.exe` на случайном loopback-порту, повторно использовать загруженную модель, остановить runtime при отмене/timeout/превышении памяти и выгрузить его после idle TTL.

Оба отдельных пака воспроизводимо собираются, но пока **не входят в основной portable ZIP и не объявлены production-рекомендацией**. Перед публикацией нужен записанный с согласия участников русский GTA5RP-набор и сравнительный quality-отчёт. Английский JFK smoke подтверждает только совместимость runtime/API, но не качество русского распознавания.

## Зафиксированные кандидаты

- runtime: официальный `whisper.cpp v1.9.1`, `whisper-bin-x64.zip`;
- SHA-256 runtime archive: `7d8be46ecd31828e1eb7a2ecdd0d6b314feafd82163038ab6092594b0a063539`;
- baseline model: multilingual `ggml-base-q8_0.bin`, revision `5359861c739e955e79d9a303bcbc70fb988958b1`;
- размер модели: `81 768 585` байт;
- SHA-256 модели: `c577b9a86e7e048a0b7eada054f4dd79a56bbfa911fbdacf900ac5b567cbb7d9`;
- второй кандидат: multilingual `ggml-small-q5_1.bin`, та же pinned revision;
- размер второго кандидата: `190 085 487` байт;
- SHA-256 второго кандидата: `ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb`;
- лицензия runtime/model repository: MIT; license SHA-256 `94f29bbed6a22c35b992c5c6ebf0e7c92f13b836b90f36f461c9cf2f0f1d010d`;
- режим: CPU-only, 2 threads, один активный запрос, русский язык, без ffmpeg и GPU offload;
- hard private/working-set limit: 1 100 MiB; request timeout: 45 секунд; idle TTL: 120 секунд.

### Техническое сравнение на текущей машине

| Кандидат | Cold lifecycle, 3 запуска | Peak working set | Peak private | Orphan processes |
|---|---:|---:|---:|---:|
| `base-q8_0` | 2,66–3,08 с | 282 MiB | 773 MiB | 0 |
| `small-q5_1` | 8,04–8,32 с | 500 MiB | 992 MiB | 0 |

Дополнительно `small-q5_1` установлен из ZIP в каталог с пробелами и успешно выполнил реальную транскрибацию оттуда за 8,53 с при 991 MiB private memory. Оба runtime уложились в единый hard limit 1 100 MiB. Отмена и уничтожение дерева процессов покрыты интеграционным тестом provider; три независимых start/transcribe/dispose цикла каждого кандидата не оставили дочерних процессов.

Это **не русский quality result**. На техническом smoke `base-q8_0` существенно быстрее и легче, однако победитель не выбирается до WER/term-recall прогона на одинаковых русских WAV.

Официальные источники: [релиз whisper.cpp v1.9.1](https://github.com/ggml-org/whisper.cpp/releases/tag/v1.9.1), [server contract](https://github.com/ggml-org/whisper.cpp/tree/v1.9.1/examples/server), [model repository](https://huggingface.co/ggerganov/whisper.cpp).

## Сборка отдельного пака

Веса и runtime сохраняются только в игнорируемом `artifacts/stt`; основной ZIP не изменяется.

```powershell
.\eng\build-stt-pack.ps1 -Candidate base-q8_0
.\eng\build-stt-pack.ps1 -Candidate small-q5_1
```

Повторная сборка существующей папки:

```powershell
.\eng\build-stt-pack.ps1 -Candidate base-q8_0 -Force
```

Нестандартные каталоги поддерживаются параметрами `-Destination` и `-DownloadDirectory`. Скрипт загружает только pinned HTTPS-артефакты, сверяет опубликованные SHA-256 до распаковки, копирует минимальный CPU runtime, формирует `stt-pack.json` и отдельный ZIP с checksum.

## Установка и использование

```powershell
$hash = (Get-Content .\artifacts\stt\release\GtaRpAssistant-STT-base-q8_0-v1.9.1-win-x64.zip.sha256 -Raw).Split()[0]
.\eng\install-stt-pack.ps1 `
  -Package .\artifacts\stt\release\GtaRpAssistant-STT-base-q8_0-v1.9.1-win-x64.zip `
  -ExpectedSha256 $hash
```

Стандартный путь: `%LOCALAPPDATA%\GtaRpAssistant\model-packs\stt`. Можно передать любой безопасный `-Destination`, затем выбрать эту папку на странице **Аудио → Локальное распознавание речи**. Пустое поле означает стандартный путь.

При включённой галочке валидный embedded provider становится первым в STT route. Если пак отсутствует или повреждён, приложение не запускает его и продолжает использовать настроенные OpenAI-compatible local/cloud providers. Ручной текстовый ввод работает всегда. Chat-модель LM Studio не является STT-моделью.

## Runtime и безопасность

1. `CheckHealthAsync` только проверяет пак и не загружает модель.
2. Первая транскрибация запускает `whisper-server.exe` без shell и передаёт аргументы через `ProcessStartInfo.ArgumentList`.
3. Сервер слушает только случайный `127.0.0.1` порт; публичная директория пуста; `--convert`/ffmpeg не используются.
4. Аудио передаётся как PCM16 mono 16 kHz WAV в `/inference`; файл на диск не записывается.
5. Semaphore допускает только одну транскрибацию. Отмена, timeout и hard memory limit завершают всё дерево процесса, после чего следующий provider route может выполнить fallback.
6. Успешный runtime остаётся загруженным на idle TTL, затем освобождает память. Выход приложения также уничтожает процесс.

## Quality gate

Benchmark запускается так:

```powershell
dotnet run --project tools/GtaRpAssistant.SttBenchmark -- `
  evaluate <pack-directory> <dataset.json> <report.json>
```

Готовый манифест из 40 фраз находится в `ml/evaluation/stt-russian-gta5rp-v1.json`. В нём нет аудио и персональных данных: WAV записываются отдельно, только после явного действия участника. Интерактивная запись с системного микрофона:

```powershell
dotnet run --project tools/GtaRpAssistant.SttBenchmark -- `
  record ml/evaluation/stt-russian-gta5rp-v1.json
```

По умолчанию используется активный микрофон связи Windows. Его точный device ID можно передать после пути к датасету. Существующие WAV не перезаписываются; для осознанной повторной записи нужен `--overwrite`. Инструмент работает только в интерактивной консоли, сохраняет PCM16 mono 16 kHz локально рядом с манифестом и ничего не отправляет в сеть.

Список активных устройств и их точных ID:

```powershell
dotnet run --project tools/GtaRpAssistant.SttBenchmark -- devices
```

Production-набор должен содержать все 40 согласованно записанных фраз: разные голоса, микрофоны, тихий фон/игровой шум, короткие и длинные вопросы, числа и термины GTA5RP. Gate: средний WER ≤25%, recall обязательных терминов ≥85%, ноль пустых/error transcript, p95 ≤5 секунд и память ≤1 100 MiB.

Перед ADR сравниваются как минимум `base-q8_0` и более точный кандидат `small-q5_1` на одних WAV и одном ПК. Если ни один не проходит gate, embedded pack не публикуется: сохраняются manual text и внешний STT fallback.

Полный сравнительный прогон одной командой:

```powershell
.\eng\compare-stt-candidates.ps1
```

Скрипт до запуска моделей проверяет наличие всех WAV, безопасные относительные пути и оба pack manifest. Затем он запускает два `evaluate` на одном файле датасета и формирует `artifacts/stt/comparisons/current/comparison.json`. Версионированный compare gate повторно проверяет schema, SHA-256 и число сценариев датасета, одинаковые gate/case definitions, WER каждого transcript, term recall, агрегаты, ошибки, p95, память и флаг PASS. Повреждённый, устаревший либо собранный на другом датасете отчёт отклоняется.

Политика рекомендации:

1. не прошли оба — `reject-both`;
2. прошёл только один — рекомендуется он;
3. прошли оба — материальная разница WER от 2 процентных пунктов имеет первый приоритет;
4. при близком WER разница term recall от 2 пунктов имеет второй приоритет;
5. при близком качестве выбирается меньшая peak memory, затем меньший p95.

После успешного quality gate можно сразу выполнить lifecycle победителя:

```powershell
.\eng\compare-stt-candidates.ps1 `
  -RunLifecycle `
  -LifecycleWave <pcm16-mono-16khz.wav> `
  -LifecycleIterations 100
```

При `reject-both` lifecycle автоматически не запускается. Exit code `0` означает рекомендацию кандидата, `2` — корректно выполненный gate без победителя, остальные ошибки означают проблему инфраструктуры или целостности данных.

Проверка многократного запуска и гарантированного завершения дочернего процесса:

```powershell
dotnet run --project tools/GtaRpAssistant.SttBenchmark -- `
  lifecycle <pack-directory> <pcm16-mono-16khz.wav> 100 <report.json>
```

Каждая итерация создаёт provider, запускает реальную транскрибацию, освобождает provider и проверяет, что PID runtime больше не существует. В JSON сохраняются ошибки, p95, working set, private memory и признак orphan process.

## Следующие действия

1. Записать все 40 фраз `stt-russian-gta5rp-v1.json` с явным согласием и без персональных данных, желательно несколькими голосами и на нескольких микрофонах.
2. Запустить `compare-stt-candidates.ps1`; не подменять quality gate синтетической или английской речью.
3. Для кандидата, прошедшего quality gate, выполнить 100 lifecycle cycles, weak-PC и шумовой профиль.
4. Записать ADR с победителем либо отказом от обоих кандидатов.
5. Только после PASS добавить downloadable STT pack в GitHub Release и выполнить P0.3 на чистом Windows-профиле без сети, LM Studio и Python.
