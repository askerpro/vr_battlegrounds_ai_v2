using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    /// Base custom inspector for <see cref="ArsenalSlotController"/> subclasses.
    /// Provides: WeaponRegistry dropdown, offset editing with Save/Reset.
    /// Превью содержимого слота создаёт и восстанавливает <see cref="ArsenalSlotPreview"/>,
    /// инспектор лишь пересоздаёт его при смене предмета.
    ///
    /// Subclasses override <see cref="DrawExtraFields"/> to add their own UI.
    /// </summary>
    public abstract class ArsenalSlotEditorBase : UnityEditor.Editor
    {
        private const string RegistrySearchFilter = "t:WeaponRegistry";

        private SerializedProperty _weaponInfoProp;
        private SerializedProperty _itemAnchorProp;
        private SerializedProperty _slotLightProp;
        private SerializedProperty _availableColorProp;
        private SerializedProperty _unavailableColorProp;
        private SerializedProperty _takenColorProp;

        private WeaponRegistry _cachedRegistry;

        private GameObject _previewItem;

        private bool _isEditingOffsets;
        private string _offsetWriteError;

        // ── Lifecycle ──────────────────────────────────────────

        protected virtual void OnEnable()
        {
            _weaponInfoProp       = serializedObject.FindProperty("_weaponInfo");
            _itemAnchorProp       = serializedObject.FindProperty("_itemAnchor");
            _slotLightProp        = serializedObject.FindProperty("_slotLight");
            _availableColorProp   = serializedObject.FindProperty("_availableColor");
            _unavailableColorProp = serializedObject.FindProperty("_unavailableColor");
            _takenColorProp       = serializedObject.FindProperty("_takenColor");

            FindRegistry();

            _previewItem = ArsenalSlotPreview.Ensure(target as ArsenalSlotController);
        }

        protected virtual void OnDisable()
        {
            // Preview persists across selection changes
        }

        // ── Extension Points ───────────────────────────────────

        /// <summary>Override to draw extra inspector fields (e.g. mag anchor).</summary>
        protected virtual void DrawExtraFields() { }

        // ── Inspector GUI ──────────────────────────────────────

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            string presentationError = ArsenalSlotPreview.PresentationError(target as ArsenalSlotController);
            if (!string.IsNullOrEmpty(presentationError)) EditorGUILayout.HelpBox(presentationError, MessageType.Error);

            // Превью пересоздаётся извне (выход из Play Mode, открытие сцены) — подхватываем новое.
            if (_previewItem == null)
                _previewItem = ArsenalSlotPreview.Find((Component)target, ArsenalSlotPreview.ItemPreviewName);

            // ── Item Selection from Registry ──
            EditorGUILayout.LabelField("Item Selection", EditorStyles.boldLabel);

            if (_cachedRegistry == null)
                FindRegistry();

            WeaponInfo previousItem = _weaponInfoProp.objectReferenceValue as WeaponInfo;

            if (_cachedRegistry != null && _cachedRegistry.Count > 0)
            {
                var names = _cachedRegistry.GetDisplayNames();
                var options = new string[names.Length + 1];
                options[0] = "(None)";
                for (int i = 0; i < names.Length; i++)
                    options[i + 1] = names[i];

                int currentIndex = 0;
                if (previousItem != null)
                {
                    int regIdx = _cachedRegistry.IndexOf(previousItem);
                    if (regIdx >= 0)
                        currentIndex = regIdx + 1;
                }

                int newIndex = EditorGUILayout.Popup("Item", currentIndex, options);
                if (newIndex != currentIndex)
                {
                    _weaponInfoProp.objectReferenceValue =
                        newIndex == 0 ? null : _cachedRegistry.GetByIndex(newIndex - 1);
                }

                var currentItem = _weaponInfoProp.objectReferenceValue as WeaponInfo;
                if (currentItem != null)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField("Price", $"${currentItem.Price}");
                    EditorGUILayout.LabelField("Category", currentItem.Category.ToString());
                    if (currentItem.MagazinePrefab != null)
                        EditorGUILayout.LabelField("Magazine", "Yes (decorative)");
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No WeaponRegistry found. Create one via:\n" +
                    "Create → VR Battlegrounds → Arsenal → Weapon Registry\n" +
                    "Then add WeaponInfo assets to it.",
                    MessageType.Warning);
                EditorGUILayout.PropertyField(_weaponInfoProp, new GUIContent("Item Info (manual)"));
            }

            EditorGUILayout.Space(4);

            // ── Offset Editing ──
            var weaponInfo = _weaponInfoProp.objectReferenceValue as WeaponInfo;
            if (weaponInfo != null && _previewItem != null)
            {
                DrawOffsetEditor(weaponInfo);
            }

            EditorGUILayout.Space(4);

            // ── Preview Controls ──
            EditorGUILayout.BeginHorizontal();

            bool hasPreview = _previewItem != null;
            var statusLabel = hasPreview ? "🟢 Preview Active" : "⚪ No Preview";
            EditorGUILayout.LabelField(statusLabel, GUILayout.Width(130));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("↻ Preview", GUILayout.Width(90)))
            {
                RebuildPreview();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("↻ Registry", GUILayout.Width(90)))
            {
                _cachedRegistry = null;
                FindRegistry();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // ── Anchor ──
            EditorGUILayout.LabelField("Anchor", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_itemAnchorProp);

            // ── Subclass extra fields ──
            DrawExtraFields();

            EditorGUILayout.Space(8);

            // ── Visual Feedback ──
            EditorGUILayout.LabelField("Visual Feedback", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_slotLightProp);
            EditorGUILayout.PropertyField(_availableColorProp);
            EditorGUILayout.PropertyField(_unavailableColorProp);
            EditorGUILayout.PropertyField(_takenColorProp);

            serializedObject.ApplyModifiedProperties();

            // ── After apply: check if item changed → update preview ──
            var newItem = _weaponInfoProp.objectReferenceValue as WeaponInfo;
            if (newItem != previousItem)
            {
                RebuildPreview();
                SceneView.RepaintAll();
            }
        }

        // ── Offset Editor UI ───────────────────────────────────

        private void DrawOffsetEditor(WeaponInfo weaponInfo)
        {
            var slot = target as ArsenalSlotController;
            var presentation = ArsenalPresentationApplicator.Resolve(slot);
            var stored = ArsenalPresentationApplicator.ItemLocalPose(slot, false, presentation);
            var bgColor = _isEditingOffsets ? new Color(0.2f, 0.35f, 0.2f, 1f) : GUI.backgroundColor;
            GUI.backgroundColor = bgColor;

            EditorGUILayout.BeginVertical("box");
            GUI.backgroundColor = Color.white;

            EditorGUILayout.LabelField("⚙ Offset Adjustment", EditorStyles.boldLabel);

            if (!_isEditingOffsets)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Position", stored.Position.ToString("F3"));
                EditorGUILayout.LabelField("Rotation", stored.EulerAngles.ToString("F1"));
                if (presentation.IsStyled) EditorGUILayout.LabelField("Источник", presentation.Style.name + " / " + slot.PresentationZone);
                EditorGUI.indentLevel--;

                EditorGUILayout.Space(2);

                if (GUILayout.Button("🔓 Enable Offset Editing (move preview in Scene View)"))
                {
                    _isEditingOffsets = true;
                    MakePreviewEditable(true);
                    SceneView.RepaintAll();
                }
            }
            else
            {
                if (_previewItem != null)
                {
                    var livePos = _previewItem.transform.localPosition;
                    var liveRot = _previewItem.transform.localEulerAngles;

                    EditorGUILayout.LabelField("Stored Position", stored.Position.ToString("F3"));
                    EditorGUILayout.LabelField("Stored Rotation", stored.EulerAngles.ToString("F1"));

                    EditorGUILayout.Space(2);

                    EditorGUI.BeginChangeCheck();
                    var newPos = EditorGUILayout.Vector3Field("Live Position", livePos);
                    var newRot = EditorGUILayout.Vector3Field("Live Rotation", liveRot);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _previewItem.transform.localPosition = newPos;
                        _previewItem.transform.localEulerAngles = newRot;
                        SceneView.RepaintAll();
                    }

                    bool hasChanges = livePos != stored.Position || Quaternion.Angle(_previewItem.transform.localRotation, stored.Rotation) > .0001f;

                    EditorGUILayout.Space(4);

                    if (hasChanges)
                    {
                        EditorGUILayout.HelpBox(
                            presentation.IsStyled ? "Превью перемещено. Сохранение запишет target якоря в Style для этого оружия и зоны." :
                            "Превью перемещено. Сохранение запишет legacy offsets в " + weaponInfo.name + ".",
                            MessageType.Info);
                    }

                    EditorGUILayout.BeginHorizontal();

                    GUI.enabled = hasChanges;
                    GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                    if (GUILayout.Button("Сохранить → " + (presentation.IsStyled ? presentation.Style.name : weaponInfo.name), GUILayout.Height(28)))
                    {
                        RunOffsetWrite(presentation, weaponInfo, () => SaveOffsetsToWeaponInfo(weaponInfo));
                    }
                    GUI.backgroundColor = Color.white;
                    GUI.enabled = true;

                    if (GUILayout.Button("↩ Reset", GUILayout.Width(60), GUILayout.Height(28)))
                    {
                        if (presentation.IsStyled) RunOffsetWrite(presentation, weaponInfo, () => ResetStyleItemException(presentation.Style, weaponInfo, slot.PresentationZone));
                        stored = ArsenalPresentationApplicator.ItemLocalPose(slot, false, ArsenalPresentationApplicator.Resolve(slot));
                        _previewItem.transform.SetLocalPositionAndRotation(stored.Position, stored.Rotation);
                        SceneView.RepaintAll();
                    }

                    EditorGUILayout.EndHorizontal();
                    if (!string.IsNullOrEmpty(_offsetWriteError)) EditorGUILayout.HelpBox(_offsetWriteError, MessageType.Error);
                }

                EditorGUILayout.Space(2);

                GUI.backgroundColor = new Color(0.8f, 0.3f, 0.3f);
                if (GUILayout.Button("🔒 Lock Offsets (disable editing)"))
                {
                    _isEditingOffsets = false;
                    MakePreviewEditable(false);
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;
            }

            EditorGUILayout.EndVertical();
        }

        private void SaveOffsetsToWeaponInfo(WeaponInfo weaponInfo)
        {
            if (_previewItem == null) return;
            var slot = target as ArsenalSlotController;
            var presentation = ArsenalPresentationApplicator.Resolve(slot);
            if (presentation.IsStyled)
            {
                SaveStyleItemTarget(presentation.Style, slot, weaponInfo, _previewItem.transform);
                return;
            }

            Undo.RecordObject(weaponInfo, "Save Item Offsets");

            var so = new SerializedObject(weaponInfo);
            so.Update();

            var posProp = so.FindProperty("_weaponPositionOffset");
            var rotProp = so.FindProperty("_weaponRotationOffset");

            if (posProp != null)
                posProp.vector3Value = _previewItem.transform.localPosition;
            if (rotProp != null)
                rotProp.vector3Value = _previewItem.transform.localEulerAngles;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(weaponInfo);
            AssetDatabase.SaveAssetIfDirty(weaponInfo);

            VrBattlegrounds.Core.GameLog.Arsenal.Info($"[Arsenal Editor] Saved offsets to {weaponInfo.name}: " +
                      $"pos={_previewItem.transform.localPosition:F3}, " +
                      $"rot={_previewItem.transform.localEulerAngles:F1}");
        }

        private void RunOffsetWrite(ArsenalPresentationSnapshot presentation, WeaponInfo weapon, System.Action write)
        {
            var output = presentation.IsStyled ? (Object)presentation.Style : weapon;
            try
            {
                // UI writer имеет собственную краткую аренду; moved transient preview не сохраняется.
                ArsenalEditorActions.Run("Сохранить каноническую композицию слота", new[] {
                    AssetDatabase.GetAssetPath(output), AssetDatabase.GetAssetPath(weapon), AssetDatabase.GetAssetPath(weapon.WeaponPrefab)
                }, () => { write(); return "Сохранён только адресный layout asset."; }, savesAllAssets: false);
                _offsetWriteError = null;
            }
            catch (System.InvalidOperationException exception) { _offsetWriteError = exception.Message; }
        }

        // ── Preview System ─────────────────────────────────────

        private static void SaveStyleItemTarget(ArsenalPresentationStyle style, ArsenalSlotController slot, WeaponInfo weapon, Transform preview)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Layout Save только в EditMode.");
            var source = weapon.WeaponPrefab.GetComponent<UltimateXR.Manipulation.UxrGrabbableObject>();
            var drop = source.DropAlignTransform;
            Vector3 point = preview.TransformPoint(source.transform.InverseTransformPoint(drop.position));
            Quaternion rotation = preview.rotation * Quaternion.Inverse(source.transform.rotation) * drop.rotation;
            var target = new ArsenalPresentationPose(slot.transform.InverseTransformPoint(point), Quaternion.Inverse(slot.transform.rotation) * rotation);
            ArsenalPresentationResolver.ValidatePose(target);
            ArsenalPresentationResolver.Validate(style);
            Undo.RecordObject(style, "Save Canonical Arsenal Item Target");
            var serialized = new SerializedObject(style);
            var list = serialized.FindProperty("_exceptions");
            SerializedProperty entry = FindStyleException(list, weapon, slot.PresentationZone);
            if (entry == null)
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                entry = list.GetArrayElementAtIndex(list.arraySize - 1);
                // Insert может дублировать предыдущую запись: все override flags задаются явно.
                entry.FindPropertyRelative("Weapon").objectReferenceValue = weapon;
                entry.FindPropertyRelative("Zone").enumValueIndex = (int)slot.PresentationZone;
                entry.FindPropertyRelative("OverrideMagazine").boolValue = false;
                entry.FindPropertyRelative("OverrideCard").boolValue = false;
                entry.FindPropertyRelative("OverrideSupports").boolValue = false;
                entry.FindPropertyRelative("Supports").ClearArray();
                entry.FindPropertyRelative("CardSize").vector2Value = new Vector2(.15f, .16f);
                entry.FindPropertyRelative("CardFontSize").floatValue = .16f;
            }
            entry.FindPropertyRelative("OverrideItem").boolValue = true;
            entry.FindPropertyRelative("ItemTarget").FindPropertyRelative("Position").vector3Value = target.Position;
            entry.FindPropertyRelative("ItemTarget").FindPropertyRelative("EulerAngles").vector3Value = target.EulerAngles;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(style); AssetDatabase.SaveAssetIfDirty(style);
        }

        private static void ResetStyleItemException(ArsenalPresentationStyle style, WeaponInfo weapon, ArsenalPresentationZone zone)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Layout Reset только в EditMode.");
            ArsenalPresentationResolver.Validate(style);
            var serialized = new SerializedObject(style);
            var list = serialized.FindProperty("_exceptions");
            var entry = FindStyleException(list, weapon, zone);
            if (entry == null || !entry.FindPropertyRelative("OverrideItem").boolValue) return;
            Undo.RecordObject(style, "Reset Canonical Arsenal Item Target");
            entry.FindPropertyRelative("OverrideItem").boolValue = false;
            if (!entry.FindPropertyRelative("OverrideMagazine").boolValue && !entry.FindPropertyRelative("OverrideCard").boolValue &&
                !entry.FindPropertyRelative("OverrideSupports").boolValue)
                for (int i = 0; i < list.arraySize; i++)
                    if (SerializedProperty.EqualContents(list.GetArrayElementAtIndex(i), entry)) { list.DeleteArrayElementAtIndex(i); break; }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(style); AssetDatabase.SaveAssetIfDirty(style);
        }

        private static SerializedProperty FindStyleException(SerializedProperty list, WeaponInfo weapon, ArsenalPresentationZone zone)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                var entry = list.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("Weapon").objectReferenceValue == weapon && entry.FindPropertyRelative("Zone").enumValueIndex == (int)zone) return entry;
            }
            return null;
        }

        private void RebuildPreview()
        {
            var slot = target as ArsenalSlotController;
            ArsenalSlotPreview.Destroy(slot);
            _previewItem = ArsenalSlotPreview.Ensure(slot);
            _isEditingOffsets = false;
        }

        private void MakePreviewEditable(bool editable)
        {
            if (_previewItem != null)
            {
                var flags = editable ? HideFlags.DontSave : (HideFlags.DontSave | HideFlags.NotEditable);
                ArsenalSlotPreview.SetHideFlagsRecursive(_previewItem.transform, flags);
                EditorUtility.SetDirty(_previewItem);
            }
        }

        private void FindRegistry()
        {
            var guids = AssetDatabase.FindAssets(RegistrySearchFilter);
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                _cachedRegistry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(path);
            }
        }
    }
}
