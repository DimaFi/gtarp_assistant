# GTA RP Assistant — compact project map

Source code is authoritative. Use this file to locate a component, then inspect the referenced symbol.

| Component | Responsibility | Primary file | Key symbols |
|---|---|---|---|
| Composition root | WPF startup and dependency wiring | `src/GtaRpAssistant.App/App.xaml.cs` | `App`, `ConfigureServices` |
| Answer pipeline | Single-flight question processing, retrieval, routing and grounding | `src/GtaRpAssistant.Core/AssistantSessionCoordinator.cs` | `AssistantSessionCoordinator` |
| Intent and context | Local intent classification and relevant transcript selection | `src/GtaRpAssistant.Core/DecisionServices.cs` | `RuleBasedIntentDetector`, `ContextSelector`, `AssistantConversationGrounding` |
| Model context budget | Bounded verified facts, transcript, turns, memory and output cap | `src/GtaRpAssistant.Core/AssistantContextBuilder.cs` | `IAssistantContextBuilder`, `AssistantContextBuilder`, `AssistantContextBudget` |
| Session situation | RAM-only goal/open question/recent IDs and deterministic rolling summary | `src/GtaRpAssistant.Core/AssistantSessionContext.cs` | `IAssistantSessionContextStore`, `InMemoryAssistantSessionContextStore` |
| Resource control plane | System pressure, workload leases, hysteresis and deterministic degradation | `src/GtaRpAssistant.Core/ResourceBudgetCoordinator.cs`, `src/GtaRpAssistant.Infrastructure.Windows/WindowsHardwareTelemetry.cs` | `IResourceBudgetCoordinator`, `ResourceBudgetCoordinator`, `IHardwareTelemetry` |
| Knowledge storage | SQLite exact/prepared/FTS retrieval | `src/GtaRpAssistant.Knowledge/SqliteKnowledgeRepository.cs` | `SqliteKnowledgeRepository` |
| Optional semantic rerank | Low-confidence FTS gate and fact-preserving candidate reorder | `src/GtaRpAssistant.Core/SemanticReranking.cs` | `ISemanticReranker`, `SemanticRerankPolicy`, `KnowledgeRelevanceDiagnostics` |
| Chat providers | Independent local/cloud chat route construction | `src/GtaRpAssistant.App/ChatProviderCatalog.cs` | `ChatProviderCatalog` |
| Local AI management | LM Studio discovery, model download/load/import and resource policy | `src/GtaRpAssistant.Infrastructure.Windows/LocalAiEngineManager.cs` | `LmStudioEngineAdapter`, `LocalAiEngineManager` |
| Voice orchestration | Microphone session, STT selection, preview and recovery | `src/GtaRpAssistant.App/AudioSessionController.cs` | `AudioSessionController` |
| Embedded STT | Verified whisper.cpp pack and lifecycle | `src/GtaRpAssistant.Infrastructure.Windows/EmbeddedSttPack.cs` | `EmbeddedSttPackLocator`, `WhisperCppSpeechToTextProvider` |
| Overlay | Compact/expanded non-activating in-game presentation | `src/GtaRpAssistant.App/OverlayService.cs` | `OverlayService`, `OverlayWindow` |
| Conversation history | Temporary or opt-in SQLite chat history | `src/GtaRpAssistant.LocalData/` | `SqliteAssistantConversationStore`, `ConfigurableAssistantConversationStore` |
| Validated answer cache | Versioned cache before provider discovery; RAM or opt-in SQLite | `src/GtaRpAssistant.Core/AnswerCache.cs`, `src/GtaRpAssistant.LocalData/SqliteAnswerCache.cs` | `ConfigurableAnswerCache`, `SqliteAnswerCache`, `AnswerCacheKeyBuilder` |
| Knowledge sources | Official packs and player-confirmed facts with separate provenance | `knowledge/packs/gta5rp`, `knowledge/reference/community` | JSON articles, facts, prepared answers |
| Release gate | Build, tests, knowledge/benchmark validation, WPF smoke and package | `eng/build.ps1` | `eng/build.ps1 -Configuration Release -Runtime win-x64` |
| Active handoff | Last verified stage and exact next work | `docs/DEVELOPMENT_CHECKPOINT.md` | `ACTIVE CHECKPOINT` |

Dependency direction is inward: `Core` does not depend on WPF, SQLite, Windows, or provider implementations. Do not move orchestration into `MainWindow` or `MainViewModel`.
