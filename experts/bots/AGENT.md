# Expert `bots`

Bots as ordinary headset-less players: Blaze AI decisions, bodies, equipment, shooting, networking, stand content.

## Owns
- Director and decisions (phases, buying, team, nav); nav source rule `BotNavMeshSources`; Blaze adapter and patches.
- Bodies: BotBody (party to the hand-writer contract), NPC variants, clip models, controllers; body choice before spawn.
- Equipment, shooting: BotGunner, WeaponEquipmentBinding, contract `weapon-equipment-binding`.
- Bot net inputs, what to replicate about a bot (network rules). `ServerAuthoredAvatar`: shared with avatar-ik.
- BotCombatStand content: scene, scenarios, measurements, report, capture.
- `Docs/BlazeAI/`, bot docs in `Docs/tasks/`, «Матч с ботами (T-48)» in `Docs/gameplay.md`.

## Not owned
- gameplay: match/spawn/team/economy rules, bots in minPlayers, PlayersManager, AvatarManager, AvatarTeardown.
- weapon-system: weapon mechanics, WeaponUseContext, readiness, ammo, SDK firearm.
- avatar-ik: player body, IK, native legs; hand-writer order: avatar-ik + avatar-grip. manipulation: UXR grab.
- network: GameNetworkManager, StateEventAuthority, PlayerSession, replication primitives.
- level-design: env layers/colliders. launch-infra: stand launch, `BotCombatStandEditor`, DebugAdminPolicy.
  ui: menu screens. performance: Quest budgets.

## Invariants
- Same damage, death, ghost, money, score as humans. Decisions and Blaze: server only; clients get
  the pose via NetworkTransform (`ServerAuthoredAvatar`).
- Sole body-root author: BotBody; nav gives intent. Sole bone author: visible Animator (bots-fix
  target; origin/dev still runs the old hybrid). BotBody hand obeys the hand-writer contract.
- Sole equipment author: server binding. SDK grab and equipment are exclusive; no fake UxrGrabber,
  M/C copies or second lifecycle. Prepare only via `RequestAutomationPreparation`; real, author-checked
  shot (`StateEventAuthority`).
- Tactics: stock Blaze Alert/CoverShooter/GoingToCover only; Blaze patches: narrow points from `Docs/BlazeAI/sdk-patches.md`.
- Body chosen before first Spawn; unknown model refused, never swapped for the human prefab. Heavy excluded.
- Remove only via `BotDirector.RemoveBot(session)`. Stand: no second UXR grab, never re-saves production registries.
- A clip, goingToCover or counter proves no grip, shot or cover; unaccepted = NeedsReview.

## Architecture (X = `Assets/Scripts/Bots/X.cs` unless a path is given)
- Director: BotDirector, BotMind, BotOrders, BotSenses. Match: BotMatchPlan, BotMatchStarter, BotMatchNetwork.
- Buy/team/skin: BotBuyPlan, BotShopper, BotTeamChoice, BotSkin. Nav: BotNavigator, BotNavMesh, BotNavMeshSources, BotRoute.
- Blaze: BotCombatDriver; `Assets/ThirdParty/Blaze AI/Scripts/BlazeAI.cs`.
- Body: BotBody, BotCombatAssets; `Assets/Resources/Bots/`; BotCombatSetup in `Assets/Editor/VR_Battlegrounds/Bots/`.
- Shooting: BotGunner. Net: BotNetwork (DebugAdminPolicy), registered in `Assets/Scripts/Network/GameNetworkManager.cs`.
- Foreign: `Assets/Scripts/Managers/PlayersManager.cs` (CreateBotSession); `Assets/Scripts/Player/Avatars/` AvatarManager, ServerAuthoredAvatar, AvatarTeardown.
- Stand: `Assets/Scripts/Debug/BotCombatStand/`; editor BotCombatStandBuilder, BotStand*. Tests: `Assets/Tests/EditMode/Bots/`.
- bots-fix target, not in origin/dev: WeaponEquipmentBinding, clip body, BotClipComposition.

## Details (grep the heading)
- `Docs/tasks/T-48-bots-match.md` «Решения», «Что не проверено (2026-10-01)».
- `Docs/tasks/bots-blaze-integration.md` «Контракт», «Приёмка».
- `Docs/tasks/bot-combat-stand.md` «Запуск», «Сценарии и пределы», «Наблюдения».
- `Docs/tasks/bots-validation-stand-plan.md` «Измерения и критерии», «Этапы реализации».
- `Docs/audit/bots-audit-2026-10-03.md` «Находки», «Готовые решения».
- `Docs/troubleshooting.md` «После добавления бота кнопки планшета не нажимаются».
- `tasks/bots-fix/` (not in origin/dev), owner copy `F:/CodexWorktrees/bots-fix/Vr_Battlegrounds_ai/tasks/bots-fix/`:
  Details «Границы и владение», «Архитектура и последовательность», «Контракты и зависимости»;
  `contracts/armed-development-composition.md`, `diagnosis/`.
