# Архитектура

Зависимости направлены внутрь: `Core` не зависит от WPF, Windows, SQLite или конкретного API. `Knowledge`, `Providers` и `Infrastructure.Windows` реализуют порты Core. `App` является composition root на `Microsoft.Extensions.DependencyInjection`.

`MainWindow` содержит только WPF lifecycle, PasswordBox plumbing и Win32 hotkey dispatch. Состояние UI и рабочая audio/session логика находятся в `MainViewModel` и сервисах.

Основной путь:

```text
Transcript
→ AssistantSessionCoordinator
→ RuleBasedIntentDetector
→ ContextSelector
→ SQLite Knowledge
→ AiRouter
→ deterministic/local/cloud provider
→ GroundedAnswerValidator
→ OverlayService
```

`AssistantSessionCoordinator` владеет state machine, cancellation, single-flight, proactive policy и восстановлением после ошибки. `GameSessionMonitor` отслеживает PID каждые три секунды. `ProcessPerformanceMonitor` применяет профиль деградации без доступа к FPS или памяти GTA.

Microphone и GameAudio имеют независимые VAD/segmenter/ring-buffer, но общий приоритетный STT worker: очередь микрофона проверяется первой. GameAudio используется только как контекст.

## Границы проектов

- `Core` — доменные records/interfaces, coordinator, validation и policies;
- `Knowledge` — `knowledge.db`, migrations, exact/FTS и provenance;
- `Providers` — внешние AI protocols;
- `Infrastructure.Windows` — Win32/WASAPI/process/capture;
- `LocalData` — opt-in `assistant-data.db` и история пользователя;
- `MicroModelHost` — отдельный optional процесс с named pipe;
- `App` — WPF feature modules и composition root.

`knowledge.db` никогда не содержит сообщения пользователя. `assistant-data.db` никогда не становится источником официальных игровых фактов.

## Conversation storage

```text
SettingsService.EnableLongTermConversation
→ ConfigurableAssistantConversationStore
   ├─ false: InMemoryAssistantConversationStore
   └─ true:  SqliteAssistantConversationStore (lazy)
```

UI работает только через `AssistantSessionCoordinator`/`IAssistantConversationStore`. SQLite не вызывается из ViewModel напрямую. Полная история и ограниченный model context являются разными задачами; в модель передаётся только bounded relevant snapshot.

Подробная карта composition root, данных, сборки и восстановления: [PROJECT_HANDBOOK.md](PROJECT_HANDBOOK.md).
