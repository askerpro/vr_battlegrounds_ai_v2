using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Каждая стена арсенала вмещает весь доступный арсенал игры (решение пользователя). Доступный арсенал —
    /// <see cref="WeaponRegistry" />; места на стене не хватает — из реестра убирается слабейшее, а не прячется
    /// на другой стене.
    ///
    /// <para>
    /// Класс ошибки: набор стены правился по месту — слот базовой стены, переопределение в префабе карты, — и
    /// разные стены показывали разное, часть стволов была только на соседней грани. Теперь набор задаёт одна
    /// базовая стена, переопределять её слоты в картах и сценах нельзя.
    /// </para>
    /// </summary>
    public class ArsenalWallCoversRegistryTests
    {
        private const string Wall = "Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab";
        private static readonly string[] MapRoots = { "Assets/Prefabs", "Assets/Scenes" };

        [Test]
        public void Стена_вмещает_весь_реестр()
        {
            WeaponRegistry registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/Resources/WeaponRegistry.asset");
            Assert.IsNotNull(registry, "Нет WeaponRegistry.");

            var onWall = new HashSet<WeaponInfo>();
            var wall = AssetDatabase.LoadAssetAtPath<GameObject>(Wall);
            foreach (FirearmSlotController slot in wall.GetComponentsInChildren<FirearmSlotController>(true))
            {
                var info = new SerializedObject(slot).FindProperty("_weaponInfo").objectReferenceValue as WeaponInfo;
                if (info != null) onWall.Add(info);
            }

            var missing = registry.Weapons.Where(w => w != null && !onWall.Contains(w)).Select(w => w.name).ToList();
            var extra = onWall.Where(w => !registry.Weapons.Contains(w)).Select(w => w.name).ToList();

            Assert.IsEmpty(missing, "Стволов реестра нет на стене — места не хватает, убери слабейшее из реестра: " + string.Join(", ", missing));
            Assert.IsEmpty(extra, "На стене стволы вне реестра: " + string.Join(", ", extra));
        }

        [Test]
        public void Слоты_стены_не_переопределяются_в_картах()
        {
            var offenders = new List<string>();
            foreach (string root in MapRoots)
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // Заготовки самого арсенала (стена, варианты слотов) — источник набора, не карта.
                if (path.StartsWith("Assets/Prefabs/Arsenal/")) continue;
                if (!path.EndsWith(".prefab") && !path.EndsWith(".unity")) continue;

                // Переопределение слота в экземпляре — строка propertyPath: _weaponInfo в PrefabInstance.
                string text = System.IO.File.ReadAllText(path);
                if (text.Contains("propertyPath: _weaponInfo")) offenders.Add(path);
            }

            Assert.IsEmpty(offenders, "Слоты стены переопределены вне CommonOpenArsenalStation — стены покажут разное:\n" + string.Join("\n", offenders));
        }
    }
}
