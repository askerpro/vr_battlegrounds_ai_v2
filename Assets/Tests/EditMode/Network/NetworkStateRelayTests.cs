using System;
using System.Collections.Generic;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Networking.Integrations.Net.Mirror;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    ///     Канал состояния UltimateXR как объект уровня сессии (T-12).
    ///
    ///     <para>
    ///     Что здесь можно проверить, а что нет. Саму доставку блобов этот ярус
    ///     не проверяет: в host-режиме сервер и клиент делят один экземпляр объекта,
    ///     и «долетело» получается само собой — тест был бы ложно-зелёным. Доставку
    ///     проверяет ярус C, сценарий <c>avatar-swap-death-replication</c>.
    ///     </para>
    ///
    ///     <para>
    ///     Здесь проверяется то, что ярусу C не видно и что уже один раз откатилось
    ///     (Патч 1 в <c>sdk-patches.md</c> предписывал убрать статику, а поле снова
    ///     стало статическим): число подписок на канал и отсутствие статического
    ///     состояния в <see cref="UxrMirrorAvatar" />.
    ///     </para>
    /// </summary>
    public class NetworkStateRelayTests : MirrorTestHarness
    {
        /// <summary>Ярус B: подписка релея в host-режиме — это и есть предмет NET-03.</summary>
        protected override bool NeedsLocalClient => true;

        private const string SessionContextPath = "Assets/Prefabs/Managers/SessionContext.prefab";

        // ── Подписка на канал ─────────────────────────────────────────────

        /// <summary>
        ///     Сколько обработчиков указанного объекта висит на статическом событии
        ///     <see cref="UxrManager.ComponentStateChanged" />. Событие field-like,
        ///     поэтому под ним лежит одноимённое приватное статическое поле-делегат.
        /// </summary>
        private static int CountSubscriptions(object target)
        {
            FieldInfo field = typeof(UxrManager).GetField(
                "ComponentStateChanged", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(field,
                "Не найдено поле-делегат события UxrManager.ComponentStateChanged — " +
                "SDK изменился, тест надо переписать, а не удалять.");

            Delegate handler = field.GetValue(null) as Delegate;
            if (handler == null) return 0;

            int count = 0;
            foreach (Delegate entry in handler.GetInvocationList())
            {
                if (ReferenceEquals(entry.Target, target)) count++;
            }

            return count;
        }

        [Test]
        public void Канал_состояния_подписан_ровно_один_раз_на_хосте()
        {
            SilenceMirrorNoise();

            NetworkStateRelay relay = CreateNetworkComponent<NetworkStateRelay>("StateRelay");
            Assert.AreEqual(0, CountSubscriptions(relay), "До спавна релей подписываться не должен.");

            SpawnOnServer(relay);

            // Без прокрутки сети сработал бы только OnStartServer, и тест проверял бы
            // половину механизма: сообщение о спавне лежит в очереди локального
            // соединения до Update. Прокрутка доводит его до OnStartClient — а именно
            // на паре «OnStartServer + OnStartClient у одного объекта» и возникала NET-03.
            PumpNetwork();

            Assert.IsTrue(NetworkClient.spawned.ContainsKey(relay.netId),
                "Клиент host-режима не увидел релей — прокрутка сети не сработала, " +
                "и OnStartClient не вызывался. Тест в таком виде ничего не проверяет.");

            // Двойная подписка здесь — это NET-03: каждое изменение состояния
            // уходило клиентам двумя одинаковыми Rpc, а отписка была одна.
            Assert.AreEqual(1, CountSubscriptions(relay),
                "На хосте релей должен подписаться на UxrManager.ComponentStateChanged ровно один раз.");
        }

        [Test]
        public void Канал_состояния_отписывается_при_остановке_сервера()
        {
            SilenceMirrorNoise();

            NetworkStateRelay relay = CreateNetworkComponent<NetworkStateRelay>("StateRelay");
            SpawnOnServer(relay);
            PumpNetwork();
            Assert.AreEqual(1, CountSubscriptions(relay), "Релей должен быть подписан после спавна.");

            relay.OnStopServer();

            Assert.AreEqual(0, CountSubscriptions(relay),
                "После остановки сервера подписка обязана сняться: висящий обработчик " +
                "переживёт объект и будет слать события в никуда.");
        }

        // ── Отсутствие статики ────────────────────────────────────────────

        [Test]
        public void UxrMirrorAvatar_не_держит_статического_состояния()
        {
            SilenceMirrorNoise();

            FieldInfo[] statics = typeof(UxrMirrorAvatar).GetFields(
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

            List<string> names = new List<string>();
            foreach (FieldInfo field in statics)
            {
                // Weaver Mirror кладёт в класс свои статические служебные поля
                // (хэши Rpc и подобное) — они к состоянию канала отношения не имеют.
                if (field.Name.StartsWith("<") || field.Name.Contains("Weaver") ||
                    field.Name.Contains("Rpc") || field.Name.Contains("Cmd"))
                    continue;

                names.Add(field.Name);
            }

            Assert.IsEmpty(names,
                "В UxrMirrorAvatar снова появились статические поля: " + string.Join(", ", names.ToArray()) +
                ". Именно так канал состояния и оказался привязан ко времени жизни аватара " +
                "(NET-01, NET-02). Состояние канала живёт в NetworkStateRelay — см. " +
                "Docs/UltimateXR/sdk-patches.md, Патч 1.");
        }

        [Test]
        public void NetworkStateRelay_держит_состояние_канала_в_экземпляре()
        {
            SilenceMirrorNoise();

            FieldInfo[] statics = typeof(NetworkStateRelay).GetFields(
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);

            List<string> names = new List<string>();
            foreach (FieldInfo field in statics)
            {
                // Единственная разрешённая статика — ссылка на сам релей (Instance),
                // плюс служебные поля Weaver.
                if (field.Name.Contains("Instance") || field.Name.Contains("Weaver") ||
                    field.Name.Contains("Rpc") || field.Name.Contains("Cmd"))
                    continue;

                names.Add(field.Name);
            }

            Assert.IsEmpty(names,
                "В NetworkStateRelay появилась статика: " + string.Join(", ", names.ToArray()) +
                ". Релей один на процесс, поэтому состояние канала обязано быть полями экземпляра: " +
                "статика переживает объект и снова сделает канал общим на процесс.");
        }

        // ── Проводка ──────────────────────────────────────────────────────

        [Test]
        public void Префаб_SessionContext_несёт_релей_канала_состояния()
        {
            SilenceMirrorNoise();

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SessionContextPath);
            Assert.IsNotNull(prefab, "Не найден префаб " + SessionContextPath);

            Assert.IsNotNull(prefab.GetComponent<NetworkStateRelay>(),
                "На SessionContext нет NetworkStateRelay. Этот префаб спавнит " +
                "GameNetworkManager.OnStartServer — без релея на нём канал состояния " +
                "не поднимется вообще, и здоровье со смертью до клиентов не доедут.");

            Assert.IsNotNull(prefab.GetComponent<Mirror.NetworkIdentity>(),
                "На SessionContext пропал NetworkIdentity — объект перестанет спавниться по сети.");

            uint assetId = prefab.GetComponent<Mirror.NetworkIdentity>().assetId;
            Assert.AreNotEqual(0u, assetId,
                "У SessionContext обнулился assetId: Mirror не сможет заспавнить префаб на клиентах. " +
                "Так бывает после сохранения префаба скриптом — значение восстанавливается переимпортом.");
        }
    }
}
