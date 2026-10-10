# Источники: где что лежит

Адресный указатель для проверки фактов. Читать нужное место поиском, не загружать файлы целиком.
Сверено с кодом 2026-10-10 (origin/dev `8e3c513f`); номера строк — ориентир, ищи по символу.

## Вибрация и отклик (код игры)

| Что | Где |
|---|---|
| Сервис, единственный владелец мотора | `Assets/Scripts/Haptics/HapticService.cs` — `Play(clip, grabber, role, gain)`, `Play(clip, side, gain, role)`, `PlayBoth`, `Begin(clip, side, owner, gain, role)` → `HapticHandle`, `IsLocalHand`, `GetHandState` |
| Смешивание | `Assets/Scripts/Haptics/HapticMixer.cs`; мотор — `UnityXRHapticDevice.cs` (`IHapticDevice`) |
| Исполнитель отклика | `Assets/Scripts/Haptics/InteractionFeedback.cs` — готовность руки, события, пул, `IsPartOfHeld` |
| Проигрыватель клипа на GO | `Assets/Scripts/Haptics/HapticPlayer.cs` — `Bind(side, role)`, режимы `WhileActive` / `OnceOnEnable`, `AssetEdited` |
| Конфиг по умолчанию | `Assets/Scripts/Haptics/InteractionFeedbackConfig.cs` + `Assets/Resources/InteractionFeedbackConfig.asset` |
| Свой отклик якоря/предмета | `Assets/Scripts/Haptics/InteractionFeedbackOverride.cs` |
| Слот для событий чужого игрока | `Assets/Scripts/Haptics/NetworkedFeedback.cs` |
| Префабы отклика | `Assets/Prefabs/Feedback/Interaction/Feedback_GrabReady.prefab` (Steady 0.08 Low, вторая рука 0.5) |
| Формы | `Assets/Data/Haptics/Waveforms/` — Steady, Click, DoublePulse, TriplePulse, RecoilPistol/Rifle/Shotgun, Death |
| Роль якоря | `Assets/Scripts/Interaction/AnchorRole.cs` (`AnchorRoleKind`: Magazine, Primary, Secondary, AvatarOther, World) |
| Готовность якоря | `Assets/Scripts/Interaction/AnchorPlacementReadiness.cs` (поверх `GetAnchorPlacementCandidate`) |
| Временный писатель weapon-system | `Assets/Scripts/Weapons/WeaponSystem/WeaponHapticOutput.cs` (в `HapticOwnershipTests.Pending`) |
| Устаревшая оболочка | `Assets/Scripts/Interaction/PocketHaptics.cs` — пустой компонент на `PlayerBase`/`Cyborg`, убирает этап `pocket-removal` |

## SDK UltimateXR (`Assets/ThirdParty/UltimateXR/Runtime/Scripts/`)

Журнал старых патчей — `Docs/UltimateXR/sdk-patches.md` (исторический); новые — `tasks/<задача>/changelog/<дата>-sdk-<N>.md`.
Номер нового патча — только `coordination.py patch-reserve`. Метки в коде — `VR Battlegrounds patch <N>`.

| Патч | Где | Суть |
|---|---|---|
| 2 | `UxrGrabber.Custom.cs`, `UxrGrabbableObject.CanBeGrabbedByGrabber` | `CanGrabDelegate` — сюда подключены правила хвата игры |
| 3 | `UxrGrabbableObjectAnchor`, `UxrGrabbableObject`, `UxrGrabManager.Manipulation.cs` | `AllowSwap`, `GrabProxy` якоря (прокси кармана), `RemoveObjectFromAnchor(unparent)`, `ClearLastAnchor` |
| 6 | `UxrGrabbableObject.Custom.cs` | `AutoCreateStartAnchor`, `SetNetworkAnchor` |
| 11 | `UxrGrabbableObject.GetDistanceFromGrabber` | без штрафа за занятую соседнюю точку — хват двумя руками |
| 12 | `UxrGrabManager.cs` | `RemoveOrphanedManipulations` — хваты уничтоженных рук |
| 13 | `UxrMirrorAvatar.CmdRequestAuthority` | хват уже уничтоженного сервером предмета не рвёт соединение |
| 16 | `UxrGrabManager.IsLocalAffordanceGrabber` | подсказки хвата только для рук локального аватара |
| 17 | `UxrGrabbableObject`, `UxrGrabManager.Querying.cs` | поиск держащей руки только для удерживаемых |
| 18 | `UxrGrabManager.HasLocalAffordanceGrab` | «можно положить в якорь» только для предмета в своей руке |
| 19 | `UxrGrabber.cs`, `UxrGrabber.PhysicsSample.cs` | скорость броска без аллокаций |
| 25–28 | `UxrGrabbableObject(.Custom).cs`, `Querying.cs` | ускорение поиска хвата без смены результата (порядок проверок, грубая отсечка) |
| 26 | `UxrGrabManager.cs`, `#if VRB_UXR_ANCHOR_GRABBER_NEAR` | проход «рука рядом с якорем» выключен (не включать) |
| 49 | `UxrGrabbableObjectAnchor.IsCompatibleObjectTag` | совместимость по тегу публична |
| 66 | `Haptics/UxrHapticClip.cs`, `UxrHapticWaveform.cs`, `UxrHapticPriority.cs` | форма, сила, приоритет, пауза, кулдаун, вторая рука в клипе — [запись](../changelog/2026-10-08-sdk-66.md) |
| 67 | `UxrGrabManager.cs` (`UpdateAffordances`), `UxrGrabManager.GrabCandidates.cs` | кандидат хвата руки — [запись](../changelog/2026-10-09-sdk-67.md) |
| T-39 | `UxrGrabManager.PlacementReadiness.cs`, `.WeaponHandoff.cs`, `UxrManipulationEventArgs.cs` | `GetAnchorPlacementCandidate`; свежий снимок позы в Grabbing |
| 65 (резерв) | `Devices/UxrControllerInput.cs`, `UxrUnityXRControllerInput.cs`, `Haptics/UxrHapticRouting.cs` | перехват вибраций SDK — этап `sdk-routing`, не реализован |

`UxrManipulationHapticFeedback` и `SendHapticFeedback` SDK пока без патчей — они вибрируют мимо сервиса до `sdk-routing`.

## Известные проблемы и симптомы

`Docs/UltimateXR/known-issues.md`: Issue 6 (предмет после `RemoveObjectFromAnchor` летит за игроком — патч 3),
7 (`_autoCreateStartAnchor` при сетевом спавне), 10 (`Activate On Placed` при спавне — звук), 13 (вторая рука
перехватывает — патч 11 + `TwoHandGrabPolicy`), 14 (вторая рука вращает оружие — `MainGripAimLock`),
15 (`GrabberNear = null` — основа патчей 26 и 67), 16 (пустые `Compatible Tags` — только без тега), 17 (уничтоженная
рука — патч 12), 18 (команда хвата уничтоженного предмета — патч 13), 20 (подсказки чужих рук ~4 мс — патч 16),
26 (нет позы хвата у аватара — предмет в ладони по pivot), 35 (`IsCompatibleObject` = «можно положить сейчас»),
38 (синхронный вызов из обработчика Placed/Removed/Released не уходит в сеть).

`Docs/troubleshooting.md`, раздел «Взаимодействие с предметами»: хват не реплицируется, вторая рука перехватывает,
схватил за затвор, пистолет за опору, вращение при второй руке, предмет в якоре падает при загрузке,
`RuntimeManipulationInfo not found`, тянусь к кобуре — берёт магазин, **карман не вибрирует** (запись про
`PocketHaptics` устарела — обновить в `docs-transfer`), карман выдаёт не тот магазин, отпустил у кармана — упал,
не вешается на стену арсенала, оружие на стене тусклое.

## Тесты (`Assets/Tests/EditMode/`, только EditMode)

- **Вибрация** (`Haptics/`): `HapticOwnershipTests` (2), `HapticMixerTests` (13), `HapticWaveformTests` (3),
  `ManipulationFeedbackCoverageTests` (2), `InteractionFeedbackTests` (4). Группа запуска `VrBattlegrounds.Tests.Haptics`.
- **Хват** (`Player/`): `AnchoredItemGrabTests`, `WeaponPartGrabTests`, `SupportGripTests`, `TwoHandGrabPolicyTests`,
  `GunTwoHandGrabTests`, `GunTwoHandAimTests`, `PumpGrabFollowTests`, `GrabQueryPatchTests` (патчи 25–27),
  `EquipmentStripTests`, `MagazineRefillPlannerTests`.
- **Якоря/карманы** (`Interaction/`): `MagazinePocketSelectionTests`, `AnchorReachZonesTests`,
  `GrabbableDependencyLifetimeTests`, `LooseItemTests`; `Arsenal/DogTagHeldDisableTests`.
- **Префабы** (`Prefabs/`): `GrabProximityTests`, `GrabPoseCoverageTests`, `WeaponDropPhysicsTests`,
  `OutOfWorldGuardTests`, `AnchorSoundCoverageTests`, `AnchorActivationAudioTests`, `WeaponFeedbackTests`,
  `AvatarLoadoutTests` (карманы, прокси, позы; содержит проверку `PocketHaptics` до `pocket-removal`),
  `PrefabCompositionTests`, `AvatarHandPoseChainTests`.
- **Сеть** (`Network/`, `Bots/`): `StateEventAuthorityTests`, `AuthorityRequestForDestroyedTests`,
  `AvatarStateEventGateTests`, `BotAuthorityTests`.

PlayMode-тестов нет. Стенд `Docs/test-stand.md` хват/XR-ввод пока не проверяет — механику принимает пользователь в шлеме.

## Инструменты редактора

- `Tools/VR Battlegrounds/Haptics/Вибрация` — `Assets/Editor/VR_Battlegrounds/Haptics/HapticTuningWindow.cs`; drawer
  клипа с пробой — `UxrHapticClipDrawer.cs`; инспектор формы — `UxrHapticWaveformEditor.cs`; автозапись при выходе
  из Play — `HapticEditorPreview.cs` (`HapticAutoSave`).
- `Tools/VR Battlegrounds/Gameplay/Apply Anchor Sounds` — `Gameplay/AnchorSoundInstaller.cs`.
- Гизмо якорей (куда встанет, где берётся через прокси) — `Gameplay/GrabbableAnchorGizmos.cs`.
- Настройка хвата оружия — `Gameplay/WeaponGrabHighlight.cs`, `WeaponInteractionInstaller`/`Recipes`/`Review`.
- Карманы и позы — `Avatars/AvatarPocketSetup.cs`, `PocketZonesPrefabWriter.cs`, `HandPosesSetup.cs`;
  Hand Pose Fit / Hand Rig Quality — область avatar-grip-expert.

## Документы задачи

[Readme](../Readme.md) — статус и следующий шаг; [Details](../Details.md) — модель (п. 0.4 — отклик, п. 2 — голоса,
п. 5 — патч 65, п. 9 — все источники вибрации); [contracts/haptics-api.md](../contracts/haptics-api.md) — контракт с
WeaponSystem; [research.md](../research.md) — исследование LRA/Quest; [changelog/](../changelog/) — факты проверок.
