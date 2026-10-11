# Expert `weapon-system`

Firearms: WeaponSystem, state machine, SDK ammo ledger, feedback, weapon grab, barrel authoring, KINEMATION
weapon clips. Facts: KB `Docs/weapons/` (start at README); here — navigation. User decisions:
`Docs/weapons/decisions.md` — search before asking; new ones go there, journal links.

## Owns
- `Assets/Scripts/Weapons/**` except Equipment (bots); `Assets/Data/WeaponSystem/**`; `Assets/Prefabs/Weapons/**`
  (via the writer); weapon, HitEffectBuilder and sight editor tools (list — expert.json watch_paths).
- SDK: weapon part of UXR Mechanics, Hands_Weapons_Animations_Pack, KINEMATION.
- Contracts `weapon-use-context`, `weapon-grab-lifetime`, `weapon-equipment-shooter`; the KB; weapon
  sections of `Docs/gameplay.md`.

## Not owned
- manipulation: grab mechanics (`GrabRules`, `ManipulationConstraints`, `TwoHandGrabPolicy`), UXR Manipulation,
  `HapticService`; SDK edits to `UxrGrabber`/`UxrGrabManager` only under contract.
- avatar-grip: finger poses, weapon/magazine grab points. network: how to replicate; what — yours.
- gameplay: economy, arsenal wall (`WeaponInfo`), rounds. bots: `WeaponEquipmentBinding`, bot weapon use.
- level-design: `LobbyRangeLayoutBuilder` (in the Calibration folder).

## Invariants (1–18, И1–И13 — `Docs/weapons/invariants.md`)
- Mechanism decisions only in `WeaponStateMachine`, one transition table; new behaviour = row/signal, not a
  flag. Machine has no Unity/SDK refs (`noEngineReferences`).
- Ammo ledger only in SDK; machine derives state from `LedgerView` each step, no copies; only the socket knows the magazine.
- One writer per channel (pose, mechanism sound, vibration, hint); executors neither decide nor read the ledger.
- Ledger commands only by the author (`StateEventAuthority.IsAuthorOfItem`), outside replay, with expected
  revision. No new Mirror Cmd/Rpc; `CartridgeIntake` Cmd names never change (hash).
- Mechanism sound/pose for all; dry click, refusal, vibration, hint author-only. Ledger mismatch →
  `GameLog.WeaponSystem.Error`, shooting continues.
- Prefabs only via `WeaponSystemAuthoring` (preflight, readback), never by hand; no table row → not configured;
  barrels hold feedback overrides only.
- Ledger inits on barrel readiness: while `Uninitialized` the author retries `Initialize`.
- Own second hand never takes over a firearm (`AllowHandTransfer = false`). Technical verified ≠ accepted.

## Architecture (full — `Docs/weapons/architecture.md`)
- Machine (table) — `Assets/Scripts/Weapons/Core/`.
- Host (sensors, ledger port, executors) — `Assets/Scripts/Weapons/WeaponSystem/`.
- SDK ledger (M, C, revision) — `Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/`.
- Data (profiles, category defaults) — `Assets/Data/WeaponSystem/`.
- Only weapon Cmds: `Assets/Scripts/Weapons/CartridgeIntake.cs`.
- Writer (barrel table) — `Assets/Editor/VR_Battlegrounds/Gameplay/WeaponSystemAuthoring.cs`.
- KINEMATION — `Assets/ThirdParty/KINEMATION/`, `Assets/Editor/VR_Battlegrounds/Gameplay/KinemationWeaponBuilder.cs`.
- Sights `Assets/Scripts/Weapons/Sights/`; tests `Assets/Tests/EditMode/Weapons/`.

## Details (grep heading)
- `Docs/weapons/architecture.md` «Учёт (SDK)…», «Сеть», «Каналы и владельцы», «Инициализация учёта», «Авторинг».
- `Docs/weapons/workflow.md` «Unity (linked worktree)», «Грабли»; `Docs/weapons/sight-calibration.md` «7. Открытые вопросы».
- `tasks/weapon-system/refactor-plan.md` (100 KB) «3.5 Таблица переходов», «3.7 Инварианты…».
- `tasks/weapon-system/Details.md` «Этап waves-f»; `tasks/weapon-system/ejection-manual.md`.
- `Docs/gameplay.md` «Оружие KINEMATION…», «Баланс оружия (T-38)», «Накопленная отдача (T-38)».
- Task Readme and `Docs/weapons/roadmap.md` in origin/dev lag the owner branch; status — awake.
