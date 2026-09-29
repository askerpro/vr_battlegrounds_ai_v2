using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Аватар уничтожается, держа предмет в руке (смена скина, отключение игрока).
    ///
    /// <para>
    /// Рука UltimateXR при уничтожении ничего не отпускает, и в <c>UxrGrabManager</c> остаётся
    /// захват мёртвой руки. Первый же телепорт или поворот с затемнением бросал на нём
    /// <c>MissingReferenceException</c> из <c>UxrAvatar_GlobalAvatarMoved</c>, корутина
    /// обрывалась до проявления, и экран оставался чёрным (Issue 17 в known-issues).
    /// </para>
    ///
    /// <para>
    /// Два слоя защиты — два теста: игра отпускает руки перед уничтожением
    /// (<see cref="AvatarTeardown"/>), а SDK (патч 12) пропускает и вычищает осиротевшие
    /// захваты, если игра что-то упустила.
    /// </para>
    ///
    /// <para>
    /// Обработчик перемещения зовётся рефлексией: в EditMode <c>UxrGrabManager.OnEnable</c> не
    /// выполняется, и на <c>UxrAvatar.GlobalAvatarMoved</c> менеджер не подписан —
    /// <c>UxrManager.MoveAvatarTo</c> до него просто не дошёл бы. Путь через <c>MoveAvatarTo</c>
    /// проверяется в Play Mode.
    /// </para>
    /// </summary>
    public class AvatarTeardownTests
    {
        private static UxrGrabManager Manager => UxrGrabManager.Instance;

        private static TwoHandGrabCase AnyCase()
        {
            List<TwoHandGrabCase> cases = TwoHandGrabCases.Collect();
            Assume.That(cases.Count, Is.GreaterThan(0), "Нет ни одной пары оружие–аватар с позами хвата.");
            return cases[0];
        }

        [TearDown]
        public void TearDown()
        {
            // Мёртвые записи в синглтоне пережили бы тест и отравили соседей.
            IDictionary manipulations = CurrentManipulations();
            foreach (object key in manipulations.Keys.Cast<object>().ToList())
            {
                if (IsDead(key) || GrabbersOf(manipulations[key]).Any(IsDead))
                {
                    manipulations.Remove(key);
                }
            }
        }

        [Test]
        public void Освобождение_перед_уничтожением_не_оставляет_захватов_мёртвой_руки()
        {
            TwoHandGrabCase c = AnyCase();

            using (var harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint))
            {
                PlayerController player = harness.Avatar.GetComponent<PlayerController>();
                Assert.IsNotNull(player, $"У аватара {c.AvatarPath} нет PlayerController.");

                AvatarTeardown.ReleaseBeforeDestroy(player, "тест");
                harness.DestroyAvatarLikePlayMode();

                Assert.AreEqual(0, CountDeadGrabs(),
                                "После AvatarTeardown и уничтожения аватара в UxrGrabManager остались захваты уничтоженной руки.");
                Assert.IsFalse(Manager.IsBeingGrabbed(harness.Grabbable), "Оружие всё ещё числится в руке уничтоженного аватара.");

                MoveOtherAvatar(c.AvatarPath);
            }
        }

        /// <summary>
        /// Смена скина забирает у прежнего аватара всё снаряжение (T-35): оружие из руки
        /// уничтожается, а не падает на пол, — иначе ствол переживал бы смену и оставался
        /// ничьим в мире. Руки при этом отпущены до уничтожения, как и при обычном освобождении.
        /// </summary>
        [Test]
        public void Смена_аватара_изымает_оружие_из_руки()
        {
            TwoHandGrabCase c = AnyCase();

            using (var harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint))
            {
                PlayerController player = harness.Avatar.GetComponent<PlayerController>();
                Assert.IsNotNull(player, $"У аватара {c.AvatarPath} нет PlayerController.");
                Assert.IsTrue(Manager.IsBeingGrabbed(harness.Grabbable), "Контроль харнесса: оружие в руке.");

                AvatarTeardown.ConfiscateBeforeDestroy(player, "тест");

                Assert.IsTrue(harness.Weapon == null,
                              "Оружие из руки пережило смену аватара — осталось в мире вместо изъятия.");
                Assert.IsNull(harness.Right.GrabbedObject, "Рука прежнего аватара всё ещё что-то держит.");

                harness.DestroyAvatarLikePlayMode();
                Assert.AreEqual(0, CountDeadGrabs(), "После изъятия и уничтожения аватара остались захваты мёртвой руки.");
            }
        }

        [Test]
        public void Перемещение_аватара_не_бросает_на_захвате_мёртвой_руки()
        {
            TwoHandGrabCase c = AnyCase();

            using (var harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint))
            {
                // Игра ничего не отпустила — ровно то, что было до исправления.
                harness.DestroyAvatarLikePlayMode();

                Assert.Greater(CountDeadGrabs(), 0,
                               "Контроль харнесса: без освобождения захват мёртвой руки обязан остаться, иначе страховке SDK нечего доказывать.");

                MoveOtherAvatar(c.AvatarPath);

                Assert.AreEqual(0, CountDeadGrabs(), "Перемещение аватара не вычистило захваты уничтоженной руки.");
                Assert.IsFalse(Manager.IsBeingGrabbed(harness.Grabbable), "Оружие осталось числиться захваченным мёртвой рукой.");
            }
        }

        /// <summary>Другой, локальный аватар телепортируется: зовётся обработчик GlobalAvatarMoved менеджера.</summary>
        private static void MoveOtherAvatar(string avatarPath)
        {
            GameObject other = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath));

            try
            {
                UxrAvatar avatar = other.GetComponent<UxrAvatar>();
                avatar.AvatarMode = UxrAvatarMode.Local;

                Vector3 from = other.transform.position;
                var args = new UxrAvatarMoveEventArgs(from, other.transform.rotation, from + Vector3.forward * 3f, other.transform.rotation);

                MethodInfo handler = typeof(UxrGrabManager).GetMethod("UxrAvatar_GlobalAvatarMoved", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(handler, "В UxrGrabManager нет UxrAvatar_GlobalAvatarMoved — SDK обновился, поправь тест.");

                try
                {
                    handler.Invoke(Manager, new object[] { avatar, args });
                }
                catch (TargetInvocationException e)
                {
                    Assert.Fail($"Перемещение аватара бросило {e.InnerException?.GetType().Name}: {e.InnerException?.Message}. " +
                                "В игре это обрывает телепорт с затемнением — экран остаётся чёрным.");
                }
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        private static int CountDeadGrabs()
        {
            IDictionary manipulations = CurrentManipulations();
            int dead = 0;

            foreach (DictionaryEntry entry in manipulations)
            {
                if (IsDead(entry.Key)) dead++;
                dead += GrabbersOf(entry.Value).Count(IsDead);
            }

            return dead;
        }

        private static IDictionary CurrentManipulations()
        {
            FieldInfo field = typeof(UxrGrabManager).GetField("_currentManipulations", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "В UxrGrabManager нет _currentManipulations — SDK обновился, поправь тест.");
            return (IDictionary)field.GetValue(Manager);
        }

        private static IEnumerable<object> GrabbersOf(object manipulationInfo)
        {
            IEnumerable grabs = (IEnumerable)manipulationInfo.GetType().GetProperty("Grabs").GetValue(manipulationInfo);

            foreach (object grab in grabs)
            {
                yield return grab.GetType().GetProperty("Grabber").GetValue(grab);
            }
        }

        private static bool IsDead(object unityObject) => unityObject is Object o ? o == null : unityObject == null;
    }
}
