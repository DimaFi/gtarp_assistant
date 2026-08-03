# Audio pipeline

Microphone и GameAudio нормализуются в PCM signed 16-bit, mono, 16 kHz, little-endian. Каждый канал имеет отдельные фиксированные ring buffer, adaptive energy detector и bounded segmenter с pre-roll 250 мс, post-roll 400 мс, завершением после 700 мс тишины и пределом 20 секунд.

Это детектор громкости, а не полноценное распознавание человеческой речи.

Готовые сегменты помещаются в bounded channels. Один STT worker всегда проверяет microphone queue первой, поэтому одновременно выполняется максимум один STT-запрос. Аудио на диск не записывается.

Предпочтительный GameAudio path использует Windows application loopback для PID GTA и его process tree. `GameSessionMonitor` проверяет процесс каждые три секунды и перепривязывает capture после перезапуска. Если режим недоступен, включается явно обозначенный system-loopback fallback выбранного render device.

GameAudio может пополнять transcript context, но не передаётся intent detector как текущий запрос. Похожие фразы сравниваются в окне ±2 секунды; при конфликте сохраняется microphone version.

Cloud STT требует общего разрешения облака, а отправка GameAudio — отдельного `AllowGameAudioCloud`.

Валидный optional embedded STT pack имеет первый приоритет независимо от Chat route. Он проверяется по manifest/size/SHA-256, лениво запускается на случайном loopback-порту, обрабатывает не более одного сегмента и выгружается после idle TTL. При отсутствии, повреждении, timeout, отмене или process failure worker переходит к прежнему настроенному STT fallback. Полный контракт и текущий quality status описаны в [EMBEDDED_STT](EMBEDDED_STT.md).
