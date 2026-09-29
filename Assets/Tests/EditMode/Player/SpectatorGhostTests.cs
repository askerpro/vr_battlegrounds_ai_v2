using NUnit.Framework;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Player;
using VrBattlegrounds.Tests.Network;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Выбывший (режим наблюдателя): тело спрятано, хитбоксы выключены — пули сквозь него
    /// проходят, — триггеры (голова для зоны спавна) остаются. Применяется и на сервере без хука
    /// SyncVar: на выделенном сервере пули считаются именно там.
    /// </summary>
    public class SpectatorGhostTests : MirrorTestHarness
    {
        private SpectatorController _spectator;
        private GameObject _geo;
        private Collider _hitbox;
        private Collider _headTrigger;

        [SetUp]
        public void Prepare()
        {
            SilenceMirrorNoise();

            GameObject go = CreateNetworkObject("Avatar");
            go.AddComponent<UxrActor>();
            go.AddComponent<PlayerController>();
            _spectator = go.AddComponent<SpectatorController>();
            InvokeLifecycleMethod(_spectator, "Awake");

            _geo = new GameObject("Geo");
            _geo.transform.SetParent(go.transform);
            _spectator.Geo = _geo;

            var bone = new GameObject("Hitbox_Chest");
            bone.transform.SetParent(go.transform);
            _hitbox = bone.AddComponent<CapsuleCollider>();

            var camera = new GameObject("Camera");
            camera.transform.SetParent(go.transform);
            _headTrigger = camera.AddComponent<BoxCollider>();
            _headTrigger.isTrigger = true;

            EnableNetworking(go);
            SpawnOnServer(_spectator);
        }

        [Test]
        public void Выбывший_без_хитбоксов_и_тела_но_с_триггером_головы()
        {
            SilenceMirrorNoise();
            _spectator.StartSpectating();

            Assert.IsFalse(_hitbox.enabled, "Хитбокс выбывшего включён — он останавливает пули живых.");
            Assert.IsTrue(_headTrigger.enabled, "Триггер головы выключен — зона спавна не увидит выбывшего, и он не оживёт.");
            Assert.IsFalse(_geo.activeSelf, "Тело выбывшего видно.");
            Assert.IsNotNull(_spectator.GetComponent<GhostBody>(), "Универсальный призрак не добавлен.");
        }

        [Test]
        public void Оживший_снова_с_хитбоксами_и_телом()
        {
            SilenceMirrorNoise();
            _spectator.StartSpectating();
            _spectator.EndSpectating();

            Assert.IsTrue(_hitbox.enabled, "После оживления хитбокс не вернулся — в игрока нельзя попасть.");
            Assert.IsTrue(_geo.activeSelf);
        }

        [Test]
        public void Аватару_добавлен_отклик_выбывания()
        {
            Assert.IsNotNull(_spectator.GetComponent<GhostViewEffect>(), "Нет GhostViewEffect — погибший не почувствует и не увидит, что выбыл.");
        }

        [TestCase(true, false, true, TestName = "Выбывший_вне_своей_зоны_видит_мир_чёрно_белым")]
        [TestCase(true, true, false, TestName = "Выбывший_в_своей_зоне_видит_обычно")]
        [TestCase(false, false, false, TestName = "Живой_вне_зоны_видит_обычно")]
        [TestCase(false, true, false, TestName = "Живой_в_зоне_видит_обычно")]
        public void Правило_вида_выбывшего(bool spectating, bool inOwnZone, bool expected)
        {
            Assert.AreEqual(expected, GhostViewRule.Wanted(spectating, inOwnZone));
        }
    }
}
