using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Core.StateSync;
using UltimateXR.Core.Unique;
using UltimateXR.Manipulation;
using UltimateXR.Networking.Integrations.Net.Mirror;
using UnityEngine;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    ///     Событие сетевого аватара, порождённое до <c>CombineUniqueId</c>, не должно уйти
    ///     в сеть с исходными id префаба (NET-26).
    ///
    ///     <para>
    ///     Замер 2026-09-27, хост + клиент MPPM со шлемом на клиенте: клиент прислал
    ///     <c>UxrAvatar.OnControllerInputChanged</c> с id <c>aee45772-…</c> — это исходный id
    ///     <c>UxrMetaTouchQuest3Input</c> в префабе Cyborg, а у хоста тот же компонент после
    ///     выравнивания — <c>f109c6f0-…</c>. Событие отвергнуто: <c>UxrComponentNotFoundException</c>.
    ///     </para>
    ///
    ///     <para>
    ///     Выравнивание в тестах идёт настоящим путём SDK —
    ///     <see cref="UxrMirrorAvatar.InitializeNetworkAvatar" />: он зовёт <c>CombineUniqueId</c>
    ///     и поднимает <c>AvatarSpawned</c>, по которому гейт и отдаёт события.
    ///     </para>
    /// </summary>
    public class AvatarStateEventGateTests : MirrorTestHarness
    {
        private const float Now = 100f;

        private readonly List<GameObject> _registered = new List<GameObject>();
        private List<IUxrStateSync> _sent;
        private AvatarStateEventGate _gate;

        [SetUp]
        public void CreateGate()
        {
            _sent = new List<IUxrStateSync>();
            _gate = new AvatarStateEventGate((component, eventArgs) => _sent.Add(component));
        }

        [TearDown]
        public void UnregisterIds()
        {
            _gate.Clear();

            // В EditMode Unity не зовёт OnDestroy у ни разу не активных объектов, и id
            // остались бы занятыми для следующих тестов.
            foreach (GameObject go in _registered)
            {
                if (go == null) continue;
                foreach (IUxrUniqueId unique in go.GetComponentsInChildren<IUxrUniqueId>(true))
                    unique.Unregister();
            }

            _registered.Clear();
        }

        /// <summary>Сетевой аватар с компонентом-потомком, как у игрока до OnStartClient.</summary>
        private UxrMirrorAvatar CreateNetworkAvatar(out UxrAvatar avatar, out UxrGrabbableObjectAnchor child)
        {
            GameObject go = CreateNetworkObject("NetworkAvatar");
            avatar = go.AddComponent<UxrAvatar>();
            UxrMirrorAvatar mirror = go.AddComponent<UxrMirrorAvatar>();
            EnableNetworking(go);

            GameObject pocket = new GameObject("Pocket");
            pocket.transform.SetParent(go.transform);
            child = pocket.AddComponent<UxrGrabbableObjectAnchor>();

            _registered.Add(go);
            return mirror;
        }

        /// <summary>То, что делает <c>UxrMirrorAvatar.OnStartClient</c> для чужого аватара.</summary>
        private static void InitializeLikeOnStartClient(UxrMirrorAvatar mirror, UxrAvatar avatar)
        {
            mirror.InitializeNetworkAvatar(avatar, false, "151", "Player 151 (External)");
        }

        // ── Кого придерживать ─────────────────────────────────────────────

        [Test]
        public void Событие_невыровненного_сетевого_аватара_придерживается()
        {
            SilenceMirrorNoise();
            CreateNetworkAvatar(out UxrAvatar avatar, out UxrGrabbableObjectAnchor child);

            Assert.IsTrue(_gate.TryDefer(avatar, null, Now),
                "Событие самого аватара до CombineUniqueId ушло бы с исходными id префаба.");
            Assert.IsTrue(_gate.TryDefer(child, null, Now),
                "Событие компонента внутри аватара до CombineUniqueId ушло бы с исходными id префаба.");
            Assert.AreEqual(2, _gate.PendingCount);
            Assert.IsEmpty(_sent);
        }

        [Test]
        public void Событие_выровненного_аватара_уходит_сразу()
        {
            SilenceMirrorNoise();
            UxrMirrorAvatar mirror = CreateNetworkAvatar(out UxrAvatar avatar, out UxrGrabbableObjectAnchor child);
            InitializeLikeOnStartClient(mirror, avatar);

            Assert.IsFalse(_gate.TryDefer(child, null, Now),
                "После CombineUniqueId придерживать нечего — событие задержалось бы без причины.");
            Assert.AreEqual(0, _gate.PendingCount);
        }

        [Test]
        public void Локальный_аватар_без_UxrMirrorAvatar_не_придерживается()
        {
            SilenceMirrorNoise();
            GameObject go = CreateObject("OfflineAvatar");
            UxrAvatar avatar = go.AddComponent<UxrAvatar>();
            _registered.Add(go);

            Assert.IsFalse(_gate.TryDefer(avatar, null, Now),
                "Несетевой аватар никто не выравнивает — его события ждали бы сигнала, который не придёт.");
        }

        [Test]
        public void Компонент_вне_аватара_не_придерживается()
        {
            SilenceMirrorNoise();
            GameObject go = CreateObject("WorldItem");
            UxrGrabbableObject item = go.AddComponent<UxrGrabbableObject>();
            _registered.Add(go);

            Assert.IsFalse(_gate.TryDefer(item, null, Now));
        }

        // ── Когда отдавать ────────────────────────────────────────────────

        [Test]
        public void Придержанное_уходит_по_AvatarSpawned_с_выровненными_id_и_по_порядку()
        {
            SilenceMirrorNoise();
            UxrMirrorAvatar mirror = CreateNetworkAvatar(out UxrAvatar avatar, out UxrGrabbableObjectAnchor child);
            System.Guid prefabId = avatar.UniqueId;
            var idsAtSend = new List<System.Guid>();
            _gate.Clear();
            _gate = new AvatarStateEventGate((c, e) =>
            {
                idsAtSend.Add(((IUxrUniqueId)c).UniqueId);
                _sent.Add(c);
            });

            _gate.TryDefer(avatar, null, Now);
            _gate.TryDefer(child, null, Now);
            Assert.IsEmpty(_sent, "До выравнивания отдавать нельзя.");

            InitializeLikeOnStartClient(mirror, avatar);

            CollectionAssert.AreEqual(new IUxrStateSync[] { avatar, child }, _sent,
                "По AvatarSpawned события обязаны уйти ровно один раз и в порядке поступления.");
            Assert.AreNotEqual(prefabId, idsAtSend[0],
                "В момент отправки id всё ещё исходный — событие опять ушло бы неразрешимым.");
            Assert.AreEqual(0, _gate.PendingCount);
        }

        [Test]
        public void Аватар_снятый_до_выравнивания_не_держит_события()
        {
            SilenceMirrorNoise();
            UxrMirrorAvatar mirror = CreateNetworkAvatar(out UxrAvatar avatar, out _);

            _gate.TryDefer(avatar, null, Now);
            mirror.OnStopClient();

            Assert.AreEqual(0, _gate.PendingCount,
                "AvatarDespawned обязан выбросить очередь: аватара больше нет, отдавать некому.");
            Assert.IsEmpty(_sent);
        }

        [Test]
        public void Просроченное_выбрасывается_а_не_копится()
        {
            SilenceMirrorNoise();
            CreateNetworkAvatar(out UxrAvatar first, out _);
            CreateNetworkAvatar(out UxrAvatar second, out _);

            _gate.TryDefer(first, null, Now);
            _gate.TryDefer(second, null, Now + AvatarStateEventGate.MaxWaitSeconds + 1f);

            Assert.AreEqual(1, _gate.PendingCount,
                "Очередь аватара, который так и не выровнялся, должна выбрасываться, а не расти вечно.");
            Assert.IsEmpty(_sent);
        }
    }
}
