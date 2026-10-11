# Зона launch-infra утверждена картой экспертов; vr-test-stand — одна из задач

Пользователь 2026-10-11: отдельный E2E-эксперт не нужен — E2E-каркас входит в launch-infra; сценарии
и ожидаемые результаты принадлежат экспертам предметных зон. В зону добавлены сборки целиком
(GameBuilder, BuildSceneResolver, E2EPlayerBuilder, Tools/release), обычный Play, DebugBootstrapGate,
конфиг MPPM. Соседи: gameplay (карта, раунды, серии; наследие map-runtime-bootstrap), network
(GameNetworkManager/Discovery, сессия, авторитет), manipulation (UXR-ввод).

Инвентаризация (read-only субагенты, 2026-10-11): Run-E2E.ps1, Tools/release/**, VirtualProjectsConfig.json
не покрыты writes ни одного этапа хаба; маски vr-test-stand `Debug/**`, `Bootstrap/**` захватывают чужие
меню; E2ERunner/IE2EScenario уже менялись map-runtime-bootstrap (startup-route, `IE2EServerStartup`).

Источники: `tasks/expert-workspace-infra/expert-map.md`, `tasks/expert-workspace-infra/Details.md`.
