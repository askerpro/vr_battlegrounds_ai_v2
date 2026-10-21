using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Досылание при инициализации учёта не зависит от <c>HasReloaded</c> (UltimateXR patch 90, задача bots-input;
    /// пользователь проверил в Play 2026-10-20: Viper из кобуры стреляет первым нажатием).
    ///
    /// <para>
    /// <b>Дефект.</b> Стартовый пистолет, выданный в кобуру при включённом оружии (Respawn, стенд, выделенный сервер),
    /// инициализировал учёт до <c>UxrFirearmWeapon.Start</c>, где <c>HasReloaded</c> ещё false, — патронник оставался пуст,
    /// первое нажатие давало <c>ChamberingRequired</c>. Тот же ствол при хвате (Start уже прошёл) досылался. Теперь
    /// <c>prepared = M&gt;0 ∧ ValidateChamberCompletion</c> (Action в покое, магазин в гнезде, автор) — контракт T01.
    /// </para>
    /// <para>
    /// Харнесс как у <see cref="LedgerSpawnReadyTests"/>: вне Play Mode Awake/OnEnable/Start вызываются в игровом порядке,
    /// но <c>UxrFirearmWeapon.Start</c> не вызывается — так в кадре выдачи хост делает T01 раньше старта оружия.
    /// </para>
    /// </summary>
    public class InitializePreparedTests
    {
        private const string Pistol = "Assets/Prefabs/Weapons/Viper/Viper.prefab";
        private const string Magazine = "Assets/Prefabs/Weapons/Viper/Viper_mag.prefab";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private UxrWeaponManager _createdManager;
        private bool _hadGrabManager;
        private GameObject _weapon;
        private GameObject _magazineObject;
        private UxrFirearmWeapon _firearm;
        private UxrGrabbableObject _magazine;
        private WeaponSystem _host;
        private bool _hostEnabled;

        [SetUp]
        public void SetUp()
        {
            WeaponManagerTestLease.Acquire(out _createdManager);
            _hadGrabManager = Object.FindFirstObjectByType<UxrGrabManager>(FindObjectsInactive.Include) != null;
            _weapon = Object.Instantiate(Load(Pistol));
            _magazineObject = Object.Instantiate(Load(Magazine));
            _firearm = _weapon.GetComponent<UxrFirearmWeapon>();
            _host = _weapon.GetComponent<WeaponSystem>();
            _magazine = _magazineObject.GetComponent<UxrGrabbableObject>();
            Assert.That(_firearm, Is.Not.Null, "Контроль: огнестрельное оружие.");
            Assert.That(_host, Is.Not.Null, "Контроль: хост WeaponSystem.");
            Assert.That(_magazine, Is.Not.Null, "Контроль: магазин — grabbable.");
            Assert.That(_host.TryValidateConfiguration(out string error), Is.True, $"Контроль: данные хоста верны — {error}.");
            Call(_firearm, "Awake");
            Call(_firearm, "OnEnable");
            _firearm.RegisterIfNecessary();
            Call(_magazine, "Awake");
            Call(_magazine, "OnEnable");
            _magazine.RegisterIfNecessary();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_hostEnabled && _host != null) Call(_host, "OnDisable");
                if (_firearm != null)
                {
                    Call(_firearm, "OnDisable");
                    Call(_firearm, "OnDestroy");
                }
                if (_magazine != null)
                {
                    Call(_magazine, "OnDisable");
                    Call(_magazine, "OnDestroy");
                }
                if (_weapon != null) Object.DestroyImmediate(_weapon);
                if (_magazineObject != null) Object.DestroyImmediate(_magazineObject);
            }
            finally
            {
                if (!_hadGrabManager)
                    foreach (UxrGrabManager manager in Object.FindObjectsByType<UxrGrabManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        Object.DestroyImmediate(manager.gameObject);
                if (_createdManager != null)
                {
                    Call(_createdManager, "OnDestroy");
                    Object.DestroyImmediate(_createdManager.gameObject);
                    _createdManager = null;
                }
            }
        }

        [Test]
        public void Магазин_в_гнезде_до_Start_оружия_досылает_патрон()
        {
            int trigger = _host.TriggerIndex;
            SeatMagazine(rounds: null);
            int rounds = _firearm.GetMagazineRounds(trigger);
            Assert.That(rounds, Is.GreaterThan(0), "Контроль: магазин в гнезде не пуст.");
            Assert.That(HasReloaded(trigger), Is.False, "Контроль: UxrFirearmWeapon.Start не вызван — HasReloaded ещё false.");

            StartHost();

            UxrFirearmReadinessState state = _firearm.GetReadinessState(trigger);
            Assert.That(state?.ReadinessInitialized ?? false, Is.True, "T01: учёт инициализирован в Start хоста.");
            Assert.That(state.ChamberRound, Is.True,
                "Ствол с полным магазином и Action в покое досылается при Initialize (до патча 90 — пусто из-за HasReloaded).");
            Assert.That(_firearm.GetMagazineRounds(trigger), Is.EqualTo(rounds - 1), "Патрон ушёл из магазина в патронник.");
        }

        [Test]
        public void Пустой_магазин_до_Start_оружия_не_досылает()
        {
            int trigger = _host.TriggerIndex;
            SeatMagazine(rounds: 0);

            StartHost();

            UxrFirearmReadinessState state = _firearm.GetReadinessState(trigger);
            Assert.That(state?.ReadinessInitialized ?? false, Is.True, "T01: учёт инициализирован в Start хоста.");
            Assert.That(state.ChamberRound, Is.False, "Пустой магазин — патронник пуст (M>0 обязательно).");
            Assert.That(_firearm.GetMagazineRounds(trigger), Is.Zero);
        }

        [Test]
        public void Action_не_в_покое_до_Start_оружия_не_досылает()
        {
            int trigger = _host.TriggerIndex;
            SeatMagazine(rounds: null);
            int rounds = _firearm.GetMagazineRounds(trigger);
            Assert.That(rounds, Is.GreaterThan(0), "Контроль: магазин в гнезде не пуст.");

            // OnEnable готовит риг (запоминает покой Action); затвор сдвигается до Start — HasReloaded больше не
            // страхует, проверка покоя — единственная защита досылания при Initialize (патч 90).
            Call(_host, "OnEnable");
            _hostEnabled = true;
            Assert.That(_host.IsActionAtRest, Is.True, "Контроль: после подготовки рига Action в покое.");
            Transform action = FirstActionTarget();
            action.localPosition += Vector3.forward * 0.02f;
            Assert.That(_host.IsActionAtRest, Is.False, "Контроль харнесса: Action сдвинут из покоя.");

            Call(_host, "Start");

            UxrFirearmReadinessState state = _firearm.GetReadinessState(trigger);
            Assert.That(state?.ChamberRound ?? false, Is.False, "Action вне покоя — Initialize не досылает патрон.");
            Assert.That(_firearm.GetMagazineRounds(trigger), Is.EqualTo(rounds), "Патроны магазина не тронуты.");
        }

        // ── Харнесс ─────────────────────────────────────────────────────────

        /// <summary>Первая деталь Action рига (привязки подготовлены в OnEnable хоста).</summary>
        private Transform FirstActionTarget()
        {
            FieldInfo field = typeof(WeaponMechanismRig).GetField("_bindings", Any);
            Assert.That(field, Is.Not.Null, "WeaponMechanismRig изменился: нет _bindings — обновить харнесс.");
            var bindings = (ChamberActionBinding[])field.GetValue(_host.Rig);
            Assert.That(bindings, Is.Not.Empty, "Контроль: у Viper есть детали Action.");
            Assert.That(bindings[0].Target, Is.Not.Null, "Контроль: у детали Action есть цель.");
            return bindings[0].Target;
        }

        private void SeatMagazine(int? rounds)
        {
            Assert.That(_firearm.TryGetTriggerMagazineAnchor(_host.TriggerIndex, out UxrGrabbableObjectAnchor anchor), Is.True,
                "Контроль: у спуска есть гнездо магазина.");
            Assert.That(anchor.CurrentPlacedObject, Is.Null, "Контроль: гнездо пусто до установки.");
            if (rounds.HasValue)
            {
                var ammo = _magazineObject.GetComponentInChildren<UxrFirearmMag>(true);
                Assert.That(ammo, Is.Not.Null, "Контроль: у магазина есть UxrFirearmMag.");
                ammo.Rounds = rounds.Value;
            }
            Assert.That(UxrGrabManager.Instance.PlaceObject(_magazine, anchor, UxrPlacementOptions.None, false), Is.True,
                "Контроль харнесса: магазин встал в гнездо.");
            Assert.That(_magazine.CurrentAnchor, Is.SameAs(anchor), "Контроль харнесса: магазин в гнезде.");
        }

        private void StartHost()
        {
            Call(_host, "OnEnable");
            _hostEnabled = true;
            Call(_host, "Start");
            Assert.That(_host.IsConfigured, Is.True, "Контроль: Start хоста запустил машину.");
        }

        /// <summary>Runtime-флаг SDK, от которого патч 90 отвязал Initialize (только для контроля предусловия).</summary>
        private bool HasReloaded(int trigger)
        {
            FieldInfo field = null;
            for (Type type = _firearm.GetType(); type != null && field == null; type = type.BaseType)
                field = type.GetField("_runtimeTriggers", Any | BindingFlags.DeclaredOnly);
            Assert.That(field, Is.Not.Null, "SDK изменился: нет _runtimeTriggers — обновить харнесс.");
            object runtime = ((IDictionary)field.GetValue(_firearm))[trigger];
            Assert.That(runtime, Is.Not.Null, "Контроль: runtime-данные спуска созданы в Awake.");
            FieldInfo flag = runtime.GetType().GetField("HasReloaded", Any);
            PropertyInfo property = flag == null ? runtime.GetType().GetProperty("HasReloaded", Any) : null;
            Assert.That(flag != null || property != null, "SDK изменился: нет HasReloaded — обновить харнесс.");
            return (bool)(flag != null ? flag.GetValue(runtime) : property.GetValue(runtime));
        }

        private static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"Контроль: префаб '{path}' загружается.");
            return prefab;
        }

        private static void Call(object target, string method)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                MethodInfo info = type.GetMethod(method, Any | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (info == null) continue;
                info.Invoke(target, null);
                return;
            }
            Assert.Fail($"{target.GetType().Name}.{method} не найден.");
        }
    }
}
