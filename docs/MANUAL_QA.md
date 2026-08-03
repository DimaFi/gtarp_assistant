# Manual QA

Перед выпуском выполнить на Windows 10 и Windows 11:

- чистый запуск без GTA, LM Studio и API-ключей;
- tray open/pause/exit и повторное открытие окна;
- navigation по страницам Ассистент, Аудио, AI и модели, Поведение, Приватность и База знаний; selected state, клавиатурный focus и сохранение несохранённых полей при переключении;
- проверка endpoint и DND-команд из соответствующих feature pages; диалог «Источник и факты» имеет владельца и не теряется за главным окном;
- `Ctrl+Alt+Q/A/S/P`, включая конфликт hotkey с другой программой;
- overlay поверх borderless/fullscreen, отсутствие фокуса и Alt+Tab, четыре позиции, DPI 100/150/200% и два монитора;
- compact overlay не забирает фокус у GTA; «Раскрыть» открывает expanded overlay, `Esc` закрывает его, а «Свернуть» возвращает compact-представление;
- expanded overlay остаётся в рабочей области экрана, прокручивает длинный ответ и показывает отдельную пометку для community-данных;
- сохранение настроек, DPAPI round-trip и отсутствие ключей в `settings.json`/logs;
- microphone start/stop, трёхсекундный test/level meter, unplug/replug, default-device switch, STT timeout/cancellation;
- manual voice toggle: повторный `Ctrl+Alt+A` отменяет capture/STT/preview; распознанный текст редактируется и отправляется только после подтверждения, а opt-in auto-submit пропускает preview;
- после переходов между feature pages состояние введённого вопроса, выбранных audio devices и voice settings сохраняется в соответствующем модуле;
- в обычном режиме создать два диалога, переключиться между ними, переименовать и удалить один с подтверждением; после перезапуска история должна отсутствовать;
- включить **Долгосрочное общение**, повторить сценарий, закрыть и открыть приложение; current conversation, названия и сообщения должны восстановиться из `assistant-data.db`;
- проверить Enter=отправить, Shift+Enter=новая строка, retry последнего вопроса, copy последнего ответа и cancellation медленного provider-запроса;
- проверить Markdown: заголовок, список, `**жирный**` и inline-code отображаются, HTML остаётся текстом, URL не открывается автоматически;
- при минимальном размере окна список диалогов, вопрос и действия не перекрываются; кнопки действий переносятся на следующую строку;
- запуск, закрытие и перезапуск GTA с новым PID; process-loopback rebind и system fallback;
- подтверждение, что GameAudio никогда не активирует overlay;
- CloudLite/Balanced/LocalHybrid под нагрузкой и деградация game-audio STT;
- независимо переключить STT/Chat/Vision/TTS/Embeddings между Disabled/Cloud/Local/Automatic/Custom и убедиться, что изменение одного route не меняет остальные или PerformanceProfile;
- загрузить старый `settings.json`, проверить появление `ProviderSettingsVersion`, `ProviderConnections` и `ProviderRouting`, сохранение DPAPI references и прежнего cloud opt-in;
- proactive cooldown: 1/мин, 3/10 мин, topic 2 мин, DND 5 мин/session;
- manual vision preview/cancel/send; проверка очистки памяти и exclusive-fullscreen limitation;
- TTS off by default, запуск только после `Ctrl+Alt+A`, выбор голоса/устройства и остановка при паузе;
- community lookup: запросить «какая награда за Вращайте барабан», «рецепт Оливье», «что делать при артериальном кровотечении», «износ двигателя» и «как фармить BP в тире»;
- убедиться, что каждый ответ из community lookup начинается с «По данным игроков:», а медицинский ответ сформулирован только как игровая подсказка;
- 30 минут idle и игровая сессия с измерением CPU, working set, очередей и размера логов.
- убедиться, что portable-релиз содержит `micro-model-host/GtaRpAssistant.MicroModelHost.exe`; настоящий model pack в текущем релизе отсутствует намеренно.
- перед любым реальным model pack выполнить шаги из [`MICRO_MODEL_BENCHMARK.md`](./MICRO_MODEL_BENCHMARK.md): сравнить минимум два GGUF, проверить русский язык, strict JSON, grounding/prompt injection, peak private memory, лицензию, notices и SHA-256; без успешного отчёта и ADR модель не добавлять в релиз.

Автоматизированная проверка не заменяет эти hardware/UI сценарии.

Перед ручной матрицей выполняется полный автоматический gate:

```powershell
.\eng\build.ps1
```

Для отдельной проверки уже опубликованного приложения:

```powershell
.\eng\smoke.ps1 -Executable .\artifacts\publish\win-x64\GtaRpAssistant.App.exe
```

Smoke-режим создаёт окно и tray, инициализирует DI, настройки и knowledge DB, переключает все зарегистрированные feature pages и проверяет их layout, selected state и automation contract, затем штатно освобождает сервисы и завершается с кодом 0.

Для повторной генерации визуальных снимков всех модулей:

```powershell
.\eng\capture-ui.ps1 -Executable .\artifacts\publish\win-x64\GtaRpAssistant.App.exe
```

Снимки `assistant.png`, `audio.png`, `providers.png`, `behavior.png`, `privacy.png`, `knowledge.png`, `about.png`, `overlay-compact.png`, `overlay-expanded.png`, `vision-preview.png` и `voice-preview.png` сохраняются в `artifacts/ui-snapshots`. Скрипт проверяет наличие и непустой рендер каждого файла. Полный `eng/build.ps1` запускает smoke и capture автоматически, если не указан `-SkipSmoke`.

WPF smoke дополнительно выполняет compact → expanded → compact → hidden, открывает vision preview для отдельных Cancel/Confirm сценариев и проверяет редактируемый voice preview с подтверждением. Используется сгенерированное тестовое изображение и тестовый transcript: provider не вызывается, сетевой запрос не выполняется.

Smoke и capture всегда запускаются с уникальным временным профилем. Они не читают пользовательский `%LocalAppData%\GtaRpAssistant`, не меняют Startup и не регистрируют глобальные сочетания клавиш. Внутри временного профиля проверяется сохранение endpoint, DPAPI round-trip тестового секрета и отсутствие секрета в `settings.json`.

Повторяемый lifecycle-soak запускается командой `./eng/soak.ps1 -Executable <path> -Iterations 10` либо как часть gate через `./eng/build.ps1 -SoakIterations 10`. JSON-отчёт сохраняется в `artifacts/reports/lifecycle-soak.json`; он содержит exit code, timeout, длительность и peak working set каждого запуска.
