using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Звук падения оружию и магазинам реестра (<see cref="ImpactSound"/>): вид предмета (ствол — тяжёлый удар, магазин —
    /// лёгкий), объёмный источник и громкость. Клипы по умолчанию в префаб не пишутся (решение пользователя 2026-10-23:
    /// звуковые дефолты — данные в ассете): без своих клипов предмет звучит набором своего вида из
    /// <see cref="ImpactSoundDefaults"/> (<c>Resources/ImpactSoundDefaults.asset</c>) в момент удара. Свои клипы предмета —
    /// оверрайд, установщик их не трогает; копию набора по умолчанию (прежняя запись установщика) снимает.
    /// Зовётся командой меню и сборщиком пака (<c>HandsPackWeaponBuilder</c>). Проверка — <c>ImpactSoundTests</c>.
    /// </summary>
    public static class ImpactSoundInstaller
    {
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

        /// <summary>
        /// Ставит или обновляет <see cref="ImpactSound"/> и его источник на корне предмета: вид, источник, громкость. Свои
        /// клипы, совпадающие с набором вида в <see cref="ImpactSoundDefaults"/>, снимаются (звучит тот же набор из ассета).
        /// </summary>
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

            ImpactSoundKind kind = magazine ? ImpactSoundKind.Magazine : ImpactSoundKind.Weapon;
            so.FindProperty("_kind").intValue = (int)kind;
            SerializedProperty own = so.FindProperty("_clips");
            ImpactSoundDefaults defaults = ImpactSoundDefaults.Instance;
            AudioClip[] common = defaults != null ? defaults.For(kind) : System.Array.Empty<AudioClip>();
            if (own.arraySize > 0 && SameSet(own, common)) own.arraySize = 0;
            if (own.arraySize == 0 && common.Length == 0)
                GameLog.Error($"[ImpactSoundInstaller] {root.name}: нет своих клипов удара и набора «{kind}» в Resources/ImpactSoundDefaults.asset — падает молча.");
            so.FindProperty("_maxVolume").floatValue = magazine ? 0.6f : 0.9f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Свои клипы — тот же набор, что дефолт (порядок не важен: вариант выбирается случайно).</summary>
        internal static bool SameSet(SerializedProperty own, AudioClip[] common)
        {
            if (common == null || common.Length == 0) return false;
            var mine = new AudioClip[own.arraySize];
            for (int i = 0; i < mine.Length; i++) mine[i] = own.GetArrayElementAtIndex(i).objectReferenceValue as AudioClip;
            return SameSet(mine, common);
        }

        /// <summary>Два набора клипов совпадают как множества (без пустых элементов).</summary>
        internal static bool SameSet(AudioClip[] a, AudioClip[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0) return false;
            if (a.Any(c => c == null) || b.Any(c => c == null)) return false;
            return a.Distinct().OrderBy(c => c.GetInstanceID()).SequenceEqual(b.Distinct().OrderBy(c => c.GetInstanceID()));
        }
    }
}
