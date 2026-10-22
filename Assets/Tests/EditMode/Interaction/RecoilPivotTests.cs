using System.Reflection;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// Шарнир отдачи в руках — запястье руки на основной рукояти (решение пользователя 2026-10-22): точка
    /// <c>RecoilSourceAxes</c> встаёт в кость запястья, оси остаются от оружия; без руки — точка сборки (ладонь пака),
    /// и при отключении оружия она возвращается.
    /// </summary>
    public class RecoilPivotTests
    {
        private static readonly Vector3 Authored = new(0.02f, -0.06f, -0.07f);

        private GameObject _weapon;
        private Transform _axes;
        private RecoilAccumulator _recoil;

        [SetUp]
        public void SetUp()
        {
            _weapon = new GameObject("Weapon");
            _weapon.AddComponent<UxrFirearmWeapon>();
            _axes = new GameObject("RecoilSourceAxes").transform;
            _axes.SetParent(_weapon.transform, false);
            _axes.localPosition = Authored;
            _axes.localRotation = Quaternion.Euler(0f, 10f, 0f);
            _recoil = _weapon.AddComponent<RecoilAccumulator>();
            typeof(RecoilAccumulator).GetField("_axes", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_recoil, _axes);
            Call("Awake");
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_weapon);

        [Test]
        public void Шарнир_встаёт_в_запястье_оси_от_оружия()
        {
            var wrist = new GameObject("Wrist").transform;
            try
            {
                wrist.SetPositionAndRotation(new Vector3(0.1f, 1.2f, 0.3f), Quaternion.Euler(30f, 40f, 50f));
                Quaternion axesRotation = _axes.rotation;

                Call("PlacePivot", wrist);

                Assert.That(Vector3.Distance(_axes.position, wrist.position), Is.LessThan(1e-5f), "Шарнир не в запястье.");
                Assert.That(Quaternion.Angle(_axes.rotation, axesRotation), Is.LessThan(1e-3f), "Оси отдачи повернулись за запястьем.");
            }
            finally
            {
                Object.DestroyImmediate(wrist.gameObject);
            }
        }

        [Test]
        public void Без_запястья_точка_сборки()
        {
            _axes.localPosition = Vector3.one;

            Call("PlacePivot", (Transform)null);

            Assert.That(Vector3.Distance(_axes.localPosition, Authored), Is.LessThan(1e-6f));
        }

        [Test]
        public void Отключение_возвращает_точку_сборки()
        {
            Call("OnEnable");
            _axes.localPosition = Vector3.one;

            Call("OnDisable");

            Assert.That(Vector3.Distance(_axes.localPosition, Authored), Is.LessThan(1e-6f));
        }

        private void Call(string method, params object[] args)
        {
            MethodInfo info = typeof(RecoilAccumulator).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(info, $"RecoilAccumulator.{method} не найден — поправь тест.");
            info.Invoke(_recoil, args.Length == 0 ? null : args);
        }
    }
}
