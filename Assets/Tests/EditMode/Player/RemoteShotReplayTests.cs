using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Один источник выстрела (патч SDK 23): выстрел решает только машина стрелка, остальные получают
    /// его синхронизируемым <c>UxrProjectileSource.Shoot</c> и по нему же играют звук и отдачу.
    ///
    /// <para>
    /// <b>Дефект.</b> UltimateXR пересчитывал выстрел на каждой машине: копия оружия в чужих руках по
    /// синхронизированному спуску сама звала <c>TryToShootRound</c> → <c>Shoot</c>. Снаряд рождался и от
    /// события стрелка, и от пересчёта: у стрелка вторая пуля (серверный <c>Shoot</c> копии возвращался
    /// к нему), на выделенном сервере — двойной урон. Звук и отдача копии шли от своего пересчёта и
    /// расходились с настоящим выстрелом (патроны копии не синхронизируются, ствол у стены — по чужой
    /// позе). Дробь множилась: <c>ProjectileShot</c> поднимался и на копиях.
    /// </para>
    /// </summary>
    public class RemoteShotReplayTests
    {
        private const string Pistol  = "Assets/Prefabs/Weapons/GunReal/Gun_real.prefab";
        private const string Shotgun = "Assets/Prefabs/Weapons/ShotgunReal/Shotgun_real.prefab";

        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private UxrWeaponManager _createdManager;
        private IList _projectiles;
        private int _start;

        [SetUp]
        public void SetUp()
        {
            WeaponManagerTestLease.Acquire(out _createdManager);

            _projectiles = Projectiles();
            _start = _projectiles.Count;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _projectiles.Count - 1; i >= _start; i--)
            {
                var go = _projectiles[i].GetType().GetProperty("Projectile").GetValue(_projectiles[i]) as GameObject;
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
        public void Копия_в_чужих_руках_не_решает_стрелять_по_спуску()
        {
            IgnoreEditModeDestroyNoise();
            TwoHandGrabCase grabCase = TwoHandGrabCases.Collect().FirstOrDefault(c => c.WeaponPath == Pistol);
            Assume.That(grabCase.WeaponPath, Is.Not.Null, $"Нет аватара с позами хвата для {Pistol}.");

            // Харнесс держит аватар в UpdateExternally — ровно чужой игрок по сети.
            var harness = new TwoHandGrabHarness(grabCase.WeaponPath, grabCase.AvatarPath, grabCase.SupportPoint);
            var firearm = harness.Weapon.GetComponent<UxrFirearmWeapon>();

            try
            {
                StartFirearm(firearm);
                var counter = new ShotCounter(firearm);

                PressTrigger(firearm);
                int before = _projectiles.Count;
                Call(firearm, "UxrManager_AvatarsUpdated");

                Assert.AreEqual(0, _projectiles.Count - before,
                    "Копия в чужих руках по спуску родила снаряд — вторая пуля у стрелка и двойной урон на сервере.");
                Assert.AreEqual(0, counter.Shots, "Копия подняла ProjectileShot — подписчики (дробь) выстрелят второй раз.");
                Assert.AreEqual(0, counter.Replays, "Эффекты копии идут от пришедшего Shoot, а не от спуска.");

                // Контроль: спуск дошёл бы до выстрела — у стрелка тот же вызов стреляет.
                ResetShotTimer(firearm);
                Assert.IsTrue(firearm.TryToShootRound(0), "Контроль: TryToShootRound стреляет (CanUse, патроны).");
                Assert.AreEqual(1, counter.Shots, "Контроль: свой выстрел поднимает ProjectileShot.");
                Assert.AreEqual(0, counter.Replays, "Свой выстрел не повторяется по ShotFired.");
            }
            finally
            {
                StopFirearm(firearm);
                harness.Dispose();
            }
        }

        [Test]
        public void Пришедший_выстрел_один_снаряд_и_эффекты_без_ProjectileShot()
        {
            IgnoreEditModeDestroyNoise();
            var weapon = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Pistol));
            var firearm = weapon.GetComponent<UxrFirearmWeapon>();

            try
            {
                StartFirearm(firearm);
                var counter = new ShotCounter(firearm);
                var source = weapon.GetComponent<UxrProjectileSource>();

                // Повтор события стрелка: так Shoot исполняется у получателя.
                int before = _projectiles.Count;
                source.Shoot(firearm.GetTriggerShotIndex(0));

                Assert.AreEqual(1, _projectiles.Count - before, "Пришедший выстрел — ровно один снаряд.");
                Assert.AreEqual(1, counter.Replays, "Копия сыграла эффекты выстрела (ProjectileShotReplayed).");
                Assert.AreEqual(0, counter.Shots, "ProjectileShot у получателя не поднимается.");
            }
            finally
            {
                StopFirearm(firearm);
                Object.DestroyImmediate(weapon);
            }
        }

        [Test]
        public void Дробь_только_у_стрелка_повтор_без_дроби()
        {
            IgnoreEditModeDestroyNoise();
            var weapon = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Shotgun));
            var firearm = weapon.GetComponent<UxrFirearmWeapon>();

            try
            {
                foreach (MonoBehaviour behaviour in weapon.GetComponents<MonoBehaviour>())
                {
                    if (behaviour == null || behaviour.GetType().Namespace?.StartsWith("VrBattlegrounds") != true) continue;
                    Call(behaviour, "Awake");
                    Call(behaviour, "OnEnable");
                }

                StartFirearm(firearm);
                var counter = new ShotCounter(firearm);
                var source = weapon.GetComponent<UxrProjectileSource>();
                int main = firearm.GetTriggerShotIndex(0);
                int pellet = Enumerable.Range(0, source.ShotTypes.Count).First(i => i != main);

                // Получатель: основной выстрел и дробинка стрелка приходят событиями.
                int before = _projectiles.Count;
                source.Shoot(main);
                source.Shoot(pellet);

                Assert.AreEqual(2, _projectiles.Count - before, "Повтор: ровно пришедшие снаряды, своей дроби копия не выпускает.");
                Assert.AreEqual(1, counter.Replays, "Эффекты — один раз на основной выстрел; дробинки их не повторяют.");

                // Контроль: у стрелка выстрел выпускает дробь.
                MakeInfiniteAmmo(firearm);
                ResetShotTimer(firearm);
                before = _projectiles.Count;
                Assert.IsTrue(firearm.TryToShootRound(0), "Контроль: TryToShootRound стреляет.");
                Assert.Greater(_projectiles.Count - before, 1, "Контроль: у стрелка выстрел выпускает дробь.");
            }
            finally
            {
                StopFirearm(firearm);
                Object.DestroyImmediate(weapon);
            }
        }

        // ── Харнесс ─────────────────────────────────────────────────────────

        /// <summary>
        /// Звук выстрела и вспышку у дула SDK удаляет через Destroy(obj, life) — вне Play Mode это ошибка
        /// в лог. Шум харнесса, а не отказ логики (CLAUDE.md, «Самопроверка», п. 4). Флаг действует только
        /// из тела теста — из SetUp Unity его не учитывает.
        /// </summary>
        private static void IgnoreEditModeDestroyNoise() => LogAssert.ignoreFailingMessages = true;

        private sealed class ShotCounter
        {
            public int Shots;
            public int Replays;

            public ShotCounter(UxrFirearmWeapon firearm)
            {
                firearm.ProjectileShot += _ => Shots++;
                firearm.ProjectileShotReplayed += _ => Replays++;
            }
        }

        /// <summary>Вне Play Mode Awake/OnEnable/Start не зовутся — руками, как в TwoHandGrabHarness.</summary>
        private static void StartFirearm(UxrFirearmWeapon firearm)
        {
            Call(firearm, "Awake");
            Call(firearm, "OnEnable");
            Call(firearm, "Start");
            MakeInfiniteAmmo(firearm);
        }

        private static void StopFirearm(UxrFirearmWeapon firearm)
        {
            if (firearm != null) Call(firearm, "OnDisable");
        }

        /// <summary>Без якоря магазина <c>GetAmmoLeft</c> отдаёт бесконечный боезапас — магазин не нужен.</summary>
        private static void MakeInfiniteAmmo(UxrFirearmWeapon firearm)
        {
            foreach (object trigger in (IList)Field(firearm, "_triggers"))
            {
                trigger.GetType().GetField("_ammunitionMagAnchor", Any).SetValue(trigger, null);
            }
        }

        private static void PressTrigger(UxrFirearmWeapon firearm)
        {
            object runtime = RuntimeTrigger(firearm);
            Set(runtime, "TriggerPressed", true);
            Set(runtime, "TriggerPressStarted", true);
            Set(runtime, "HasReloaded", true);
            Set(runtime, "LastShotTimer", -1f);
        }

        private static void ResetShotTimer(UxrFirearmWeapon firearm)
        {
            object runtime = RuntimeTrigger(firearm);
            Set(runtime, "LastShotTimer", -1f);
            Set(runtime, "HasReloaded", true);
        }

        private static object RuntimeTrigger(UxrFirearmWeapon firearm) => ((IDictionary)Field(firearm, "_runtimeTriggers"))[0];

        private static void Set(object target, string property, object value) =>
            target.GetType().GetProperty(property, Any).SetValue(target, value);

        private static object Field(object target, string name)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, Any);
                if (field != null) return field.GetValue(target);
            }
            throw new MissingFieldException(target.GetType().Name, name);
        }

        private static IList Projectiles() =>
            (IList)typeof(UxrWeaponManager).GetField("_projectiles", Any).GetValue(UxrWeaponManager.Instance);

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
