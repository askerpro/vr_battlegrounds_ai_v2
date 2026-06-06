---
description: Master Orchestration Workflow for setting up a custom FBX avatar for VR autonomously.
---

# Setup Custom Avatar (Master Orchestration Workflow)

Этот воркфлоу является **мастер-инструкцией**. Он объединяет все необходимые действия для подготовки "сырого" 3D аватара (FBX) к работе в VR-окружении UltimateXR.

## Шаг 0: Валидация окружения
Этот workflow теперь выполняется через Unity Editor menu items. Для ручного запуска выбери FBX asset в Unity Project window. Для агентского запуска используй Unity MCP `execute_menu_item` с теми же путями меню.

Требования:
1. Unity Editor открыт на проекте.
2. Blender установлен. Если Unity не находит `blender.exe`, один раз запусти:
   `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/Configure Blender Executable...`
3. Для автоматического агентского запуска должен быть доступен Unity MCP.

---

## Шаг 1: Ампутация кистей аватара
> **Цель:** Изолировать и отсечь "вшитые" родные кисти аватара и зашить образовавшиеся пустоты на рукавах, чтобы обеспечить надежное крепление модульных VR-перчаток. А также почистить дубликаты материалов FBX (mat.001).

**Меню:** `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/1. Amputate Selected FBX Hands`

Скрипт: `Assets/Editor/VR_Battlegrounds/Avatars/BlenderScripts/amputate_avatar_hands.py`

---

## Шаг 2: Добавление костей скручивания (Torsion Bones)
> **Цель:** Решить проблему искажения меша (эффект выжатого полотенца) предплечья при вращении кистей в VR.

**Меню:** `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/2. Add Wrist Torsion Bones`

Скрипт: `Assets/Editor/VR_Battlegrounds/Avatars/BlenderScripts/add_wrist_torsion_bones.py`

---

## Шаг 3: Добавление костей глаз (LeftEye, RightEye)
> **Цель:** Избежать клиппинга камеры со шлемом или мешем головы. Добавляет кости LeftEye и RightEye внутрь кости головы в Blender, чтобы Unity и UltimateXR автоматически назначали положение камеры по глазам аватара.

**Меню:** `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/3. Add Eye Bones And Map Humanoid`

Скрипт добавляет кости через Blender, затем Unity-команда `ApplyEyeMapping` прописывает `LeftEye` / `RightEye` в `HumanDescription` выбранного FBX.

---

## Шаг 4: Настройка Unity Префаба
> **Цель:** Импортировать подготовленный FBX в Unity, присвоить ему правильные компоненты UltimateXR (UxrAvatar, BigHandsIntegration) и настроить иерархию камеры при помощи C# автоматизации.

1. Выдели подготовленный FBX asset.
2. Выполни: `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/4. Create Scene Target From Selected FBX`
3. Выполни: `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/5. Run UXR Setup On Current Target`

Команда `5. Run UXR Setup On Current Target` вызывает существующие шаги:
1. `Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/1. Core Setup`
2. `Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/2. Hands Integration`
3. `Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/3. Controller & Camera`
4. `Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/4. Finalize Rig Mapping`
5. `Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/5. Save as Prefab`
6. `Tools/VR Battlegrounds/Avatars/UXR Setup Wizard/6. Generate Default Poses`

> **Дефолтные настройки UxrStandardAvatarController (Body IK)**
> Должны быть заданы непосредственно на префабе рига (напр. `Heavy_Soldier_Rig_Mask_Winter`):
> - `Use Arm IK`: **True**, `Use Leg IK`: **True**, `Use Body IK`: **True**
> - `Lock Body Pivot`: **False**, `Body Pivot Rotation Speed`: **0.2**
> - Ветка Bend: Spine `0.2`, Chest `0.3`, UpperChest `0.4`
> - Ветка Torsion: Spine `0.4`, Chest `0.8`, UpperChest `0.2`
> - `Neck Head Balance`: **0.748**

---

## Быстрый полный запуск
Если нужно пройти весь pipeline одной командой:

1. Выдели FBX asset в Project window.
2. Выполни `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/Run Full Selected FBX Pipeline`.

Для ИИ-агента последовательность MCP menu items такая же:
1. `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/1. Amputate Selected FBX Hands`
2. `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/2. Add Wrist Torsion Bones`
3. `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/3. Add Eye Bones And Map Humanoid`
4. `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/4. Create Scene Target From Selected FBX`
5. `Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/5. Run UXR Setup On Current Target`

---

## Дополнительно: Legs Animator
> **Цель:** Настроить процедурную анимацию ног и устранить проблему "проваливания" ботинок сквозь пол при приседаниях в шлеме.
> ⚠️ **ВАЖНО:** Эти компоненты должны висеть на самом объекте модели/рига (например: `Heavy_Soldier_Rig_Mask_Winter`), до того как он будет вложен в игровой `PlayerBase`.

**Инструкция:**
1. Добавь на корень модели компонент `Legs Animator` (от FImpossible Creations). 
   - Автоматически должны подхватиться `Mecanim` (Animator) и тазовая кость `Hips` (например, `pelvis`).
   - Дефолтные настройки модулей: убедись, что активированы кастомные пресеты `Extra_Rotation Stability` и `UxrLamStepFurther`.
2. Добавь кастомный скрипт-мост `LegsAnimatorUxrBridge` (находится в `Assets/Integration/LegsAnimatorUxrBridge.cs`).
3. Задай эталонные параметры моста `LegsAnimatorUxrBridge`:
   - `Use Dynamic Floor Offset`: **Checked (true)**
   - `Foot Height Offset`: **0.15** (динамический отступ от пола, чтобы ботинки не проваливались).

---

## 🔎 Устранение неполадок (Troubleshooting)
Если возникли проблемы со сломанными префабами, странным масштабом или деформацией подмышек, изучи документ с продвинутыми решениями:
`workflows/setup_custom_avatar/blender_known_issues.md`

---

**Задача завершена.** Предоставь пользователю отчёт: все зависимости расставлены, скрипты выполнены, аватар готов к использованию в игре!