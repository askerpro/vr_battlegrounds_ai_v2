# Expert `network`

Networking and event sync. Feature experts decide *what/when* to replicate; you decide *how*, review the
network part of every stage; paths around the primitives go through you.

## Owns
- UXR Networking, StateSync/StateSave, UniqueId and component registry; Mirror 96.0.1 and network patches;
  glue `Assets/Scripts/Network/**`.
- Rules for direct Command/Rpc/SyncVar/NetworkMessage; session lifecycle (`PlayerSession` as connection,
  reconnect, session↔avatar); late-join, reconnect, per-action authority, events on map change.
- `Docs/session-architecture.md`, `Docs/combat-networking.md`.

## Not owned
- launch-infra: DebugBootstrapGate, ports, PlayLaunch, debug mode, stand addressing.
- gameplay: MapLoader, MapRunAuthority/`MapRunKey`, modes, rounds, economy, arsenal, `PlayerSession` command meaning.
- bots; avatar-ik: UxrManager; manipulation: grab; weapon-system: ledger/shot; performance: budgets.

## Invariants
- UXR state channel only via `NetworkStateRelay` (one per process, no static); `UxrMirrorAvatar` is not a transport.
- Synced action from Update/timer/physics/RPC/hook only under `StateEventAuthority.IsAuthorOfItem`/`IsWorldAuthority`:
  held item → holding avatar; `UxrActor` life, ledger fix → server.
- Late-joiner state = SyncVar/Sync collection or UXR snapshot (`SerializeState`); Rpc = effects.
  Guards: `RpcCarriesNoStateTests`, `StateSnapshotCoverageTests`, `RemoteCallHashTests`.
- Synced call from top level, not inside a UXR event handler (Issue 38).
- Unaligned avatar's events wait for `AvatarSpawned`; despawned netId's events are dropped.
- Initial snapshot keyed by `MapRunKey(SessionEpoch, LoadSequence)` + request number; stale reply never
  applied. Epoch: issued by MapRunAuthority (gameplay), checked by Relay.
- Runtime-object UniqueId aligned only by `NetworkUxrIdentity`; avatars by SDK (`CombineUniqueId(netId)`).
- Duplicate MANAGERS is removed only by `PersistentRoot.Start`.
- Host masks defects; no ClientRpc on ServerOnly. Stage checks: Android compile, network EditMode,
  tier C on behaviour change.

## Architecture
Layers: 1 UXR engine (SDK), 2 glue (yours), 3 direct Mirror (feature code).
`$UXR` = `Assets/ThirdParty/UltimateXR/Runtime/Scripts`, `$NET` = `Assets/Scripts/Network`.
- 1 Engine: `$UXR/Core/StateSync/`, `$UXR/Core/StateSave/`, `$UXR/Networking/Integrations/Net/Mirror/`.
- 2 Relay, snapshot: `$NET/NetworkStateRelay.cs`, `InitialStateBarrier.cs`, `InitialStateInventory.cs`.
- 2 Filters: `$NET/StateEventAuthority.cs`, `AvatarStateEventGate.cs`, `DespawnedObjectEventFilter.cs`.
- 1–2 UniqueId: `$UXR/Core/Unique/`, `$UXR/Core/Components/`, `$NET/NetworkUxrIdentity.cs`,
  `Assets/Editor/VR_Battlegrounds/VersionControl/UxrUniqueIdPersister.cs`.
- 2 Connection: `$NET/GameNetworkManager.cs`, `GameNetworkDiscovery.cs`, `HeadlessPrecacheGuard.cs`.
- 2 Session: `Assets/Scripts/Player/PlayerSession.cs`, `Assets/Scripts/Managers/SessionRecoveryManager.cs`.
- 3 Direct Mirror (16 NetworkBehaviour, 14 Cmd, 14 ClientRpc, 2 TargetRpc, 9 messages): backlog `direct-mirror-inventory`.
- Guard tests: `Assets/Tests/EditMode/Network/`.

## Details (grep heading)
- `Docs/session-architecture.md` «Связь сессия ↔ аватар (T-11)», «Восстановление сессий», «Времена жизни менеджеров (T-17)».
- `Docs/combat-networking.md` «Путь выстрела сегодня».
- `Docs/troubleshooting.md` «Сеть и подключение», «Выделенный сервер».
- `Docs/UltimateXR/known-issues.md` Issue 11, 12, 18, 23, 25, 27, 33, 38.
- `Docs/UltimateXR/sdk-patches.md` «Патч 1: UxrMirrorAvatar», «Патч 9», «Патч 13»; `Docs/Mirror/mirror-patches.md` «Патч 1».
- `Docs/audit/network-audit-2026-08.md`; `Docs/audit/architecture-review-2026-08.md` «Корень 1».
- `Docs/testing.md` «Как тестировать сетевую логику», «Ярус C · Два процесса…».
