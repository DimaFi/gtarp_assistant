# Производительность

Нет постоянного render loop, локальной встроенной LLM, постоянной GPU-нагрузки, записи аудио или busy waiting. Audio buffers и channels ограничены; одновременно выполняется не более одного STT и одного chat/vision запроса.

`ProcessPerformanceMonitor` раз в пять секунд измеряет только собственный процесс. `PerformanceController` может отключить experimental proactivity и game-audio STT, сохранив microphone path.

Контрольный smoke-замер Release-сборки 13.07.2026 после DI/MVVM, session monitor, manual vision и ленивого opt-in TTS: working set 138,2 МБ, суммарное процессорное время за первые восемь секунд 0,141 с. Это единичный startup-замер на одной машине, а не сравнительный benchmark. Значение ниже цели 150 МБ и жёсткого ориентира 200 МБ.

Перед релизом нужны длительные idle/in-game измерения на слабом ПК и проверка профилей `CloudLite`, `Balanced`, `LocalHybrid`.
