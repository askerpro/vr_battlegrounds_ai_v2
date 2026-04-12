using UnityEditor;
using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    /// Custom inspector for <see cref="FirearmSlotController"/>.
    /// Extends the base arsenal slot editor with decorative magazine preview.
    /// </summary>
    [InitializeOnLoad]
    [CustomEditor(typeof(FirearmSlotController))]
    public class FirearmSlotControllerEditor : ArsenalSlotEditorBase
    {
        private const string MagPreviewName = "__MagPreview__";

        private SerializedProperty _magAnchorProp;
        private GameObject _previewMagazine;

        // ── Domain Reload Cleanup ──────────────────────────────
        static FirearmSlotControllerEditor()
        {
            EditorApplication.delayCall += () => CleanupByName(MagPreviewName);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _magAnchorProp = serializedObject.FindProperty("_magAnchor");

            // Reclaim existing mag preview
            var slot = target as FirearmSlotController;
            if (slot != null)
                _previewMagazine = FindExistingPreview(slot, MagPreviewName);
        }

        protected override void DrawExtraFields()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Decorative Magazine", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_magAnchorProp);
        }

        protected override void OnPreviewRefreshed()
        {
            var slot = target as FirearmSlotController;
            if (slot == null) return;

            var weaponInfo = GetCurrentWeaponInfo();
            if (weaponInfo == null || weaponInfo.MagazinePrefab == null) return;

            // Find mag anchor
            var magAnchor = _magAnchorProp?.objectReferenceValue as UxrGrabbableObjectAnchor;
            if (magAnchor == null && slot.MagAnchor != null)
                magAnchor = slot.MagAnchor;

            if (magAnchor != null)
            {
                _previewMagazine = (GameObject)GameObject.Instantiate(weaponInfo.MagazinePrefab);
                if (_previewMagazine != null)
                {
                    SetupPreviewObject(_previewMagazine, MagPreviewName, magAnchor.transform,
                        Vector3.zero, Quaternion.identity);
                }
            }
        }

        protected override void OnPreviewDestroyed()
        {
            if (_previewMagazine != null)
            {
                DestroyImmediate(_previewMagazine);
                _previewMagazine = null;
            }

            var slot = target as FirearmSlotController;
            if (slot != null)
                DestroyPreviewByName(slot, MagPreviewName);
        }
    }
}
