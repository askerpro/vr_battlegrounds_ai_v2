using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// Принятая логика T-38: пуля совпадает с осью дула, включая поворот ствола отдачей;
    /// дробь сохраняет собственный конус без общей неточности залпа.
    /// </summary>
    public class WeaponSpreadTests
    {
        private const string Smg = "Assets/Prefabs/Weapons/MKR9/MKR9.prefab";
        private const string Shotgun = "Assets/Prefabs/Weapons/ShotgunReal/Shotgun_real.prefab";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private UxrWeaponManager _createdManager;
        private IList _projectiles;
        private int _start;
        private bool _previousIgnoreFailingMessages;

        [SetUp]
        public void SetUp()
        {
            _previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            WeaponManagerTestLease.Acquire(out _createdManager);
            _projectiles = (IList)typeof(UxrWeaponManager).GetField("_projectiles", Any).GetValue(UxrWeaponManager.Instance);
            _start = _projectiles.Count;
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = _previousIgnoreFailingMessages;
            for (int i = _projectiles.Count - 1; i >= _start; i--)
            {
                GameObject go = Projectile(i);
                _projectiles.RemoveAt(i);
                if (go != null) Object.DestroyImmediate(go);
            }
            if (_createdManager != null)
            {
                Call(_createdManager, "OnDestroy");
                Object.DestroyImmediate(_createdManager.gameObject);
                _createdManager = null;
            }
        }

        [Test]
        public void Очередь_ПП_летит_по_оси_повёрнутого_ствола()
        {
            LogAssert.ignoreFailingMessages = true; // вспышка и звук — Destroy(obj, t) вне Play Mode: шум харнесса
            GameObject weapon = Start(Smg, out UxrFirearmWeapon firearm, out WeaponSpread spread);
            try
            {
                Assert.IsNull(spread, "У пули не должно быть WeaponSpread.");
                Assert.IsNull(firearm.ShotOrientationModifier, "Пуля не должна получать случайную поправку.");
                Transform muzzle = weapon.GetComponent<UxrProjectileSource>().ShotTypes[firearm.GetTriggerShotIndex(0)].ShotSource;

                for (int i = 0; i < 30; i++)
                {
                    weapon.transform.rotation = Quaternion.Euler(i * 0.7f, i * 1.1f, 0f);
                    ResetShotTimer(firearm);
                    int before = _projectiles.Count;
                    Assert.IsTrue(firearm.TryToShootRound(0), "TryToShootRound не выстрелил.");
                    Assert.AreEqual(before + 1, _projectiles.Count, "Выстрел — не один снаряд.");

                    float angle = Vector3.Angle(muzzle.forward, Projectile(_projectiles.Count - 1).transform.forward);
                    Assert.LessOrEqual(angle, 0.03f, $"Выстрел {i + 1} отклонился от оси дула на {angle:F3}°.");
                }
            }
            finally
            {
                Stop(weapon, firearm);
            }
        }

        [Test]
        public void Дробь_включая_первую_в_своём_конусе_без_неточности_залпа()
        {
            LogAssert.ignoreFailingMessages = true;
            GameObject weapon = Start(Shotgun, out UxrFirearmWeapon firearm, out WeaponSpread spread);
            try
            {
                var source = weapon.GetComponent<UxrProjectileSource>();
                Transform muzzle = source.ShotTypes[firearm.GetTriggerShotIndex(0)].ShotSource;
                ResetShotTimer(firearm);
                int before = _projectiles.Count;
                Assert.IsNotNull(spread);
                float allowed = WeaponAccuracy.ToDegrees(spread.Pattern.Spread) + 0.03f;
                Assert.IsTrue(firearm.TryToShootRound(0));

                int count = _projectiles.Count - before;
                Assert.AreEqual(weapon.GetComponent<ShotgunPellets>().Pellets, count, "Залп — не столько снарядов, сколько дробин.");
                var directions = new HashSet<Vector3>();
                for (int i = before; i < _projectiles.Count; i++)
                {
                    Vector3 forward = Projectile(i).transform.forward;
                    Assert.LessOrEqual(Vector3.Angle(muzzle.forward, forward), allowed, "Дробина вне конуса CS2.");
                    directions.Add(forward);
                }
                Assert.Greater(Vector3.Angle(muzzle.forward, Projectile(before).transform.forward), 0.01f,
                    "Первая дробина осталась строго в центре — хук SDK не применён.");
                Assert.Greater(directions.Count, count / 2, "Дробины легли в одну точку.");
            }
            finally
            {
                Stop(weapon, firearm);
            }
        }

        // ── Харнесс (как RemoteShotReplayTests) ──────────────────────────────

        private static GameObject Start(string path, out UxrFirearmWeapon firearm, out WeaponSpread spread)
        {
            var weapon = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            firearm = weapon.GetComponent<UxrFirearmWeapon>();
            spread = weapon.GetComponent<WeaponSpread>();

            Call(firearm, "Awake");
            Call(firearm, "OnEnable");
            Call(firearm, "Start");
            foreach (object trigger in (IList)Field(firearm, "_triggers"))
                trigger.GetType().GetField("_ammunitionMagAnchor", Any).SetValue(trigger, null); // бесконечные патроны

            foreach (MonoBehaviour behaviour in weapon.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null || behaviour.GetType().Namespace?.StartsWith("VrBattlegrounds") != true) continue;
                Call(behaviour, "Awake");
                Call(behaviour, "OnEnable");
            }
            return weapon;
        }

        private static void Stop(GameObject weapon, UxrFirearmWeapon firearm)
        {
            if (firearm != null) Call(firearm, "OnDisable");
            Object.DestroyImmediate(weapon);
        }

        private static void ResetShotTimer(UxrFirearmWeapon firearm)
        {
            object runtime = ((IDictionary)Field(firearm, "_runtimeTriggers"))[0];
            runtime.GetType().GetProperty("LastShotTimer", Any).SetValue(runtime, -1f);
            runtime.GetType().GetProperty("HasReloaded", Any).SetValue(runtime, true);
        }

        private GameObject Projectile(int index) =>
            _projectiles[index].GetType().GetProperty("Projectile").GetValue(_projectiles[index]) as GameObject;

        private static float Max(List<float> values)
        {
            float max = 0f;
            foreach (float v in values) max = Mathf.Max(max, v);
            return max;
        }

        private static object Field(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, Any);
                if (field != null) return field.GetValue(target);
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }

        private static void Call(object target, string method)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                MethodInfo info = type.GetMethod(method, Any | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (info != null)
                {
                    info.Invoke(target, null);
                    return;
                }
            }
        }
    }
}
