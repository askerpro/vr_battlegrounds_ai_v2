using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Вспышка у дула оружия проекта — только частицы: без своего звука и света.
    ///
    /// <para>
    /// <b>Дефект.</b> У M16 и <c>Gun_real</c> у дула стояла вспышка <c>Fn_Scar</c> пака Hands: в ней
    /// свой <c>AudioSource</c> с <c>MachineGun2</c> (Play On Awake) и точечный свет. После замены
    /// звука выстрела пистолета он звучал дважды — новым клипом и пулемётом из вспышки; свет на
    /// каждый выстрел дорог на Quest. Замена — одноразовый <c>Muzzle_Default</c> из Particle Pack.
    /// </para>
    /// </summary>
    public class MuzzleEffectTests
    {
        private const string ProjectEffects = "Assets/Prefabs/Weapons/Effects/";

        [Test]
        public void Вспышка_у_дула_без_звука_и_света_и_привязана_к_оружию()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (GameObject weapon in ProjectWeapons())
            {
                SerializedProperty shots = new SerializedObject(weapon.GetComponent<UxrProjectileSource>()).FindProperty("_shotTypes");
                for (int i = 0; i < shots.arraySize; i++)
                {
                    SerializedProperty shot = shots.GetArrayElementAtIndex(i);
                    var muzzle = shot.FindPropertyRelative("_prefabInstantiateOnTipWhenShot").objectReferenceValue as GameObject;
                    string name = $"{weapon.name} (выстрел {i})";

                    // Дробинкам вспышка не нужна: она одна на выстрел, у основного снаряда.
                    if (muzzle == null)
                    {
                        if (i == 0) failures.Add($"{name}: нет вспышки у дула");
                        continue;
                    }

                    checks++;
                    if (!AssetDatabase.GetAssetPath(muzzle).StartsWith(ProjectEffects))
                        failures.Add($"{name}: вспышка {muzzle.name} не из {ProjectEffects}");
                    if (muzzle.GetComponentsInChildren<AudioSource>(true).Length > 0)
                        failures.Add($"{name}: у вспышки {muzzle.name} свой звук — выстрел прозвучит дважды");
                    if (muzzle.GetComponentsInChildren<Light>(true).Length > 0)
                        failures.Add($"{name}: у вспышки {muzzle.name} источник света — дорого на Quest");
                    foreach (ParticleSystem ps in muzzle.GetComponentsInChildren<ParticleSystem>(true))
                        if (ps.main.loop) failures.Add($"{name}: {muzzle.name}/{ps.name} зациклен");
                    if (!shot.FindPropertyRelative("_prefabInstantiateOnTipParent").boolValue)
                        failures.Add($"{name}: вспышка не привязана к оружию — не масштабируется с ним и отстаёт от дула");
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Контроль: вспышек у оружия проекта не найдено.");
            Assert.IsEmpty(failures, "Вспышка у дула:\n" + string.Join("\n", failures));
        }

        /// <summary>Оружие арсенала, кроме вариантов сэмплов UltimateXR.</summary>
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
