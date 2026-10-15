using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>Наблюдение переживает уничтожение компонента: журнал принадлежит самому тесту.</summary>
    [ExecuteAlways]
    public sealed class ArsenalTeardownParentChangeRecorder : MonoBehaviour
    {
        public List<Transform> Parents { get; set; }

        private void OnTransformParentChanged()
        {
            Parents?.Add(transform.parent);
        }
    }

    /// <summary>
    /// Разборка уже принятой сборки: свои слоты уничтожаются, чужие предметы сохраняют вложенность.
    /// Preview-сцена изолирует временную иерархию; префабы, Mirror и запуск карты здесь не участвуют.
    /// </summary>
    public class ArsenalCompositionTeardownTests
    {
        private Scene _scene;
        private ArsenalStationCompositionBinding _station;
        private ArsenalSlotController _slot;
        private ArsenalCompositionHandle _handle;
        private Dictionary<ArsenalStationCompositionBinding, ArsenalCompositionHandle> _live;

        [SetUp]
        public void CreateStation()
        {
            // Не очищаем общий реестр: тест добавляет и снимает только собственную станцию.
            FieldInfo liveField = typeof(ArsenalStationComposer).GetField("Live",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(liveField, Is.Not.Null, "Изменился внутренний реестр сборок станции.");
            _live = (Dictionary<ArsenalStationCompositionBinding, ArsenalCompositionHandle>)liveField.GetValue(null);
            _scene = EditorSceneManager.NewPreviewScene();
            _station = CreateObject("Station").AddComponent<ArsenalStationCompositionBinding>();
            _slot = CreateObject("Slot", _station.transform).AddComponent<ArsenalSlotController>();
        }

        [TearDown]
        public void RemoveStation()
        {
            try
            {
                _handle?.Dispose();
            }
            finally
            {
                // CLR-ссылка остаётся ключом и после уничтожения Unity-компонента станции.
                if (!ReferenceEquals(_station, null) && _live != null &&
                    _live.TryGetValue(_station, out ArsenalCompositionHandle current) &&
                    ReferenceEquals(current, _handle))
                    _live.Remove(_station);
                if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
                _handle = null;
                _station = null;
                _slot = null;
                _live = null;
            }
        }

        [Test]
        public void Dispose_отцепляет_чужой_верхний_предмет_и_сохраняет_мировую_позу()
        {
            Transform anchor = CreateObject("OwnedAnchor", _slot.transform).transform;
            _slot.transform.SetPositionAndRotation(new Vector3(2f, 3f, 4f), Quaternion.Euler(0f, 35f, 0f));
            RegisterHandle();
            UxrGrabbableObject weapon = CreateGrabbable("Weapon", anchor);
            weapon.transform.localPosition = new Vector3(0.25f, 0.5f, -0.75f);
            weapon.transform.localRotation = Quaternion.Euler(15f, 25f, 35f);
            Vector3 position = weapon.transform.position;
            Quaternion rotation = weapon.transform.rotation;

            _handle.Dispose();

            Assert.That(weapon != null, Is.True, "Чужое оружие должно пережить уничтожение слота.");
            Assert.That(weapon.transform.parent, Is.Null);
            Assert.That(weapon.gameObject.scene, Is.EqualTo(_scene));
            Assert.That(Vector3.Distance(weapon.transform.position, position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(weapon.transform.rotation, rotation), Is.LessThan(0.01f));
            Assert.That(_slot == null, Is.True, "Собственный слот должен быть уничтожен.");
            Assert.That(_handle.IsDisposed, Is.True);
        }

        [Test]
        public void Dispose_оставляет_вложенный_чужой_предмет_в_оружии()
        {
            RegisterHandle();
            UxrGrabbableObject weapon = CreateGrabbable("Weapon", _slot.transform);
            Transform socket = CreateObject("MagazineSocket", weapon.transform).transform;
            UxrGrabbableObject magazine = CreateGrabbable("Magazine", socket);
            magazine.transform.localPosition = new Vector3(0.1f, 0.2f, 0.3f);
            Vector3 localPosition = magazine.transform.localPosition;

            _handle.Dispose();

            Assert.That(weapon != null && magazine != null, Is.True);
            Assert.That(weapon.transform.parent, Is.Null);
            Assert.That(magazine.transform.parent, Is.SameAs(socket),
                "Вложенный магазин нельзя отцеплять отдельно от перевозящего его оружия.");
            Assert.That(socket.parent, Is.SameAs(weapon.transform));
            Assert.That(magazine.transform.localPosition, Is.EqualTo(localPosition));
        }

        [Test]
        public void Dispose_не_считает_свои_вложенные_хватаемые_предметы_чужими()
        {
            UxrGrabbableObject owned = CreateGrabbable("OwnedGrabbable", _slot.transform);
            UxrGrabbableObject ownedChild = CreateGrabbable("OwnedChild", owned.transform);
            RegisterHandle();

            _handle.Dispose();

            Assert.That(owned == null && ownedChild == null, Is.True,
                "Предметы, существовавшие при сборке, должны уничтожаться со своим слотом.");
        }

        [Test]
        public void Dispose_спасает_чужой_предмет_под_своим_хватаемым_предком()
        {
            UxrGrabbableObject owned = CreateGrabbable("OwnedGrabbable", _slot.transform);
            Transform ownedSocket = CreateObject("OwnedSocket", owned.transform).transform;
            RegisterHandle();
            UxrGrabbableObject foreign = CreateGrabbable("ForeignMagazine", ownedSocket);

            _handle.Dispose();

            Assert.That(owned == null, Is.True);
            Assert.That(foreign != null, Is.True,
                "Свой хватаемый предок уничтожается со слотом и не может увезти чужое вложение.");
            Assert.That(foreign.transform.parent, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReleaseWithScene_не_переподвешивает_чужие_предметы(bool destroyStationFirst)
        {
            RegisterHandle();
            UxrGrabbableObject weapon = CreateGrabbable("Weapon", _slot.transform);
            UxrGrabbableObject magazine = CreateGrabbable("Magazine", weapon.transform);
            List<Transform> weaponParents = ObserveParentChanges(weapon.transform);
            List<Transform> magazineParents = ObserveParentChanges(magazine.transform);
            if (destroyStationFirst) Object.DestroyImmediate(_station);

            ArsenalStationComposer.ReleaseWithScene(_scene);
            Assert.That(_handle.ReleasesWithScene, Is.True,
                "Объявление владельца должно найти сборку по запомненной сцене даже при Unity-null станции.");
            _handle.Dispose();

            Assert.That(weaponParents, Is.Empty, "При выгрузке нельзя создавать новый корень оружия.");
            Assert.That(magazineParents, Is.Empty, "При выгрузке нельзя вырывать магазин из оружия.");
            Assert.That(weapon == null && magazine == null, Is.True,
                "Без отцепления чужие предметы уничтожаются вместе со слотом.");
            Assert.That(_live.ContainsKey(_station), Is.False);
        }

        [Test]
        public void Dispose_снимает_сборку_из_реестра_после_Unity_null_станции()
        {
            RegisterHandle();
            Object.DestroyImmediate(_station);
            Assert.That(_station == null, Is.True);
            Assert.That(ReferenceEquals(_station, null), Is.False);
            Assert.That(_live.ContainsKey(_station), Is.True, "Контроль: в реестре ещё есть сборка.");

            _handle.Dispose();

            Assert.That(_live.ContainsKey(_station), Is.False,
                "Forget должен сверять CLR-ссылку, поскольку Unity-null не означает отсутствующий ключ.");
        }

        [Test]
        public void Dispose_повторно_не_трогает_сохранённый_предмет()
        {
            RegisterHandle();
            UxrGrabbableObject weapon = CreateGrabbable("Weapon", _slot.transform);
            _handle.Dispose();
            Assert.That(weapon != null, Is.True);
            Transform newParent = CreateObject("NewParent").transform;
            weapon.transform.SetParent(newParent, true);

            Assert.DoesNotThrow(() => _handle.Dispose());

            Assert.That(weapon != null, Is.True);
            Assert.That(weapon.transform.parent, Is.SameAs(newParent));
            Assert.That(_live.ContainsKey(_station), Is.False);
        }

        private GameObject CreateObject(string name, Transform parent = null)
        {
            var created = new GameObject(name);
            // Не запускаем Awake UXR и не создаём singleton/Auto Anchor в сценах редактора.
            created.SetActive(false);
            SceneManager.MoveGameObjectToScene(created, _scene);
            if (parent != null) created.transform.SetParent(parent, false);
            return created;
        }

        private UxrGrabbableObject CreateGrabbable(string name, Transform parent)
        {
            return CreateObject(name, parent).AddComponent<UxrGrabbableObject>();
        }

        private void RegisterHandle()
        {
            // Манифест/сеть не нужны: здесь проверяется жизнь уже созданной сборки.
            _handle = new ArsenalCompositionHandle(null, null, _station,
                new[] { _slot }, new NetworkUxrIdentityAssignment[0]);
            _live.Add(_station, _handle);
        }

        private static List<Transform> ObserveParentChanges(Transform item)
        {
            var parents = new List<Transform>();
            item.gameObject.AddComponent<ArsenalTeardownParentChangeRecorder>().Parents = parents;
            Transform originalParent = item.parent;
            // Контроль сообщения на выключенном объекте: пустой журнал не должен дать ложный PASS.
            item.SetParent(null, true);
            item.SetParent(originalParent, true);
            Assert.That(parents.Count, Is.EqualTo(2), "Стенд не получил сообщения о смене родителя.");
            parents.Clear();
            return parents;
        }
    }
}
