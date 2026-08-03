# Встроенный автономный STT

## Статус

Процессный и упаковочный фундамент P0.2 реализован 3 августа 2026 года. Приложение умеет обнаружить отдельный STT-пак, проверить его манифест, размеры и SHA-256 каждого файла, лениво запустить `whisper-server.exe` на случайном loopback-порту, повторно использовать загруженную модель, остановить runtime при отмене/timeout/превышении памяти и выгрузить его после idle TTL.

Пак пока **не входит в основной portable ZIP и не объявлен production-рекомендацией**. Перед публикацией нужен записанный с согласия участников русский GTA5RP-набор и сравнительный отчёт минимум для двух моделей. Английский JFK smoke подтверждает только совместимость runtime/API, но не качество русского распознавания.

## Зафиксированный baseline

- runtime: официальный `whisper.cpp v1.9.1`, `whisper-bin-x64.zip`;
- SHA-256 runtime archive: `7d8be46ecd31828e1eb7a2ecdd0d6b314feafd82163038ab6092594b0a063539`;
- baseline model: multilingual `ggml-base-q8_0.bin`, revision `5359861c739e955e79d9a303bcbc70fb988958b1`;
- размер модели: `81 768 585` байт;
- SHA-256 модели: `c577b9a86e7e048a0b7eada054f4dd79a56bbfa911fbdacf900ac5b567cbb7d9`;
- лицензия runtime/model repository: MIT; license SHA-256 `94f29bbed6a22c35b992c5c6ebf0e7c92f13b836b90f36f461c9cf2f0f1d010d`;
- режим: CPU-only, 2 threads, один активный запрос, русский язык, без ffmpeg и GPU offload;
- hard private/working-set limit: 1 100 MiB; request timeout: 45 секунд; idle TTL: 120 секунд.

На текущей машине runtime smoke из каталога с пробелами показал 2,36–2,56 секунды на официальный JFK WAV, около 282 MiB working set и 771–773 MiB private memory. Это не русский quality result.

Официальные источники: [релиз whisper.cpp v1.9.1](https://github.com/ggml-org/whisper.cpp/releases/tag/v1.9.1), [server contract](https://github.com/ggml-org/whisper.cpp/tree/v1.9.1/examples/server), [model repository](https://huggingface.co/ggerganov/whisper.cpp).

## Сборка отдельного пака

Веса и runtime сохраняются только в игнорируемом `artifacts/stt`; основной ZIP не изменяется.

```powershell
.\eng\build-stt-pack.ps1
```

Повторная сборка существующей папки:

```powershell
.\eng\build-stt-pack.ps1 -Force
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

Формат находится в `ml/evaluation/stt-russian-gta5rp.example.json`. Production-набор должен содержать минимум 40 согласованно записанных фраз: разные голоса, микрофоны, тихий фон/игровой шум, короткие и длинные вопросы, числа и термины GTA5RP. Минимальный технический gate инструмента: не менее 12 сценариев, средний WER ≤25%, recall обязательных терминов ≥85%, ноль пустых/error transcript, p95 ≤5 секунд и память ≤1 100 MiB.

Перед ADR сравниваются как минимум `base-q8_0` и более точный кандидат `small-q5_1` на одних WAV и одном ПК. Если ни один не проходит gate, embedded pack не публикуется: сохраняются manual text и внешний STT fallback.

## Следующие действия

1. Собрать и проверить русский GTA5RP speech dataset с явным согласием и без персональных данных.
2. Добавить второй pinned candidate, прогнать одинаковый benchmark и 100 start/cancel/idle циклов.
3. Записать ADR с победителем либо отказом от обоих кандидатов.
4. Только после PASS добавить downloadable STT pack в GitHub Release и выполнить P0.3 на чистом Windows-профиле без сети, LM Studio и Python.
