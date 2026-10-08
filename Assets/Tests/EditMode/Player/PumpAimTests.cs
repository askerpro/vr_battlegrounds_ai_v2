using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;
using WS = VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Рука на помпе наводит ствол, как рука на цевье винтовки.
    ///
    /// <para>
    /// <b>Дефект.</b> У помпы <c>Shotgun_real</c> стояли <c>Control Parent Direction</c> выключен и
    /// <c>Ignore Grabbable Parent Dependency</c> включён — флаги, скопированные с рукоятки затвора
    /// M16. Для затвора они верны (он не должен крутить винтовку), а помпу держит вторая рука, и
    /// дробовик переставал за ней доворачиваться. У сэмплового дробовика UltimateXR оба флага
    /// наоборот, и в шлеме он доворачивается.
    /// </para>
    ///
    /// <para>
    /// <b>Почему проверка флагов, а не поворота.</b> Деталь, зависящую от родителя, UltimateXR
    /// решает в полном цикле обновления; в EditMode через <c>TwoHandGrabHarness</c> она разваливается
    /// даже у сэмплового дробовика (помпа уходит на метр от места) — это шум харнесса. Поэтому
    /// проверяется конфигурация, с которой механика работает у сэмпла.
    /// </para>
    /// </summary>
    public class PumpAimTests
    {
        [Test]
        public void Помпа_наводит_ствол()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Weapons" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var weapon = AssetDatabase.LoadAssetAtPath<GameObject>(path);

                var pumps = new List<UxrGrabbableObject>();
                foreach (UxrShotgunPump pumpAction in weapon.GetComponentsInChildren<UxrShotgunPump>(true))
                {
                    var pump = new SerializedObject(pumpAction).FindProperty("_pump").objectReferenceValue as UxrGrabbableObject;
                    if (pump == null) failures.Add($"{path}: у UxrShotgunPump не назначена помпа");
                    else pumps.Add(pump);
                }

                // Этап drive: у пилотов WeaponSystem (FABARM) UxrShotgunPump снят — помпой управляет хост, ручка помпы —
                // Rig.Handle. Помпу от затвора отличает ось ReleasedAction = Stay (у помпы нет пружины, решение S4);
                // затвор с пружиной (Herrington) наводить ствол не должен, поэтому он в проверку не входит.
                foreach (WeaponSystem host in weapon.GetComponentsInChildren<WeaponSystem>(true))
                    if (host.Rig.HasAction && host.Rig.ReleasedAction == WS.WeaponReleasedAction.Stay)
                        pumps.Add(host.Rig.Handle);

                foreach (UxrGrabbableObject pump in pumps)
                {
                    checks++;
                    if (!pump.ControlParentDirection)
                        failures.Add($"{path} / {pump.name}: Control Parent Direction выключен — рука на помпе не наводит ствол");
                    // UsesGrabbableParentDependency у ассета неверен: родителя SDK берёт из кэша,
                    // который заполняется в Awake. Поэтому — сам флаг и ограничение хода.
                    if (new SerializedObject(pump).FindProperty("_ignoreGrabbableParentDependency").boolValue)
                        failures.Add($"{path} / {pump.name}: Ignore Grabbable Parent Dependency включён — помпа не зависит от оружия");
                    if (!pump.IsConstrained)
                        failures.Add($"{path} / {pump.name}: у помпы нет ограничения хода — зависимость от оружия не работает");
                }
            }

            Assert.That(checks, Is.GreaterThan(0), "Контроль: оружия с помпой не найдено.");
            Assert.IsEmpty(failures, "Помпа не наводит ствол:\n" + string.Join("\n", failures));
        }
    }
}
