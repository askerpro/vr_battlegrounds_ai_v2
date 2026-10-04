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
            return Create(part, part.GetComponent<MeshFilter>().sharedMesh, ObjectName);
        }

        public static GameObject Create(Transform part, Mesh mesh, string name)
        {
            Transform existing = part.Find(name);
            var highlight = existing != null ? existing.gameObject : new GameObject(name) { layer = part.gameObject.layer };
            highlight.transform.SetParent(part, false);
            highlight.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            highlight.transform.localScale = Vector3.one;
            GameObject geometry = highlight;
            if (name != ObjectName)
            {
                // Семантический holder отдельно, дочерняя геометрия сохраняет контракт имени GrabHighlight.
                if (highlight.TryGetComponent(out MeshFilter oldFilter)) Object.DestroyImmediate(oldFilter);
                if (highlight.TryGetComponent(out MeshRenderer oldRenderer)) Object.DestroyImmediate(oldRenderer);
                Transform child = highlight.transform.Find(ObjectName);
                geometry = child != null ? child.gameObject : new GameObject(ObjectName) { layer = part.gameObject.layer };
                geometry.transform.SetParent(highlight.transform, false);
                geometry.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                geometry.transform.localScale = Vector3.one;
            }
            if (!geometry.TryGetComponent(out MeshFilter filter)) filter = geometry.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            if (!geometry.TryGetComponent(out MeshRenderer renderer)) renderer = geometry.AddComponent<MeshRenderer>();
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
