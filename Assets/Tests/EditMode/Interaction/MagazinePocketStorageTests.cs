using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Tests.Interaction
{
    public class MagazinePocketStorageTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private UxrAvatar _avatar;
        private Transform _calibration;
        private UxrMagazinePocket _pocket;
        private UxrGrabber _grabber;

        [SetUp]
        public void SetUp()
        {
            // Карман на масштабируемой кости, сам аватар вложен в отдельный корень сцены.
            GameObject sceneRoot = Create("Корень сцены");
            GameObject avatarRoot = Create("Аватар", sceneRoot.transform);
            _avatar = avatarRoot.AddComponent<UxrAvatar>();
            _calibration = Create("Калибровка", avatarRoot.transform).transform;
            _calibration.localScale = Vector3.one * 0.8f;
            GameObject pocketRoot = Create("Карман", _calibration);
            pocketRoot.AddComponent<UxrGrabbableObjectAnchor>();
            _pocket = pocketRoot.AddComponent<UxrMagazinePocket>();
            _grabber = Create("Рука", avatarRoot.transform).AddComponent<UxrGrabber>();
            _grabber.transform.position = new Vector3(1f, 2f, 3f);
        }

        [TearDown]
        public void TearDown()
        {
            // Контейнер убирается первым: OnDestroy кармана не должен вызывать отложенный Destroy в EditMode.
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        private GameObject Create(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            _created.Add(go);
            return go;
        }

        private UxrGrabbableObject CreateMagazine()
        {
            GameObject go = Create("Магазин");
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            var item = go.AddComponent<UxrGrabbableObject>();
            item.RigidBodySource = body;
            go.AddComponent<UxrFirearmMag>();
            go.transform.localScale = Vector3.one * 0.7f;
            return item;
        }

        private void Store(UxrGrabbableObject item)
        {
            _pocket.ForceStoreItem(item);
            if (_pocket.Contains(item) && item.transform.parent != null)
            {
                GameObject storage = item.transform.parent.gameObject;
                if (!_created.Contains(storage)) _created.Add(storage);
            }
        }

        private static void AssertScale(Vector3 expected, Vector3 actual)
        {
            Assert.Less((expected - actual).sqrMagnitude, 1e-8f, $"Ожидался масштаб {expected}, получен {actual}.");
        }

        [Test]
        public void Спрятанный_магазин_сохраняет_activeSelf_под_выключенным_контейнером()
        {
            UxrGrabbableObject item = CreateMagazine();
            Store(item);

            Assert.IsTrue(item.gameObject.activeSelf);
            Assert.IsFalse(item.gameObject.activeInHierarchy);
            Assert.IsFalse(item.transform.parent.gameObject.activeSelf);
            Assert.AreSame(_avatar.transform, item.transform.parent.parent,
                "Контейнер должен жить прямо под аватаром, а не под костью или общим корнем сцены.");
            AssertScale(Vector3.one, item.transform.parent.lossyScale);
            AssertScale(Vector3.one * 0.7f, item.transform.lossyScale);
            AssertScale(Vector3.one * 0.7f, item.transform.localScale);
        }

        [Test]
        public void Калибровка_кости_не_меняет_масштаб_спрятанного_и_извлечённого_магазина()
        {
            UxrGrabbableObject item = CreateMagazine();
            Store(item);
            _calibration.localScale = Vector3.one * 1.3f;
            AssertScale(Vector3.one * 0.7f, item.transform.lossyScale);

            Assert.AreSame(item, _pocket.ExtractMagazine(_grabber));
            Assert.IsNull(item.transform.parent);
            Assert.IsTrue(item.gameObject.activeSelf);
            Assert.IsTrue(item.gameObject.activeInHierarchy);
            AssertScale(Vector3.one * 0.7f, item.transform.lossyScale);
            Assert.AreEqual(_grabber.transform.position, item.transform.position);
        }

        [Test]
        public void ItemStored_сообщает_о_фактической_укладке_один_раз_и_повторно_после_возврата()
        {
            UxrGrabbableObject item = CreateMagazine();
            int stored = 0;
            int released = 0;
            _pocket.ItemStored += observed =>
            {
                Assert.AreSame(item, observed);
                Assert.IsTrue(_pocket.Contains(observed));
                Assert.IsFalse(observed.gameObject.activeInHierarchy,
                    "Подписчик учёта должен видеть уже спрятанный предмет.");
                stored++;
            };
            _pocket.ItemReleased += observed =>
            {
                Assert.AreSame(item, observed);
                Assert.IsFalse(_pocket.Contains(observed));
                Assert.IsTrue(observed.gameObject.activeInHierarchy);
                released++;
            };

            Store(item);
            Store(item);
            Assert.AreEqual(1, stored, "Выдача сервером и клиентом хоста не дублирует укладку.");
            Assert.AreEqual(1, _pocket.Count);
            CollectionAssert.AreEqual(new[] { item }, _pocket.StoredItems);

            Assert.AreSame(item, _pocket.ExtractMagazine(_grabber));
            Assert.AreEqual(1, released);
            Assert.AreEqual(0, _pocket.Count);
            Assert.IsFalse(_pocket.HasItems());
            Assert.IsNull(_pocket.ExtractMagazine(_grabber));
            Assert.AreEqual(1, released);

            Store(item);
            Assert.AreEqual(2, stored, "Новая укладка после выхода снова ставит предмет на учёт.");
            Assert.AreEqual(1, _pocket.Count);
        }

        [TestCase("RaiseGrabbingEvent")]
        [TestCase("RaisePlacingEvent")]
        public void Сетевой_захват_или_размещение_выводит_предмет_из_учёта_без_дублей(string eventMethod)
        {
            UxrGrabbableObject item = CreateMagazine();
            Store(item);
            int released = 0;
            _pocket.ItemReleased += _ => released++;

            // SDK поднимает эти события внутри менеджера; здесь проверяется подписка кармана,
            // без создания второго владельца grab-state или имитации сетевого writer.
            MethodInfo raise = typeof(UxrGrabbableObject).GetMethod(eventMethod,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(raise);
            UxrManipulationEventArgs args = eventMethod == "RaiseGrabbingEvent"
                ? UxrManipulationEventArgs.FromGrab(item, null, _grabber, 0, false, false, Vector3.zero, Quaternion.identity)
                : UxrManipulationEventArgs.FromPlace(item, null, _grabber, 0, default);
            raise.Invoke(item, new object[] { args });
            raise.Invoke(item, new object[] { args });

            Assert.AreEqual(1, released);
            Assert.AreEqual(0, _pocket.Count);
            Assert.IsFalse(_pocket.Contains(item));
            Assert.IsNull(item.transform.parent);
            Assert.IsTrue(item.gameObject.activeInHierarchy);
            AssertScale(Vector3.one * 0.7f, item.transform.lossyScale);
        }

        [Test]
        public void Неудачное_извлечение_не_снимает_магазин_с_учёта()
        {
            UxrGrabbableObject item = CreateMagazine();
            Store(item);
            int released = 0;
            _pocket.ItemReleased += _ => released++;

            Assert.IsNull(_pocket.ExtractMagazine(null));
            Assert.AreEqual(0, released);
            Assert.AreEqual(1, _pocket.Count);
            Assert.IsTrue(_pocket.Contains(item));
            Assert.IsFalse(item.gameObject.activeInHierarchy);
        }
    }
}
