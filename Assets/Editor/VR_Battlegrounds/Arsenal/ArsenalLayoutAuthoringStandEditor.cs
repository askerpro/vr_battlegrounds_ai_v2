using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using Object = UnityEngine.Object;
using TMPro;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    ///     Стенд ручной раскладки слота. Restore показывает каждое оружие пресета над панелью слота того вида, в ряд
    ///     которого оно стоит, по действующей раскладке (<see cref="ArsenalSlotLayout" />): своей у оружия или умолчанию
    ///     слота. Ручки: оружие, магазин, карточка, опоры, коробка приёма (поза ручки — центр и поворот, размер — её
    ///     <see cref="BoxCollider" />). Bake записывает ручки в действующий ассет раскладки; его могут разделять
    ///     несколько стволов. «Отдельная раскладка» копирует ассет и назначает копию стволу.
    /// </summary>
    public static class ArsenalLayoutAuthoring
    {
        private const string DemoPath = "Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab";
        public const string LayoutFolder = "Assets/Data/Arsenal/SlotLayouts";

        /// <summary>Первичная сборка собственной fixture под внешней агентской lease; сцена не включается в build.</summary>
        public static ArsenalLayoutAuthoringStand PopulatePrepared(Scene scene, ArsenalPreset preset, ArsenalPresentationStyle style)
        {
            if (!scene.isLoaded || scene.path != ArsenalLayoutAuthoringStand.ScenePath || scene.GetRootGameObjects().Length != 1)
                throw new InvalidOperationException("Не собственная initial authoring fixture.");
            var root = scene.GetRootGameObjects()[0];
            if (root.GetComponentsInChildren<Component>(true).Length != 1 || root.name != "ArsenalLayoutAuthoring — owned staging fixture")
                throw new InvalidOperationException("Initial root уже имеет контент; ничего не заменяем.");
            var stand = root.AddComponent<ArsenalLayoutAuthoringStand>();
            if (stand == null) throw new InvalidOperationException("Data carrier не прикрепился: проверьте runtime assembly boundary.");
            WriteSources(stand, preset, style);
            RestorePrepared(stand, false); ValidateStand(stand);
            var lightObject = new GameObject("Authoring light (owned)"); lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(25, -25, 0);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2.2f;
            return stand;
        }

        // Единственный Editor writer сериализуемого derived cache. Carrier не содержит editor API/setters.
        private static void WriteSources(ArsenalLayoutAuthoringStand stand, ArsenalPreset preset, ArsenalPresentationStyle style)
        {
            var input = new SerializedObject(stand);
            input.FindProperty("_preset").objectReferenceValue = preset;
            input.FindProperty("_style").objectReferenceValue = style;
            input.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void WriteBaselines(ArsenalLayoutAuthoringStand stand, int selected)
        {
            var stamps = stand.Slots.Select((slot, index) => selected < 0 || selected == index ? SlotFingerprint(slot) : slot.CommittedBaseline).ToArray();
            var input = new SerializedObject(stand); var slots = input.FindProperty("_slots");
            for (int i = 0; i < stamps.Length; i++) slots.GetArrayElementAtIndex(i).FindPropertyRelative("CommittedBaseline").stringValue = stamps[i];
            input.FindProperty("_sourceBaseline").stringValue = SourceFingerprint(stand);
            input.FindProperty("_stagedBaseline").stringValue = Hash(string.Join("|", stamps));
            input.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void WriteSlots(ArsenalLayoutAuthoringStand stand, List<ArsenalLayoutAuthoringStand.SlotHandles> cache)
        {
            var input = new SerializedObject(stand); var slots = input.FindProperty("_slots"); slots.arraySize = cache.Count;
            for (int i = 0; i < cache.Count; i++)
            {
                var source = cache[i]; var row = slots.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("Weapon").objectReferenceValue = source.Weapon;
                row.FindPropertyRelative("Zone").enumValueIndex = (int)source.Zone;
                row.FindPropertyRelative("Frame").objectReferenceValue = source.Frame;
                row.FindPropertyRelative("Item").objectReferenceValue = source.Item;
                row.FindPropertyRelative("Magazine").objectReferenceValue = source.Magazine;
                row.FindPropertyRelative("Card").objectReferenceValue = source.Card;
                row.FindPropertyRelative("PlaceZone").objectReferenceValue = source.PlaceZone;
                row.FindPropertyRelative("CommittedBaseline").stringValue = source.CommittedBaseline;
                var supports = row.FindPropertyRelative("Supports"); supports.arraySize = source.Supports.Count;
                for (int j = 0; j < source.Supports.Count; j++)
                {
                    var support = supports.GetArrayElementAtIndex(j); var data = source.Supports[j];
                    support.FindPropertyRelative("Role").stringValue = data.Role;
                    support.FindPropertyRelative("AnchorKind").enumValueIndex = (int)data.AnchorKind;
                    support.FindPropertyRelative("Handle").objectReferenceValue = data.Handle;
                }
            }
            input.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Источники: ряды корпуса лобби, раскладки ─────────────────────

        /// <summary>Ряд корпуса лобби по ключу записи пресета.</summary>
        private static ArsenalSlotRow RowOf(string rowKey)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPath);
            if (source == null) throw new InvalidOperationException("Нет корпуса станции лобби: " + DemoPath);
            var row = source.GetComponentsInChildren<ArsenalSlotRow>(true).FirstOrDefault(r => r.RowKey == rowKey);
            if (row == null || row.SlotPrefab == null) throw new InvalidOperationException("В корпусе лобби нет ряда «" + rowKey + "» с префабом слота.");
            return row;
        }
        /// <summary>Префаб слота вида: префаб первого ряда этого вида в корпусе лобби.</summary>
        private static ArsenalSlotController SlotPrefabOf(ArsenalPresentationZone kind)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPath);
            var row = source != null ? source.GetComponentsInChildren<ArsenalSlotRow>(true).FirstOrDefault(r => r.Zone == kind && r.SlotPrefab != null) : null;
            if (row == null) throw new InvalidOperationException("В корпусе лобби нет ряда слотов вида " + kind + ".");
            return row.SlotPrefab;
        }
        private static ArsenalPresentationZone KindOf(ArsenalPreset.Entry entry) => RowOf(entry.Row).Zone;
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
            string entries = string.Join("|", stand.Preset.Entries.Select(e => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.Weapon)) + ":" + e.Row + ":" +
                AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(e.Weapon))));
            string layouts = string.Join("|", stand.Slots.Select(s => Layout(s.Weapon, s.Zone, stand.Style).Layout)
                .Distinct().Select(l => EditorJsonUtility.ToJson(l)));
            return Hash(entries + layouts + EditorJsonUtility.ToJson(stand.Style) + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(stand.Style)));
        }
        private static void RequireCleanInputs(ArsenalLayoutAuthoringStand stand)
        {
            var paths = stand.Preset.Entries.Select(e => AssetDatabase.GetAssetPath(e.Weapon)).Concat(new[] {
                AssetDatabase.GetAssetPath(stand.Style), AssetDatabase.GetAssetPath(stand.Preset), AssetDatabase.GetAssetPath(stand.Style.SupportModule),
                AssetDatabase.GetAssetPath(stand.Style.ReturnReadyMaterial), DemoPath });
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
        public static string StagedFingerprint(ArsenalLayoutAuthoringStand stand) => Hash(string.Join("|", stand.Slots.Select(SlotFingerprint)));
        public static bool HasStagedChanges(ArsenalLayoutAuthoringStand stand) => StagedFingerprint(stand) != stand.StagedBaseline;
        public static void ValidateStand(ArsenalLayoutAuthoringStand stand)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Authoring доступен только в свободном EditMode.");
            if (stand == null || stand.Owner != ArsenalLayoutAuthoringStand.OwnerId || stand.gameObject.scene.path != ArsenalLayoutAuthoringStand.ScenePath ||
                stand.transform.parent != null || !stand.gameObject.scene.isLoaded || stand.Preset == null || stand.Style == null)
                throw new InvalidOperationException("Не собственный persistent authoring стенд.");
            if (stand.gameObject.scene.GetRootGameObjects().Length != 1 ||
                stand.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ArsenalLayoutAuthoringStand>(true)).Count() != 1)
                throw new InvalidOperationException("В сцене есть посторонние корни/дубли marker; запись запрещена.");
            if (EditorBuildSettings.scenes.Any(s => s.path == ArsenalLayoutAuthoringStand.ScenePath))
                throw new InvalidOperationException("Authoring сцена не должна входить в Build Settings.");
            if ((stand.transform.lossyScale - Vector3.one).sqrMagnitude > 1e-12f)
                throw new InvalidOperationException("Authoring root не масштабируется: физические размеры предметов сохраняются.");
            ArsenalPresentationResolver.ValidateStyle(stand.Style);
            int count = stand.Preset.Entries.Count;
            if (stand.Slots.Count != count || stand.Slots.Select(s => s.Weapon).Distinct().Count() != count)
                throw new InvalidOperationException("Ручки стенда не соответствуют записям пресета: сначала Restore.");
            foreach (var slot in stand.Slots)
            {
                if (!stand.Preset.Entries.Any(e => e.Weapon == slot.Weapon && KindOf(e) == slot.Zone) || slot.Frame == null || slot.Frame.parent != stand.transform)
                    throw new InvalidOperationException("Ручки слота не соответствуют записи пресета.");
                if ((slot.Frame.localScale - Vector3.one).sqrMagnitude > 1e-12f) throw new InvalidOperationException("Slot frame не масштабируется.");
                foreach (var handle in new[] { slot.Item, slot.Magazine, slot.Card }.Concat(slot.Supports.Select(s => s.Handle))) Pose(handle, slot.Frame);
                Zone(slot.PlaceZone, slot.Frame);
                if (slot.Supports.Select(s => s.Role).Distinct().Count() != slot.Supports.Count) throw new InvalidOperationException("Дубли support roles.");
            }
        }
        private static void SetPose(SerializedProperty property, ArsenalPresentationPose value)
        {
            property.FindPropertyRelative("Position").vector3Value = value.Position;
            property.FindPropertyRelative("EulerAngles").vector3Value = value.EulerAngles;
        }

        // ── Bake ─────────────────────────────────────────────────────────

        /// <summary>Ручки выбранного слота (или всех) — в действующие ассеты раскладки.</summary>
        public static string BakePrepared(ArsenalLayoutAuthoringStand stand, int selected = -1)
        {
            ValidateStand(stand);
            RequireCleanInputs(stand);
            if (SourceFingerprint(stand) != stand.SourceBaseline) throw new InvalidOperationException("Раскладки или ассортимент изменились после Restore; staged изменения сначала сопоставьте с новым источником.");
            if (selected < -1 || selected >= stand.Slots.Count) throw new ArgumentOutOfRangeException(nameof(selected));
            var targets = stand.Slots.Where((s, index) => selected < 0 || index == selected).ToArray();
            // Один ассет — одни ручки: разные позы для общего ассета в одном bake — неоднозначно.
            foreach (var group in targets.GroupBy(s => Layout(s.Weapon, s.Zone, stand.Style).Layout))
                if (group.Select(SlotFingerprintContent).Distinct().Count() > 1)
                    throw new InvalidOperationException("У стволов с общей раскладкой " + group.Key.name + " разные позы на стенде: запеките по одному или сделайте отдельную раскладку.");
            var changed = new List<string>();
            foreach (var slot in targets)
            {
                var layout = Layout(slot.Weapon, slot.Zone, stand.Style).Layout;
                if (WriteLayout(layout, slot)) changed.Add(layout.name);
            }
            if (changed.Count == 0) return "Нет изменений раскладки.";
            WriteBaselines(stand, selected);
            EditorUtility.SetDirty(stand);
            return "Сохранены раскладки: " + string.Join(", ", changed.Distinct()) + ".";
        }
        private static string SlotFingerprintContent(ArsenalLayoutAuthoringStand.SlotHandles slot)
        {
            var copy = new ArsenalLayoutAuthoringStand.SlotHandles { Weapon = slot.Weapon, Zone = slot.Zone, Frame = slot.Frame, Item = slot.Item,
                Magazine = slot.Magazine, Card = slot.Card, PlaceZone = slot.PlaceZone, Supports = slot.Supports };
            var text = new StringBuilder();
            foreach (var handle in new[] { copy.Item, copy.Magazine, copy.Card }.Concat(copy.Supports.Select(s => s.Handle)))
                text.Append(JsonUtility.ToJson(Pose(handle, copy.Frame)));
            text.Append(JsonUtility.ToJson(Zone(copy.PlaceZone, copy.Frame)));
            return Hash(text.ToString());
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
            zoneProperty.FindPropertyRelative("Center").vector3Value = zone.Center;
            zoneProperty.FindPropertyRelative("Size").vector3Value = zone.Size;
            zoneProperty.FindPropertyRelative("EulerAngles").vector3Value = zone.EulerAngles;
            Undo.RecordObject(layout, "Bake Arsenal Slot Layout");
            so.ApplyModifiedProperties();
            if (EditorJsonUtility.ToJson(layout) == before) return false;
            EditorUtility.SetDirty(layout); AssetDatabase.SaveAssetIfDirty(layout);
            return true;
        }

        /// <summary>Копия действующей раскладки выбранного ствола — его собственная; затем его ручки пишутся в неё.</summary>
        public static string SeparateLayoutPrepared(ArsenalLayoutAuthoringStand stand, int selected)
        {
            ValidateStand(stand);
            RequireCleanInputs(stand);
            var slot = stand.Slots[selected];
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
            WriteBaselines(stand, selected);
            return "Создана отдельная раскладка " + path + " для " + slot.Weapon.WeaponId + ".";
        }

        // ── Restore ──────────────────────────────────────────────────────

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
        public static void RestorePrepared(ArsenalLayoutAuthoringStand stand, bool discardStaged)
        {
            if (stand.Slots.Count > 0) { ValidateStand(stand); if (HasStagedChanges(stand) && !discardStaged) throw new InvalidOperationException("Есть незапечённые staged позы; Restore требует явного решения."); }
            if (stand.Preset == null || stand.Style == null) throw new InvalidOperationException("Нет пресета или стиля арсенала стенда.");
            RequireCleanInputs(stand);
            foreach (var entry in stand.Preset.Entries)
            {
                Layout(entry.Weapon, KindOf(entry), stand.Style);
                var template = RowOf(entry.Row).SlotPrefab;
                if (template.transform.Find("PegboardSection") == null || template.GetComponentInChildren<ArsenalPriceTag>(true) == null)
                    throw new InvalidOperationException("Префаб слота ряда «" + entry.Row + "» без панели или карточки.");
            }
            // Только созданные этим стендом projection roots, не чужие siblings.
            foreach (var slot in stand.Slots) if (slot.Frame != null) Object.DestroyImmediate(slot.Frame.gameObject);
            var cache = new List<ArsenalLayoutAuthoringStand.SlotHandles>(); int peg = 0, shelf = 0;
            foreach (var entry in stand.Preset.Entries)
            {
                var kind = KindOf(entry);
                var resolved = Layout(entry.Weapon, kind, stand.Style);
                var template = RowOf(entry.Row).SlotPrefab;
                var frame = new GameObject(entry.Weapon.WeaponId + " / " + kind + " / " + resolved.Layout.name).transform; frame.SetParent(stand.transform, false);
                bool isShelf = kind == ArsenalPresentationZone.Shelf;
                frame.localPosition = new Vector3((isShelf ? shelf++ - 4 : peg++ - 5) * .43f, isShelf ? .35f : 1.05f, isShelf ? .3f : 0);
                var panelSource = template.transform.Find("PegboardSection"); var panel = new GameObject("Panel (derived)").transform; panel.SetParent(frame, false);
                panel.SetLocalPositionAndRotation(panelSource.localPosition, panelSource.localRotation); panel.localScale = panelSource.localScale; CopyGeometry(panelSource, panel);
                var staged = new ArsenalLayoutAuthoringStand.SlotHandles { Weapon = entry.Weapon, Zone = kind, Frame = frame };
                staged.Item = Handle(frame, "ItemTarget — move/rotate", resolved.ItemTarget); ItemVisual(entry.Weapon.WeaponPrefab, staged.Item);
                staged.Magazine = Handle(frame, "MagazineTarget — move/rotate", resolved.MagazineTarget); ItemVisual(entry.Weapon.MagazinePrefab, staged.Magazine);
                staged.Card = Handle(frame, "CardTarget — move/rotate", resolved.CardTarget);
                var card = template.GetComponentInChildren<ArsenalPriceTag>(true); CopyGeometry(card.transform, staged.Card);
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
                staged.CommittedBaseline = SlotFingerprint(staged); cache.Add(staged);
            }
            WriteSlots(stand, cache); WriteBaselines(stand, -1); EditorUtility.SetDirty(stand);
        }
    }

    [CustomEditor(typeof(ArsenalLayoutAuthoringStand))]
    public sealed class ArsenalLayoutAuthoringStandEditor : UnityEditor.Editor
    {
        private int selected;
        private string report;
        public override void OnInspectorGUI()
        {
            var stand = (ArsenalLayoutAuthoringStand)target;
            EditorGUILayout.ObjectField("Стиль арсенала (вид опор)", stand.Style, typeof(ArsenalPresentationStyle), false);
            EditorGUILayout.ObjectField("Ассортимент (source)", stand.Preset, typeof(ArsenalPreset), false);
            EditorGUILayout.HelpBox("Выберите ручку в Hierarchy и двигайте Move/Rotate; размер коробки приёма — Edit Collider. Масштаб ручек не меняйте. " +
                                    "Bake пишет в действующую раскладку слота (своя у оружия или умолчание слота); Restore читает её.", MessageType.Info);
            if (stand.Slots.Count == 0) return;
            selected = EditorGUILayout.Popup("Слот", Mathf.Clamp(selected, 0, stand.Slots.Count - 1), stand.Slots.Select(s => s.Weapon.WeaponId + " / " + s.Zone).ToArray());
            var slot = stand.Slots[selected];
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
                if (GUILayout.Button("Выбрать: " + pair.Name)) { Selection.activeGameObject = pair.Handle.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
            foreach (var support in slot.Supports)
                if (GUILayout.Button("Выбрать опору: " + support.Role)) { Selection.activeGameObject = support.Handle.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
            if (GUILayout.Button("Запечь выбранный → его раскладка")) Run(stand, () => ArsenalLayoutAuthoring.BakePrepared(stand, selected));
            if (GUILayout.Button("Запечь все → их раскладки")) Run(stand, () => ArsenalLayoutAuthoring.BakePrepared(stand));
            if (GUILayout.Button("Отдельная раскладка для выбранного ствола")) Run(stand, () => ArsenalLayoutAuthoring.SeparateLayoutPrepared(stand, selected));
            if (GUILayout.Button("Восстановить из раскладок"))
            {
                bool discard = !ArsenalLayoutAuthoring.HasStagedChanges(stand) || EditorUtility.DisplayDialog("Незапечённые позы", "Restore заменит рабочие позы сохранёнными раскладками. Запеките их перед Restore, если хотите сохранить.", "Заменить рабочие позы", "Отмена");
                if (discard) Run(stand, () => { ArsenalLayoutAuthoring.RestorePrepared(stand, true); return "Проекция восстановлена из раскладок."; });
            }
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
