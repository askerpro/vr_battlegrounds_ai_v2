# Coverage обычного MEF в production team catalog

Дата: 2026-10-10. Read-only diagnosis, без Unity/очереди/правок source/assets/tests. Факт B400 перед Play получен от principal: Team1 содержит только MEF Black index0, Team2 — только ordinary MEF index0, Cyborg отсутствует. Сам каталог команд этим аудитом не изменялся и live snapshot повторно не проверялся.

## Причина отказа

- `BotClipComposition.cs:29-31` генерирует только `Optimized_MEF_Player_Black.prefab` и `PlayerControllersCyborgAvatar.prefab`.
- GUID ordinary MEF: `b8146035f3f436a4799882b3a0d1dd45` (`Optimized_MEF_Player.prefab.meta:2`); Black: `310c90ae0d962f344a1eddbe48a429c9` (`Optimized_MEF_Player_Black.prefab.meta:2`). Это разные source identity, не взаимозаменяемые ключи.
- `BotCombatAssets.GetClipBody`/`ResolveSource` сравнивают source reference, не имя/предка prefab. В текущем `BotCombatAssets.asset` scoped поиск на ordinary GUID не находит source binding, Black source присутствует (`22` и далее).
- `AvatarManager.ExecutionPrefabFor:358-375` разрешает для NPC только supported generated variant (за исключением собственного ghost): нет ordinary binding → возвращает null. Это правильный fail-closed, а не причина для возврата human/source prefab или автоматического выбора Black.
- `BotAvatarSpawnRegistry:14-35` обходит весь каталог динамически; нового source без prefab не регистрирует, проверяет уникальность source/assetId. Добавление третьей полноценной пары не требует изменения runtime lookup/registry.

## Scoped serialized evidence о родстве скелета

Black прямо наследует ordinary: `Optimized_MEF_Player_Black.prefab:2891` содержит `m_SourcePrefab` GUID `b814...`. У Black `m_RemovedComponents`, `m_RemovedGameObjects`, `m_AddedGameObjects`, `m_AddedComponents` пусты (`2887-2890`). Scoped поиск Black не выявил overrides `m_Avatar`, `m_Mesh`, `m_Bones`, `m_RootBone` или wrist transform references. Это сильное основание использовать ту же generic MEF ветвь authoring, но не Unity-подтверждение совпадения rest/Avatar.

Ordinary сам наследует `MEF_Base_Avatar` GUID `b6fe59db941fa944696ece5e1aabc032` (`4244`), а не Cyborg. Его шесть visible skinned mesh references имеют общий model GUID `ae1c618bdc0fbc84fa5a5d230da76f5f` с разными mesh fileID (`87/281/475/669/863/1057`), общий serialized root bone fileID `3497797962188941999` (`192/386/580/774/968/1162`). Этот root transform соответствует inherited base fileID `2875316411866090419` с GUID `b6fe...` (`4425-4428`). Ordinary оптимизирован через removed base render objects/added render objects (`4160-4234`); full serialized YAML не загружался.

Black имеет material/rig-info/identity overrides и transform TRS override target fileID `3160395138974733540` (`1238-1267`): position `(-3.16,.02935,3.38288)`, rotation identity. Этот source audit не разрешает конкретный Transform через Unity и не объявляет world/rest poses идентичными. Даже без topology changes нельзя копировать готовый Black socket/speed на ordinary без повторного полного authoring по его source reference.

Avatar asset и реальные Humanoid wrist/finger references наследуются и не полностью разрешимы из двух variant snippets. Их фактическое равенство/geometry correctness здесь **UNVALIDATED**. Нельзя утверждать новый missingBefore=0/mappedFingers30 или maxRest/saved PASS до native Build. Имеющиеся gates `BotClipComposition:89-149` уже проверяют Animator/render wrists, 30 finger mappings, anatomy и render hips; `177-201` проверяют saved rest/bounds/identity. Они должны выполниться отдельно для ordinary, а не отключаться из-за родства.

## Минимальный candidate для plan review

1. **`Assets/Editor/VR_Battlegrounds/Bots/BotClipComposition.cs`**: добавить точный ordinary source path `Assets/Prefabs/Player/Optimized_MEF_Player.prefab` третьей записью `Models`. Без новой MEF/Cyborg special-case логики, без имени alias, без новых pose/root writers. Build уже проходит generic `Models` loop; report model count динамический (`221`).
2. **`tasks/bots-fix/tools/armed-authoring-package.cs:59-60`**: обновить expected gates `2/4/18 → 3/6/27` либо вычислять по заранее утверждённому explicit source manifest, сверяя фактический exact source set. Только length-driven gate недостаточен: может скрыть отсутствие ordinary и случайный третий source. Cross-variant assetId HashSet уже масштабируется на три.
3. **Generated результат через worker**: новый `Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_ClipBody.prefab` и новый `.meta`; `BotCombatAssets.asset` содержит третью source/prefab пару, её 2 sockets и 9 native motions. Существующие Black/Cyborg IDs и source assets сохранить. Новый ClipAvatar нужен только если generic generator действительно обнаружит новую mapping необходимость, не создавать заранее. Controller assets остаются общими 6/6/2.
4. **Task package/acceptance документы**: обновить собственные declared counts и точные sources в payload/пакете, не переписывать existing tests до пользователя. Root отдельно решает явный bot model selector и способы предъявления Cyborg, не меняя team catalogs этим mechanical coverage fix.

`BuildCalibration:229` уже сравнивает `clipBodies.Length` с `Models.Length` — после Models extension ожидает3 автоматически. Weapon loops по двум IDs и `library.clipBodies` (`266`) дадут6 sockets; native loops (`307-325`) дают3×(Pistol4+Rifle4+UnarmedMove1)=27. Никакого hardcoded count2/4/18 в этих calibration loops нет. Generic registry/factory/source resolver не менять.

## Риски и границы следующего authoring пакета

- Это coverage production Team2 и подготовка нового generated ordinary body. Не смешивать с SDK71 owner admission/PPK authoring, которые имеют своих владельцев; validated joint snapshot/base нужны заново после plan review.
- Root-only transport, один NetworkIdentity, уникальный assetId относительно source и всех variants, один enabled Animator, ноль IK/SDK pose writers, geometry/rest/bounds/finger gates сохраняются без ослабления. Для всех трёх sources проверить 6/6/2 controller metadata, 6 cached sockets и27 finite positive native motions.
- Первые контрольные production пары теперь включают обе реально используемые команды MEFBlack/ordinaryMEF; отдельно явный selector нужен для Cyborg. Вручную навязанный Black в Team2 скрывает coverage дефект.
- Руки/пальцы нового ordinary body разрешено только визуально предъявить и измерить через existing authoring; этот диагноз не разрешает wrist/finger pose writes, исправления anatomy или перенос SDK hand templates. Parent topology не доказывает правильный хват/ShotSource/урон.
- Нет нового NUnit, no Heavy, no scene/team/source prefab mutation. Отсутствие нового source в metadata или любой native authoring gate failure — завершить lease, вернуть конкретный report, не включать human fallback.
