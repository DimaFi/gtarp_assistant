# Безопасность

API-ключи не входят в JSON и логи; `DpapiSecretStore` защищает их в контексте Windows CurrentUser. Удалённые provider endpoints требуют HTTPS, localhost допускает HTTP.

Transcript, game audio и текст на снимке считаются недоверенными данными. Они отделены от verified facts, не могут менять настройки, вызывать инструменты или выполнять команды. Output модели никогда не исполняется.

Chat output проходит grounded validator. Manual vision использует отдельный строгий prompt, отклоняет URL и признаки автоматизации и отображается как непроверенное наблюдение, а не игровое правило.

Приложение не внедряется в GTA, не читает память процесса, не получает административные права и не генерирует игровой ввод.

Optional embedded STT не запускается из произвольной папки модели. Выбранный каталог обязан содержать поддерживаемый `stt-pack.json`; entry point, модель и лицензия должны иметь относительные безопасные пути и входить в список size/SHA-256. Reparse/symlink-файлы отклоняются. `whisper-server` запускается без shell, elevation, ffmpeg и GPU offload, слушает только случайный loopback-порт и получает пустую public-директорию. Timeout, cancellation и hard memory limit завершают всё дерево процесса. STT-пак выпускается отдельно от основного ZIP и сохраняет license/source metadata.
