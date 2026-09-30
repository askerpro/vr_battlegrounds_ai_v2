using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Брошенное оружие и магазины падают со звуком (решение пользователя): у каждого ствола и магазина реестра —
    /// <see cref="ImpactSound"/> с клипами и объёмным источником; слабый удар и повтор сразу за ударом молчат.
    /// </summary>
    public class ImpactSoundTests
    {
        [Test]
        public void Оружие_и_магазины_реестра_звучат_при_падении()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/Resources/WeaponRegistry.asset");
            var failures = new List<string>();
            foreach (WeaponInfo info in registry.Weapons)
            foreach (GameObject prefab in new[] { info.WeaponPrefab, info.MagazinePrefab })
            {
                if (prefab == null) continue;
                var impact = prefab.GetComponent<ImpactSound>();
                if (impact == null) { failures.Add($"{prefab.name}: нет ImpactSound — падает молча"); continue; }
                if (impact.Clips == null || impact.Clips.Length == 0 || System.Array.Exists(impact.Clips, c => c == null))
                    failures.Add($"{prefab.name}: ImpactSound без клипов");
                if (impact.Source == null) failures.Add($"{prefab.name}: ImpactSound без источника");
                else
                {
                    if (impact.Source.playOnAwake) failures.Add($"{prefab.name}: источник удара — Play On Awake");
                    if (impact.Source.spatialBlend < 0.5f) failures.Add($"{prefab.name}: удар не объёмный");
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void Слабый_удар_и_повтор_молчат()
        {
            Assert.IsFalse(ImpactSound.ShouldPlay(0.5f, 10f, 1.2f, 0.2f), "Улёгшийся предмет стучит.");
            Assert.IsTrue(ImpactSound.ShouldPlay(3f, 10f, 1.2f, 0.2f));
            Assert.IsFalse(ImpactSound.ShouldPlay(3f, 0.05f, 1.2f, 0.2f), "Отскоки одного падения — очередью.");
            Assert.Less(ImpactSound.Volume(1.2f, 1.2f, 5f, 1f), ImpactSound.Volume(5f, 1.2f, 5f, 1f), "Сильный удар не громче слабого.");
        }
    }
}
