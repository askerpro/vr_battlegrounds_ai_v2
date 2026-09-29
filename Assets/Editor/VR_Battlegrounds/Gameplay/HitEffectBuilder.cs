using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Собирает отклик на попадание по игроку (T-37): материалы пятна крови и объект
    /// <see cref="PlayerHitEffects"/> в <c>Resources</c>. Генерация, а не ручная правка.
    ///
    /// <para>
    /// <b>Пятно, не брызги</b> (решение пользователя): точно в точке попадания — тёмное отверстие
    /// (<c>BulletHoleRockAlbedo</c> Particle Pack, рваная маска), вокруг — подтёк (<c>GoopStreakAlbedo</c>) в цвете
    /// крови. <c>FleshImpacts</c> пака не взят: демо-мишень с коллайдером, зацикленные системы, legacy-шейдер,
    /// декаль-рана с кожей вокруг. Материалы — <c>URP/Unlit</c>, прозрачные: без освещения, дёшево на Quest.
    /// Проверка — <c>HitEffectsTests</c>.
    /// </para>
    /// </summary>
    public static class HitEffectBuilder
    {
        public const string StreakMaterialPath = "Assets/Prefabs/Weapons/Effects/Materials/BloodStreak.mat";
        public const string HoleMaterialPath = "Assets/Prefabs/Weapons/Effects/Materials/BloodHole.mat";
        public const string ServicePath = "Assets/Resources/PlayerHitEffects.prefab";
        private const string Textures = "Assets/ThirdParty/ParticlePack/EffectExamples/Weapon Effects/Textures/";

        private static readonly string[] ClipPaths =
        {
            "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/5Knife_Set/Full_Hit_k.mp3",
            "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/5Knife_Set/Full_Hit_r.mp3",
        };

        /// <summary>Попадание в голову — жёсткий удар (как по шлему).</summary>
        private static readonly string[] HeadClipPaths =
        {
            "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/2Flashlight_Set/Flashlight_Hit 2.mp3",
        };

        private static readonly Color Blood = new Color(0.33f, 0.02f, 0.02f, 0.85f);
        private static readonly Color Hole = new Color(0.1f, 0.01f, 0.01f, 1f);

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Build Hit Effects")]
        public static void Build()
        {
            Material streak = BuildMaterial(StreakMaterialPath, Textures + "GoopStreakAlbedo.tif", Blood);
            Material hole = BuildMaterial(HoleMaterialPath, Textures + "BulletHoleRockAlbedo.tif", Hole);
            BuildService(streak, hole);
            AssetDatabase.SaveAssets();
            Debug.Log($"[HitEffectBuilder] Собрано: {StreakMaterialPath}, {HoleMaterialPath}, {ServicePath}.");
        }

        private static Material BuildMaterial(string path, string texture, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_Cull", 2f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetClips(SerializedProperty property, string[] paths)
        {
            property.arraySize = paths.Length;
            for (int i = 0; i < paths.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(paths[i]);
        }

        private static void BuildService(Material streak, Material hole)
        {
            var root = new GameObject(PlayerHitEffects.Resource);
            try
            {
                PlayerHitEffects service = root.AddComponent<PlayerHitEffects>();
                var so = new SerializedObject(service);
                so.FindProperty("_streakMaterial").objectReferenceValue = streak;
                so.FindProperty("_holeMaterial").objectReferenceValue = hole;
                SerializedProperty clips = so.FindProperty("_clips");
                clips.arraySize = ClipPaths.Length;
                for (int i = 0; i < ClipPaths.Length; i++)
                    clips.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPaths[i]);
                SetClips(so.FindProperty("_headClips"), HeadClipPaths);
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, ServicePath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
