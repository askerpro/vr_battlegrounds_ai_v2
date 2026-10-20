using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Editor.Weapons;
using Object = UnityEngine.Object;
using TMPro;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    ///     Стенд раскладки одного ствола. <see cref="OpenPrepared" /> строит над панелью слота выбранного вида
    ///     выбранное оружие по действующей раскладке (<see cref="ArsenalSlotLayout" />): своей у оружия или умолчанию
    ///     слота. Ручки: оружие, магазин, карточка, опоры, коробка приёма (поза ручки — центр и поворот, размер — её
    ///     <see cref="BoxCollider" />). Bake записывает ручки в действующий ассет раскладки; его могут разделять
    ///     несколько стволов. «Отдельная раскладка» копирует ассет и назначает копию стволу. Проекция временная
    ///     (<see cref="HideFlags.DontSaveInEditor" />): сцена стенда хранит только carrier.
    /// </summary>
    public static class ArsenalLayoutAuthoring
    {
        private const string DemoPath = "Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab";
        public const string LayoutFolder = "Assets/Data/Arsenal/SlotLayouts";

        /// <summary>Первичная сборка собственной fixture (carrier и свет, без слота); сцена не включается в build.</summary>
        public static ArsenalLayoutAuthoringStand PopulatePrepared(Scene scene, ArsenalPresentationStyle style)
        {
            if (!scene.isLoaded || scene.path != ArsenalLayoutAuthoringStand.ScenePath || scene.GetRootGameObjects().Length != 1)
                throw new InvalidOperationException("Не собственная initial authoring fixture.");
            var root = scene.GetRootGameObjects()[0];
            if (root.GetComponentsInChildren<Component>(true).Length != 1 || root.name != "ArsenalLayoutAuthoring — owned staging fixture")
                throw new InvalidOperationException("Initial root уже имеет контент; ничего не заменяем.");
            var stand = root.AddComponent<ArsenalLayoutAuthoringStand>();
            if (stand == null) throw new InvalidOperationException("Data carrier не прикрепился: проверьте runtime assembly boundary.");
            var input = new SerializedObject(stand);
            input.FindProperty("_style").objectReferenceValue = style;
            input.ApplyModifiedPropertiesWithoutUndo();
            ValidateOwnership(stand);
            var lightObject = new GameObject("Authoring light (owned)"); lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(25, -25, 0);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.2f;
            return stand;
        }

        // Единственный Editor writer сериализуемого derived cache. Carrier не содержит editor API/setters.
        private static void WriteBaselines(ArsenalLayoutAuthoringStand stand)
        {
            var input = new SerializedObject(stand);
            input.FindProperty("_sourceBaseline").stringValue = stand.HasSlot ? SourceFingerprint(stand) : null;
            input.FindProperty("_stagedBaseline").stringValue = stand.HasSlot ? StagedFingerprint(stand) : null;
            input.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void WriteSlot(ArsenalLayoutAuthoringStand stand, ArsenalLayoutAuthoringStand.SlotHandles source)
        {
            var input = new SerializedObject(stand); var row = input.FindProperty("_slot");
            row.FindPropertyRelative("Weapon").objectReferenceValue = source.Weapon;
            row.FindPropertyRelative("Zone").enumValueIndex = (int)source.Zone;
            row.FindPropertyRelative("Frame").objectReferenceValue = source.Frame;
            row.FindPropertyRelative("Item").objectReferenceValue = source.Item;
            row.FindPropertyRelative("Magazine").objectReferenceValue = source.Magazine;
            row.FindPropertyRelative("Card").objectReferenceValue = source.Card;
            row.FindPropertyRelative("PlaceZone").objectReferenceValue = source.PlaceZone;
            var supports = row.FindPropertyRelative("Supports"); supports.arraySize = source.Supports.Count;
            for (int j = 0; j < source.Supports.Count; j++)
            {
                var support = supports.GetArrayElementAtIndex(j); var data = source.Supports[j];
                support.FindPropertyRelative("Role").stringValue = data.Role;
                support.FindPropertyRelative("AnchorKind").enumValueIndex = (int)data.AnchorKind;
                support.FindPropertyRelative("Handle").objectReferenceValue = data.Handle;
            }
            input.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Источники: вид слота, корпус лобби, раскладки ────────────────

        /// <summary>Префаб слота вида: префаб первого ряда этого вида в корпусе лобби.</summary>
        private static ArsenalSlotController SlotPrefabOf(ArsenalPresentationZone kind)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPath);
            var row = source != null ? source.GetComponentsInChildren<ArsenalSlotRow>(true).FirstOrDefault(r => r.Zone == kind && r.SlotPrefab != null) : null;
            if (row == null) throw new InvalidOperationException("В корпусе лобби нет ряда слотов вида " + kind + ".");
            return row.SlotPrefab;
        }
        /// <summary>
        ///     Виды слота, в которых стоит оружие: ряды его записей во всех ассортиментах проекта (ключ ряда → вид по
        ///     корпусу лобби); нет ни одной записи — виды его собственных раскладок.
        /// </summary>
        public static IReadOnlyList<ArsenalPresentationZone> KindsOf(WeaponInfo weapon)
        {
            if (weapon == null) return Array.Empty<ArsenalPresentationZone>();
            var corpus = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPath);
            var rows = corpus != null ? corpus.GetComponentsInChildren<ArsenalSlotRow>(true) : Array.Empty<ArsenalSlotRow>();
            var kinds = AssetDatabase.FindAssets("t:ArsenalPreset")
                .Select(g => AssetDatabase.LoadAssetAtPath<ArsenalPreset>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p != null)
                .SelectMany(p => p.Entries).Where(e => e.Weapon == weapon)
                .SelectMany(e => rows.Where(r => r.RowKey == e.Row).Select(r => r.Zone)).Distinct().ToList();
            if (kinds.Count == 0) kinds = weapon.SlotLayouts.Where(l => l != null).Select(l => l.SlotKind).Distinct().ToList();
            kinds.Sort();
            return kinds;
        }
        /// <summary>Действующее представление: своя раскладка оружия или умолчание слота.</summary>
        private static ArsenalPresentationSnapshot Layout(WeaponInfo weapon, ArsenalPresentationZone kind, ArsenalPresentationStyle style) =>
            ArsenalPresentationResolver.Resolve(weapon, kind, style, SlotPrefabOf(kind).DefaultLayout);
        /// <summary>Все оружия проекта, ссылающиеся на ассет раскладки; плюс слоты, у которых он по умолчанию.</summary>
        public static string Usage(ArsenalSlotLayout layout)
        {
            var weapons = AssetDatabase.FindAssets("t:WeaponInfo").Select(g => AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(w => w != null && w.SlotLayouts.Contains(layout)).Select(w => w.WeaponId).ToArray();
            bool isDefault = Enum.GetValues(typeof(ArsenalPresentationZone)).Cast<ArsenalPresentationZone>()
                .Any(k => { try { return SlotPrefabOf(k).DefaultLayout == layout; } catch (InvalidOperationException) { return false; } });
            return (isDefault ? "умолчание слота (все стволы без своей раскладки)" : "") +
                   (weapons.Length == 0 ? "" : (isDefault ? "; " : "") + "своя у: " + string.Join(", ", weapons));
        }

        public static string SourceFingerprint(ArsenalLayoutAuthoringStand stand)
        {
            var slot = stand.Slot; string weaponPath = AssetDatabase.GetAssetPath(slot.Weapon);
            string weapon = AssetDatabase.AssetPathToGUID(weaponPath) + ":" + slot.Zone + ":" + AssetDatabase.GetAssetDependencyHash(weaponPath);
            string layout = EditorJsonUtility.ToJson(Layout(slot.Weapon, slot.Zone, stand.Style).Layout);
            return Hash(weapon + layout + EditorJsonUtility.ToJson(stand.Style) + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(stand.Style)));
        }
        private static void RequireCleanInputs(ArsenalLayoutAuthoringStand stand, WeaponInfo weapon)
        {
            var paths = new[] { AssetDatabase.GetAssetPath(weapon),
                AssetDatabase.GetAssetPath(stand.Style), AssetDatabase.GetAssetPath(stand.Style.SupportModule),
                AssetDatabase.GetAssetPath(stand.Style.ReturnReadyMaterial), DemoPath }.AsEnumerable();
            if (stand.Style.SupportModule != null) paths = paths.Concat(stand.Style.SupportModule.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(AssetDatabase.GetAssetPath));
            ArsenalEditorActions.RequireCleanAssets(paths.Where(p => !string.IsNullOrEmpty(p)));
        }
        private static string Hash(string value)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
        private static ArsenalPresentationPose Pose(Transform handle, Transform frame)
        {
            if (handle == null || handle.parent != frame || (handle.localScale - Vector3.one).sqrMagnitude > 1e-12f)
                throw new InvalidOperationException("Ручка утрачена, перенесена или масштабирована: изменяйте только Move/Rotate.");
            var pose = new ArsenalPresentationPose(handle.localPosition, handle.localRotation);
            ArsenalPresentationResolver.ValidatePose(pose);
            return pose;
        }
        /// <summary>Коробка приёма с ручки: поза — центр и поворот, BoxCollider — размер.</summary>
        private static ArsenalPlaceZone Zone(Transform handle, Transform frame)
        {
            var pose = Pose(handle, frame);
            var box = handle.GetComponent<BoxCollider>();
            if (box == null) throw new InvalidOperationException("У ручки коробки приёма нет BoxCollider.");
            return new ArsenalPlaceZone { Center = pose.Position, EulerAngles = pose.EulerAngles, Size = box.size };
        }
        private static string SlotFingerprint(ArsenalLayoutAuthoringStand.SlotHandles slot)
        {
            var text = new StringBuilder();
            text.Append(slot.Weapon.WeaponId).Append(slot.Zone);
            foreach (var handle in new[] { slot.Item, slot.Magazine, slot.Card }.Concat(slot.Supports.Select(s => s.Handle)))
                text.Append(JsonUtility.ToJson(Pose(handle, slot.Frame)));
            text.Append(JsonUtility.ToJson(Zone(slot.PlaceZone, slot.Frame)));
            return Hash(text.ToString());
        }
        public static string StagedFingerprint(ArsenalLayoutAuthoringStand stand) => stand.HasSlot ? SlotFingerprint(stand.Slot) : "";
        public static bool HasStagedChanges(ArsenalLayoutAuthoringStand stand) => stand.HasSlot && StagedFingerprint(stand) != stand.StagedBaseline;
        public static void ValidateStand(ArsenalLayoutAuthoringStand stand)
        {
            ValidateOwnership(stand);
            ValidateProjection(stand);
        }
        /// <summary>Свой стенд в своей сцене вне build, свободный EditMode — без этого никакая запись не допускается.</summary>
        private static void ValidateOwnership(ArsenalLayoutAuthoringStand stand)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Authoring доступен только в свободном EditMode.");
            if (stand == null || stand.Owner != ArsenalLayoutAuthoringStand.OwnerId || stand.gameObject.scene.path != ArsenalLayoutAuthoringStand.ScenePath ||
                stand.transform.parent != null || !stand.gameObject.scene.isLoaded || stand.Style == null)
                throw new InvalidOperationException("Не собственный persistent authoring стенд.");
            if (stand.gameObject.scene.GetRootGameObjects().Length != 1 ||
                stand.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArsenalLayoutAuthoringStand>(true)).Count() != 1)
                throw new InvalidOperationException("В сцене есть посторонние корни/дубли marker; запись запрещена.");
            if (EditorBuildSettings.scenes.Any(s => s.path == ArsenalLayoutAuthoringStand.ScenePath))
                throw new InvalidOperationException("Authoring сцена не должна входить в Build Settings.");
            if ((stand.transform.lossyScale - Vector3.one).sqrMagnitude > 1e-12f)
                throw new InvalidOperationException("Authoring root не масштабируется: физические размеры предметов сохраняются.");
            ArsenalPresentationResolver.ValidateStyle(stand.Style);
        }
        /// <summary>Слот выбранного ствола построен этим стендом, ручки на месте и не масштабированы.</summary>
        private static void ValidateProjection(ArsenalLayoutAuthoringStand stand)
        {
            if (!stand.HasSlot) throw new InvalidOperationException("Стенд пуст: откройте ствол из окна «Настройка оружия» → «Раскладка в слоте».");
            var slot = stand.Slot;
            if (slot.Frame.parent != stand.transform) throw new InvalidOperationException("Слот стенда перенесён из корня стенда.");
            if ((slot.Frame.localScale - Vector3.one).sqrMagnitude > 1e-12f) throw new InvalidOperationException("Slot frame не масштабируется.");
            foreach (var handle in new[] { slot.Item, slot.Magazine, slot.Card }.Concat(slot.Supports.Select(s => s.Handle))) Pose(handle, slot.Frame);
            Zone(slot.PlaceZone, slot.Frame);
            if (slot.Supports.Select(s => s.Role).Distinct().Count() != slot.Supports.Count) throw new InvalidOperationException("Дубли support roles.");
        }
        /// <summary>Незапечённые позы исправного слота; слот пуст или не проходит проверку — false (его нечего запекать).</summary>
        private static bool HasBakeableStagedChanges(ArsenalLayoutAuthoringStand stand)
        {
            try { ValidateProjection(stand); return HasStagedChanges(stand); }
            catch (Exception) { return false; }
        }
        private static void SetPose(SerializedProperty property, ArsenalPresentationPose value) => SetPose(property, "Position", value);
        private static void SetPose(SerializedProperty property, string positionName, ArsenalPresentationPose value)
        {
            // Ручка хранит кватернион: обратный перевод в Эйлер даёт шум (280 → 279.99994). Ту же позу не переписываем.
            var position = property.FindPropertyRelative(positionName);
            var euler = property.FindPropertyRelative("EulerAngles");
            if ((position.vector3Value - value.Position).sqrMagnitude <= 1e-12f && Quaternion.Angle(Quaternion.Euler(euler.vector3Value), value.Rotation) < 1e-3f) return;
            position.vector3Value = value.Position;
            euler.vector3Value = value.EulerAngles;
        }

        // ── Bake ─────────────────────────────────────────────────────────

        /// <summary>Ручки слота — в действующий ассет раскладки (свой у оружия или умолчание слота).</summary>
        public static string BakePrepared(ArsenalLayoutAuthoringStand stand)
        {
            ValidateStand(stand);
            var slot = stand.Slot;
            RequireCleanInputs(stand, slot.Weapon);
            if (SourceFingerprint(stand) != stand.SourceBaseline) throw new InvalidOperationException("Раскладка или оружие изменились после открытия стенда; staged изменения сначала сопоставьте с новым источником.");
            var layout = Layout(slot.Weapon, slot.Zone, stand.Style).Layout;
            if (!WriteLayout(layout, slot)) return "Нет изменений раскладки.";
            WriteBaselines(stand);
            return "Сохранена раскладка " + layout.name + ".";
        }
        private static bool WriteLayout(ArsenalSlotLayout layout, ArsenalLayoutAuthoringStand.SlotHandles slot)
        {
            string before = EditorJsonUtility.ToJson(layout);
            var so = new SerializedObject(layout);
            SetPose(so.FindProperty("_itemTarget"), Pose(slot.Item, slot.Frame));
            SetPose(so.FindProperty("_magazineTarget"), Pose(slot.Magazine, slot.Frame));
            SetPose(so.FindProperty("_cardTarget"), Pose(slot.Card, slot.Frame));
            var supports = so.FindProperty("_supports"); supports.arraySize = slot.Supports.Count;
            for (int i = 0; i < slot.Supports.Count; i++)
            {
                var target = supports.GetArrayElementAtIndex(i); var handle = slot.Supports[i];
                target.FindPropertyRelative("Role").stringValue = handle.Role;
                target.FindPropertyRelative("AnchorKind").enumValueIndex = (int)handle.AnchorKind;
                SetPose(target.FindPropertyRelative("SlotPose"), Pose(handle.Handle, slot.Frame));
            }
            var zone = Zone(slot.PlaceZone, slot.Frame);
            var zoneProperty = so.FindProperty("_placeZone");
            SetPose(zoneProperty, "Center", new ArsenalPresentationPose(zone.Center, zone.Rotation));
            zoneProperty.FindPropertyRelative("Size").vector3Value = zone.Size;
            Undo.RecordObject(layout, "Bake Arsenal Slot Layout");
            so.ApplyModifiedProperties();
            if (EditorJsonUtility.ToJson(layout) == before) return false;
            EditorUtility.SetDirty(layout); AssetDatabase.SaveAssetIfDirty(layout);
            return true;
        }

        /// <summary>Копия действующей раскладки ствола — его собственная; затем его ручки пишутся в неё.</summary>
        public static string SeparateLayoutPrepared(ArsenalLayoutAuthoringStand stand)
        {
            ValidateStand(stand);
            var slot = stand.Slot;
            RequireCleanInputs(stand, slot.Weapon);
            var current = Layout(slot.Weapon, slot.Zone, stand.Style);
            if (current.FromWeapon && !AssetDatabase.FindAssets("t:WeaponInfo").Select(g => AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(g)))
                    .Any(w => w != null && w != slot.Weapon && w.SlotLayouts.Contains(current.Layout)))
                return "У " + slot.Weapon.WeaponId + " уже отдельная раскладка " + current.Layout.name + ".";
            if (!AssetDatabase.IsValidFolder(LayoutFolder)) AssetDatabase.CreateFolder("Assets/Data/Arsenal", "SlotLayouts");
            string path = AssetDatabase.GenerateUniqueAssetPath(LayoutFolder + "/" + slot.Weapon.WeaponId + "_" + slot.Zone + ".asset");
            var copy = Object.Instantiate(current.Layout);
            AssetDatabase.CreateAsset(copy, path);
            var weapon = new SerializedObject(slot.Weapon);
            var list = weapon.FindProperty("_layouts");
            int index = -1;
            for (int i = 0; i < list.arraySize; i++)
            {
                var item = list.GetArrayElementAtIndex(i).objectReferenceValue as ArsenalSlotLayout;
                if (item != null && item.SlotKind == slot.Zone) index = i;
            }
            if (index < 0) { index = list.arraySize; list.arraySize++; }
            list.GetArrayElementAtIndex(index).objectReferenceValue = copy;
            Undo.RecordObject(slot.Weapon, "Separate Arsenal Slot Layout");
            weapon.ApplyModifiedProperties(); EditorUtility.SetDirty(slot.Weapon); AssetDatabase.SaveAssetIfDirty(slot.Weapon);
            WriteLayout(copy, slot);
            WriteBaselines(stand);
            return "Создана отдельная раскладка " + path + " для " + slot.Weapon.WeaponId + ".";
        }

        // ── Открытие ствола / Restore ────────────────────────────────────

        /// <summary>
        ///     Решение человека перед пересборкой: спрашивает только при незапечённых позах текущего исправного слота.
        ///     Пустой или устаревший слот пересобирается без вопроса — его ручки запечь нельзя.
        /// </summary>
        public static bool ConfirmRebuild(ArsenalLayoutAuthoringStand stand, string action) =>
            !HasBakeableStagedChanges(stand) || EditorUtility.DisplayDialog("Незапечённые позы",
                "На стенде незапечённые позы " + stand.Slot.Weapon.WeaponId + ". " + action + " заменит их сохранённой раскладкой. " +
                "Запеките их, если хотите сохранить.", "Заменить рабочие позы", "Отмена");

        /// <summary>Пересобирает слот текущего ствола из его действующей раскладки.</summary>
        public static string RestorePrepared(ArsenalLayoutAuthoringStand stand, bool discardStaged)
        {
            if (stand.Slot == null || stand.Slot.Weapon == null) throw new InvalidOperationException("Стенд пуст: откройте ствол из окна «Настройка оружия».");
            return OpenPrepared(stand, stand.Slot.Weapon, stand.Slot.Zone, discardStaged);
        }

        /// <summary>
        ///     Строит на стенде только <paramref name="weapon" /> в слоте вида <paramref name="kind" /> по действующей
        ///     раскладке. Прежний слот удаляется; его незапечённые позы требуют <paramref name="discardStaged" />.
        ///     Ассеты не создаются и не меняются; объекты слота не сохраняются в сцену.
        /// </summary>
        public static string OpenPrepared(ArsenalLayoutAuthoringStand stand, WeaponInfo weapon, ArsenalPresentationZone kind, bool discardStaged)
        {
            ValidateOwnership(stand);
            if (weapon == null || weapon.WeaponPrefab == null || weapon.MagazinePrefab == null)
                throw new InvalidOperationException("Нет оружия или его префабов оружия/магазина.");
            if (!discardStaged && HasBakeableStagedChanges(stand)) throw new InvalidOperationException("Есть незапечённые staged позы; смена ствола требует явного решения.");
            RequireCleanInputs(stand, weapon);
            var resolved = Layout(weapon, kind, stand.Style);
            var template = SlotPrefabOf(kind);
            var panelSource = template.transform.Find("PegboardSection");
            var card = template.GetComponentInChildren<ArsenalPriceTag>(true);
            if (panelSource == null || card == null) throw new InvalidOperationException("Префаб слота вида " + kind + " без панели или карточки.");

            ClearProjection(stand);
            var frame = new GameObject(weapon.WeaponId + " / " + kind + " / " + resolved.Layout.name).transform; frame.SetParent(stand.transform, false);
            bool isShelf = kind == ArsenalPresentationZone.Shelf;
            frame.localPosition = new Vector3(0, isShelf ? .35f : 1.05f, isShelf ? .3f : 0);
            var panel = new GameObject("Panel (derived)").transform; panel.SetParent(frame, false);
            panel.SetLocalPositionAndRotation(panelSource.localPosition, panelSource.localRotation); panel.localScale = panelSource.localScale; CopyGeometry(panelSource, panel);
            var staged = new ArsenalLayoutAuthoringStand.SlotHandles { Weapon = weapon, Zone = kind, Frame = frame };
            staged.Item = Handle(frame, "ItemTarget — move/rotate", resolved.ItemTarget); ItemVisual(weapon.WeaponPrefab, staged.Item);
            staged.Magazine = Handle(frame, "MagazineTarget — move/rotate", resolved.MagazineTarget); ItemVisual(weapon.MagazinePrefab, staged.Magazine);
            staged.Card = Handle(frame, "CardTarget — move/rotate", resolved.CardTarget);
            CopyGeometry(card.transform, staged.Card);
            foreach (var support in resolved.Supports)
            {
                var handle = Handle(frame, support.Role + " support — move/rotate", support.SlotPose);
                CopyGeometry(stand.Style.SupportModule.transform, handle);
                staged.Supports.Add(new ArsenalLayoutAuthoringStand.SupportHandle { Role = support.Role, AnchorKind = support.AnchorKind, Handle = handle });
            }
            // Коробка приёма: Edit Collider меняет размер, Move/Rotate — центр и поворот. Не задана — куб у позы оружия.
            var zone = resolved.PlaceZone.IsSet ? resolved.PlaceZone
                : new ArsenalPlaceZone { Center = resolved.ItemTarget.Position, Size = Vector3.one * .2f };
            staged.PlaceZone = Handle(frame, "PlaceZone — move/rotate, Edit Collider", new ArsenalPresentationPose(zone.Center, zone.Rotation));
            var box = staged.PlaceZone.gameObject.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = zone.Size; box.enabled = false;
            // Проекция временная: сохранение сцены стенда не пишет слот, в Git остаётся только carrier.
            foreach (var part in frame.GetComponentsInChildren<Transform>(true)) part.gameObject.hideFlags |= HideFlags.DontSaveInEditor;
            WriteSlot(stand, staged); WriteBaselines(stand);
            return "Стенд показывает " + weapon.WeaponId + " в слоте " + kind + " (раскладка " + resolved.Layout.name + ").";
        }

        /// <summary>Пустой стенд: удаляет слот, построенный этим стендом. Состояние, которое хранит сцена в Git.</summary>
        public static void ClearPrepared(ArsenalLayoutAuthoringStand stand)
        {
            ValidateOwnership(stand);
            ClearProjection(stand);
            WriteSlot(stand, new ArsenalLayoutAuthoringStand.SlotHandles());
            WriteBaselines(stand);
        }
        // Только созданный этим стендом projection root, не чужие siblings.
        private static void ClearProjection(ArsenalLayoutAuthoringStand stand)
        {
            if (stand.Slot != null && stand.Slot.Frame != null && stand.Slot.Frame.parent == stand.transform) Object.DestroyImmediate(stand.Slot.Frame.gameObject);
        }

        private static Transform Handle(Transform frame, string name, ArsenalPresentationPose pose)
        {
            var handle = new GameObject(name).transform; handle.SetParent(frame, false);
            handle.SetLocalPositionAndRotation(pose.Position, pose.Rotation); return handle;
        }
        private static void CopyGeometry(Transform source, Transform target)
        {
            var text = source.GetComponent<TextMeshPro>();
            var mesh = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<MeshRenderer>();
            if (text != null)
            {
                // Dynamic TMP mesh не сохраняется как borrowed object: собственный text пересоздаёт её после scene load.
                var copy = target.gameObject.AddComponent<TextMeshPro>();
                copy.font = text.font; copy.fontSharedMaterial = text.fontSharedMaterial; copy.text = text.text; copy.color = text.color;
                copy.alignment = text.alignment; copy.rectTransform.sizeDelta = text.rectTransform.sizeDelta;
                copy.textWrappingMode = text.textWrappingMode; copy.overflowMode = text.overflowMode;
                copy.enableAutoSizing = text.enableAutoSizing; copy.fontSizeMin = text.fontSizeMin; copy.fontSizeMax = text.fontSizeMax; copy.fontSize = text.fontSize;
                copy.ForceMeshUpdate(true, true);
            }
            else if (mesh != null && mesh.sharedMesh != null && renderer != null)
            {
                target.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                var copy = target.gameObject.AddComponent<MeshRenderer>(); copy.sharedMaterials = renderer.sharedMaterials; copy.enabled = renderer.enabled;
            }
            foreach (Transform child in source)
            {
                if (child.name == "GrabHighlight") continue;
                var copy = (child.GetComponent<TextMeshPro>() != null ? new GameObject(child.name, typeof(RectTransform)) : new GameObject(child.name)).transform; copy.SetParent(target, false);
                copy.SetLocalPositionAndRotation(child.localPosition, child.localRotation); copy.localScale = child.localScale;
                CopyGeometry(child, copy); copy.gameObject.SetActive(child.gameObject.activeSelf);
                copy.gameObject.hideFlags |= HideFlags.NotEditable;
            }
        }
        private static void ItemVisual(GameObject prefab, Transform handle)
        {
            var visual = new GameObject("Visual (derived DropAlign)").transform; visual.SetParent(handle, false);
            var pose = ArsenalPresentationApplicator.RootPose(prefab, handle.position, handle.rotation);
            visual.SetPositionAndRotation(pose.Position, pose.Rotation); visual.localScale = prefab.transform.localScale;
            CopyGeometry(prefab.transform, visual);
            visual.gameObject.hideFlags |= HideFlags.NotEditable;
        }
    }

    [CustomEditor(typeof(ArsenalLayoutAuthoringStand))]
    public sealed class ArsenalLayoutAuthoringStandEditor : UnityEditor.Editor
    {
        private string report;
        public override void OnInspectorGUI()
        {
            var stand = (ArsenalLayoutAuthoringStand)target;
            EditorGUILayout.ObjectField("Стиль арсенала (вид опор)", stand.Style, typeof(ArsenalPresentationStyle), false);
            if (!stand.HasSlot)
            {
                EditorGUILayout.HelpBox("Стенд пуст. Ствол открывается из окна «Настройка оружия» → «Раскладка в слоте»: стенд строит " +
                                        "только его слот.", MessageType.Info);
                if (GUILayout.Button("Открыть «Настройка оружия»")) WeaponSetupWindow.Open(WeaponSetupWindow.Tab.SlotLayout, stand.Slot?.Weapon);
                return;
            }
            var slot = stand.Slot;
            EditorGUILayout.ObjectField("Оружие", slot.Weapon, typeof(WeaponInfo), false);
            EditorGUILayout.LabelField("Вид слота", slot.Zone.ToString());
            EditorGUILayout.HelpBox("Выберите ручку и двигайте Move/Rotate; размер коробки приёма — Edit Collider. Масштаб ручек не меняйте. " +
                                    "Bake пишет в действующую раскладку слота (своя у оружия или умолчание слота); Restore читает её.", MessageType.Info);
            ArsenalSlotLayout layout = null;
            try { layout = ArsenalPresentationResolver.Resolve(slot.Weapon, slot.Zone, stand.Style, null).Layout; } catch (InvalidOperationException) { }
            if (layout == null)
            {
                foreach (var row in AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab")?.GetComponentsInChildren<ArsenalSlotRow>(true) ?? Array.Empty<ArsenalSlotRow>())
                    if (row.Zone == slot.Zone && row.SlotPrefab != null) { layout = row.SlotPrefab.DefaultLayout; break; }
            }
            if (layout != null)
            {
                EditorGUILayout.ObjectField("Действующая раскладка", layout, typeof(ArsenalSlotLayout), false);
                EditorGUILayout.HelpBox("Используется: " + ArsenalLayoutAuthoring.Usage(layout) + ". Bake меняет её для всех.", MessageType.None);
            }
            foreach (var pair in new[] { new { Name = "Оружие", Handle = slot.Item }, new { Name = "Магазин", Handle = slot.Magazine },
                         new { Name = "Карточка", Handle = slot.Card }, new { Name = "Коробка приёма", Handle = slot.PlaceZone } })
                if (pair.Handle != null && GUILayout.Button("Выбрать: " + pair.Name)) { Selection.activeGameObject = pair.Handle.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
            foreach (var support in slot.Supports)
                if (support.Handle != null && GUILayout.Button("Выбрать опору: " + support.Role)) { Selection.activeGameObject = support.Handle.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
            if (GUILayout.Button("Запечь → действующая раскладка")) Run(stand, () => ArsenalLayoutAuthoring.BakePrepared(stand));
            if (GUILayout.Button("Отдельная раскладка для этого ствола")) Run(stand, () => ArsenalLayoutAuthoring.SeparateLayoutPrepared(stand));
            if (GUILayout.Button("Восстановить из раскладки") && ArsenalLayoutAuthoring.ConfirmRebuild(stand, "Restore"))
                Run(stand, () => ArsenalLayoutAuthoring.RestorePrepared(stand, true));
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, MessageType.Info);
        }
        private void Run(ArsenalLayoutAuthoringStand stand, Func<string> action)
        {
            try
            {
                using (var lease = ArsenalEditorActions.AcquireAuthoringLease("Раскладка слота: bake/restore", stand))
                { lease.RequireActive(); report = action(); }
            }
            catch (Exception exception) { report = exception.Message; }
        }
    }
}
