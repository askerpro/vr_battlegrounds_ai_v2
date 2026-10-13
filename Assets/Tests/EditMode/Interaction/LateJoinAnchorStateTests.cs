using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Core.Components;
using UltimateXR.Core.Serialization;
using UltimateXR.Core.StateSave;
using UltimateXR.Core.StateSync;
using UltimateXR.Manipulation;
using UltimateXR.Networking;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// SDK-патч 75 (<c>tasks/grab-authority/changelog/2026-10-12-sdk-75.md</c>): размещение в якорях доезжает
    /// позднему клиенту, а предмет, снятый с якоря и ставший динамическим, шлёт позу от автора снятия.
    ///
    /// <para>
    /// Снимок — тот же уровень <see cref="UxrStateSaveLevel.ChangesSinceBeginning" />, что
    /// <c>NetworkStateRelay</c> отдаёт позднему клиенту. «Начало» фиксируется так же, как SDK в конце
    /// первого кадра (<see cref="StoreInitialState" />): предмет, рождённый в якоре, имеет размещение,
    /// равное начальному, — именно этот случай раньше терялся. Сериализуется и загружается один и тот же
    /// компонент; между записью и чтением он приводится к виду «свежий клиент».
    /// </para>
    ///
    /// <para>
    /// <b>Что ловит откат патча 75</b> (метка <c>VR Battlegrounds patch 75</c>):
    /// убран <c>DontCheckCache</c> у <c>_currentAnchor</c> или <c>_currentPlacedObject</c> — падает
    /// <see cref="Снимок_пишет_размещение_равное_начальному" />, а также загрузочные тесты (ссылка не доезжает);
    /// принудительная запись утекла в покадровый уровень — падает
    /// <see cref="Покадровый_уровень_не_пишет_размещение" />;
    /// убран вызов или шаги согласователя (<c>ReconcileLoadedPlacement</c>/<c>ApplyLoadedPlacement</c>:
    /// ссылки, родитель, kinematic) — падают <see cref="Загрузка_предмета_ставит_его_в_якорь_без_событий" /> и
    /// <see cref="Загрузка_якоря_ставит_в_него_предмет" />; согласователь заменён на <c>PlaceObject</c> или
    /// шлёт события — падает проверка нуля событий; убран <c>DetachLoadedPlacement</c> — падает
    /// <see cref="Снимок_вне_якоря_снимает_устаревшее_размещение" />; убраны перехват
    /// <c>UxrComponentNotFoundException</c> или ожидание по <c>UxrComponent.GlobalRegistered</c> — падает
    /// <see cref="Неразрешённый_якорь_размещается_при_регистрации" />; убраны гейты или вызов
    /// <c>StartPhysicsSyncAfterAnchorRemoval</c> (5133) — падают тесты синхронизации физики.
    /// </para>
    /// </summary>
    public class LateJoinAnchorStateTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        /// <summary>Погрешность float при повторном выравнивании — не перемещение.</summary>
        private const float NoOpPosition = 1e-5f;

        private const float NoOpAngle = 0.01f;

        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<UxrComponent> _registered = new List<UxrComponent>();

        private GameObject _holder;
        private UxrGrabbableObjectAnchor _anchor;
        private UxrGrabbableObject _item;
        private Rigidbody _body;

        private static UxrGrabManager Manager => UxrGrabManager.Instance;

        [SetUp]
        public void SetUp()
        {
            TestEnvironmentContract.ResetDestroyedSingleton<UxrGrabManager>();
            TestEnvironmentContract.IsActive(Manager);

            // Якорь под отдельным родителем: снятие с якоря возвращает предмет к родителю якоря.
            _holder = Create("LJ75_Holder");
            _anchor = CreateAnchor("LJ75_Anchor", _holder.transform);
            _item = CreateItem("LJ75_Item", out _body);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UxrComponent component in _registered)
            {
                if (component != null) component.Unregister();
            }

            foreach (GameObject go in _created)
            {
                if (go != null) Object.DestroyImmediate(go);
            }

            _registered.Clear();
            _created.Clear();
        }

        // ── (1) Запись ─────────────────────────────────────────────────────

        /// <summary>Пара «предмет в якоре», равная начальной: обе ссылки всё равно в снимке.</summary>
        [Test]
        public void Снимок_пишет_размещение_равное_начальному()
        {
            PlaceByFields(_item, _anchor);
            StoreInitialState(_item);
            StoreInitialState(_anchor);

            List<string> itemVars = WrittenVars(_item, UxrStateSaveLevel.ChangesSinceBeginning);
            List<string> anchorVars = WrittenVars(_anchor, UxrStateSaveLevel.ChangesSinceBeginning);

            Assert.That(itemVars, Does.Contain("_currentAnchor"),
                        "Предмет, рождённый в якоре, не пишет _currentAnchor в снимок — поздний клиент увидит кобуру " +
                        "пустой (снят DontCheckCache у _currentAnchor, патч 75).");
            Assert.That(anchorVars, Does.Contain("_currentPlacedObject"),
                        "Якорь с исходным предметом не пишет _currentPlacedObject в снимок (снят DontCheckCache у " +
                        "_currentPlacedObject, патч 75).");
        }

        /// <summary>Покадровые дельты не пишут ссылку размещения: её ведут события.</summary>
        [Test]
        public void Покадровый_уровень_не_пишет_размещение()
        {
            PlaceByFields(_item, _anchor);
            StoreInitialState(_item);
            StoreInitialState(_anchor);

            Assert.That(WrittenVars(_item, UxrStateSaveLevel.ChangesSincePreviousSave), Does.Not.Contain("_currentAnchor"),
                        "Покадровый уровень пишет _currentAnchor — принудительная запись патча 75 утекла из снимка в дельты.");
            Assert.That(WrittenVars(_anchor, UxrStateSaveLevel.ChangesSincePreviousSave), Does.Not.Contain("_currentPlacedObject"),
                        "Покадровый уровень пишет _currentPlacedObject — принудительная запись патча 75 утекла в дельты.");
        }

        // ── (2) Загрузка: размещение ──────────────────────────────────────

        /// <summary>
        /// Свежий клиент: предмет вне якоря, без родителя, не kinematic. Снимок предмета ставит его в якорь —
        /// ссылки, родитель <see cref="UxrGrabbableObjectAnchor.AlignTransform" />, kinematic — без событий
        /// размещения и синхронизации. Повтор загрузки ничего не меняет.
        /// </summary>
        [Test]
        public void Загрузка_предмета_ставит_его_в_якорь_без_событий()
        {
            Assert.That(_item.UseParenting, Is.True, "[TestEnvironment] Тест рассчитан на UseParenting по умолчанию.");
            PlaceByFields(_item, _anchor);
            _body.isKinematic = true;
            StoreInitialState(_item);
            StoreInitialState(_anchor);
            byte[] snapshot = Save(_item);

            MakeFreshClient();

            using (var events = new EventCounter(_item, _anchor))
            {
                Load(_item, snapshot);
                AssertPlaced("после загрузки снимка предмета");
                Vector3 position = _item.transform.localPosition;
                Quaternion rotation = _item.transform.localRotation;

                Load(_item, snapshot);
                AssertPlaced("после повторной загрузки");
                // Повтор пересчитывает то же выравнивание: допускается только погрешность float.
                Assert.That(Vector3.Distance(_item.transform.localPosition, position), Is.LessThan(NoOpPosition),
                            "Повторная загрузка сдвинула предмет в якоре.");
                Assert.That(Quaternion.Angle(_item.transform.localRotation, rotation), Is.LessThan(NoOpAngle),
                            "Повторная загрузка повернула предмет в якоре.");

                events.AssertNone("Согласователь патча 75 обязан доводить размещение молча: состояние уже серверное.");
            }
        }

        /// <summary>Тот же «свежий клиент», но снимок якоря: предмет встаёт в него с якорной стороны.</summary>
        [Test]
        public void Загрузка_якоря_ставит_в_него_предмет()
        {
            PlaceByFields(_item, _anchor);
            _body.isKinematic = true;
            StoreInitialState(_item);
            StoreInitialState(_anchor);
            byte[] snapshot = Save(_anchor);

            MakeFreshClient();

            using (var events = new EventCounter(_item, _anchor))
            {
                Load(_anchor, snapshot);
                AssertPlaced("после загрузки снимка якоря");
                events.AssertNone("Согласователь патча 75 со стороны якоря шлёт события.");
            }
        }

        // ── (3) Загрузка: снятие ──────────────────────────────────────────

        /// <summary>
        /// На сервере предмет вне якоря и это его начальное состояние; у клиента устаревшее размещение.
        /// Снимок снимает его с якоря: ссылки очищены, родитель — родитель якоря.
        /// </summary>
        [Test]
        public void Снимок_вне_якоря_снимает_устаревшее_размещение()
        {
            StoreInitialState(_item);
            StoreInitialState(_anchor);
            byte[] snapshot = Save(_item);

            PlaceByFields(_item, _anchor);
            _body.isKinematic = true;

            using (var events = new EventCounter(_item, _anchor))
            {
                Load(_item, snapshot);
                events.AssertNone("Снятие по снимку шлёт события.");
            }

            Assert.That(_item.CurrentAnchor, Is.Null, "Предмет после снимка «вне якоря» всё ещё считает себя в якоре.");
            Assert.That(_anchor.CurrentPlacedObject, Is.Null,
                        "Якорь после снимка «предмет ушёл» держит ссылку на предмет (DetachLoadedPlacement, патч 75).");
            Assert.That(_item.transform.parent, Is.SameAs(_holder.transform),
                        "Предмет после снятия по снимку не вернулся к родителю якоря.");
        }

        // ── (4) Отложенное размещение ─────────────────────────────────────

        /// <summary>
        /// Якорь (аватар появится позже) не зарегистрирован в момент загрузки: размещение откладывается,
        /// а при регистрации якоря под тем же id предмет встаёт в него.
        /// </summary>
        [Test]
        public void Неразрешённый_якорь_размещается_при_регистрации()
        {
            PlaceByFields(_item, _anchor);
            _body.isKinematic = true;
            StoreInitialState(_item);
            byte[] snapshot = Save(_item);
            Guid anchorId = _anchor.UniqueId;

            // Сначала снять предмет с якоря: иначе он уничтожится вместе с ним как дочерний.
            MakeFreshClient();
            DestroyComponentObject(_anchor);
            Assert.That(UltimateXR.Core.Unique.UxrUniqueIdImplementer.TryGetComponentById(anchorId, out UltimateXR.Core.Unique.IUxrUniqueId _), Is.False,
                        "[TestEnvironment] Якорь не снят с регистрации — неразрешённая ссылка не воспроизведена.");

            Assert.DoesNotThrow(() => Load(_item, snapshot),
                                "Ссылка на незарегистрированный якорь роняет загрузку предмета (перехват " +
                                "UxrComponentNotFoundException, патч 75).");
            Assert.That(_item.CurrentAnchor, Is.Null, "Предмет встал в якорь, которого ещё нет.");

            // Якорь появляется позже под тем же id. Вне Play ChangeUniqueId id не регистрирует, поэтому id задаётся
            // сериализованным полем до штатной регистрации (RegisterIfNecessary, как в Awake).
            GameObject lateGo = Create("LJ75_LateAnchor");
            lateGo.transform.SetParent(_holder.transform, false);
            var late = lateGo.AddComponent<UxrGrabbableObjectAnchor>();
            SetBaseField(late, "_uxrUniqueId", anchorId.ToString());
            SetBaseField(late, "_cachedGuid", Guid.Empty);
            Register(late);
            Assume.That(late.UniqueId, Is.EqualTo(anchorId), "[TestEnvironment] Поздний якорь зарегистрирован не под ожидаемым id.");

            Assert.That(_item.CurrentAnchor, Is.SameAs(late),
                        "Якорь зарегистрировался под ожидаемым id, а предмет в него не встал (ожидание по " +
                        "UxrComponent.GlobalRegistered, патч 75).");
            Assert.That(late.CurrentPlacedObject, Is.SameAs(_item), "Якорь не получил обратную ссылку на предмет.");
            Assert.That(_item.transform.parent, Is.SameAs(late.AlignTransform), "Отложенное размещение не сменило родителя.");
            Assert.That(_body.isKinematic, Is.True, "Отложенное размещение оставило предмет динамическим.");
        }

        // ── (5) Синхронизация физики после снятия с якоря (5133) ──────────

        [Test]
        public void Динамический_снятый_предмет_запускает_синхронизацию()
        {
            using (new NetworkStub())
            {
                _body.isKinematic = false;
                StartPhysicsSync(_item);
                Assert.That(SyncCoroutine(_item), Is.Not.Null,
                            "Динамический предмет вне якоря и вне руки у автора снятия не запустил синхронизацию физики " +
                            "(StartPhysicsSyncAfterAnchorRemoval, патч 75 / 5133).");
            }
        }

        [Test]
        public void Снятие_с_якоря_запускает_синхронизацию()
        {
            using (new NetworkStub())
            {
                PlaceByFields(_item, _anchor);
                _body.isKinematic = true;
                Assume.That(_item.RigidBodyDynamicOnRelease, Is.True, "[TestEnvironment] Предмет должен стать динамическим.");

                Manager.RemoveObjectFromAnchor(_item, false, true);

                Assert.That(_body.isKinematic, Is.False, "[TestEnvironment] Снятие не сделало предмет динамическим.");
                Assert.That(SyncCoroutine(_item), Is.Not.Null,
                            "RemoveObjectFromAnchor больше не запускает синхронизацию позы (вызов после EndSyncMethod, " +
                            "патч 75 / 5133).");
            }
        }

        [Test]
        public void Повтор_события_на_клиенте_не_запускает_синхронизацию()
        {
            using (new NetworkStub())
            {
                _body.isKinematic = false;
                PropertyInfo inside = typeof(UxrManager).GetProperty(nameof(UxrManager.IsInsideStateSync));
                bool saved = UxrManager.Instance.IsInsideStateSync;
                inside.SetValue(UxrManager.Instance, true);

                try
                {
                    StartPhysicsSync(_item);
                }
                finally
                {
                    inside.SetValue(UxrManager.Instance, saved);
                }

                Assert.That(SyncCoroutine(_item), Is.Null,
                            "Повтор события снятия на другой машине запустил синхронизацию — слать позу начнёт каждая машина.");
            }
        }

        [Test]
        public void Kinematic_предмет_не_запускает_синхронизацию()
        {
            using (new NetworkStub())
            {
                _body.isKinematic = true;
                StartPhysicsSync(_item);
                Assert.That(SyncCoroutine(_item), Is.Null, "Kinematic предмет (не стал динамическим) запустил синхронизацию физики.");
            }
        }

        [Test]
        public void Предмет_в_якоре_не_запускает_синхронизацию()
        {
            using (new NetworkStub())
            {
                PlaceByFields(_item, _anchor);
                _body.isKinematic = false;
                StartPhysicsSync(_item);
                Assert.That(SyncCoroutine(_item), Is.Null, "Предмет, лежащий в якоре, запустил синхронизацию физики.");
            }
        }

        [Test]
        public void Удерживаемый_предмет_не_запускает_синхронизацию()
        {
            using (new NetworkStub())
            {
                _body.isKinematic = false;
                IDictionary manipulations = (IDictionary)typeof(UxrGrabManager).GetField("_currentManipulations", Instance).GetValue(Manager);
                Type infoType = typeof(UxrGrabManager).GetNestedType("RuntimeManipulationInfo", BindingFlags.NonPublic);
                Assert.That(infoType, Is.Not.Null, "[TestEnvironment] Изменился контракт SDK: нет RuntimeManipulationInfo.");
                manipulations.Add(_item, FormatterServices.GetUninitializedObject(infoType));

                try
                {
                    Assume.That(_item.IsBeingGrabbed, Is.True, "[TestEnvironment] Хват не воспроизведён.");
                    StartPhysicsSync(_item);
                }
                finally
                {
                    manipulations.Remove(_item);
                }

                Assert.That(SyncCoroutine(_item), Is.Null, "Предмет в руке запустил синхронизацию физики после снятия с якоря.");
            }
        }

        // ── Стенд ──────────────────────────────────────────────────────────

        private void AssertPlaced(string when)
        {
            Assert.That(_item.CurrentAnchor, Is.SameAs(_anchor), $"Предмет не встал в якорь {when} (согласователь патча 75).");
            Assert.That(_anchor.CurrentPlacedObject, Is.SameAs(_item), $"Якорь не получил обратную ссылку {when}.");
            Assert.That(_item.transform.parent, Is.SameAs(_anchor.AlignTransform), $"Родитель предмета не AlignTransform якоря {when}.");
            Assert.That(_body.isKinematic, Is.True, $"Предмет в якоре остался динамическим {when}.");
        }

        /// <summary>Клиент, который только что получил объекты спавном: размещения нет.</summary>
        private void MakeFreshClient()
        {
            SetField(_item, "_currentAnchor", null);
            if (_anchor != null) SetField(_anchor, "_currentPlacedObject", null);
            _item.transform.SetParent(null, false);
            _item.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 20f, 30f));
            _body.isKinematic = false;
        }

        private static void PlaceByFields(UxrGrabbableObject item, UxrGrabbableObjectAnchor anchor)
        {
            SetField(item, "_currentAnchor", anchor);
            SetField(anchor, "_currentPlacedObject", item);
            item.transform.SetParent(anchor.AlignTransform, false);
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
        }

        private GameObject Create(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        private UxrGrabbableObjectAnchor CreateAnchor(string name, Transform parent)
        {
            GameObject go = Create(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0.2f, 0f, 0f);
            var anchor = go.AddComponent<UxrGrabbableObjectAnchor>();
            Register(anchor);
            return anchor;
        }

        private UxrGrabbableObject CreateItem(string name, out Rigidbody body)
        {
            GameObject go = Create(name);
            body = go.AddComponent<Rigidbody>();
            var item = go.AddComponent<UxrGrabbableObject>();
            item.RigidBodySource = body;
            Register(item);
            return item;
        }

        /// <summary>В EditMode Awake не зовётся — регистрацию id делаем, как SDK в Awake.</summary>
        private void Register(UxrComponent component)
        {
            component.RegisterIfNecessary();
            _registered.Add(component);
        }

        private void DestroyComponentObject(UxrComponent component)
        {
            component.Unregister();
            _registered.Remove(component);
            _created.Remove(component.gameObject);
            Object.DestroyImmediate(component.gameObject);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, Instance);
            Assert.That(field, Is.Not.Null, $"[TestEnvironment] В {target.GetType().Name} нет поля {name} — тест устарел вместе с SDK.");
            field.SetValue(target, value);
        }

        private static void SetBaseField(object target, string name, object value)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, Instance | BindingFlags.DeclaredOnly);

                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"[TestEnvironment] Нет поля {name} — тест устарел вместе с SDK.");
        }

        /// <summary>То же, что <c>StoreInitialState</c> SDK в конце первого кадра: запомнить «начало».</summary>
        private static void StoreInitialState(IUxrStateSave component)
        {
            component.SerializeState(UxrDummySerializer.WriteModeSerializer, component.StateSerializationVersion,
                                     UxrStateSaveLevel.ChangesSinceBeginning,
                                     UxrStateSaveOptions.DontSerialize | UxrStateSaveOptions.ResetChangesCache | UxrStateSaveOptions.FirstFrame);
        }

        private static byte[] Save(IUxrStateSave component, UxrStateSaveLevel level = UxrStateSaveLevel.ChangesSinceBeginning)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream))
                {
                    component.SerializeState(new UxrBinarySerializer(writer), component.StateSerializationVersion, level, UxrStateSaveOptions.None);
                }

                return stream.ToArray();
            }
        }

        private static void Load(IUxrStateSave component, byte[] snapshot)
        {
            using (var reader = new BinaryReader(new MemoryStream(snapshot)))
            {
                component.SerializeState(new UxrBinarySerializer(reader, UxrConstants.Serialization.CurrentBinaryVersion),
                                         component.StateSerializationVersion, UxrStateSaveLevel.ChangesSinceBeginning, UxrStateSaveOptions.None);
            }
        }

        /// <summary>Переменные, которые компонент действительно записал в поток на этом уровне.</summary>
        private static List<string> WrittenVars(IUxrStateSave component, UxrStateSaveLevel level)
        {
            var names = new List<string>();

            void Handler(object sender, UxrStateSaveEventArgs e)
            {
                if (ReferenceEquals(sender, component) && !e.Options.HasFlag(UxrStateSaveOptions.DontSerialize))
                {
                    names.Add(e.VarName);
                }
            }

            UxrStateSaveImplementer.VarSerialized += Handler;

            try
            {
                Save(component, level);
            }
            finally
            {
                UxrStateSaveImplementer.VarSerialized -= Handler;
            }

            return names;
        }

        private static void StartPhysicsSync(UxrGrabbableObject item)
        {
            MethodInfo method = typeof(UxrGrabbableObject).GetMethod("StartPhysicsSyncAfterAnchorRemoval", Instance);
            Assert.That(method, Is.Not.Null, "Нет StartPhysicsSyncAfterAnchorRemoval — патч 75 (5133) откатан.");
            method.Invoke(item, null);
        }

        private static object SyncCoroutine(UxrGrabbableObject item)
        {
            FieldInfo field = typeof(UxrGrabbableObject).GetField("_regularPhysicsSyncCoroutine", Instance);
            Assert.That(field, Is.Not.Null, "[TestEnvironment] Изменился контракт SDK: нет _regularPhysicsSyncCoroutine.");
            return field.GetValue(item);
        }

        /// <summary>
        /// События, которыми согласователь не должен пользоваться: размещение/снятие менеджера, предмета и якоря
        /// и события синхронизации их компонентов.
        /// </summary>
        private sealed class EventCounter : IDisposable
        {
            private readonly UxrGrabbableObject _item;
            private readonly UxrGrabbableObjectAnchor _anchor;
            private readonly List<string> _events = new List<string>();

            public EventCounter(UxrGrabbableObject item, UxrGrabbableObjectAnchor anchor)
            {
                _item = item;
                _anchor = anchor;
                Manager.ObjectPlacing += OnManipulation;
                Manager.ObjectPlaced += OnManipulation;
                Manager.ObjectRemoving += OnManipulation;
                Manager.ObjectRemoved += OnManipulation;
                Manager.StateChanged += OnStateChanged;
                _item.StateChanged += OnStateChanged;
                _anchor.StateChanged += OnStateChanged;
                UxrManager.ComponentStateChanged += OnComponentStateChanged;
            }

            public void AssertNone(string message)
            {
                Assert.That(_events, Is.Empty, message + " События: " + string.Join(", ", _events));
            }

            public void Dispose()
            {
                Manager.ObjectPlacing -= OnManipulation;
                Manager.ObjectPlaced -= OnManipulation;
                Manager.ObjectRemoving -= OnManipulation;
                Manager.ObjectRemoved -= OnManipulation;
                Manager.StateChanged -= OnStateChanged;
                if (_item != null) _item.StateChanged -= OnStateChanged;
                if (_anchor != null) _anchor.StateChanged -= OnStateChanged;
                UxrManager.ComponentStateChanged -= OnComponentStateChanged;
            }

            private void OnManipulation(object sender, UxrManipulationEventArgs e) => _events.Add(e.EventType.ToString());

            private void OnStateChanged(object sender, UxrSyncEventArgs e) => _events.Add("StateChanged:" + e);

            private void OnComponentStateChanged(IUxrStateSync component, UxrSyncEventArgs e) => _events.Add("ComponentStateChanged:" + e);
        }

        /// <summary>
        /// Сеть, без которой синхронизация физики SDK не стартует: <see cref="UxrNetworkManager" /> с реализацией
        /// Mirror (без NetworkTransform на предмете). В EditMode его нет — подставляется на время теста.
        /// </summary>
        private sealed class NetworkStub : IDisposable
        {
            private readonly GameObject _go;
            private readonly FieldInfo _instanceField;
            private readonly object _savedInstance;

            public NetworkStub()
            {
                _instanceField = FindStaticField(typeof(UxrNetworkManager), "s_instance");
                Assert.That(_instanceField, Is.Not.Null, "[TestEnvironment] Изменился контракт singleton SDK.");
                _savedInstance = _instanceField.GetValue(null);
                Assume.That(_savedInstance == null || ((Object)_savedInstance) == null, "[TestEnvironment] В сцене уже есть UxrNetworkManager.");

                Type mirror = typeof(UxrNetworkImplementation).Assembly.GetType("UltimateXR.Networking.Integrations.Net.Mirror.UxrMirrorNetwork");
                Assert.That(mirror, Is.Not.Null, "[TestEnvironment] Нет UxrMirrorNetwork.");

                _go = new GameObject("LJ75_Network");
                var manager = _go.AddComponent<UxrNetworkManager>();
                Component implementation = _go.AddComponent(mirror);
                typeof(UxrNetworkManager).GetField("_networkImplementation", Instance).SetValue(manager, implementation);
                _instanceField.SetValue(null, manager);
                Assert.That(UxrNetworkManager.HasInstance, Is.True, "[TestEnvironment] Сеть не подставлена.");
            }

            public void Dispose()
            {
                _instanceField.SetValue(null, null);
                Object.DestroyImmediate(_go);
            }

            private static FieldInfo FindStaticField(Type type, string name)
            {
                for (; type != null; type = type.BaseType)
                {
                    FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (field != null) return field;
                }

                return null;
            }
        }
    }
}
