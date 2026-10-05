using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using Object = UnityEngine.Object;
using TMPro;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Явный bake/restore staging-проекции; source для генератора остаётся только Style.</summary>
    public static class ArsenalLayoutAuthoring
    {
        private const string DemoPath = "Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab";
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
        public static string SourceFingerprint(ArsenalLayoutAuthoringStand stand)
        {
            string entries = string.Join("|", stand.Preset.Entries.Select(e => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(e.Weapon)) + ":" + e.Zone + ":" +
                AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(e.Weapon))));
            return Hash(entries + EditorJsonUtility.ToJson(stand.Style) + AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(stand.Style)));
        }
        private static void RequireCleanInputs(ArsenalLayoutAuthoringStand stand)
        {
            var paths = stand.Preset.Entries.Select(e => AssetDatabase.GetAssetPath(e.Weapon)).Concat(new[] {
                AssetDatabase.GetAssetPath(stand.Style), AssetDatabase.GetAssetPath(stand.Preset), AssetDatabase.GetAssetPath(stand.Style.SupportModule),
                AssetDatabase.GetAssetPath(stand.Style.ReturnReadyMaterial), DemoPath });
            if (stand.Style.SupportModule != null) paths = paths.Concat(stand.Style.SupportModule.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Select(AssetDatabase.GetAssetPath));
            ArsenalEditorActions.RequireCleanAssets(paths);
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
        private static bool Same(ArsenalPresentationPose a, ArsenalPresentationPose b) =>
            (a.Position - b.Position).sqrMagnitude < 1e-12f && Quaternion.Angle(a.Rotation, b.Rotation) < .001f;
        private static string SlotFingerprint(ArsenalLayoutAuthoringStand.SlotHandles slot)
        {
            var text = new StringBuilder();
            text.Append(slot.Weapon.WeaponId).Append(slot.Zone);
            foreach (var handle in new[] { slot.Item, slot.Magazine, slot.Card }.Concat(slot.Supports.Select(s => s.Handle)))
            {
                var pose = Pose(handle, slot.Frame);
                text.Append(JsonUtility.ToJson(pose));
            }
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
            ArsenalPresentationResolver.Validate(stand.Style);
            if (stand.Preset.Entries.Count != 20 || stand.Slots.Count != 20 || stand.Slots.Select(s => s.Weapon).Distinct().Count() != 20)
                throw new InvalidOperationException("Нужны 20 уникальных canonical entries/handles.");
            foreach (var slot in stand.Slots)
            {
                if (!stand.Preset.Entries.Any(e => e.Weapon == slot.Weapon && e.Zone == slot.Zone) || slot.Frame == null || slot.Frame.parent != stand.transform)
                    throw new InvalidOperationException("Staged key/frame не соответствует источнику.");
                if ((slot.Frame.localScale - Vector3.one).sqrMagnitude > 1e-12f) throw new InvalidOperationException("Slot frame не масштабируется.");
                foreach (var handle in new[] { slot.Item, slot.Magazine, slot.Card }.Concat(slot.Supports.Select(s => s.Handle))) Pose(handle, slot.Frame);
                if (slot.Zone == ArsenalPresentationZone.Shelf && slot.Supports.Count != 0)
                    throw new InvalidOperationException("Shelf не использует крючки.");
                if (slot.Supports.Select(s => s.Role).Distinct().Count() != slot.Supports.Count) throw new InvalidOperationException("Дубли support roles.");
            }
        }
        private static void SetPose(SerializedProperty property, ArsenalPresentationPose value)
        {
            property.FindPropertyRelative("Position").vector3Value = value.Position;
            property.FindPropertyRelative("EulerAngles").vector3Value = value.EulerAngles;
        }
        private static SerializedProperty Exception(SerializedObject input, ArsenalLayoutAuthoringStand.SlotHandles slot)
        {
            var list = input.FindProperty("_exceptions");
            for (int i = 0; i < list.arraySize; i++)
            {
                var row = list.GetArrayElementAtIndex(i);
                if (row.FindPropertyRelative("Weapon").objectReferenceValue == slot.Weapon && row.FindPropertyRelative("Zone").enumValueIndex == (int)slot.Zone) return row;
            }
            int index = list.arraySize; list.arraySize++;
            var entry = list.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("Weapon").objectReferenceValue = slot.Weapon;
            entry.FindPropertyRelative("Zone").enumValueIndex = (int)slot.Zone;
            foreach (var key in new[] { "OverrideItem", "OverrideMagazine", "OverrideCard", "OverrideSupports" }) entry.FindPropertyRelative(key).boolValue = false;
            entry.FindPropertyRelative("Supports").arraySize = 0;
            return entry;
        }
        public static string BakePrepared(ArsenalLayoutAuthoringStand stand, int selected = -1)
        {
            ValidateStand(stand);
            RequireCleanInputs(stand);
            if (SourceFingerprint(stand) != stand.SourceBaseline) throw new InvalidOperationException("Style/ассортимент изменился после Restore; staged изменения сначала сопоставьте с новым источником.");
            if (selected < -1 || selected >= stand.Slots.Count) throw new ArgumentOutOfRangeException(nameof(selected));
            var draft = Object.Instantiate(stand.Style);
            try
            {
                var input = new SerializedObject(draft);
                foreach (var slot in stand.Slots.Where((s, index) => selected < 0 || index == selected))
                {
                    var baseline = ArsenalPresentationResolver.Resolve(slot.Weapon, slot.Zone, stand.Style);
                    var item = Pose(slot.Item, slot.Frame); var magazine = Pose(slot.Magazine, slot.Frame); var card = Pose(slot.Card, slot.Frame);
                    bool supportsChanged = baseline.Supports.Count != slot.Supports.Count;
                    for (int i = 0; !supportsChanged && i < slot.Supports.Count; i++) supportsChanged =
                        baseline.Supports[i].Role != slot.Supports[i].Role || baseline.Supports[i].AnchorKind != slot.Supports[i].AnchorKind || !Same(baseline.Supports[i].SlotPose, Pose(slot.Supports[i].Handle, slot.Frame));
                    if (Same(item, baseline.ItemTarget) && Same(magazine, baseline.MagazineTarget) && Same(card, baseline.CardTarget) && !supportsChanged) continue;
                    var entry = Exception(input, slot);
                    if (!Same(item, baseline.ItemTarget)) { entry.FindPropertyRelative("OverrideItem").boolValue = true; SetPose(entry.FindPropertyRelative("ItemTarget"), item); }
                    if (!Same(magazine, baseline.MagazineTarget)) { entry.FindPropertyRelative("OverrideMagazine").boolValue = true; SetPose(entry.FindPropertyRelative("MagazineTarget"), magazine); }
                    if (!Same(card, baseline.CardTarget)) { entry.FindPropertyRelative("OverrideCard").boolValue = true; SetPose(entry.FindPropertyRelative("CardTarget"), card); entry.FindPropertyRelative("CardSize").vector2Value = baseline.CardSize; entry.FindPropertyRelative("CardFontSize").floatValue = baseline.CardFontSize; }
                    if (supportsChanged)
                    {
                        entry.FindPropertyRelative("OverrideSupports").boolValue = true;
                        var supports = entry.FindPropertyRelative("Supports"); supports.arraySize = slot.Supports.Count;
                        for (int i = 0; i < slot.Supports.Count; i++)
                        {
                            var target = supports.GetArrayElementAtIndex(i); var handle = slot.Supports[i];
                            target.FindPropertyRelative("Role").stringValue = handle.Role;
                            target.FindPropertyRelative("AnchorKind").enumValueIndex = (int)handle.AnchorKind;
                            SetPose(target.FindPropertyRelative("SlotPose"), Pose(handle.Handle, slot.Frame));
                        }
                    }
                }
                input.ApplyModifiedPropertiesWithoutUndo(); draft.name = stand.Style.name;
                ArsenalPresentationResolver.Validate(draft);
                string desired = EditorJsonUtility.ToJson(draft);
                if (desired == EditorJsonUtility.ToJson(stand.Style)) return "Нет изменений canonical Style.";
                Undo.RecordObject(stand.Style, "Bake Arsenal Canonical Layout");
                EditorJsonUtility.FromJsonOverwrite(desired, stand.Style);
                EditorUtility.SetDirty(stand.Style); AssetDatabase.SaveAssetIfDirty(stand.Style);
                // Выбранный bake не уничтожает unsaved edits других слотов.
                WriteBaselines(stand, selected);
                EditorUtility.SetDirty(stand);
                return "Сохранены только exact canonical Style poses; другие staged handles оставлены.";
            }
            finally { Object.DestroyImmediate(draft); }
        }
        public static string BakeCardDefaultPrepared(ArsenalLayoutAuthoringStand stand, int selected)
        {
            ValidateStand(stand);
            RequireCleanInputs(stand);
            if (SourceFingerprint(stand) != stand.SourceBaseline) throw new InvalidOperationException("Canonical source изменился после Restore.");
            var slot = stand.Slots[selected];
            // Общая карточка — явное действие; прочие staged edits не уничтожаются.
            var target = Pose(slot.Card, slot.Frame);
            var input = new SerializedObject(stand.Style); var zones = input.FindProperty("_zones");
            SerializedProperty zone = null;
            for (int i = 0; i < zones.arraySize; i++) if (zones.GetArrayElementAtIndex(i).FindPropertyRelative("Zone").enumValueIndex == (int)slot.Zone) zone = zones.GetArrayElementAtIndex(i);
            if (zone == null) throw new InvalidOperationException("Нет canonical zone default.");
            var previous = ArsenalPresentationResolver.Resolve(slot.Weapon, slot.Zone, stand.Style);
            var already = new ArsenalPresentationPose(zone.FindPropertyRelative("CardTarget").FindPropertyRelative("Position").vector3Value,
                Quaternion.Euler(zone.FindPropertyRelative("CardTarget").FindPropertyRelative("EulerAngles").vector3Value));
            if (Same(target, already)) return "Zone card default не изменился.";
            // Перед записью требуем, чтобы кроме выбранной карточки ничего не было staged.
            foreach (var other in stand.Slots)
            {
                string current = SlotFingerprint(other);
                if (other == slot)
                {
                    var stagedPosition = other.Card.localPosition; var stagedRotation = other.Card.localRotation;
                    try { other.Card.SetLocalPositionAndRotation(previous.CardTarget.Position, previous.CardTarget.Rotation); current = SlotFingerprint(other); }
                    finally { other.Card.SetLocalPositionAndRotation(stagedPosition, stagedRotation); }
                }
                if (current != other.CommittedBaseline) throw new InvalidOperationException("Сначала запеките прочие staged edits; общий default затем безопасно обновит все карточки зоны.");
            }
            Undo.RecordObject(stand.Style, "Bake Arsenal Zone Card Default"); SetPose(zone.FindPropertyRelative("CardTarget"), target);
            // Выбранная карточка становится общей, её индивидуальный override больше не нужен.
            var exceptions = input.FindProperty("_exceptions");
            for (int i = 0; i < exceptions.arraySize; i++)
            {
                var entry = exceptions.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("Weapon").objectReferenceValue == slot.Weapon && entry.FindPropertyRelative("Zone").enumValueIndex == (int)slot.Zone)
                    entry.FindPropertyRelative("OverrideCard").boolValue = false;
            }
            input.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(stand.Style); AssetDatabase.SaveAssetIfDirty(stand.Style);
            RestorePrepared(stand, true); return "Общий CardTarget зоны сохранён; exact исключения остальных карточек сохранены.";
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
                if (child.name == "GrabHighlight" || child.name == "__ItemPreview__" || child.name == "__MagPreview__") continue;
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
            if (stand.Preset == null || stand.Style == null) throw new InvalidOperationException("Нет canonical sources.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(DemoPath);
            RequireCleanInputs(stand);
            if (source == null) throw new InvalidOperationException("Нет source Demo для render-only panels.");
            var templates = source.GetComponentsInChildren<FirearmSlotController>(true);
            foreach (var entry in stand.Preset.Entries)
            {
                var resolved = ArsenalPresentationResolver.Resolve(entry.Weapon, entry.Zone, stand.Style);
                if (entry.Zone == ArsenalPresentationZone.Shelf && resolved.Supports.Count != 0) throw new InvalidOperationException("Shelf Supports должны быть пусты.");
                var template = templates.Single(s => s.WeaponData == entry.Weapon);
                if (template.transform.Find("PegboardSection") == null || template.GetComponentInChildren<ArsenalPriceTag>(true) == null)
                    throw new InvalidOperationException("Source panel/card неполон.");
            }
            // Только созданные этим стендом projection roots, не чужие siblings.
            foreach (var slot in stand.Slots) if (slot.Frame != null) Object.DestroyImmediate(slot.Frame.gameObject);
            var cache = new List<ArsenalLayoutAuthoringStand.SlotHandles>(); int peg = 0, shelf = 0;
            foreach (var entry in stand.Preset.Entries)
            {
                var resolved = ArsenalPresentationResolver.Resolve(entry.Weapon, entry.Zone, stand.Style);
                var template = templates.Single(s => s.WeaponData == entry.Weapon);
                var frame = new GameObject(entry.Weapon.WeaponId + " / " + entry.Zone).transform; frame.SetParent(stand.transform, false);
                bool isShelf = entry.Zone == ArsenalPresentationZone.Shelf;
                frame.localPosition = new Vector3((isShelf ? shelf++ - 4 : peg++ - 5) * .43f, isShelf ? .35f : 1.05f, isShelf ? .3f : 0);
                var panelSource = template.transform.Find("PegboardSection"); var panel = new GameObject("Panel (derived)").transform; panel.SetParent(frame, false);
                panel.SetLocalPositionAndRotation(panelSource.localPosition, panelSource.localRotation); panel.localScale = panelSource.localScale; CopyGeometry(panelSource, panel);
                var staged = new ArsenalLayoutAuthoringStand.SlotHandles { Weapon = entry.Weapon, Zone = entry.Zone, Frame = frame };
                staged.Item = Handle(frame, "ItemTarget — move/rotate", resolved.ItemTarget); ItemVisual(entry.Weapon.WeaponPrefab, staged.Item);
                staged.Magazine = Handle(frame, "MagazineTarget — move/rotate", resolved.MagazineTarget); ItemVisual(entry.Weapon.MagazinePrefab, staged.Magazine);
                staged.Card = Handle(frame, "CardTarget — move/rotate", resolved.CardTarget);
                var card = template.GetComponentInChildren<ArsenalPriceTag>(true); CopyGeometry(card.transform, staged.Card);
                foreach (var support in resolved.Supports)
                {
                    if (isShelf) throw new InvalidOperationException("Shelf Style должен иметь Supports=[].");
                    var handle = Handle(frame, support.Role + " support — move/rotate", support.SlotPose);
                    CopyGeometry(stand.Style.SupportModule.transform, handle);
                    staged.Supports.Add(new ArsenalLayoutAuthoringStand.SupportHandle { Role = support.Role, AnchorKind = support.AnchorKind, Handle = handle });
                }
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
            EditorGUILayout.ObjectField("Canonical Style", stand.Style, typeof(ArsenalPresentationStyle), false);
            EditorGUILayout.ObjectField("Ассортимент (source)", stand.Preset, typeof(ArsenalPreset), false);
            EditorGUILayout.HelpBox("Выберите ItemTarget/MagazineTarget/CardTarget или Peg support в Hierarchy и двигайте Move/Rotate. Масштаб не меняйте. Явный Bake сохраняет Style; Restore читает его. Shelf без крючков.", MessageType.Info);
            if (stand.Slots.Count == 0) return;
            selected = EditorGUILayout.Popup("Слот", Mathf.Clamp(selected, 0, stand.Slots.Count - 1), stand.Slots.Select(s => s.Weapon.WeaponId + " / " + s.Zone).ToArray());
            foreach (var pair in new[] { new { Name = "Оружие", Handle = stand.Slots[selected].Item }, new { Name = "Магазин", Handle = stand.Slots[selected].Magazine }, new { Name = "Карточка", Handle = stand.Slots[selected].Card } })
                if (GUILayout.Button("Выбрать: " + pair.Name)) { Selection.activeGameObject = pair.Handle.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
            foreach (var support in stand.Slots[selected].Supports)
                if (GUILayout.Button("Выбрать опору: " + support.Role)) { Selection.activeGameObject = support.Handle.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
            if (GUILayout.Button("Запечь выбранный → Style")) Run(stand, () => ArsenalLayoutAuthoring.BakePrepared(stand, selected));
            if (GUILayout.Button("Запечь все 20 → Style")) Run(stand, () => ArsenalLayoutAuthoring.BakePrepared(stand));
            if (GUILayout.Button("Карточка выбранного → общий default зоны")) Run(stand, () => ArsenalLayoutAuthoring.BakeCardDefaultPrepared(stand, selected));
            if (GUILayout.Button("Восстановить из Style"))
            {
                bool discard = !ArsenalLayoutAuthoring.HasStagedChanges(stand) || EditorUtility.DisplayDialog("Незапечённые позы", "Restore заменит рабочие poses сохранённым Style. Запеките их перед Restore, если хотите сохранить.", "Заменить рабочие позы", "Отмена");
                if (discard) Run(stand, () => { ArsenalLayoutAuthoring.RestorePrepared(stand, true); return "Проекция восстановлена из Style."; });
            }
            if (!string.IsNullOrEmpty(report)) EditorGUILayout.HelpBox(report, MessageType.Info);
        }
        private void Run(ArsenalLayoutAuthoringStand stand, Func<string> action)
        {
            try
            {
                using (var lease = ArsenalEditorActions.AcquireAuthoringLease("Явный canonical layout bake/restore", stand))
                { lease.RequireActive(); report = action(); }
            }
            catch (Exception exception) { report = exception.Message; }
        }
    }
}
