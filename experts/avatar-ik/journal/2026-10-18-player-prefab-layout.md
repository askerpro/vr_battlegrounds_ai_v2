# Heavy: только перенос в Unused

Задача `player-prefab-layout`, 2026-10-18. Native MoveAsset Heavy + meta в `Assets/Prefabs/Player/Unused/Heavy_Soldier_Base_Avatar.prefab`; GUID `fefcf679beb2d0f42b6cce742988357e`, blob prefab/meta и содержимое прежние. Variant остаётся от `Unused/PlayerBase_SdkHands`. PlayerBase/CyborgHandsBase/Controllers/MEF/Black/Ghost native chain до/после идентична; Ghost наследует Controllers.

Два точных Editor path updates: FixHandTrackingCache и только Heavy output CreateBaseAvatars. Генераторы не запускались. FIFO819 finish/receive, R `8874a93c`. PrefabComposition17/17; AvatarLoadout60/63, три прежних BotCombatStand/Floor failures, не исправлялись. Guard ready, Console errorCS0; Play и новые/переписанные tests отсутствуют. UserQA впереди, accepted commit/merge/push нет. Подробности и ignored receipt — `tasks/player-prefab-layout/`.

Следующий layout stage разрешён пользователем/9661: Bases, CyborgHands/{Base,Cyborg,MEF}, Ghost папкой в Avatars. Editor paths подготовлены, source asset moves ещё не исполнялись: native MCP startup tools/list timeout30s, issue9678. Heavy сохранён. Новый compile/runtime proof отсутствует; execution spec и точные writes — в задаче.

Итог layout:14native moves46assets завершены,823 DONE/IMPORTED R32b72bd3; worker released. Семь avatar prefabs/source chain и все95transfer body/meta/foldermeta сохранили GUID/fileID/байты. Ghost перенесён всей папкой, наследует Controllers.11новыхfolderGUID сохранены изpartial821; старый MEF-контейнер удалён доказанно пустым. CompilationEditor0CS, Composition17/17,Loadout60/63 (3прежнихFloor),NetworkAssetIdcatalogue1/1. Пол/механика/IK/калибровка не менялись; UserQA/Quest впереди, acceptedcommit/merge/push нет.
