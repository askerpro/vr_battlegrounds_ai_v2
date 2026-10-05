using System;
using UltimateXR.Core;
using UltimateXR.Extensions.Unity;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Разделяет доступную точку приёма патрона и конечную позу в трубчатом магазине.</summary>
    public static class WeaponInternalAmmoAlignment
    {
        public const string ProximityName = "LoadingProximity";
        public const string EjectionName = "EjectionPose";

        /// <summary>
        /// Патрон целиком проходит над подающей деталью и за её передний край по +Z оружия.
        /// Приёмная точка, радиус приёма, боезапас, масштаб и ссылки магазина не меняются.
        /// </summary>
        public static void ConfigureAnchor(UxrGrabbableObjectAnchor anchor, Transform loadingPart, UxrGrabbableObject magazine)
        {
            if (anchor == null || loadingPart == null || magazine == null)
                throw new ArgumentException("Внутреннему магазину нужны якорь, подающая деталь и вложенный патрон.");

            Transform frame = anchor.transform.parent;
            MeshFilter loadingMesh = loadingPart.GetComponent<MeshFilter>();
            Transform shellMeshTransform = magazine.transform.Find("Mesh");
            MeshFilter shellMesh = shellMeshTransform != null ? shellMeshTransform.GetComponent<MeshFilter>() : null;
            if (frame == null || loadingMesh == null || loadingMesh.sharedMesh == null || shellMesh == null || shellMesh.sharedMesh == null)
                throw new InvalidOperationException("Для позы внутреннего магазина отсутствует геометрия окна или патрона.");

            Bounds loading = BoundsInFrame(loadingMesh, frame);
            Bounds shell = BoundsInFrame(shellMesh, frame);
            // +Y — внутрь над подающей деталью, +Z — в переднюю трубу. Размеры — из мешей,
            // а не фиксированные сантиметры. Геометрия патрона может иметь отдельный DropAlign.
            Vector3 shellCenter = new Vector3(loading.center.x, loading.max.y + shell.extents.y, loading.max.z + shell.extents.z);
            Vector3 centerFromDropAlign = shell.center - frame.InverseTransformPoint(magazine.DropAlignTransform.position);
            Vector3 finalDropAlign = frame.TransformPoint(shellCenter - centerFromDropAlign);

            // Корень якоря — конечная поза внутри. Отдельный child сохраняет прежнюю
            // мировую позу приёма, включая повторное применение уже мигрированного prefab.
            Vector3 proximityPosition = anchor.DropProximityTransform.position;
            Quaternion proximityRotation = anchor.DropProximityTransform.rotation;
            Transform proximity = anchor.transform.Find(ProximityName);
            if (proximity == null)
            {
                proximity = new GameObject(ProximityName).transform;
                proximity.SetParent(anchor.transform, false);
            }
            anchor.transform.position = finalDropAlign;
            proximity.SetPositionAndRotation(proximityPosition, proximityRotation);
            proximity.localScale = Vector3.one;

            var serialized = new SerializedObject(anchor);
            serialized.FindProperty("_alignTransformUseSelf").boolValue = true;
            serialized.FindProperty("_alignTransform").objectReferenceValue = null;
            serialized.FindProperty("_dropProximityTransformUseSelf").boolValue = false;
            serialized.FindProperty("_dropProximityTransform").objectReferenceValue = proximity;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Стартовое вложение SDK регистрирует в Awake без повторного выравнивания.
            // Используем те же DropAlign/DropSnapMode, что UxrGrabManager.PlaceObject.
            magazine.transform.ApplyAlignment(magazine.DropAlignTransform, anchor.AlignTransform,
                UxrUtils.BuildTransformations(magazine.DropSnapMode == UxrSnapToAnchorMode.PositionOnly || magazine.DropSnapMode == UxrSnapToAnchorMode.PositionAndRotation,
                                              magazine.DropSnapMode == UxrSnapToAnchorMode.RotationOnly || magazine.DropSnapMode == UxrSnapToAnchorMode.PositionAndRotation));
            ConfigureEjection(anchor, magazine, frame.TransformPoint(loading.center));
        }

        /// <summary>
        /// Ищет выход от наружной точки приёма в сторону от центра подающей детали.
        /// Проверяет padded BoxCollider патрона: contactOffset уже входит в сохранённый зазор.
        /// При неподдерживаемой геометрии или отсутствии свободной позы prefab не сохраняется.
        /// </summary>
        private static void ConfigureEjection(UxrGrabbableObjectAnchor anchor, UxrGrabbableObject magazine, Vector3 loadingCenter)
        {
            if (!magazine.UseParenting)
                throw new InvalidOperationException("Внутренний патрон должен парентиться в якорь для подписки на SDK Removing.");
            Vector3 outward = anchor.DropProximityTransform.position - loadingCenter;
            if (outward.sqrMagnitude < 1e-12f)
                throw new InvalidOperationException("Точка приёма не задаёт наружное направление от окна заряжания.");
            outward.Normalize();

            Rigidbody weaponBody = anchor.GetComponentInParent<Rigidbody>();
            Rigidbody shellBody = magazine.GetComponent<Rigidbody>();
            if (weaponBody == null || shellBody == null || weaponBody == shellBody)
                throw new InvalidOperationException("Нет отдельных физических тел корпуса и патрона.");
            var bodyColliders = new System.Collections.Generic.List<Collider>();
            float margin = 0.0001f;
            Bounds bodyBounds = new Bounds(anchor.DropProximityTransform.position, Vector3.zero);
            foreach (Collider c in weaponBody.GetComponentsInChildren<Collider>(true))
            {
                if (!c.isTrigger && c.enabled && c.attachedRigidbody == weaponBody)
                {
                    bodyColliders.Add(c);
                    bodyBounds.Encapsulate(c.bounds);
                    margin = Mathf.Max(margin, c.contactOffset);
                }
            }
            var shellColliders = new System.Collections.Generic.List<BoxCollider>();
            foreach (Collider c in magazine.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || c.attachedRigidbody != shellBody) continue;
                if (!(c is BoxCollider box))
                    throw new InvalidOperationException("Внутренний патрон требует BoxCollider для предвычисленного ejection clearance.");
                shellColliders.Add(box);
                margin = Mathf.Max(margin, c.contactOffset);
            }
            if (bodyColliders.Count == 0 || shellColliders.Count == 0)
                throw new InvalidOperationException("Нет nontrigger геометрии корпуса или патрона для ejection preflight.");

            var probes = new System.Collections.Generic.List<BoxCollider>();
            try
            {
                float maxDistance = bodyBounds.size.magnitude + margin * 2f;
                foreach (BoxCollider source in shellColliders)
                {
                    Vector3 scale = source.transform.lossyScale;
                    if (Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) < 1e-6f)
                        throw new InvalidOperationException("Нулевой масштаб collider патрона.");
                    var go = new GameObject("InternalAmmoClearanceProbe") { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, anchor.gameObject.scene);
                    go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    go.transform.localScale = scale;
                    var box = go.AddComponent<BoxCollider>();
                    probes.Add(box);
                    box.center = source.center;
                    box.size = source.size + new Vector3(2f * margin / Mathf.Abs(scale.x), 2f * margin / Mathf.Abs(scale.y), 2f * margin / Mathf.Abs(scale.z));
                    maxDistance += Vector3.Scale(source.size, scale).magnitude;
                }
                Vector3 externalDelta = anchor.DropProximityTransform.position - magazine.DropAlignTransform.position;
                Vector3 candidate = anchor.DropProximityTransform.position;
                bool clear = false;
                int steps = Mathf.CeilToInt(maxDistance / margin);
                for (int step = 0; step <= steps; step++)
                {
                    Vector3 delta = externalDelta + outward * (step * margin);
                    clear = true;
                    foreach (BoxCollider probe in probes)
                    {
                        foreach (Collider body in bodyColliders)
                        {
                            if (Physics.ComputePenetration(probe, probe.transform.position + delta, probe.transform.rotation,
                                body, body.transform.position, body.transform.rotation, out _, out float depth) && depth > 1e-6f)
                            {
                                clear = false;
                                break;
                            }
                        }
                        if (!clear) break;
                    }
                    if (clear) { candidate += outward * (step * margin); break; }
                }
                if (!clear) throw new InvalidOperationException("Не найдена безопасная наружная ejection pose; prefab не сохраняется.");

                Transform pose = anchor.transform.Find(EjectionName);
                if (pose == null)
                {
                    pose = new GameObject(EjectionName).transform;
                    pose.SetParent(anchor.transform, false);
                }
                pose.SetPositionAndRotation(candidate, anchor.DropProximityTransform.rotation);
                pose.localScale = Vector3.one;
                var component = anchor.GetComponent<WeaponInternalAmmoEjection>();
                if (component == null) component = anchor.gameObject.AddComponent<WeaponInternalAmmoEjection>();
                var serialized = new SerializedObject(component);
                serialized.FindProperty("_ejectionPose").objectReferenceValue = pose;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            finally
            {
                foreach (BoxCollider probe in probes) if (probe != null) UnityEngine.Object.DestroyImmediate(probe.gameObject);
            }
        }

        /// <summary>Ориентированные габариты меша в нормализованных осях оружия.</summary>
        public static Bounds BoundsInFrame(MeshFilter filter, Transform frame)
        {
            Bounds source = filter.sharedMesh.bounds;
            Matrix4x4 matrix = frame.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Bounds result = new Bounds(matrix.MultiplyPoint3x4(source.center), Vector3.zero);
            for (int mask = 0; mask < 8; mask++)
            {
                Vector3 signs = new Vector3((mask & 1) == 0 ? -1f : 1f, (mask & 2) == 0 ? -1f : 1f, (mask & 4) == 0 ? -1f : 1f);
                result.Encapsulate(matrix.MultiplyPoint3x4(source.center + Vector3.Scale(source.extents, signs)));
            }
            return result;
        }

        /// <summary>Обновляет только alignment двух существующих дробовиков, без пересборки модели/хватов.</summary>
        public static string ApplyCurrentShotguns()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Изменение якорей запрещено в Play Mode.");
            var output = new System.Text.StringBuilder();
            foreach (HandsPackWeaponRecipe recipe in new[] { HandsPackWeaponBuilder.ShotgunReal, KinemationWeaponBuilder.Herrington.Weapon })
            {
                if (!recipe.MagazineIsInternal) throw new InvalidOperationException(recipe.Name + ": не задан внутренний магазин.");
                string path = $"Assets/Prefabs/Weapons/{recipe.PrefabFolder}/{recipe.Name}.prefab";
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Transform frame = root.transform.Find("MeshContainer");
                    var anchor = frame.Find("MagAnchor").GetComponent<UxrGrabbableObjectAnchor>();
                    var magazine = anchor.GetComponentInChildren<UxrFirearmMag>(true).GetComponent<UxrGrabbableObject>();
                    Transform loading = frame.Find(HandsPackWeaponBuilder.Clean(recipe.LoadingPart));
                    Vector3 proximity = anchor.DropProximityTransform.position;
                    int rounds = magazine.GetComponent<UxrFirearmMag>().Rounds;
                    int capacity = magazine.GetComponent<UxrFirearmMag>().Capacity;
                    ConfigureAnchor(anchor, loading, magazine);
                    if ((anchor.DropProximityTransform.position - proximity).sqrMagnitude > 1e-12f ||
                        magazine.GetComponent<UxrFirearmMag>().Rounds != rounds || magazine.GetComponent<UxrFirearmMag>().Capacity != capacity)
                        throw new InvalidOperationException("Alignment изменил точку приёма или боезапас: " + path);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    output.AppendLine(path + ": align=" + frame.InverseTransformPoint(anchor.AlignTransform.position).ToString("F6") +
                        ", proximity=" + frame.InverseTransformPoint(anchor.DropProximityTransform.position).ToString("F6") +
                        ", ejection=" + frame.InverseTransformPoint(anchor.transform.Find(EjectionName).position).ToString("F6"));
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return output.ToString();
        }
    }
}
