# K. Тишина в начале игровых звуков (2026-10-24)

Read-only аудит. Источник ссылок: префабы `Assets/Prefabs/Weapons/**`, `Assets/Data/WeaponSystem/*FeedbackDefaults.asset`, `Assets/Resources/{MagazineAnchorSoundDefaults,ImpactSoundDefaults}.asset`, `PlayerHitEffects.prefab`, карманы `Prefabs/Player/Equipment/Pockets`, слоты `Prefabs/Arsenal/Slots`. Все GUID в этих файлах, разрешённые через `.meta` в аудиофайлы. Дополнительно — все `Audio/SFX/Impacts/*.wav` и `Audio/SFX/Handling/*_mono.wav` (проверка обрезки).

Метрики: огибающая — максимум |x| по каналам, окна 10 мс; `onset_5`/`onset_30` — начало первого окна с пиком ≥ 5 % / 30 % пика файла (точность 10 мс, 0 = звук в первом окне). Кандидат: onset_30 ≥ 40 мс или onset_5 ≥ 30 мс; сильная задержка: onset_30 ≥ 100 мс. WAV — свой RIFF-парсер (PCM16/24/32, float); mp3/flac — декодированы audaspace из Blender 5.1 (ffmpeg в PATH нет; у mp3 возможна задержка кодера ~25 мс). Учтено: `KinemationAudio.CutAtEvent` намеренно оставляет PreRoll 30 мс, поэтому onset_30 ≈ 30–40 мс у таких кусков ожидаем; `KinemationAudio.Shot` и первый кусок `SplitAtGap` режут от 0 с и тишину источника не срезают.

## Кандидаты (по onset_30, убыв.)

| файл | происхождение | длина, с | пик, dBFS | onset_5, мс | onset_30, мс | вердикт |
|---|---|---|---|---|---|---|
| `Audio/SFX/Weapons/Kinemation/Herrington/Herrington_MagIn.wav` | билдер (KinemationAudio) | 0.700 | -4.9 | 390 | 580 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/Kinemation/MKR9/MKR9_MagOut.wav` | билдер (KinemationAudio) | 0.600 | -3.4 | 270 | 350 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Handling/Magazine_attach_mono.wav` | ручной ассет | 0.784 | -3.1 | 100 | 240 | обрезан НЕ до удара, ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/Kinemation/Herrington/Herrington_MagOut.wav` | билдер (KinemationAudio) | 0.750 | -48.8 | 160 | 220 | почти тишина целиком, ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/Kinemation/R08/R08_MagIn.wav` | билдер (KinemationAudio) | 0.390 | -5.8 | 130 | 160 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/HandsPack/PPK/PPK_Shot.wav` | билдер (KinemationAudio) | 0.770 | -1.0 | 120 | 130 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Impacts/IMPACT_Metal_Objects_Drop_Hard_Surface_01_mono.wav` | ручной ассет | 1.936 | -0.5 | 80 | 110 | обрезан НЕ до удара, ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Universal/Zatvor1.mp3` | ручной ассет | 0.359 | -4.7 | 40 | 100 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/HandsPack/BrowningHiPower/BrowningHiPower_BoltBack.wav` | билдер (KinemationAudio) | 0.180 | -27.9 | 70 | 100 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/HandsPack/FabarmSDASS/FabarmSDASS_BoltBack.wav` | билдер (KinemationAudio) | 0.375 | -4.7 | 40 | 100 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/HandsPack/PPK/PPK_BoltBack.wav` | билдер (KinemationAudio) | 0.180 | -27.9 | 70 | 100 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Universal/Zatvor2.mp3` | ручной ассет | 0.216 | -4.4 | 20 | 90 | кандидат |
| `Audio/SFX/Weapons/HandsPack/BrowningHiPower/BrowningHiPower_Shot.wav` | билдер (KinemationAudio) | 0.590 | -2.8 | 90 | 90 | кандидат |
| `Audio/SFX/Universal/Weapon_Select.flac` | ручной ассет | 0.500 | -3.9 | 40 | 80 | кандидат |
| `Audio/SFX/Weapons/Kinemation/R08/R08_MagOut.wav` | билдер (KinemationAudio) | 0.370 | -5.9 | 30 | 80 | кандидат |
| `Audio/SFX/Weapons/HandsPack/FabarmSDASS/FabarmSDASS_Load.wav` | билдер (KinemationAudio) | 0.287 | -4.1 | 60 | 70 | кандидат |
| `Audio/SFX/Weapons/Kinemation/SRM12/SRM12_BoltForward_Cut.wav` | билдер (KinemationAudio) | 0.322 | -4.7 | 20 | 70 | кандидат |
| `Audio/SFX/Weapons/HandsPack/BrowningHiPower/BrowningHiPower_BoltForward.wav` | билдер (KinemationAudio) | 0.350 | -3.1 | 50 | 60 | кандидат |
| `Audio/SFX/Weapons/HandsPack/FabarmSDASS/FabarmSDASS_Shot.wav` | билдер (KinemationAudio) | 0.549 | -2.3 | 60 | 60 | кандидат |
| `Audio/SFX/Weapons/HandsPack/PPK/PPK_BoltForward.wav` | билдер (KinemationAudio) | 0.350 | -3.1 | 50 | 60 | кандидат |
| `Audio/SFX/Weapons/HandsPack/FabarmSDASS/FabarmSDASS_BoltForward.wav` | билдер (KinemationAudio) | 0.200 | -6.7 | 10 | 50 | кандидат |
| `Audio/SFX/Weapons/Kinemation/SRM12/SRM12_Shot.wav` | билдер (KinemationAudio) | 1.650 | -4.8 | 50 | 50 | кандидат |
| `Audio/SFX/Weapons/HandsPack/AR15/AR15_Shot.wav` | билдер (KinemationAudio) | 0.392 | -1.0 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/HandsPack/MP5K/MP5K_Shot.wav` | билдер (KinemationAudio) | 0.444 | -1.2 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/HandsPack/Scar/Scar_Shot.wav` | билдер (KinemationAudio) | 0.261 | -1.2 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/HandsPack/SniperRifle/SniperRifle_Shot.wav` | билдер (KinemationAudio) | 1.500 | -1.8 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/Kinemation/AK105/AK105_Shot.wav` | билдер (KinemationAudio) | 0.520 | -4.8 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/Kinemation/Herrington/Herrington_Shot.wav` | билдер (KinemationAudio) | 1.500 | -4.8 | 30 | 40 | кандидат |
| `Audio/SFX/Weapons/Kinemation/Viper/Viper_Shot.wav` | билдер (KinemationAudio) | 0.470 | -4.8 | 30 | 40 | кандидат |
| `Audio/SFX/Weapons/SniperRifleAuto/SniperRifleAuto_Shot.wav` | билдер (KinemationAudio) | 0.550 | -2.2 | 40 | 40 | кандидат |
| `Audio/SFX/Handling/HANDLING_B_FS92_9mm_Remove_Empty_Magazine_mono.wav` | ручной ассет | 0.942 | -18.0 | 30 | 30 | обрезан НЕ до удара, кандидат |
| `Audio/SFX/Weapons/HandsPack/MP5K/MP5K_BoltBack.wav` | билдер (KinemationAudio) | 0.450 | -9.8 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/HandsPack/Scar/Scar_MagOut.wav` | билдер (KinemationAudio) | 0.600 | -5.6 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/Kinemation/AK105/AK105_BoltBack.wav` | билдер (KinemationAudio) | 0.450 | -2.3 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/Kinemation/AK105/AK105_BoltForward.wav` | билдер (KinemationAudio) | 0.450 | -2.3 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/Kinemation/Mk14/Mk14_MagOut.wav` | билдер (KinemationAudio) | 0.600 | -5.8 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/Kinemation/TR15/TR15_MagOut.wav` | билдер (KinemationAudio) | 0.600 | -10.7 | 30 | 30 | кандидат |

## Полная таблица

| файл | кто использует (файл :: поле) | длина, с | пик, dBFS | onset_5, мс | onset_30, мс | вердикт |
|---|---|---|---|---|---|---|
| `Audio/SFX/Casings/CASING_45_Hard_Surface_RR2_mono.wav` | Data/WeaponSystem/SniperFeedbackDefaults.asset :: MonoBehaviour._ejectaImpacts | 0.900 | -1.6 | 0 | 0 | ок |
| `Audio/SFX/Casings/CASING_7-62_mm_Hard_Surface_RR3_mono.wav` | Data/WeaponSystem/RifleFeedbackDefaults.asset :: MonoBehaviour._ejectaImpacts | 0.678 | -1.0 | 0 | 0 | ок |
| `Audio/SFX/Casings/CASING_9mm_Hard_Surface_RR4_mono.wav` | Data/WeaponSystem/PistolFeedbackDefaults.asset :: MonoBehaviour._ejectaImpacts | 0.556 | -3.2 | 0 | 0 | ок |
| `Audio/SFX/Casings/CASING_Shotgun_Shell_Hard_Surface_RR1_mono.wav` | Data/WeaponSystem/ShotgunFeedbackDefaults.asset :: MonoBehaviour._ejectaImpacts | 1.205 | -1.9 | 0 | 0 | ок |
| `Audio/SFX/Handling/HANDLING_B_FS92_9mm_Remove_Empty_Magazine_mono.wav` | Data/WeaponSystem/PistolFeedbackDefaults.asset :: MonoBehaviour._magazineTakeOut<br>Data/WeaponSystem/RifleFeedbackDefaults.asset :: MonoBehaviour._magazineTakeOut<br>Data/WeaponSystem/ShotgunFeedbackDefaults.asset :: MonoBehaviour._magazineTakeOut<br>Data/WeaponSystem/SniperFeedbackDefaults.asset :: MonoBehaviour._magazineTakeOut | 0.942 | -18.0 | 30 | 30 | обрезан НЕ до удара, кандидат |
| `Audio/SFX/Handling/Magazine_attach_mono.wav` | Data/WeaponSystem/PistolFeedbackDefaults.asset :: MonoBehaviour._magazineInsert<br>Data/WeaponSystem/RifleFeedbackDefaults.asset :: MonoBehaviour._magazineInsert<br>Data/WeaponSystem/ShotgunFeedbackDefaults.asset :: MonoBehaviour._magazineInsert<br>Data/WeaponSystem/SniperFeedbackDefaults.asset :: MonoBehaviour._magazineInsert | 0.784 | -3.1 | 100 | 240 | обрезан НЕ до удара, ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Handling/S_Aim_In_mono.wav` | Resources/MagazineAnchorSoundDefaults.asset :: MonoBehaviour._weaponAnchor.takeOut | 1.194 | -23.9 | 0 | 10 | обрезан: ок |
| `Audio/SFX/Handling/S_Aim_Out_mono.wav` | Resources/MagazineAnchorSoundDefaults.asset :: MonoBehaviour._weaponAnchor.place | 2.127 | -20.6 | 0 | 10 | обрезан: ок |
| `Audio/SFX/Handling/S_DrawPistolWithHolster_mono.wav` | Resources/MagazineAnchorSoundDefaults.asset :: MonoBehaviour._pistolPocket.takeOut | 1.380 | -33.1 | 10 | 10 | обрезан: ок |
| `Audio/SFX/Handling/S_Equip_mono.wav` | Resources/MagazineAnchorSoundDefaults.asset :: MonoBehaviour._riflePocket.takeOut | 0.822 | -28.8 | 0 | 10 | обрезан: ок |
| `Audio/SFX/Handling/S_HolsterPistolWithHolster_mono.wav` | Resources/MagazineAnchorSoundDefaults.asset :: MonoBehaviour._pistolPocket.place | 1.240 | -31.3 | 0 | 10 | обрезан: ок |
| `Audio/SFX/Handling/S_Jump_mono.wav` | Resources/MagazineAnchorSoundDefaults.asset :: MonoBehaviour._magazinePocket.takeOut | 0.900 | -15.5 | 0 | 10 | обрезан: ок |
| `Audio/SFX/Handling/S_Pickup_mono.wav` | Resources/MagazineAnchorSoundDefaults.asset :: MonoBehaviour._magazinePocket.place | 1.120 | -20.9 | 0 | 10 | обрезан: ок |
| `Audio/SFX/Handling/S_UnEquip_mono.wav` | Resources/MagazineAnchorSoundDefaults.asset :: MonoBehaviour._riflePocket.place | 0.979 | -29.1 | 0 | 10 | обрезан: ок |
| `Audio/SFX/Impacts/Hit_Body_01.wav` | Resources/PlayerHitEffects.prefab :: MonoBehaviour._clips | 0.830 | -0.0 | 0 | 10 | обрезан: ок |
| `Audio/SFX/Impacts/Hit_Head.wav` | Resources/PlayerHitEffects.prefab :: MonoBehaviour._headClips | 1.890 | -0.0 | 0 | 0 | обрезан: ок |
| `Audio/SFX/Impacts/Hit_Head_Helmet.wav` | Resources/PlayerHitEffects.prefab :: MonoBehaviour._headHelmetClips | 0.450 | -0.0 | 0 | 0 | обрезан: ок |
| `Audio/SFX/Impacts/IMPACT_Bullet_Metal_01_mono.wav` | W/Effects/ImpactDecal_Metal.prefab :: m_Modification.m_Modifications.objectReference | 0.233 | -0.5 | 0 | 0 | обрезан: ок |
| `Audio/SFX/Impacts/IMPACT_Metal_Objects_Drop_Hard_Surface_01_mono.wav` | Resources/ImpactSoundDefaults.asset :: MonoBehaviour._weapon | 1.936 | -0.5 | 80 | 110 | обрезан НЕ до удара, ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Impacts/IMPACT_Metal_Rod_Drop_Hard_Surface_mono.wav` | Resources/ImpactSoundDefaults.asset :: MonoBehaviour._weapon | 1.203 | -0.8 | 0 | 0 | обрезан: ок |
| `Audio/SFX/Impacts/IMPACT_Metal_Soft_Plate_01_mono.wav` | (в перечисленных ассетах не найден) | 0.388 | -0.5 | 0 | 0 | обрезан: ок |
| `Audio/SFX/Impacts/IMPACT_Metal_Soft_Plate_02_mono.wav` | (в перечисленных ассетах не найден) | 0.478 | -0.5 | 0 | 0 | обрезан: ок |
| `Audio/SFX/Impacts/IMPACT_Metal_Tool_Drop_Hard_Surface_mono.wav` | Resources/ImpactSoundDefaults.asset :: MonoBehaviour._weapon | 1.484 | -0.5 | 0 | 0 | обрезан: ок |
| `Audio/SFX/Impacts/IMPACT_Wood_Stick_On_Wood_Post_01_mono.wav` | W/Effects/ImpactDecal_Wood.prefab :: m_Modification.m_Modifications.objectReference | 0.279 | -1.2 | 0 | 0 | обрезан: ок |
| `Audio/SFX/Impacts/Magazine_Fall.wav` | Resources/ImpactSoundDefaults.asset :: MonoBehaviour._magazine | 1.052 | -2.8 | 10 | 10 | обрезан: ок |
| `Audio/SFX/Impacts/Weapon_Fall_Pistol.wav` | Data/WeaponSystem/PistolFeedbackDefaults.asset :: MonoBehaviour._dropImpacts | 1.006 | -2.9 | 10 | 10 | обрезан: ок |
| `Audio/SFX/Impacts/Weapon_Fall_Rifle.wav` | Data/WeaponSystem/RifleFeedbackDefaults.asset :: MonoBehaviour._dropImpacts<br>Data/WeaponSystem/ShotgunFeedbackDefaults.asset :: MonoBehaviour._dropImpacts<br>Data/WeaponSystem/SniperFeedbackDefaults.asset :: MonoBehaviour._dropImpacts | 0.949 | -1.5 | 10 | 10 | обрезан: ок |
| `Audio/SFX/Universal/Weapon_Select.flac` | Prefabs/Player/Equipment/Pockets/Anchor_Back.prefab :: AudioSource.m_Resource<br>Prefabs/Player/Equipment/Pockets/MagazinePocket.prefab :: AudioSource.m_Resource | 0.500 | -3.9 | 40 | 80 | кандидат |
| `Audio/SFX/Universal/Zatvor1.mp3` | Data/WeaponSystem/SniperFeedbackDefaults.asset :: _audio._actionForwardChambered._clip<br>Data/WeaponSystem/SniperFeedbackDefaults.asset :: _audio._actionForwardEmpty._clip<br>W/SniperRifle/SniperRifle.prefab :: _audio._actionForwardChambered._clip<br>W/SniperRifle/SniperRifle.prefab :: _audio._actionForwardEmpty._clip<br>W/SniperRifleAuto/SniperRifleAuto.prefab :: _audio._actionForwardChambered._clip<br>W/SniperRifleAuto/SniperRifleAuto.prefab :: _audio._actionForwardEmpty._clip | 0.359 | -4.7 | 40 | 100 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Universal/Zatvor2.mp3` | Data/WeaponSystem/SniperFeedbackDefaults.asset :: _audio._actionBack._clip<br>W/SniperRifle/SniperRifle.prefab :: _audio._actionBack._clip<br>W/SniperRifleAuto/SniperRifleAuto.prefab :: _audio._actionBack._clip | 0.216 | -4.4 | 20 | 90 | кандидат |
| `Audio/SFX/Weapons/HandsPack/AR15/AR15_MagOut.wav` | W/AR15/AR15.prefab :: MonoBehaviour._takeOutClip | 0.600 | -5.6 | 20 | 20 | ок |
| `Audio/SFX/Weapons/HandsPack/AR15/AR15_Shot.wav` | W/AR15/AR15.prefab :: _triggers._shotAudio._clip | 0.392 | -1.0 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/HandsPack/BrowningHiPower/BrowningHiPower_BoltBack.wav` | W/BrowningHiPower/BrowningHiPower.prefab :: _audio._actionBack._clip | 0.180 | -27.9 | 70 | 100 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/HandsPack/BrowningHiPower/BrowningHiPower_BoltForward.wav` | W/BrowningHiPower/BrowningHiPower.prefab :: _audio._actionForwardChambered._clip<br>W/BrowningHiPower/BrowningHiPower.prefab :: _audio._actionForwardEmpty._clip | 0.350 | -3.1 | 50 | 60 | кандидат |
| `Audio/SFX/Weapons/HandsPack/BrowningHiPower/BrowningHiPower_MagIn.wav` | W/BrowningHiPower/BrowningHiPower.prefab :: AudioSource.m_Resource | 0.447 | -2.0 | 10 | 20 | ок |
| `Audio/SFX/Weapons/HandsPack/BrowningHiPower/BrowningHiPower_MagOut.wav` | W/BrowningHiPower/BrowningHiPower.prefab :: MonoBehaviour._takeOutClip | 0.600 | -1.7 | 20 | 30 | ок |
| `Audio/SFX/Weapons/HandsPack/BrowningHiPower/BrowningHiPower_Shot.wav` | W/BrowningHiPower/BrowningHiPower.prefab :: _triggers._shotAudio._clip | 0.590 | -2.8 | 90 | 90 | кандидат |
| `Audio/SFX/Weapons/HandsPack/FabarmSDASS/FabarmSDASS_BoltBack.wav` | W/FabarmSDASS/FabarmSDASS.prefab :: _audio._actionBack._clip | 0.375 | -4.7 | 40 | 100 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/HandsPack/FabarmSDASS/FabarmSDASS_BoltForward.wav` | W/FabarmSDASS/FabarmSDASS.prefab :: _audio._actionForwardEmpty._clip | 0.200 | -6.7 | 10 | 50 | кандидат |
| `Audio/SFX/Weapons/HandsPack/FabarmSDASS/FabarmSDASS_Load.wav` | W/FabarmSDASS/FabarmSDASS.prefab :: AudioSource.m_Resource | 0.287 | -4.1 | 60 | 70 | кандидат |
| `Audio/SFX/Weapons/HandsPack/FabarmSDASS/FabarmSDASS_Shot.wav` | W/FabarmSDASS/FabarmSDASS.prefab :: _triggers._shotAudio._clip | 0.549 | -2.3 | 60 | 60 | кандидат |
| `Audio/SFX/Weapons/HandsPack/MP5K/MP5K_BoltBack.wav` | W/MP5K/MP5K.prefab :: _audio._actionBack._clip | 0.450 | -9.8 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/HandsPack/MP5K/MP5K_MagIn.wav` | W/MP5K/MP5K.prefab :: AudioSource.m_Resource | 0.227 | -4.2 | 0 | 10 | ок |
| `Audio/SFX/Weapons/HandsPack/MP5K/MP5K_MagOut.wav` | W/MP5K/MP5K.prefab :: MonoBehaviour._takeOutClip | 0.600 | -4.5 | 20 | 20 | ок |
| `Audio/SFX/Weapons/HandsPack/MP5K/MP5K_Shot.wav` | W/MP5K/MP5K.prefab :: _triggers._shotAudio._clip | 0.444 | -1.2 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/HandsPack/PPK/PPK_BoltBack.wav` | W/PPK/PPK.prefab :: _audio._actionBack._clip | 0.180 | -27.9 | 70 | 100 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/HandsPack/PPK/PPK_BoltForward.wav` | W/PPK/PPK.prefab :: _audio._actionForwardChambered._clip<br>W/PPK/PPK.prefab :: _audio._actionForwardEmpty._clip | 0.350 | -3.1 | 50 | 60 | кандидат |
| `Audio/SFX/Weapons/HandsPack/PPK/PPK_MagOut.wav` | W/PPK/PPK.prefab :: MonoBehaviour._takeOutClip | 0.600 | -1.7 | 20 | 30 | ок |
| `Audio/SFX/Weapons/HandsPack/PPK/PPK_Shot.wav` | W/PPK/PPK.prefab :: _triggers._shotAudio._clip | 0.770 | -1.0 | 120 | 130 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/HandsPack/Scar/Scar_BoltBack.wav` | W/Scar/Scar.prefab :: _audio._actionBack._clip | 0.227 | -4.2 | 0 | 10 | ок |
| `Audio/SFX/Weapons/HandsPack/Scar/Scar_BoltForward.wav` | W/Scar/Scar.prefab :: _audio._actionForwardChambered._clip<br>W/Scar/Scar.prefab :: _audio._actionForwardEmpty._clip | 0.227 | -4.2 | 0 | 10 | ок |
| `Audio/SFX/Weapons/HandsPack/Scar/Scar_MagOut.wav` | W/Scar/Scar.prefab :: MonoBehaviour._takeOutClip | 0.600 | -5.6 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/HandsPack/Scar/Scar_Shot.wav` | W/Scar/Scar.prefab :: _triggers._shotAudio._clip | 0.261 | -1.2 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/HandsPack/SniperRifle/SniperRifle_MagIn.wav` | W/SniperRifle/SniperRifle.prefab :: AudioSource.m_Resource | 0.600 | -2.8 | 0 | 20 | ок |
| `Audio/SFX/Weapons/HandsPack/SniperRifle/SniperRifle_MagOut.wav` | W/SniperRifle/SniperRifle.prefab :: MonoBehaviour._takeOutClip | 0.600 | -8.9 | 20 | 20 | ок |
| `Audio/SFX/Weapons/HandsPack/SniperRifle/SniperRifle_Shot.wav` | W/SniperRifle/SniperRifle.prefab :: _triggers._shotAudio._clip | 1.500 | -1.8 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/HandsPack/SniperRifleAuto/SniperRifleAuto_MagIn.wav` | W/SniperRifleAuto/SniperRifleAuto.prefab :: AudioSource.m_Resource | 0.600 | -2.8 | 0 | 20 | ок |
| `Audio/SFX/Weapons/HandsPack/SniperRifleAuto/SniperRifleAuto_MagOut.wav` | W/SniperRifleAuto/SniperRifleAuto.prefab :: MonoBehaviour._takeOutClip | 0.600 | -8.9 | 20 | 20 | ок |
| `Audio/SFX/Weapons/HandsPack/Uzi/Uzi_MagOut.wav` | W/Uzi/Uzi.prefab :: MonoBehaviour._takeOutClip | 0.600 | -5.0 | 10 | 30 | ок |
| `Audio/SFX/Weapons/Kinemation/AK105/AK105_BoltBack.wav` | Data/WeaponSystem/RifleFeedbackDefaults.asset :: _audio._actionBack._clip<br>W/AK105/AK105.prefab :: _audio._actionBack._clip | 0.450 | -2.3 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/Kinemation/AK105/AK105_BoltForward.wav` | Data/WeaponSystem/RifleFeedbackDefaults.asset :: _audio._actionForwardChambered._clip<br>Data/WeaponSystem/RifleFeedbackDefaults.asset :: _audio._actionForwardEmpty._clip<br>W/AK105/AK105.prefab :: _audio._actionForwardChambered._clip<br>W/AK105/AK105.prefab :: _audio._actionForwardEmpty._clip | 0.450 | -2.3 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/Kinemation/AK105/AK105_MagIn.wav` | W/AK105/AK105.prefab :: AudioSource.m_Resource | 0.600 | -2.2 | 20 | 30 | ок |
| `Audio/SFX/Weapons/Kinemation/AK105/AK105_MagOut.wav` | W/AK105/AK105.prefab :: MonoBehaviour._takeOutClip | 0.600 | -2.2 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/AK105/AK105_Shot.wav` | W/AK105/AK105.prefab :: _triggers._shotAudio._clip | 0.520 | -4.8 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/Kinemation/Herrington/Herrington_BoltBack.wav` | W/Herrington/Herrington.prefab :: _audio._actionBack._clip | 0.450 | -4.6 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/Herrington/Herrington_BoltForward.wav` | W/Herrington/Herrington.prefab :: _audio._actionForwardChambered._clip<br>W/Herrington/Herrington.prefab :: _audio._actionForwardEmpty._clip | 0.450 | -4.6 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/Herrington/Herrington_MagIn.wav` | W/Herrington/Herrington.prefab :: AudioSource.m_Resource | 0.700 | -4.9 | 390 | 580 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/Kinemation/Herrington/Herrington_MagOut.wav` | W/Herrington/Herrington.prefab :: MonoBehaviour._takeOutClip | 0.750 | -48.8 | 160 | 220 | почти тишина целиком, ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/Kinemation/Herrington/Herrington_Shot.wav` | W/Herrington/Herrington.prefab :: _triggers._shotAudio._clip | 1.500 | -4.8 | 30 | 40 | кандидат |
| `Audio/SFX/Weapons/Kinemation/MKR9/MKR9_BoltBack.wav` | W/MKR9/MKR9.prefab :: _audio._actionBack._clip | 0.450 | -3.1 | 0 | 20 | ок |
| `Audio/SFX/Weapons/Kinemation/MKR9/MKR9_BoltForward.wav` | W/MKR9/MKR9.prefab :: _audio._actionForwardChambered._clip<br>W/MKR9/MKR9.prefab :: _audio._actionForwardEmpty._clip | 0.450 | -3.1 | 0 | 20 | ок |
| `Audio/SFX/Weapons/Kinemation/MKR9/MKR9_MagIn.wav` | W/MKR9/MKR9.prefab :: AudioSource.m_Resource | 0.600 | -2.9 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/MKR9/MKR9_MagOut.wav` | W/MKR9/MKR9.prefab :: MonoBehaviour._takeOutClip | 0.600 | -3.4 | 270 | 350 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/Kinemation/MKR9/MKR9_Shot.wav` | W/MKR9/MKR9.prefab :: _triggers._shotAudio._clip | 1.040 | -3.3 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/Mk14/Mk14_BoltBack.wav` | W/Mk14/Mk14.prefab :: _audio._actionBack._clip | 0.450 | -5.5 | 0 | 20 | ок |
| `Audio/SFX/Weapons/Kinemation/Mk14/Mk14_BoltForward.wav` | W/Mk14/Mk14.prefab :: _audio._actionForwardChambered._clip<br>W/Mk14/Mk14.prefab :: _audio._actionForwardEmpty._clip | 0.450 | -5.5 | 0 | 20 | ок |
| `Audio/SFX/Weapons/Kinemation/Mk14/Mk14_MagIn.wav` | W/Mk14/Mk14.prefab :: AudioSource.m_Resource | 0.600 | -5.8 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/Mk14/Mk14_MagOut.wav` | W/Mk14/Mk14.prefab :: MonoBehaviour._takeOutClip | 0.600 | -5.8 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/Kinemation/Mk14/Mk14_Shot.wav` | W/Mk14/Mk14.prefab :: _triggers._shotAudio._clip | 1.370 | -5.8 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/R08/R08_MagIn.wav` | W/R08/R08.prefab :: AudioSource.m_Resource | 0.390 | -5.8 | 130 | 160 | ЗАДЕРЖКА (сильная) |
| `Audio/SFX/Weapons/Kinemation/R08/R08_MagOut.wav` | W/R08/R08.prefab :: MonoBehaviour._takeOutClip | 0.370 | -5.9 | 30 | 80 | кандидат |
| `Audio/SFX/Weapons/Kinemation/R08/R08_Shot.wav` | W/R08/R08.prefab :: _triggers._shotAudio._clip | 1.500 | -5.7 | 20 | 30 | ок |
| `Audio/SFX/Weapons/Kinemation/SRM12/SRM12_BoltBack_Cut.wav` | W/SRM12/SRM12.prefab :: _audio._actionBack._clip | 0.333 | -4.8 | 10 | 30 | ок |
| `Audio/SFX/Weapons/Kinemation/SRM12/SRM12_BoltForward_Cut.wav` | W/SRM12/SRM12.prefab :: _audio._actionForwardChambered._clip<br>W/SRM12/SRM12.prefab :: _audio._actionForwardEmpty._clip | 0.322 | -4.7 | 20 | 70 | кандидат |
| `Audio/SFX/Weapons/Kinemation/SRM12/SRM12_MagIn.wav` | W/SRM12/SRM12.prefab :: AudioSource.m_Resource | 0.600 | -4.8 | 0 | 10 | ок |
| `Audio/SFX/Weapons/Kinemation/SRM12/SRM12_MagOut.wav` | W/SRM12/SRM12.prefab :: MonoBehaviour._takeOutClip | 0.600 | -13.5 | 20 | 30 | ок |
| `Audio/SFX/Weapons/Kinemation/SRM12/SRM12_Shot.wav` | W/SRM12/SRM12.prefab :: _triggers._shotAudio._clip | 1.650 | -4.8 | 50 | 50 | кандидат |
| `Audio/SFX/Weapons/Kinemation/TR15/TR15_BoltBack.wav` | W/TR15/TR15.prefab :: _audio._actionBack._clip | 0.450 | -4.5 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/TR15/TR15_BoltForward.wav` | W/TR15/TR15.prefab :: _audio._actionForwardChambered._clip<br>W/TR15/TR15.prefab :: _audio._actionForwardEmpty._clip | 0.450 | -4.5 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/TR15/TR15_MagOut.wav` | W/TR15/TR15.prefab :: MonoBehaviour._takeOutClip | 0.600 | -10.7 | 30 | 30 | кандидат |
| `Audio/SFX/Weapons/Kinemation/TR15/TR15_Shot.wav` | W/TR15/TR15.prefab :: _triggers._shotAudio._clip | 0.520 | -4.8 | 20 | 20 | ок |
| `Audio/SFX/Weapons/Kinemation/Viper/Viper_BoltBack.wav` | Data/WeaponSystem/PistolFeedbackDefaults.asset :: _audio._actionBack._clip<br>W/Viper/Viper.prefab :: _audio._actionBack._clip | 0.450 | -4.5 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/Viper/Viper_BoltForward.wav` | Data/WeaponSystem/PistolFeedbackDefaults.asset :: _audio._actionForwardChambered._clip<br>Data/WeaponSystem/PistolFeedbackDefaults.asset :: _audio._actionForwardEmpty._clip<br>W/Viper/Viper.prefab :: _audio._actionForwardChambered._clip<br>W/Viper/Viper.prefab :: _audio._actionForwardEmpty._clip | 0.450 | -4.5 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Kinemation/Viper/Viper_MagIn.wav` | W/Viper/Viper.prefab :: AudioSource.m_Resource | 0.600 | -4.8 | 0 | 30 | ок |
| `Audio/SFX/Weapons/Kinemation/Viper/Viper_MagOut.wav` | W/Viper/Viper.prefab :: MonoBehaviour._takeOutClip | 0.600 | -21.6 | 0 | 10 | ок |
| `Audio/SFX/Weapons/Kinemation/Viper/Viper_Shot.wav` | W/Viper/Viper.prefab :: _triggers._shotAudio._clip | 0.470 | -4.8 | 30 | 40 | кандидат |
| `Audio/SFX/Weapons/MagazineInsert_Default.wav` | Resources/MagazineAnchorSoundDefaults.asset :: MonoBehaviour._insertClip | 0.363 | 0.0 | 0 | 0 | ок |
| `Audio/SFX/Weapons/Revolver/Revolver_Shot.wav` | W/Revolver/Revolver.prefab :: _triggers._shotAudio._clip | 1.500 | -5.7 | 20 | 20 | ок |
| `Audio/SFX/Weapons/SniperRifleAuto/SniperRifleAuto_Shot.wav` | W/SniperRifleAuto/SniperRifleAuto.prefab :: _triggers._shotAudio._clip | 0.550 | -2.2 | 40 | 40 | кандидат |
| `Audio/SFX/Weapons/UI_Error_Subtle_Deep_stereo.wav` | Data/WeaponSystem/PistolFeedbackDefaults.asset :: _audio._refusal._clip<br>Data/WeaponSystem/RifleFeedbackDefaults.asset :: _audio._refusal._clip<br>Data/WeaponSystem/ShotgunFeedbackDefaults.asset :: _audio._refusal._clip<br>Data/WeaponSystem/SniperFeedbackDefaults.asset :: _audio._refusal._clip | 0.205 | -12.7 | 0 | 0 | ок |
| `ThirdParty/Hands_Weapons_Animations_Pack_Update/Effects/Automatic_rifle/VFX/Sounds/MachineGun2.wav` | W/Uzi/Uzi.prefab :: _triggers._shotAudio._clip | 1.000 | 0.0 | 0 | 0 | ок (в worktree LFS-указатель; измерена копия main checkout (размер совпадает с oid)) |
| `ThirdParty/UltimateXR/Samples/FullScene/Audio/ShotImpact.wav` | W/Effects/ImpactDecal_Concrete.prefab :: MonoBehaviour._audioImpact._clip | 0.715 | -3.3 | 0 | 10 | ок |
| `ThirdParty/UltimateXR/Samples/FullScene/Audio/ShotgunPump01.mp3` | Data/WeaponSystem/ShotgunFeedbackDefaults.asset :: _audio._actionBack._clip | 0.192 | -1.8 | 20 | 20 | ок |
| `ThirdParty/UltimateXR/Samples/FullScene/Audio/ShotgunPump02.mp3` | Data/WeaponSystem/ShotgunFeedbackDefaults.asset :: _audio._actionForwardChambered._clip<br>Data/WeaponSystem/ShotgunFeedbackDefaults.asset :: _audio._actionForwardEmpty._clip | 0.410 | -1.2 | 10 | 30 | ок |
| `ThirdParty/UltimateXR/Samples/FullScene/Audio/TriggerNoAmmo.wav` | Data/WeaponSystem/PistolFeedbackDefaults.asset :: _audio._dryFire._clip<br>Data/WeaponSystem/RifleFeedbackDefaults.asset :: _audio._dryFire._clip<br>Data/WeaponSystem/ShotgunFeedbackDefaults.asset :: _audio._dryFire._clip<br>Data/WeaponSystem/SniperFeedbackDefaults.asset :: _audio._dryFire._clip<br>W/AK105/AK105.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/AR15/AR15.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/BrowningHiPower/BrowningHiPower.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/FabarmSDASS/FabarmSDASS.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/Herrington/Herrington.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/MKR9/MKR9.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/MP5K/MP5K.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/Mk14/Mk14.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/PPK/PPK.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/R08/R08.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/Revolver/Revolver.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/SRM12/SRM12.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/Scar/Scar.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/SniperRifle/SniperRifle.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/SniperRifleAuto/SniperRifleAuto.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/TR15/TR15.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/Uzi/Uzi.prefab :: _triggers._shotAudioNoAmmo._clip<br>W/Viper/Viper.prefab :: _triggers._shotAudioNoAmmo._clip | 0.450 | -0.4 | 0 | 0 | ок |

Поля: `W/` = `Assets/Prefabs/Weapons/`; `AudioSource.m_Resource` на объекте `CartridgeIntake` (Herrington, FabarmSDASS) — звук загрузки патрона в трубку, на `MagAnchor` — вставка магазина.
