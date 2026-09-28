using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Ствол, упёртый в геометрию, не стреляет — пуля не проходит сквозь стену.
    ///
    /// <para>
    /// <b>Дефект.</b> Снаряд рождается у дула (<c>ShotOriginTests</c>): ствол, просунутый сквозь
    /// стену, стрелял бы по ту сторону. <see cref="BarrelObstruction" /> проверяет отрезок от
    /// казённой части до среза ствола; чужой коллайдер на нём запрещает выстрел
    /// (<c>UxrWeapon.IsUseBlocked</c>, патч SDK 15), своё оружие и аватар-владелец — нет.
    /// </para>
    ///
    /// <para>
    /// Физика — в preview-сцене: своя <see cref="PhysicsScene" />, игровой цикл не нужен.
    /// </para>
    /// </summary>
    public class BarrelObstructionTests
    {
        private const float MinBarrelChecked = 0.05f;

        [Test]
        public void У_оружия_проекта_есть_проверка_ствола()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (GameObject weapon in ProjectWeapons())
            {
                checks++;
                var guard = weapon.GetComponent<BarrelObstruction>();
                if (guard == null) { failures.Add($"{weapon.name}: нет BarrelObstruction — ствол сквозь стену стреляет"); continue; }
                if (guard.Breech == null) { failures.Add($"{weapon.name}: у BarrelObstruction не назначена казённая точка"); continue; }

                Transform tip = weapon.GetComponent<UxrProjectileSource>().ShotTypes[0].Tip;
                float length = Vector3.Distance(guard.Breech.position, tip.position);
                if (length < MinBarrelChecked)
                    failures.Add($"{weapon.name}: проверяется только {length * 100f:F0} см ствола");
            }

            Assert.That(checks, Is.GreaterThan(0), "Контроль: оружия проекта не найдено.");
            Assert.IsEmpty(failures, "Проверка ствола:\n" + string.Join("\n", failures));
        }

        [Test]
        public void Стена_на_стволе_запрещает_выстрел_а_свой_аватар_нет()
        {
            int checks = 0;

            foreach (GameObject prefab in ProjectWeapons())
            {
                if (prefab.GetComponent<BarrelObstruction>() == null) continue; // ловит первый тест
                checks++;

                Scene scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    var weapon = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var guard = weapon.GetComponent<BarrelObstruction>();
                    Transform tip = weapon.GetComponent<UxrProjectileSource>().ShotTypes[0].Tip;
                    Vector3 middle = Vector3.Lerp(guard.Breech.position, tip.position, 0.5f);

                    var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    SceneManager.MoveGameObjectToScene(wall, scene);
                    wall.transform.localScale = Vector3.one * 0.05f;

                    wall.transform.position = middle + Vector3.up * 1f;
                    Physics.SyncTransforms();
                    Assert.IsFalse(guard.IsBarrelObstructed(null), $"{prefab.name}: ствол свободен, а проверка видит помеху (своё оружие не исключено?).");

                    wall.transform.position = middle;
                    Physics.SyncTransforms();
                    Assert.IsTrue(guard.IsBarrelObstructed(null), $"{prefab.name}: стена на стволе, а выстрел не запрещён.");

                    var owner = new GameObject("Owner");
                    SceneManager.MoveGameObjectToScene(owner, scene);
                    wall.transform.SetParent(owner.transform, true);
                    Physics.SyncTransforms();
                    Assert.IsFalse(guard.IsBarrelObstructed(owner.transform), $"{prefab.name}: ствол задевает самого владельца, а выстрел запрещён.");

                    var firearm = weapon.GetComponent<UxrFirearmWeapon>();
                    firearm.IsUseBlocked = true;
                    Assert.IsFalse(firearm.CanUse, $"{prefab.name}: IsUseBlocked не запрещает CanUse — патч SDK 15 не на месте.");
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Контроль: оружия с проверкой ствола не найдено.");
        }

        /// <summary>Оружие арсенала, кроме вариантов сэмплов UltimateXR.</summary>
        private static IEnumerable<GameObject> ProjectWeapons()
        {
            var seen = new HashSet<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" }))
            {
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(AssetDatabase.GUIDToAssetPath(guid));
                if (info == null || info.WeaponPrefab == null || info.WeaponPrefab.GetComponent<UxrFirearmWeapon>() == null) continue;

                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(info.WeaponPrefab);
                if (source != null && AssetDatabase.GetAssetPath(source).StartsWith("Assets/ThirdParty/")) continue;

                if (seen.Add(info.WeaponPrefab)) yield return info.WeaponPrefab;
            }
        }
    }
}
