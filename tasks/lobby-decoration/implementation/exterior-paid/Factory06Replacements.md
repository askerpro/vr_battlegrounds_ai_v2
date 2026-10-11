# Четыре согласованные ближние замены

09.10.2026. Root явно одобрил только4 replacements, без увеличения104 деревьев.
Fresh guard успешен. Изменён только exclusive `LobbyExteriorDecorationBuilder.cs`.

`nearTreeOrdinal` сбрасывается в0 в Build и учитывает только первые38 Tree calls.
Factory06 выбирается на0-based indices3/13/22/31: EastNearForest, WestNearForest,
NorthWestForest и SouthEastForest. Это четыре разных островка, включая оба window flanks.
После первых38 calls ordinal остаётся38, поэтому66 far calls используют прежний M_01.

Source path: `Assets/Art/Lobby/Imported/Catalog/AbandonedFactory/Art/Prefabs/SM_SilverFir_06.prefab`;
GUID main source подтверждён `abc00000000016159412300961956038`.
Для replacements прежние позиции и random yaw сохраняются; height=Clamp(originalHeight*.55,4.8,6.8).
Остальные100 M_01 сохраняют explicit Bark/Atlas, исходные позиции/height/yaw и4LOD.
Factory06 scene copies называются SilverFir_Factory06, сохраняют source green materials
и отсутствие LOD: generic ConfigureLods при одном source уровне не создаёт LODGroup.
Все renderers участвуют в grounding/height bounds; strip behaviours/colliders/shadows
выполняется существующим Model/Sanitize. Source prefab/materials не модифицируются.

Closed horizon768 triangles,104 tree objects, API Build(parent,arena) сохранены.
Native rendered appearance, fixed actual height и component inventory проверяет ведущий.
Собственная Roslyn compile после patch:exit0, лог `reports/implementation/exterior-paid/offline-compile.txt`.
Frozen helper SHA `D0F2D758D6B838F61935EAF58C7650118929EE2EFF54A43CC8AA46440BC5BC28`.
Native Unity/scene writes, новые tests, materials/source changes и commits этим исполнителем не выполнялись.
