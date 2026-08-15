# Производительность

Нет постоянного render loop, локальной встроенной LLM, постоянной GPU-нагрузки, записи аудио или busy waiting. Audio buffers и channels ограничены; одновременно выполняется не более одного STT и одного chat/vision запроса.

`ProcessPerformanceMonitor` раз в пять секунд измеряет CPU/working set процесса, системную available/total RAM и наличие GTA. Снимок передаётся в единый `ResourceBudgetCoordinator`. На NVIDIA выделенная total/free VRAM читается через штатный `nvidia-smi` не чаще раза в 30 секунд с timeout 750 мс. На AMD/Intel или без утилиты VRAM остаётся неизвестной, а не подменяется фиктивным значением.

Тяжёлые локальные операции получают короткоживущую аренду: Chat, Vision, STT, TTS и загрузка модели уже подключены; контракты также предусмотрены для Embeddings и BackgroundIndexing. При soft pressure откладываются Vision/Embeddings/background, при hard pressure — все локальные AI-workloads. Во время GTA фоновые Embeddings/Indexing не запускаются. Для Compact/Balanced Chat и Vision взаимоисключаются. Выход из pressure требует трёх последовательных здоровых замеров, поэтому политика не осциллирует около порога. Exact/prepared/cache/FTS не требуют аренды и продолжают работать.

Старый fallback без системной telemetry оставлен только для совместимости и использует существенно более высокий аварийный порог процесса; штатный WPF composition root всегда подключает системный sampler.

Контрольный smoke-замер Release-сборки 13.07.2026 после DI/MVVM, session monitor, manual vision и ленивого opt-in TTS: working set 138,2 МБ, суммарное процессорное время за первые восемь секунд 0,141 с. Это единичный startup-замер на одной машине, а не сравнительный benchmark. Значение ниже цели 150 МБ и жёсткого ориентира 200 МБ.

Перед релизом нужны длительные idle/in-game измерения на слабом ПК, проверка профилей `CloudLite`, `Balanced`, `LocalHybrid` и отдельный VRAM adapter для AMD/Intel. Idle TTL применяется к chat, ручной загрузке и Vision.
