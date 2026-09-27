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
            var bgColor = _isEditingOffsets ? new Color(0.2f, 0.35f, 0.2f, 1f) : GUI.backgroundColor;
            GUI.backgroundColor = bgColor;

            EditorGUILayout.BeginVertical("box");
            GUI.backgroundColor = Color.white;

            EditorGUILayout.LabelField("⚙ Offset Adjustment", EditorStyles.boldLabel);

            if (!_isEditingOffsets)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Position", weaponInfo.WeaponPositionOffset.ToString("F3"));
                EditorGUILayout.LabelField("Rotation", weaponInfo.WeaponRotationOffset.ToString("F1"));
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

                    EditorGUILayout.LabelField("Stored Position", weaponInfo.WeaponPositionOffset.ToString("F3"));
                    EditorGUILayout.LabelField("Stored Rotation", weaponInfo.WeaponRotationOffset.ToString("F1"));

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

                    bool hasChanges = livePos != weaponInfo.WeaponPositionOffset ||
                                      liveRot != weaponInfo.WeaponRotationOffset;

                    EditorGUILayout.Space(4);

                    if (hasChanges)
                    {
                        EditorGUILayout.HelpBox(
                            "Preview has been moved. Click 'Save' to write offsets back to " +
                            weaponInfo.name + " asset.",
                            MessageType.Info);
                    }

                    EditorGUILayout.BeginHorizontal();

                    GUI.enabled = hasChanges;
                    GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);
                    if (GUILayout.Button("💾 Save Offsets → " + weaponInfo.name, GUILayout.Height(28)))
                    {
                        SaveOffsetsToWeaponInfo(weaponInfo);
                    }
                    GUI.backgroundColor = Color.white;
                    GUI.enabled = true;

                    if (GUILayout.Button("↩ Reset", GUILayout.Width(60), GUILayout.Height(28)))
                    {
                        _previewItem.transform.localPosition = weaponInfo.WeaponPositionOffset;
                        _previewItem.transform.localEulerAngles = weaponInfo.WeaponRotationOffset;
                        SceneView.RepaintAll();
                    }

                    EditorGUILayout.EndHorizontal();
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
            AssetDatabase.SaveAssets();

            Debug.Log($"[Arsenal Editor] Saved offsets to {weaponInfo.name}: " +
                      $"pos={_previewItem.transform.localPosition:F3}, " +
                      $"rot={_previewItem.transform.localEulerAngles:F1}");
        }

        // ── Preview System ─────────────────────────────────────

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
