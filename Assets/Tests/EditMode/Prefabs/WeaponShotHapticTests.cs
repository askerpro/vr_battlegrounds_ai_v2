using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Haptics;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Вибрация выстрела стволов реестра — <see cref="UxrHapticClipType.ShotBig" />, как было у всех стволов до отказа
    /// от доноров сборки.
    ///
    /// <para>
    /// <b>Дефект.</b> Сборщик без доноров ставил спуску дефолт SDK — <see cref="UxrHapticClipType.Shot" /> (слабее): у всех
    /// стволов молча ослабла отдача в руку (ревью weapon-system 2026-10-24). Своего клипа вибрации у спуска нет —
    /// играет тип по умолчанию, поэтому проверяется он; пишет его <c>HandsPackBuildDefaults.NewTrigger</c>.
    /// </para>
    /// </summary>
    public class WeaponShotHapticTests
    {
        [Test]
        public void Вибрация_выстрела_стволов_реестра_ShotBig()
        {
            var registry = Resources.Load<WeaponRegistry>("WeaponRegistry");
            Assert.That(registry, Is.Not.Null, "Контроль: реестр оружия в Resources.");
            SerializedProperty list = new SerializedObject(registry).FindProperty("_weapons");
            var problems = new List<string>();
            int checkedCount = 0;
            for (int i = 0; i < list.arraySize; i++)
            {
                var info = list.GetArrayElementAtIndex(i).objectReferenceValue as WeaponInfo;
                if (info == null || info.WeaponPrefab == null) continue;
                var firearm = info.WeaponPrefab.GetComponent<UxrFirearmWeapon>();
                if (firearm == null)
                {
                    problems.Add($"{info.WeaponPrefab.name}: нет UxrFirearmWeapon");
                    continue;
                }

                SerializedProperty triggers = new SerializedObject(firearm).FindProperty("_triggers");
                for (int t = 0; t < triggers.arraySize; t++)
                {
                    SerializedProperty type = triggers.GetArrayElementAtIndex(t).FindPropertyRelative("_shotHapticClip._fallbackClipType");
                    checkedCount++;
                    if (type.intValue != (int)UxrHapticClipType.ShotBig)
                        problems.Add($"{info.WeaponPrefab.name}: спуск {t} — {(UxrHapticClipType)type.intValue}, ожидается {UxrHapticClipType.ShotBig}");
                }
            }

            Assert.That(checkedCount, Is.GreaterThan(0), "Контроль: в реестре есть спуски.");
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }
    }
}
