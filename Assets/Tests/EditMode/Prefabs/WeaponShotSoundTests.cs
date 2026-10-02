using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// У каждого ствола арсенала свой голос: два ствола реестра не стреляют одним и тем же клипом.
    ///
    /// <para>
    /// Класс ошибки: сборщик копирует звук выстрела у донора (<c>Gun_real</c> для пистолетов, M16 для винтовок), и
    /// новый ствол без своего звука молча звучит чужим. Так тяжёлый <c>Revolver</c> стрелял тихим выстрелом лёгкого
    /// пистолета (<c>9Run_Set/Shot 4</c>, как у <c>Gun_real</c>) — «как с глушителем» (жалоба пользователя, 2026-10-01).
    /// </para>
    /// </summary>
    public class WeaponShotSoundTests
    {
        private const string RegistryPath = "Assets/Data/Weapons/Resources/WeaponRegistry.asset";

        [Test]
        public void Стволы_реестра_не_делят_звук_выстрела()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(RegistryPath);
            Assert.IsNotNull(registry, "Нет WeaponRegistry.");

            var byClip = new Dictionary<AudioClip, List<string>>();
            var failures = new List<string>();
            foreach (WeaponInfo info in registry.Weapons.Where(w => w != null && w.WeaponPrefab != null))
            {
                var weapon = info.WeaponPrefab.GetComponent<UxrFirearmWeapon>();
                if (weapon == null) continue;
                var clip = new SerializedObject(weapon).FindProperty("_triggers").GetArrayElementAtIndex(0)
                                                       .FindPropertyRelative("_shotAudio._clip").objectReferenceValue as AudioClip;
                if (clip == null) { failures.Add($"{info.WeaponPrefab.name}: нет звука выстрела"); continue; }
                if (!byClip.TryGetValue(clip, out List<string> owners)) byClip[clip] = owners = new List<string>();
                owners.Add(info.WeaponPrefab.name);
            }

            failures.AddRange(byClip.Where(p => p.Value.Count > 1)
                                    .Select(p => $"{string.Join(", ", p.Value)} — один выстрел {AssetDatabase.GetAssetPath(p.Key)}"));
            Assert.IsEmpty(failures, "Стволы звучат одинаково:\n" + string.Join("\n", failures));
        }
    }
}
