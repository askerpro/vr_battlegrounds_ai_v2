# Expert `manipulation`

Interaction with grabbables on top of UltimateXR (grab rules, two hands, anchors, pockets, placement,
release, grab networking by network's rules, loose items) plus interaction effects, vibration, feedback.
`haptics-system` is one task, not the zone. Facts: KB `tasks/haptics-system/expert/`; here — navigation.

## Owns
- Grab admission (`GrabRules`, `PlayerGrabManager` delegates), two-hand policy, anchors/pockets and placement
  readiness, release and cleanup (`LooseItems`).
- Motor (`HapticService`), feedback (`InteractionFeedback`), grab/haptics UXR patches (table in `sources.md`),
  contract `haptics-api`, the KB.
- Consumers: weapon-system, ui, gameplay (`ArsenalGrabRule`), launch-infra (stand).

## Not owned
- avatar-grip: grab pose, fingers, grab points, Hand Pose Fit. weapon-system: weapon mechanics, barrel
  feedback, contract `weapon-grab-lifetime`.
- network: how to replicate (relay, authority, StateSync), UniqueId/registry; reviews your network part.
- gameplay: arsenal economy. avatar-ik: UxrManager, frame order, input (Devices), locomotion, body IK, calibration.

## Invariants (numbered — `tasks/haptics-system/expert/invariants.md`)
- SDK owns grab state. Grab-state copy, second motor/feedback writer, own proximity math, bypassing
  `GrabRules`/`StateEventAuthority` — conflict.
- Motor written only by `UnityXRHapticDevice` on `HapticMixer` command inside `HapticService`; vibration only
  on the local player's hand, never networked.
- Readiness = the SDK answer grip will use: empty hand `UxrGrabManager.TryGetGrabCandidate` (patch 67), held
  item `AnchorPlacementReadiness` over `GetAnchorPlacementCandidate`. Local; only events cross the network.
- Patch 26 (`GrabberNear`) stays off (perf win). Patch 67 calls in `UxrGrabManager.cs` survive any edit
  (ACK condition of `weapon-grab-lifetime`).
- Synced call (`ReleaseObject`, `IsGrabbable`…) from Update/timer/physics/RPC only under `IsAuthorOfItem`/`IsWorldAuthority`.
- Vibration numbers (strength, waveform, priority) are a project setting the user tunes in Play Mode via
  Quest Link; never pick them; no player vibration settings.
- `architecture.md` §8 = code-reading hypotheses until a run confirms them.
- User decisions: `tasks/haptics-system/expert/decisions.md` (new ones there, journal links).

## Architecture
`$UXR` = `Assets/ThirdParty/UltimateXR/Runtime/Scripts`.
- SDK grab (state, anchors, events, candidates) — `$UXR/Manipulation/UxrGrabManager.cs`.
- Admission — `Assets/Scripts/Player/PlayerGrabManager.cs`, `GrabRules.cs`.
- Two hands — `Assets/Scripts/Player/TwoHandGrabPolicy.cs`, `ManipulationConstraints.cs`.
- Pocket, anchors — `Assets/Scripts/Interaction/UxrMagazinePocket.cs`, `AnchorRole.cs`, `AnchorPlacementReadiness.cs`.
- Release, world — `Assets/Scripts/Interaction/OutOfWorldGuard.cs`, `LooseItems.cs`.
- Vibration — `Assets/Scripts/Haptics/HapticService.cs`, `HapticMixer.cs`, `UnityXRHapticDevice.cs`.
- Feedback — `Assets/Scripts/Haptics/InteractionFeedback.cs`, `Assets/Prefabs/Feedback/`.
- Clip (SDK 66) — `$UXR/Haptics/UxrHapticClip.cs`.
- Grab network: network's Relay, `StateEventAuthority`, `NetworkUxrIdentity` in `Assets/Scripts/Network/`.

## Details (grep heading)
- `tasks/haptics-system/expert/architecture.md` §1–§9 («1. Слои» … «9. Соседние владельцы»).
- `tasks/haptics-system/expert/sources.md` «SDK UltimateXR…» (patches), «Тесты…», «Инструменты редактора».
- `tasks/haptics-system/expert/handoff.md` (`pocket-removal` plan); `tasks/haptics-system/Details.md` items 0.4, 2, 5, 9 only.
- `Docs/troubleshooting.md` «Взаимодействие с предметами»; `Docs/UltimateXR/known-issues.md` Issue 6, 7, 13–18, 20, 35, 38.
- Check facts `tasks/haptics-system/changelog/`; contract `tasks/haptics-system/contracts/haptics-api.md`.
- Stage `manipulation-kb` moves the KB under Docs; after merge re-point these links.
