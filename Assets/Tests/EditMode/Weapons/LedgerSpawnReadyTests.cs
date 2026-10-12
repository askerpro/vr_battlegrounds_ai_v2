using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Core.StateSync;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Учёт готов к replay в кадре спавна (ledger-spawn-ready, SDK-патч 76; решение пользователя 2026-10-12 «чини сразу»).
    ///
    /// <para>
    /// <b>Дефект.</b> Сервер в кадре N спавнит ствол, в N+1 <c>WeaponSystem.Start</c> делает T01 Initialize и рассылает фиксацию.
    /// Клиент обрабатывает spawn и эту фиксацию одной пачкой: объект уже активен, а <c>Start</c> ещё не вызван. Учёт спуска
    /// включался только в <c>Start</c> (<c>TryEnableReadiness</c>), поэтому replay Initialize отклонялся молча, ствол
    /// оставался на ревизии 0, а каждый следующий выстрел у наблюдателя расходился по ревизии — поправку сервер публикует
    /// только при собственном форке. Теперь пассивная часть настройки (порт, учёт, подписки) идёт в <c>OnEnable</c>, а
    /// отказ replay не по ревизии виден: <c>ReplayRejected</c> с причиной → ошибка <c>[WeaponLedger]</c> один раз на причину.
    /// </para>
    /// <para>
    /// Вне Play Mode Unity не зовёт Awake/OnEnable/Start — харнесс вызывает их в игровом порядке (<c>UxrFirearmWeapon</c>,
    /// затем <c>WeaponSystem</c> с <c>DefaultExecutionOrder(210)</c>). Replay идёт через бинарное событие SDK и
    /// <c>UxrManager.ExecuteStateSyncEvent</c> — как фиксация приходит у наблюдателя.
    /// </para>
    /// </summary>
    public class LedgerSpawnReadyTests
    {
        private const string Rifle = "Assets/Prefabs/Weapons/Herrington/Herrington.prefab";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private UxrWeaponManager _createdManager;
        private SdkScope _sdk;
        private GameObject _weapon;
        private UxrFirearmWeapon _firearm;
        private WeaponSystem _host;
        private bool _hostEnabled;

        [SetUp]
        public void SetUp()
        {
            WeaponManagerTestLease.Acquire(out _createdManager);
            _sdk = new SdkScope();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Rifle);
            Assert.That(prefab, Is.Not.Null, $"Контроль: префаб '{Rifle}' загружается.");
            _weapon = Object.Instantiate(prefab);
            _firearm = _weapon.GetComponent<UxrFirearmWeapon>();
            _host = _weapon.GetComponent<WeaponSystem>();
            Assert.That(_firearm, Is.Not.Null, "Контроль: огнестрельное оружие.");
            Assert.That(_host, Is.Not.Null, "Контроль: хост WeaponSystem.");
            Assert.That(_host.TryValidateConfiguration(out string error), Is.True, $"Контроль: данные хоста верны — {error}.");
            // Игровой порядок активации: оружие (Awake создаёт runtime-данные спусков), затем хост.
            Call(_firearm, "Awake");
            Call(_firearm, "OnEnable");
            _firearm.RegisterIfNecessary();
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
                    Call(_firearm, "OnDestroy"); // снять регистрацию UniqueId и статические подписки SDK
                }
                if (_weapon != null) Object.DestroyImmediate(_weapon);
            }
            finally
            {
                _sdk?.Dispose();
                _sdk = null;
                if (_createdManager != null)
                {
                    Call(_createdManager, "OnDestroy");
                    Object.DestroyImmediate(_createdManager.gameObject);
                    _createdManager = null;
                }
            }
        }

        [Test]
        public void Фиксация_Initialize_до_Start_применяется_у_наблюдателя()
        {
            int trigger = _host.TriggerIndex;
            EnableHost(); // SetActive(true) при spawn: OnEnable есть, Start — в следующем кадре
            Assert.That(_firearm.UsesReadinessLedger(trigger), Is.True,
                "После OnEnable учёт спуска уже включён: фиксация из той же пачки, что spawn, не должна отклоняться.");
            Assert.That(_host.IsConfigured, Is.False, "Машина запускается только в Start (T01 не раньше).");
            Assert.That(_firearm.GetReadinessState(trigger)?.Revision ?? 0u, Is.EqualTo(0u), "Контроль: до replay учёт пуст.");

            var rejected = new List<UxrFirearmReplayDivergence>();
            _firearm.ReadinessReplayDiverged += rejected.Add;
            UxrStateSyncResult result = _sdk.Replay(_firearm, InitializeCommit(trigger));
            _firearm.ReadinessReplayDiverged -= rejected.Add;

            Assert.That(result.IsError, Is.False, result.ErrorMessage);
            Assert.That(rejected, Is.Empty, "Replay Initialize не должен расходиться.");
            UxrFirearmReadinessState state = _firearm.GetReadinessState(trigger);
            Assert.That(state?.Revision ?? 0u, Is.EqualTo(1u), "Ревизия наблюдателя — 1 (до исправления оставалась 0).");
            Assert.That(state.ReadinessInitialized, Is.True);

            Call(_host, "Start");
            Assert.That(_host.IsConfigured, Is.True, "Start запускает машину поверх уже подключённого учёта.");
            Assert.That(_firearm.GetReadinessState(trigger).Revision, Is.EqualTo(1u),
                "Start с уже инициализированным учётом не пишет его повторно (T05, не T01).");
        }

        [Test]
        public void Отказ_replay_при_выключенном_учёте_виден_один_раз()
        {
            int trigger = _host.TriggerIndex;
            // Хост не активирован: учёт не включён — состояние клиента до исправления.
            Assert.That(_firearm.UsesReadinessLedger(trigger), Is.False, "Контроль: учёт спуска выключен.");
            var integrity = new WeaponLedgerIntegrity(_firearm, _host);
            integrity.Attach();
            var seen = new List<UxrFirearmReplayDivergence>();
            _firearm.ReadinessReplayDiverged += seen.Add;
            int before = WeaponLedgerIntegrity.ReplayRejectedTotal;
            try
            {
                LogAssert.Expect(LogType.Error, new Regex(@"\[WeaponLedger\] Replay фиксации отклонён \(LedgerDisabled\)"));
                Assert.That(_sdk.Replay(_firearm, InitializeCommit(trigger)).IsError, Is.False);
                Assert.That(_sdk.Replay(_firearm, InitializeCommit(trigger)).IsError, Is.False);
            }
            finally
            {
                _firearm.ReadinessReplayDiverged -= seen.Add;
                integrity.Detach();
            }

            Assert.That(seen.Count, Is.EqualTo(2), "Каждый отказ replay — событие SDK (раньше — молча).");
            foreach (UxrFirearmReplayDivergence d in seen)
            {
                Assert.That(d.Kind, Is.EqualTo(UxrFirearmReplayDivergenceKind.ReplayRejected));
                Assert.That(d.RejectReason, Is.EqualTo(UxrFirearmReplayRejectReason.LedgerDisabled));
                Assert.That(d.Operation, Is.EqualTo(UxrFirearmReadinessOperation.Initialize));
                Assert.That(d.Replay, Is.True);
            }
            Assert.That(WeaponLedgerIntegrity.ReplayRejectedTotal - before, Is.EqualTo(2), "Счётчик пробы видит оба отказа.");
            Assert.That(_firearm.GetReadinessState(trigger)?.Revision ?? 0u, Is.EqualTo(0u), "Отклонённая фиксация не применена.");
            // Ошибка — одна на причину: второй Error без Expect уронил бы тест (Unity Test Framework падает на
            // неожиданных Error по умолчанию); повтор идёт в Verbose. NoUnexpectedReceived не нужен — он ловит и Log SDK.
        }

        // ── Харнесс ─────────────────────────────────────────────────────────

        private void EnableHost()
        {
            Call(_host, "OnEnable");
            _hostEnabled = true;
        }

        /// <summary>Фиксация Initialize автора без магазина: ревизия 0 → 1, патронник пуст, M = 0.</summary>
        private static UxrFirearmReadinessCommit InitializeCommit(int trigger) => new UxrFirearmReadinessCommit
        {
            TriggerIndex = trigger, ExpectedRevision = 0, NextRevision = 1, MagazineRoundsAfter = 0,
            Operation = UxrFirearmReadinessOperation.Initialize,
            StateAfter = new UxrFirearmReadinessState { ReadinessInitialized = true, Revision = 1 },
            SourceOrientation = Quaternion.identity
        };

        /// <summary>
        /// Свой <c>UxrManager</c> на время теста (как <c>PlayerCalibrationOwnerTests.SdkCapture</c>): replay идёт через его
        /// <c>ExecuteStateSyncEvent</c>, и <c>IsInsideStateSync</c> читается у того же экземпляра.
        /// </summary>
        private sealed class SdkScope : IDisposable
        {
            private readonly UxrManager _manager;
            private readonly FieldInfo _singleton;
            private readonly object _previous;

            public SdkScope()
            {
                for (Type type = typeof(UxrManager); type != null; type = type.BaseType)
                {
                    FieldInfo field = type.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (field != null) { _singleton = field; break; }
                }
                Assert.That(_singleton, Is.Not.Null, "SDK singleton изменился: обновить харнесс.");
                _previous = _singleton.GetValue(null);
                _manager = new GameObject("LedgerSpawnReadyManager").AddComponent<UxrManager>();
                _singleton.SetValue(null, _manager);
                _manager.RegisterIfNecessary();
            }

            public UxrStateSyncResult Replay(UxrFirearmWeapon weapon, UxrFirearmReadinessCommit commit)
            {
                byte[] bytes = new UxrMethodInvokedSyncEventArgs("ApplyReadinessCommit", (object)commit).SerializeEventBinary(weapon);
                return _manager.ExecuteStateSyncEvent(bytes);
            }

            public void Dispose()
            {
                try { _manager.Unregister(); }
                finally
                {
                    if (ReferenceEquals(_singleton.GetValue(null), _manager)) _singleton.SetValue(null, _previous);
                    Object.DestroyImmediate(_manager.gameObject);
                }
            }
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
