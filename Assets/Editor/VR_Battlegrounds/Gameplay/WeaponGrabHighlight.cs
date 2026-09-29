using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Подсветка точки хвата оружия: неактивная копия детали, за которую берутся, с полупрозрачным
    /// материалом подсветки. UltimateXR включает её, пока рука рядом (<c>Enable When Hand Near</c>).
    ///
    /// <para>
    /// Копия — дочерний объект детали с единичным трансформом: повторяет её форму и ходит вместе с ней
    /// (затвор, помпа), габарит оружия не меняет (<c>WeaponScaleTests</c>). Блок-примитив вместо копии
    /// и материал корпуса ловит <c>WeaponFeedbackTests</c>.
    /// </para>
    /// </summary>
    public static class WeaponGrabHighlight
    {
        public const string ObjectName = "GrabHighlight";

        // Тот же материал, что у подсветки магазина M16 и сэмпловых магазинов SDK.
        public const string MaterialPath = "Assets/ThirdParty/UltimateXR/Samples/FullScene/Art/Interactive/Weapons/Mags/MagGrabDecalMat.mat";

        /// <summary>
        /// Копия сетки <paramref name="part" /> под ней; повторный вызов возвращает уже созданную.
        /// </summary>
        public static GameObject Create(Transform part)
        {
            Transform existing = part.Find(ObjectName);
            if (existing != null) return existing.gameObject;

            var highlight = new GameObject(ObjectName) { layer = part.gameObject.layer };
            highlight.transform.SetParent(part, false);
            highlight.AddComponent<MeshFilter>().sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;

            var renderer = highlight.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            highlight.SetActive(false);
            return highlight;
        }

        /// <summary>
        /// Назначает подсветку-копию <paramref name="part" /> точке хвата <paramref name="point" />.
        /// </summary>
        public static void Assign(UxrGrabbableObject grabbable, int point, Transform part)
        {
            grabbable.GetGrabPoint(point).EnableOnHandNear = Create(part);
            EditorUtility.SetDirty(grabbable);
        }
    }
}
