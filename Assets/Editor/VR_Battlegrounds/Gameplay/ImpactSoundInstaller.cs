using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Звук падения оружию и магазинам реестра (<see cref="ImpactSound"/>): ствол — тяжёлый металлический удар,
    /// магазин — лёгкий. Зовётся командой меню и сборщиком пака (<c>HandsPackWeaponBuilder</c>) — пересборка
    /// перезаписывает префаб. Проверка — <c>ImpactSoundTests</c>.
    /// </summary>
    public static class ImpactSoundInstaller
    {
        private const string Folder = "Assets/Audio/SFX/Impacts/";
        private static readonly string[] WeaponClips =
        {
            "IMPACT_Metal_Tool_Drop_Hard_Surface_mono.wav",
            "IMPACT_Metal_Rod_Drop_Hard_Surface_mono.wav",
            "IMPACT_Metal_Objects_Drop_Hard_Surface_01_mono.wav",
        };
        private static readonly string[] MagazineClips =
        {
            "IMPACT_Metal_Soft_Plate_01_mono.wav",
            "IMPACT_Metal_Soft_Plate_02_mono.wav",
        };

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Apply Impact Sounds")]
        public static void ApplyRegistry()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/Resources/WeaponRegistry.asset");
            int count = 0;
            foreach (WeaponInfo info in registry.Weapons)
            {
                if (info.WeaponPrefab != null && ApplyToAsset(info.WeaponPrefab, false)) count++;
                if (info.MagazinePrefab != null && ApplyToAsset(info.MagazinePrefab, true)) count++;
            }
            GameLog.Debug.Info($"[ImpactSoundInstaller] Звук падения поставлен: {count} префабов.");
        }

        private static bool ApplyToAsset(GameObject prefab, bool magazine)
        {
            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Apply(root, magazine);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Ставит или обновляет <see cref="ImpactSound"/> и его источник на корне предмета.</summary>
        public static void Apply(GameObject root, bool magazine)
        {
            // Не «??»: в редакторе GetComponent отсутствующего компонента — «поддельный null».
            var impact = root.GetComponent<ImpactSound>();
            if (impact == null) impact = root.AddComponent<ImpactSound>();
            var so = new SerializedObject(impact);

            AudioSource source = so.FindProperty("_source").objectReferenceValue as AudioSource;
            if (source == null)
            {
                var host = new GameObject("ImpactAudio");
                host.transform.SetParent(root.transform, false);
                source = host.AddComponent<AudioSource>();
            }
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 1f;
            source.minDistance = 0.5f;
            source.maxDistance = 15f;
            source.rolloffMode = AudioRolloffMode.Linear;
            so.FindProperty("_source").objectReferenceValue = source;

            AudioClip[] clips = (magazine ? MagazineClips : WeaponClips).Select(n => AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + n)).ToArray();
            if (clips.Any(c => c == null)) GameLog.Error("[ImpactSoundInstaller] Нет клипа в " + Folder);
            SerializedProperty list = so.FindProperty("_clips");
            list.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            so.FindProperty("_maxVolume").floatValue = magazine ? 0.6f : 0.9f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
