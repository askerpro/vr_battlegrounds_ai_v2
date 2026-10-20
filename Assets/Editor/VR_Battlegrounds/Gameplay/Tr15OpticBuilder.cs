using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Writer ассетов коллиматора XPS2: source alpha выделяет окно в отдельный submesh (<see cref="MeshPath"/>), в окне —
    /// материал сетки конечного фокуса (<see cref="ReticleMaterialPath"/>). Это входы комплекта <c>SightKit_XPS2</c>
    /// (<c>SightKitBuilder.Optics</c>).
    /// Раунд 1 (2026-10-20, решение пользователя): коллиматор больше не вшит в TR15 — он ставится комплектом через стенд
    /// калибровки (<c>WeaponSightAuthoring</c>). <see cref="Apply"/> поэтому только обновляет ассеты и убирает из TR15
    /// прежний узел <see cref="OpticName"/> (если он ещё есть): этот writer его и ставил (2026-10-11/12).
    /// </summary>
    public static class Tr15OpticBuilder
    {
        public const string WeaponPath = "Assets/Prefabs/Weapons/TR15/TR15.prefab";
        public const string OpticName = "SM_Attach_AR15_XPS2";
        public const string SightId = "XPS2";
        public const string MeshPath = "Assets/Art/Weapons/Kinemation/TR15/Meshes/TR15_XPS2_Optic.asset";
        public const string ReticleMaterialPath = "Assets/Art/Weapons/Kinemation/TR15/Materials/TR15_XPS2_Reticle.mat";
        private const string RawMeshPath = "Assets/Art/Weapons/Kinemation/TR15/Meshes/TR15_SM_Attach_AR15_XPS2.asset";
        private const string HousingMaterialPath = "Assets/Art/Weapons/Kinemation/Materials/M_TR15_Attachments.mat";
        private const string SourceTextures = "Assets/ThirdParty/KINEMATION/TacticalShooterPack/Meshes/Weapons/TR15/Textures/";
        private const string ReticleShaderPath = "Assets/Art/Weapons/Sights/FiniteFocusReticle.shader";
        private const string WeaponInfoPath = "Assets/Data/Weapons/TR15_Weapon.asset";
        private const string SettingsPath = "Assets/Data/Weapons/SightCalibration/WeaponSightCalibrationSettings.asset";
        private const int LensMaterialIndex = 1;
        // Цвет сетки эталона (TR15 Sight Review, Reticle.mat); остальные параметры — значения шейдера по умолчанию.
        private static readonly Color ReticleColor = new Color(1f, 0.025f, 0.012f, 1f);

        public static string[] InputPaths() => new[] { WeaponPath, RawMeshPath, HousingMaterialPath,
            SourceTextures + "T_TR15_Attachments_Alpha.TGA", ReticleShaderPath, WeaponInfoPath, SettingsPath };
        /// <summary>WeaponPath — только для снятия прежнего узла коллиматора из TR15.</summary>
        public static string[] OutputPaths() => new[] { WeaponPath, MeshPath, ReticleMaterialPath };

        /// <summary>Read-only: проверяет binding, маску и данные сведения до первого writer, не импортирует источник.</summary>
        public static string Preflight()
        {
            ReadInputs(out _, out _, out _, out _, out WeaponInfo info, out WeaponSightCalibrationSettings settings);
            bool legacy = LegacyNode(AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath)) != null;
            return $"TR15 XPS2: housing=1648, lens=52; материал сетки конечного фокуса для SightKit_XPS2; " +
                   (legacy ? $"прежний узел {OpticName} будет снят с {info.WeaponId}" : $"в {info.WeaponId} вшитого коллиматора нет");
        }

        private static void ReadInputs(out Mesh raw, out Shader shader, out List<int> housing, out List<int> lens,
                                       out WeaponInfo info, out WeaponSightCalibrationSettings settings)
        {
            raw = AssetDatabase.LoadAssetAtPath<Mesh>(RawMeshPath);
            var maskTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceTextures + "T_TR15_Attachments_Alpha.TGA");
            shader = Shader.Find(WeaponOpticView.ShaderName);
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath);
            info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(WeaponInfoPath);
            settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath);
            if (raw == null || maskTexture == null || shader == null || asset == null || info == null || settings == null)
                throw new InvalidOperationException("TR15 XPS2: отсутствует исходный меш, alpha, шейдер сетки, WeaponInfo или настройки калибровки");
            if (info.WeaponPrefab != asset)
                throw new InvalidOperationException("TR15 XPS2: " + WeaponInfoPath + " ссылается не на " + WeaponPath);
            if (!WeaponSightCalibrationSettings.IsValidDistance(settings.DefaultZeroDistance))
                throw new InvalidOperationException("TR15 XPS2: дистанция пристрелки вне (0, 20] м: " + settings.DefaultZeroDistance);
            if (!raw.isReadable || raw.subMeshCount != 1 || raw.blendShapeCount != 0)
                throw new InvalidOperationException("TR15 XPS2: требуется неизменённый game-owned исходный меш с одним submesh");
            if ((File.Exists(MeshPath) && AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath) == null) ||
                (File.Exists(ReticleMaterialPath) && AssetDatabase.LoadAssetAtPath<Material>(ReticleMaterialPath) == null))
                throw new InvalidOperationException("TR15 XPS2: output path занят чужим типом ассета или неимпортированным файлом");
            GameObject legacyNode = LegacyNode(asset);
            if (legacyNode != null) ValidateBinding(legacyNode);
            Color32[] mask = Read(maskTexture, maskTexture.width, maskTexture.height);
            Vector2[] uv = raw.uv;
            int[] triangles = raw.GetTriangles(0);
            if (uv.Length != raw.vertexCount || triangles.Length % 3 != 0)
                throw new InvalidOperationException("TR15 XPS2: отсутствуют полные UV/треугольники исходного меша");
            housing = new List<int>();
            lens = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector2 at = (uv[triangles[i]] + uv[triangles[i + 1]] + uv[triangles[i + 2]]) / 3f;
                int x = Mathf.RoundToInt(Mathf.Clamp01(at.x) * (maskTexture.width - 1));
                int y = Mathf.RoundToInt(Mathf.Clamp01(at.y) * (maskTexture.height - 1));
                var target = mask[y * maskTexture.width + x].r < 128 ? lens : housing;
                target.Add(triangles[i]); target.Add(triangles[i + 1]); target.Add(triangles[i + 2]);
            }
            // У подтверждённого source ровно 52 треугольника окна и 1648 корпуса. Изменённый пак требует нового аудита.
            if (lens.Count != 52 * 3 || housing.Count != 1648 * 3)
                throw new InvalidOperationException($"TR15 XPS2: изменённая геометрия/UV, housing={housing.Count / 3}, lens={lens.Count / 3}");
        }

        /// <summary>Прежний вшитый узел коллиматора (не внутри экземпляра комплекта SightKit_*); null — его нет.</summary>
        private static GameObject LegacyNode(GameObject root)
        {
            var nodes = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.name == OpticName && !PrefabUtility.IsPartOfPrefabInstance(f.gameObject)).Select(f => f.gameObject).ToArray();
            if (nodes.Length > 1) throw new InvalidOperationException($"TR15 XPS2: узлов {OpticName} {nodes.Length}");
            return nodes.Length == 1 ? nodes[0] : null;
        }

        private static MeshFilter ValidateBinding(GameObject node)
        {
            MeshFilter filter = node.GetComponent<MeshFilter>();
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || renderer.sharedMaterials.Length < 1 || renderer.sharedMaterials[0] == null ||
                (AssetDatabase.GetAssetPath(filter.sharedMesh) != RawMeshPath && AssetDatabase.GetAssetPath(filter.sharedMesh) != MeshPath) ||
                AssetDatabase.GetAssetPath(renderer.sharedMaterials[0]) != HousingMaterialPath)
                throw new InvalidOperationException("TR15 XPS2: чужой mesh/material binding; калибровка отказывается его перезаписывать");
            return filter;
        }

        public static void Apply()
        {
            WeaponModelPreviewScope.CheckEditor();
            ReadInputs(out Mesh raw, out Shader shader, out List<int> housing, out List<int> lens,
                       out WeaponInfo info, out WeaponSightCalibrationSettings settings);
            KinemationWeapon.EnsureFolder("Assets/Art/Weapons/Kinemation/TR15/Materials");
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool create = saved == null;
            if (create) saved = new Mesh();
            try
            {
                UpdateMesh(saved, raw, housing, lens);
                if (create) AssetDatabase.CreateAsset(saved, MeshPath);
                else EditorUtility.SetDirty(saved);
                AssetDatabase.SaveAssetIfDirty(saved);
            }
            catch { if (create && !EditorUtility.IsPersistent(saved)) UnityEngine.Object.DestroyImmediate(saved); throw; }
            BuildReticleMaterial(shader);
            if (LegacyNode(AssetDatabase.LoadAssetAtPath<GameObject>(WeaponPath)) == null) return;

            // Снятие прежнего вшитого коллиматора: на узел ссылается только его WeaponOpticView (проверено 2026-10-20).
            var root = PrefabUtility.LoadPrefabContents(WeaponPath);
            try
            {
                GameObject node = LegacyNode(root);
                ValidateBinding(node);
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true).Where(b => b != null && !b.transform.IsChildOf(node.transform)))
                {
                    var property = new SerializedObject(behaviour).GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null
                            && (property.objectReferenceValue == node || (property.objectReferenceValue is Component c && c.gameObject == node)))
                            throw new InvalidOperationException($"TR15 XPS2: {behaviour.GetType().Name} на {behaviour.name} ссылается на {OpticName} — снятие отменено");
                }
                UnityEngine.Object.DestroyImmediate(node);
                if (PrefabUtility.SaveAsPrefabAsset(root, WeaponPath) == null)
                    throw new InvalidOperationException("TR15 XPS2: сохранение префаба не удалось");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            // SaveAsPrefabAsset пишет NetworkIdentity._assetId = 0 (контент префаба для Mirror — объект сцены); с нулём
            // клиент сборки не заспавнит TR15. Вернуть канон и сверить на диске, как WeaponSystemAuthoring.
            VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(new[] { WeaponPath }, false);
            uint canonical = Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(WeaponPath)));
            uint? onDisk = VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.ReadOnDisk(WeaponPath);
            if (onDisk != canonical)
                throw new InvalidOperationException($"TR15 XPS2: NetworkIdentity._assetId на диске {onDisk}, ожидается {canonical}");
        }

        private static void UpdateMesh(Mesh target, Mesh source, List<int> housing, List<int> lens)
        {
            // CopySerialized может оставить старый GPU buffer. Mesh API обновляет его, сохраняя сам asset/GUID.
            // Копируются сырые vertex streams: normals/tangents/colors/все UV не теряют формат или размерность.
            using (var data = Mesh.AcquireReadOnlyMeshData(source))
            {
                target.Clear(false);
                target.indexFormat = source.indexFormat;
                target.SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                {
                    var bytes = data[0].GetVertexData<byte>(stream);
                    target.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream);
                }
                target.bindposes = source.bindposes;
                target.subMeshCount = 2;
                target.SetTriangles(housing, 0, false);
                target.SetTriangles(lens, 1, false);
                target.bounds = source.bounds;
                target.name = "TR15_XPS2_Optic";
                target.UploadMeshData(false);
            }
        }

        /// <summary>Game-owned материал окна: шейдер сетки конечного фокуса, параметры эталона.</summary>
        private static Material BuildReticleMaterial(Shader shader)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ReticleMaterialPath);
            bool create = material == null;
            if (create) material = new Material(shader) { name = "TR15_XPS2_Reticle" };
            material.shader = shader;
            material.SetColor("_ReticleColor", ReticleColor);
            material.enableInstancing = true;
            if (create) AssetDatabase.CreateAsset(material, ReticleMaterialPath);
            else EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static Color32[] Read(Texture source, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D read = null;
            try
            {
                Graphics.Blit(source, temporary); RenderTexture.active = temporary;
                read = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                read.ReadPixels(new Rect(0, 0, width, height), 0, 0); read.Apply();
                return read.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                if (read != null) UnityEngine.Object.DestroyImmediate(read);
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }
}
