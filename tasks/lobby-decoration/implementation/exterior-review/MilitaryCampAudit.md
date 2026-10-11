# MilitaryCamp URP — внешний реквизит, read-only аудит

09.10.2026. Fresh guard implementation epoch 1 успешен перед записью этого отчёта.
Исследованы только cached text assets из URP archive index. Assets/helper/сцена не изменены,
Unity не запускался. Binary mesh triangles, фактические bounds и material import ещё не проверены.

Данные: `reports/implementation/exterior-review/militarycamp-prefab-audit.json`, 47 префабов.
Все пути таблицы относительны к `Assets/Hivemind/MilitaryCamp/URP/Art/Prefabs/`.
У перечисленных префабов root localScale=(1,1,1); это не доказательство подходящих размеров.

| Prefab | GUID | LOD levels | Удалить из декоративной копии |
|---|---|---:|---|
| SM_ArmoredVehicle | abc00000000006507900326933438472 | 5 | 5 BoxCollider, 2 Light, 2 UniversalAdditionalLightData |
| SM_WatchTower_01 | abc00000000010367811166705668320 | 5 | 142 BoxCollider |
| SM_AmmoCase_01 | abc00000000012017236203265661070 | 3 | 2 BoxCollider |
| SM_AmmoCase_02 | abc00000000003273235488023318933 | 4 | 1 BoxCollider |
| SM_AmmoCartridgeBoxes_01 | abc00000000007223829749494322384 | 2 | 1 BoxCollider |
| SM_MilitaryCrate_01 | abc00000000015158636773066772218 | 4 | 1 BoxCollider |
| SM_MilitaryCrate_02 | abc00000000006183764951078403713 | 3 | 1 BoxCollider |
| SM_MilitaryCrate_03 | abc00000000005850773303671269560 | 4 | 1 BoxCollider |
| SM_Barrel_01 | abc00000000011043161318511504812 | 3 | 1 CapsuleCollider |
| SM_Barrel_02 | abc00000000002261166447072914791 | 3 | 1 CapsuleCollider |
| SM_Fence_2m | abc00000000014660014748177130023 | 2 | 1 BoxCollider |
| SM_Fence_long_4m | abc00000000009080195041632205316 | 3 | 4 BoxCollider |
| SM_fence_corner | abc00000000000194887990255575587 | 3 | 1 BoxCollider |
| SM_Lamp_long | abc00000000009916751149465198933 | 3 | 2 BoxCollider, 1 Light, 1 UniversalAdditionalLightData |
| SM_AlarmLight | abc00000000006506728847478610531 | 4 | 1 BoxCollider, 1 Light, 1 UniversalAdditionalLightData |
| SM_FuelCanister | abc00000000001653513829846556825 | 3 | 1 BoxCollider |

## Машина и вышка

Машина mesh dependency: `Art/Assets/SM_ArmoredVehicle.fbx`,
GUID `abc00000000010081317704044546875`. Пять MeshRenderer/MeshFilter (LOD0–4),
четыре материала M_ArmoredVehicle_01..04, каждый с BC/N/ORM textures.
Source thresholds: .6023709/.40018776/.25172976/.1541016/.019039458.
LOD size=6.217843, reference point=(.121366,1.204965,0); size — параметр LODGroup, не высота.
После native preview это предпочтительный источник вместо грубого SupplyTruckCandidate.

Вышка mesh dependency: `Art/Assets/SM_WatchTower_01.fbx`,
GUID `abc00000000005943381046651377361`. Пять MeshRenderer/MeshFilter, 161 Transform/GameObject;
большинство объектов — физическая детализация. Thresholds
.59674513/.40018776/.25355783/.14564313/.016075041.
LOD size=19.375206, reference point=(-.00698,8.504901,-.007411); реальную высоту не измеряли.
Не применять прежний масштаб prototype автоматически: native размер/силуэт нужно согласовать.
Материалы: M_Hangar_metal_03, M_Metal_01/02, M_MetalFloor, M_Concrete_01.

Критично: текущий Exterior.ConfigureLods собирает только `_LOD0..3`. Если ведущий использует
этот маршрут с purchased vehicle/tower, `_LOD4` останется вне нового LODGroup. Сохранить
source LODGroup и очищать только посторонние компоненты либо поддержать все source levels.
Не показывать несколько уровней одновременно. Исходные meshes/materials не мутировать.

## Материалы и свет

Основной shadergraph `S_BasicTextured.shadergraph`, GUID
`e6f99812ca2d22a4c98d5e88e4d78f2a`; это custom shadergraph, не builtin URP/Lit.
В cached graph присутствуют UniversalTarget/UniversalLitSubTarget, также исторические HD target
объекты; наличие HD текста само по себе не доказывает активный HDRP shader.
У некоторых материалов есть сохранённые HDRP metadata/keywords. Проверять импорт/проектные
dependencies или делать корректную own URP адаптацию; не считать archive name доказательством
успешной native URP компиляции.

M_Concrete_01 использует `S_VPWorldAligned.shadergraph`, GUID
`ef20e0d3f5654db47831a61a1013d4f5`; простая замена на одноцветный материал потеряет vertex/world
aligned detail. M_Wire_01/02 и M_MetalFloor имеют alpha clip/double-sided keywords:
alpha-test нельзя терять при адаптации; fence mesh требует проверить overdraw/aliasing в Quest.
Автоматически маппить ORM как URP MetallicGlossMap нельзя: нужны правильные каналы
AO/Roughness/Metallic и smoothness=1-roughness.

Prefab MonoBehaviour GUID `474bcb49853aa07438625e644c072ee6` live подтверждён в текущем
PackageCache как `UniversalAdditionalLightData.cs`, не неизвестная игровая логика.
Тем не менее source Light + companion component удаляются: свет создаёт единственный ведущий.
Для новых согласованных фонарей оставлять только ExteriorLightSocket. SM_Lamp_long — реальный
длинный светильник; его нельзя объявлять подтверждённым отдельно стоящим фонарным столбом.
Отдельный lightpole/floodlight prefab в текущем index по именам не найден.

## Выбор и hazards

Базовый малый набор: 1 ArmoredVehicle, 1 WatchTower, 2 варианта MilitaryCrate,
AmmoCase_01/02, 2 Barrel, Fence_2m/long_4m и FuelCanister. Без base/top split если закрытые
кейсы не требуют детализации; мелкие cartridge boxes использовать только на ближнем столе.
AmmoCase_01/02 по metadata малы (LOD size .429806/.350486), не называть их автоматически
длинными оружейными кейсами. Для такого вида сначала preview MilitaryCrate_03: LOD size
1.324569, reference y=.102993 предполагает низкую форму, но не доказывает визуальное назначение.
Во время интеграции соблюдать широкую полосу мишеней и физическую арену, проверить native
scale и каждый source material. Коллайдеры tower, source lights, лишний LOD4 и alpha-test
материалы — основные реальные риски, подтверждённые metadata.

Camo-net/backpack/vest не подтверждены этим аудитом. Net source partial не использовать.
Shared MilitaryCamp копирует единый owner; этот исполнитель не переносил никаких его assets.
