using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Попадание оружия проекта выглядит как попадание пули, а не как сэмпл UltimateXR.
    ///
    /// <para>
    /// <b>Дефект.</b> Попадания были скопированы с сэмплов SDK: искры, к концу жизни синие или
    /// оранжевые, и светящийся анимированный круг на стене (синий у M16 и <c>Gun_real</c>).
    /// Замена — одноразовый эффект из Particle Pack (пыль и каменная крошка, <c>Impact_Default</c>)
    /// и дырка-декаль SDK без светящегося круга (<c>ImpactDecal_Default</c>).
    /// </para>
    ///
    /// <para>
    /// Эффекты Particle Pack сделаны для демо-сцены: зациклены и лежат вместе с мишенью с
    /// коллайдером. Тест требует от эффекта попадания обратного: ни коллайдеров, ни мешей,
    /// все системы одноразовые, шейдеры URP.
    /// </para>
    /// </summary>
    public class ImpactEffectTests
    {
        private const string SdkSamples = "Assets/ThirdParty/UltimateXR/Samples/";

        [Test]
        public void Попадание_оружия_проекта_не_из_сэмплов_SDK()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (GameObject weapon in ProjectWeapons())
            {
                SerializedProperty shots = new SerializedObject(weapon.GetComponent<UxrProjectileSource>()).FindProperty("_shotTypes");
                for (int i = 0; i < shots.arraySize; i++)
                {
                    checks++;
                    SerializedProperty shot = shots.GetArrayElementAtIndex(i);
                    string name = $"{weapon.name} (выстрел {i})";

                    var impact = shot.FindPropertyRelative("_prefabInstantiateOnImpact").objectReferenceValue as GameObject;
                    var decal = shot.FindPropertyRelative("_prefabScenarioImpactDecal").objectReferenceValue as UxrImpactDecal;

                    if (impact == null) failures.Add($"{name}: нет эффекта попадания");
                    else if (AssetDatabase.GetAssetPath(impact).StartsWith(SdkSamples)) failures.Add($"{name}: эффект попадания из сэмплов SDK — {impact.name}");

                    if (decal == null) failures.Add($"{name}: нет декали попадания");
                    else if (decal.GetComponentsInChildren<UltimateXR.Animation.Materials.UxrAnimatedTextureFlipbook>(true).Length > 0)
                        failures.Add($"{name}: у декали {decal.name} светящийся анимированный круг");
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Контроль: оружия проекта не найдено.");
            Assert.IsEmpty(failures, "Попадание из сэмплов SDK:\n" + string.Join("\n", failures));
        }

        [Test]
        public void Эффект_попадания_одноразовый_и_без_демо_мишени()
        {
            var failures = new List<string>();
            var seen = new HashSet<GameObject>();

            foreach (GameObject weapon in ProjectWeapons())
            {
                foreach (UxrShotDescriptor shot in weapon.GetComponent<UxrProjectileSource>().ShotTypes)
                {
                    GameObject impact = shot.PrefabInstantiateOnImpact;
                    if (impact == null || !seen.Add(impact)) continue;

                    if (impact.GetComponentsInChildren<Collider>(true).Length > 0) failures.Add($"{impact.name}: коллайдер — пули будут попадать в сам эффект");
                    if (impact.GetComponentsInChildren<MeshRenderer>(true).Length > 0) failures.Add($"{impact.name}: меш (демо-мишень пака)");

                    ParticleSystem[] systems = impact.GetComponentsInChildren<ParticleSystem>(true);
                    if (systems.Length == 0) failures.Add($"{impact.name}: нет частиц");

                    foreach (ParticleSystem ps in systems)
                    {
                        if (ps.main.loop) failures.Add($"{impact.name}/{ps.name}: зациклен — эффект будет бить по кругу");
                        if (ps.emission.burstCount == 0 && ps.emission.rateOverTime.constantMax <= 0f && ps.emission.rateOverTimeMultiplier <= 0f)
                            failures.Add($"{impact.name}/{ps.name}: ничего не выпускает сам (суб-эмиттер демо)");

                        Material material = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                        // Legacy-шейдеры встроенного конвейера (у FleshImpacts пака) URP рисует как
                        // простой unlit, но мимо SRP Batcher — на Quest лишние вызовы отрисовки.
                        if (material == null || !material.shader.isSupported || material.shader.name.StartsWith("Legacy Shaders/"))
                            failures.Add($"{impact.name}/{ps.name}: шейдер {(material ? material.shader.name : "нет")} — не родной URP");
                    }
                }
            }

            Assert.That(seen.Count, Is.GreaterThan(0), "Контроль: эффектов попадания не найдено.");
            Assert.IsEmpty(failures, "Эффект попадания не годится для игры:\n" + string.Join("\n", failures));
        }

        /// <summary>Оружие арсенала, кроме вариантов сэмплов UltimateXR (Gun, Machinegun, Shotgun).</summary>
        private static IEnumerable<GameObject> ProjectWeapons()
        {
            var seen = new HashSet<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }))
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.WeaponPrefab == null || info.WeaponPrefab.GetComponent<UxrProjectileSource>() == null) continue;

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(info.WeaponPrefab);
                if (source != null && AssetDatabase.GetAssetPath(source).StartsWith("Assets/ThirdParty/")) continue;

                if (seen.Add(info.WeaponPrefab)) yield return info.WeaponPrefab;
            }
        }
    }
}
