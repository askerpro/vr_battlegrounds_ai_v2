using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.DevTools
{
    /// <summary>
    /// Метки попаданий режима отладки: попадание по игроку доходит до события на этой машине
    /// (<see cref="PlayerController.HitReceivedLocal"/>) — и прошедшее, и отменённое правилом режима, —
    /// а метка называет урон, зону хитбокса и цель.
    /// </summary>
    public class DebugHitMarkersTests
    {
        [Test]
        public void Метка_называет_урон_зону_и_цель()
        {
            Assert.AreEqual("✕ 25 · голова · Бот 1", DebugHitMarkers.Describe(25f, false, true, HitZone.Head, "Бот 1"));
            Assert.AreEqual("✕ 14 (отменён) · нога · A", DebugHitMarkers.Describe(14f, true, true, HitZone.Leg, "A"));
            Assert.AreEqual("✕ 5 · без зоны · A", DebugHitMarkers.Describe(5f, false, false, HitZone.Torso, "A"));
        }

        [Test]
        public void Попадание_по_игроку_поднимает_событие()
        {
            var go = new GameObject("Victim");
            var received = new List<UxrDamageEventArgs>();
            System.Action<PlayerController, UxrDamageEventArgs> handler = (p, e) => received.Add(e);
            PlayerController.HitReceivedLocal += handler;
            try
            {
                go.AddComponent<Mirror.NetworkIdentity>();
                go.AddComponent<UxrActor>();
                PlayerController player = go.AddComponent<PlayerController>();
                var args = new UxrDamageEventArgs(25f, false);

                MethodInfo method = typeof(PlayerController).GetMethod("OnDamageReceiving", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(method, "В PlayerController нет OnDamageReceiving — поправь тест.");
                method.Invoke(player, new object[] { null, args });

                CollectionAssert.AreEqual(new[] { args }, received, "Попадание по игроку не дошло до HitReceivedLocal — метке нечего показать.");
            }
            finally
            {
                PlayerController.HitReceivedLocal -= handler;
                Object.DestroyImmediate(go);
            }
        }
    }
}
