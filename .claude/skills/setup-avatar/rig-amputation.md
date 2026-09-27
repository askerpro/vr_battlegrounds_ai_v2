# Риг, путь А: ампутация кистей + кисти SDK

Для модели без пальцевого скелета или с кистями, вшитыми в меш рукава. Родные кисти
отрезаются в Blender, вместо них ставятся IK-кисти UltimateXR (`BigIKHandLeft/Right`), и риг
`UxrAvatar` смотрит в их кости. Так собран `Heavy_Soldier_Rig_Mask_Winter`.

Всё запускается пунктами меню Unity: вручную (выделить FBX в Project) или агентом через
`execute_menu_item`. Blender должен быть установлен; если Unity его не находит —
`Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/Configure Blender Executable...`
(путь из `C:\Program Files\WindowsApps` не годится — Windows не даёт его запускать).

## Шаги

Меню: `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/…`, скрипты —
`Assets/Editor/VR_Battlegrounds/Avatars/BlenderScripts/`. Каждый Blender-шаг открывает FBX
в фоновом Blender, правит и **перезаписывает FBX на месте** — модель из ThirdParty сначала
скопировать в свою папку.

1. `1. Amputate Selected FBX Hands` — `amputate_avatar_hands.py`. Удаляет вершины кистей по
   весам (вес костей кисти/пальцев > 0.95) и геометрически (всё дальше запястья на 1 см),
   зашивает дыры рукавов, чистит дубли материалов `.001`. Если у перчаток основание заскинено
   на `lowerarm`, останутся «пеньки» — тогда резать геометрически по длине предплечья.
2. `2. Add Wrist Torsion Bones` — `add_wrist_torsion_bones.py`. Три twist-кости
   `lowerarm_twist_0N_{l,r}` и перераспределение весов по длине предплечья (против «выжатого
   полотенца»). Имена костей скрипт ждёт в стиле UE (`lowerarm_l`, `hand_l`).
3. `3. Add Eye Bones And Map Humanoid` — `add_eye_bones.py` + `ApplyEyeMapping.MapEyes`.
   Кости `LeftEye`/`RightEye` по центрам мешей глаз (или по смещению от головы, если мешей
   нет), затем прописываются в `HumanDescription`.
4. `4. Create Scene Target From Selected FBX` — экземпляр с маркерным именем
   `AutoSetupAvatarTarget` (по нему находят объект шаги визарда).
5. `5. Run UXR Setup On Current Target` — визард `UXR Setup Wizard/1…6`:
   `Core Setup` (UxrAvatar + риг из Animator) → `Hands Integration` (`BigHandsIntegration` +
   `BigIKHand*`) → `Controller & Camera` → `Finalize Rig Mapping` (риг на кости IK-кистей) →
   **кончики пальцев для UI** (`AvatarFingertipSetup.Setup`) → `Save as Prefab` →
   `Generate Default Poses`. Кончики ставятся после разметки рига, поэтому попадают на кисти SDK,
   которые двигает UltimateXR, а не на отрезанные родные кости (ошибка Heavy: левый кончик на
   `index_03_l`, левая рука UI не нажимает).

`Run Full Selected FBX Pipeline` делает всё подряд.

⚠️ `Save as Prefab` пишет в `Assets/Prefabs/Player/`. Префаб рига перенести в
`Assets/Prefabs/Avatars/` — в `Prefabs/Player` лежат только игровые аватары.

## Перчатки и позы

`Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/Modular Glove Bone Mapper` — перепривязать кости
`SkinnedMeshRenderer` сторонних перчаток к скелету аватара по именам.

Подгонять исходную позу пальцев под образец не нужно: дескрипторы поз UltimateXR задают фаланги
относительно ладони в универсальных осях и не зависят от исходной позы. Правка поз и зеркалирование
с руки на руку — штатный Hand Pose Editor SDK (`UltimateXR → Hand Pose Editor`). Прежний
`Avatar Finger Configurator` (`AvatarHandAligner`) удалён: он переписывал повороты костей в префабе,
а зеркалил через углы Эйлера — неверно, когда оси костей рук не зеркальны.

## Известные грабли Blender

**Единицы.** FBX из магазинов хранит сантиметры (`scale = 0.01` на корне после импорта).
Размеры новой геометрии (кольца-муфты на стыке кисти и т. п.) мерить по вершинам меша в
том же пространстве, а не предполагать; надёжнее создавать геометрию дочерней к armature,
чтобы она унаследовала масштаб. Типичная ошибка — радиус 0.79 «в единицах Blender»
превращается в 79 см в Unity.

**Подмышки** (экспериментально, не автоматизировано). При подъёме рук над головой меш
плеча рвётся: вершины имеют веса одновременно торса (`spine*`, `clavicle`) и руки
(`upperarm*`). Лечится сегментацией: каждая вершина зоны плеча отдаётся целиком сегменту
с наибольшей суммой весов; граница — между `clavicle` (торс) и `upperarm` (рука).
Примеры — `media/shoulder_deformation_demo*.png`.
