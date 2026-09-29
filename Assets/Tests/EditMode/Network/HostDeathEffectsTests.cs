using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Эффекты гибели у хоста (ярус B: сервер и свой клиент в одном процессе).
    ///
    /// <para>
    /// Класс ошибки: <c>[ClientRpc]</c> на объекте, который сервер уничтожает в том же кадре. Удалённому
    /// клиенту RPC и сообщение об уничтожении приходят одним пакетом по порядку — RPC успевает. А свой
    /// клиент хоста разбирает очередь только на следующем кадре, когда тела уже нет, и RPC молча
    /// теряется. Так у хоста не падал труп (T-35) и не отпускался планшет перед сменой тела. Правило:
    /// действие «до уничтожения» хост исполняет сам, сразу; RPC — только удалённым.
    /// </para>
    /// </summary>
    public class HostDeathEffectsTests : MirrorTestHarness
    {
        protected override bool NeedsLocalClient => true;

        [TearDown]
        public void ClearCorpses() => Corpse.ClearAll("тест");

        private PlayerController CreateBody()
        {
            GameObject go = CreateNetworkObject("Body");
            go.AddComponent<UxrActor>();
            PlayerController player = go.AddComponent<PlayerController>();

            GameObject model = CreateObject("Model");
            model.transform.SetParent(go.transform, false);
            Corpse corpsePrefab = CreateObject("CorpsePrefab").AddComponent<Corpse>();

            CorpseSource source = go.AddComponent<CorpseSource>();
            SetPrivateField(source, "_corpse", corpsePrefab);
            SetPrivateField(source, "_modelRoot", model.transform);

            EnableNetworking(go);
            InvokeLifecycleMethod(player, "Awake");
            SpawnOnServer(player);
            PumpNetwork();
            return player;
        }

        [Test]
        public void Хост_роняет_труп_сразу_пока_тело_живо()
        {
            SilenceMirrorNoise();
            PlayerController body = CreateBody();
            Assert.IsTrue(body.isClient, "Контроль харнесса: тело видно своему клиенту хоста.");

            int before = Corpse.Count;
            InvokePrivateMethod(body, "ServerBecomeCorpse", DeathImpact.None);

            // Без прокрутки сети: следом сервер уничтожит тело, и RPC своему клиенту уже не дойдёт.
            Assert.AreEqual(before + 1, Corpse.Count,
                "У хоста труп не появился в кадре гибели — RPC дойдёт до своего клиента, когда тела уже нет.");
        }
    }
}
