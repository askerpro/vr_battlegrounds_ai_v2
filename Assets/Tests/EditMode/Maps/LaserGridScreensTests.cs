using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Табло лазерной сетки как компонент (T-47): видны ровно тогда, когда видна граница зоны (своих
    /// таймингов нет), не становятся окклюдером и не ловят пули; зона стены находится и у соседа зоны.
    /// </summary>
    public class LaserGridScreensTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _created)
                if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private GameObject Track(GameObject go)
        {
            _created.Add(go);
            return go;
        }

        /// <summary>Зона как на картах: группа, в ней отмасштабированная коробка зоны.</summary>
        private TeamSpawnZone MakeZone(Transform group, Vector3 position, Vector3 scale)
        {
            var go = new GameObject("Zone");
            go.transform.SetParent(group, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.AddComponent<BoxCollider>().isTrigger = true;
            go.AddComponent<MeshRenderer>();
            return go.AddComponent<TeamSpawnZone>();
        }

        private static void SetBorderVisible(TeamSpawnZone zone, bool visible)
        {
            MethodInfo set = typeof(TeamSpawnZone).GetMethod("SetBorderVisible", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(set, "TeamSpawnZone.SetBorderVisible — единственная точка смены видимости сетки.");
            set.Invoke(zone, new object[] { visible });
        }

        [Test]
        public void Табло_появляются_и_исчезают_вместе_с_сеткой()
        {
            Transform group = Track(new GameObject("Group")).transform;
            TeamSpawnZone zone = MakeZone(group, new Vector3(0f, 0f, 8f), new Vector3(15.5f, 7.4f, 3f));
            LaserGridScreens screens = zone.gameObject.AddComponent<LaserGridScreens>();
            screens.Build(new ArsenalWallController[0], new[] { zone });
            Track(screens.Root);

            // В редакторе OnEnable компонентов не зовётся — подписку проверяем через публичную точку
            // и событие зоны.
            zone.BorderVisibilityChanged += screens.ApplyBorderVisible;
            try
            {
                SetBorderVisible(zone, true);
                Assert.IsTrue(zone.BorderVisible);
                Assert.IsTrue(screens.IsShowing, "Сетка видна, а табло нет.");

                SetBorderVisible(zone, false);
                Assert.IsFalse(screens.IsShowing, "Сетка исчезла, а табло остались.");
            }
            finally
            {
                zone.BorderVisibilityChanged -= screens.ApplyBorderVisible;
            }
        }

        [Test]
        public void Компонент_подписан_на_видимость_сетки()
        {
            // Подписка живёт в OnEnable: без неё табло не узнали бы, что сетка исчезла.
            MethodInfo onEnable = typeof(LaserGridScreens).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(onEnable, "У LaserGridScreens нет OnEnable — табло не подписаны на сетку.");

            Transform group = Track(new GameObject("Group")).transform;
            TeamSpawnZone zone = MakeZone(group, Vector3.zero, new Vector3(6f, 4f, 3f));
            LaserGridScreens screens = zone.gameObject.AddComponent<LaserGridScreens>();
            screens.Build(new ArsenalWallController[0], new[] { zone });
            Track(screens.Root);
            screens.ApplyBorderVisible(false);

            onEnable.Invoke(screens, null);
            SetBorderVisible(zone, true);
            Assert.IsTrue(screens.IsShowing, "OnEnable не подписал табло на появление сетки.");
            SetBorderVisible(zone, false);
            Assert.IsFalse(screens.IsShowing, "OnEnable не подписал табло на исчезновение сетки.");

            typeof(LaserGridScreens).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(screens, null);
        }

        [Test]
        public void Табло_не_окклюдер_и_без_коллайдеров()
        {
            Transform group = Track(new GameObject("Group")).transform;
            TeamSpawnZone zone = MakeZone(group, Vector3.zero, new Vector3(15.5f, 7.4f, 3f));
            LaserGridScreens screens = zone.gameObject.AddComponent<LaserGridScreens>();
            screens.Build(new ArsenalWallController[0], new[] { zone });
            Track(screens.Root);

            Assert.AreEqual(4, screens.CommonBoards.Count);
            foreach (Transform t in screens.Root.GetComponentsInChildren<Transform>(true))
            {
                Assert.AreEqual((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(t.gameObject),
                    $"'{t.name}' статичный — запечётся окклюдером.");
                Assert.IsNull(t.GetComponent<Collider>(), $"'{t.name}' с коллайдером — будет ловить пули и руки.");
            }
            Assert.AreNotEqual(zone.transform, screens.Root.transform.parent,
                "Табло под неравномерно отмасштабированной зоной — их перекосит.");
        }

        [Test]
        public void Стена_соседка_зоны_принадлежит_зоне()
        {
            Transform group = Track(new GameObject("Group")).transform;
            TeamSpawnZone zone = MakeZone(group, new Vector3(0f, 0f, 8.06f), new Vector3(15.5f, 7.44f, 3.05f));
            var wall = new GameObject("Wall");
            wall.transform.SetParent(group, false);
            wall.transform.localPosition = new Vector3(-5.8f, 0f, 9.14f);

            Assert.IsNull(wall.GetComponentInParent<TeamSpawnZone>(), "Предпосылка: стена — не дочерний объект зоны.");
            Assert.AreSame(zone, SpawnZoneMembership.ZoneOf(wall.transform, new[] { zone }));

            var outside = new GameObject("Outside");
            outside.transform.SetParent(group, false);
            outside.transform.localPosition = new Vector3(0f, 0f, 12f);
            Assert.IsNull(SpawnZoneMembership.ZoneOf(outside.transform, new[] { zone }));
        }

        [Test]
        public void Из_вложенных_зон_точке_принадлежит_меньшая()
        {
            Transform group = Track(new GameObject("Group")).transform;
            TeamSpawnZone arena = MakeZone(group, Vector3.zero, new Vector3(40f, 10f, 40f));
            TeamSpawnZone team = MakeZone(group, new Vector3(0f, 0f, 8f), new Vector3(15f, 7f, 3f));

            Assert.AreSame(team, SpawnZoneMembership.ZoneAt(new Vector3(1f, 0f, 8.5f), new[] { arena, team }));
            Assert.AreSame(arena, SpawnZoneMembership.ZoneAt(new Vector3(1f, 0f, -5f), new[] { arena, team }));
        }

        [Test]
        public void Дочерняя_стена_принадлежит_родительской_зоне()
        {
            Transform group = Track(new GameObject("Group")).transform;
            TeamSpawnZone zone = MakeZone(group, Vector3.zero, new Vector3(4f, 4f, 4f));
            TeamSpawnZone other = MakeZone(group, new Vector3(0f, 0f, 1f), new Vector3(1f, 1f, 1f));
            var wall = new GameObject("Wall");
            wall.transform.SetParent(zone.transform, false);
            wall.transform.localPosition = new Vector3(0f, 0f, 0.25f);

            Assert.AreSame(zone, SpawnZoneMembership.ZoneOf(wall.transform, new[] { zone, other }));
        }
    }
}
