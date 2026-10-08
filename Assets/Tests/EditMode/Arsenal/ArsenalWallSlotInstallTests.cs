using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    /// Массив слотов стены заводит одна точка (генератор арсенала, задача 3a).
    ///
    /// Что доказывает. Привязка «предмет → слот» по индексу, пришедшая раньше, чем у станции появились слоты,
    /// не теряется (класс NET-17: сгенерированная станция собирается позже спавна). Сгенерированная станция
    /// не читает слоты из иерархии — порядок детей не порядок манифеста — и получает их один раз через
    /// <see cref="ArsenalWallController.InstallGeneratedSlots" />. После установки неверный индекс —
    /// именованная ошибка, а не молчаливая потеря.
    /// </summary>
    public class ArsenalWallSlotInstallTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo Queue = typeof(ArsenalWallController).GetMethod("QueueSlotBinding", Private);
        private static readonly FieldInfo Pending = typeof(ArsenalWallController).GetField("_pendingSlotBindings", Private);
        private static readonly FieldInfo Mode = typeof(ArsenalStationCompositionBinding).GetField("_mode", Private);

        private readonly List<GameObject> _roots = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _roots)
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            _roots.Clear();
        }

        private ArsenalWallController Wall(string name, bool generated)
        {
            var root = new GameObject(name) { hideFlags = HideFlags.DontSave };
            _roots.Add(root);
            root.AddComponent<NetworkIdentity>();
            var wall = root.AddComponent<ArsenalWallController>();
            if (generated) Mode.SetValue(root.AddComponent<ArsenalStationCompositionBinding>(), ArsenalCompositionMode.Generated);
            return wall;
        }

        private static ArsenalSlotController Slot(ArsenalWallController wall, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(wall.transform, false);
            return go.AddComponent<ArsenalSlotController>();
        }

        private static HashSet<int> PendingOf(ArsenalWallController wall) => (HashSet<int>)Pending.GetValue(wall);
        private static void QueueBinding(ArsenalWallController wall, int index) => Queue.Invoke(wall, new object[] { index });

        [Test]
        public void Ранняя_привязка_ждёт_появления_слотов()
        {
            var wall = Wall("EarlyAuthored", false);
            QueueBinding(wall, 0);
            Assert.IsTrue(PendingOf(wall).Contains(0), "До появления слотов привязка должна ждать, а не теряться.");
        }

        [Test]
        public void Авторская_станция_берёт_слоты_из_иерархии()
        {
            var wall = Wall("Authored", false);
            ArsenalSlotController slot = Slot(wall, "SlotA");
            Assert.IsTrue(wall.SlotsInstalled);
            Assert.AreEqual(1, wall.Slots.Count);
            Assert.AreSame(slot, wall.Slots[0]);

            LogAssert.Expect(LogType.Error, new Regex("слоту 7"));
            QueueBinding(wall, 7);
            Assert.IsFalse(PendingOf(wall).Contains(7), "После установки индекс вне массива — отказ.");
            Assert.Throws<InvalidOperationException>(() => wall.InstallGeneratedSlots(new[] { slot }));
        }

        [Test]
        public void Сгенерированная_станция_получает_слоты_в_порядке_манифеста()
        {
            var wall = Wall("Generated", true);
            ArsenalSlotController first = Slot(wall, "Hierarchy1");
            ArsenalSlotController second = Slot(wall, "Hierarchy2");
            Assert.IsFalse(wall.SlotsInstalled, "Сгенерированная станция не читает иерархию.");
            Assert.AreEqual(0, wall.Slots.Count);

            QueueBinding(wall, 0);
            QueueBinding(wall, 3);
            Assert.IsTrue(PendingOf(wall).Contains(0) && PendingOf(wall).Contains(3), "До установки индексы копятся без проверки.");

            LogAssert.Expect(LogType.Error, new Regex("ранняя привязка к слоту 3"));
            wall.InstallGeneratedSlots(new[] { second, first });

            Assert.IsTrue(wall.SlotsInstalled);
            Assert.AreSame(second, wall.Slots[0], "Порядок манифеста, а не иерархии.");
            Assert.AreSame(first, wall.Slots[1]);
            Assert.IsFalse(PendingOf(wall).Contains(3), "Ранняя привязка вне массива отклоняется при установке.");
            Assert.Throws<InvalidOperationException>(() => wall.InstallGeneratedSlots(new[] { first }), "Установка — один раз.");
        }

        [Test]
        public void Неверная_установка_отклоняется_без_изменений()
        {
            var wall = Wall("GeneratedBad", true);
            ArsenalSlotController slot = Slot(wall, "Slot");
            Assert.Throws<InvalidOperationException>(() => wall.InstallGeneratedSlots(Array.Empty<ArsenalSlotController>()));
            Assert.Throws<InvalidOperationException>(() => wall.InstallGeneratedSlots(new[] { slot, slot }));
            Assert.Throws<InvalidOperationException>(() => wall.InstallGeneratedSlots(new ArsenalSlotController[] { null }));
            Assert.IsFalse(wall.SlotsInstalled);
        }
    }
}
