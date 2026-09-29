using System;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Патчи UltimateXR 25–27 (Docs/UltimateXR/sdk-patches.md) ускоряют поиск предмета для хвата и
    /// не должны менять его результат.
    ///
    /// <para>
    /// 27 — <c>CanBeGrabbedByGrabber</c>: наши правила хвата (<c>CanGrabDelegate</c>) зовутся последними,
    /// после расстояния. Раньше их платил каждый предмет сцены в каждом переборе.
    /// 25 — предметы с точками «не кнопками по умолчанию» ведутся списком; пока он пуст, контроллер
    /// аватара не перебирает все предметы ради переопределения кнопок.
    /// </para>
    /// </summary>
    public class GrabQueryPatchTests
    {
        private static TwoHandGrabCase AnyCase()
        {
            var cases = TwoHandGrabCases.Collect();
            Assume.That(cases.Count, Is.GreaterThan(0), "Нет пары оружие–аватар с позами хвата.");
            return cases[0];
        }

        [Test]
        public void Дальний_предмет_не_зовёт_правила_хвата()
        {
            TwoHandGrabCase c = AnyCase();
            using var harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint, grabMain: false);
            Func<UxrGrabbableObject, int, bool> saved = harness.Left.CanGrabDelegate;
            int calls = 0;

            try
            {
                harness.Left.CanGrabDelegate = (g, p) => { calls++; return true; };
                harness.Left.transform.position = harness.Weapon.transform.position + Vector3.up * 50f;

                Assert.IsFalse(harness.Grabbable.CanBeGrabbedByGrabber(harness.Left, c.SupportPoint), "Рука в 50 м — взять нельзя");
                Assert.That(calls, Is.Zero, "Правила хвата звались для предмета вне досягаемости — патч 27 откатился.");
            }
            finally
            {
                harness.Left.CanGrabDelegate = saved;
            }
        }

        /// <summary>
        /// Патч 28: грубая отсечка не должна отрезать ничего, что взял бы полный расчёт. Рука обходит
        /// предмет по сфере радиусом от 0 до 0.6 м вокруг каждой точки хвата: везде, где точка досягаема
        /// полным расчётом, отсечка обязана пропускать предмет.
        /// </summary>
        [Test]
        public void Отсечка_по_расстоянию_не_отрезает_досягаемое()
        {
            TwoHandGrabCase c = AnyCase();
            using var harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint, grabMain: false);
            UxrGrabbableObject grabbable = harness.Grabbable;
            var method = typeof(UxrGrabbableObject).GetMethod("IsOutsideCoarseGrabRange",
                         System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "Нет отсечки патча 28");

            int reachable = 0;

            foreach (UltimateXR.Manipulation.UxrGrabber grabber in new[] { harness.Left, harness.Right })
            {
                for (int point = 0; point < grabbable.GrabPointCount; point++)
                {
                    grabbable.ComputeRequiredGrabberTransform(grabber, point, out Vector3 onPoint, out Quaternion rotation, false);

                    for (int i = 0; i < 200; i++)
                    {
                        Vector3 offset = UnityEngine.Random.insideUnitSphere * 0.6f;
                        grabber.transform.SetPositionAndRotation(onPoint + offset, rotation);

                        grabbable.GetDistanceFromGrabber(grabber, point, out float _, out float plain);
                        bool reachableByFullCheck = plain <= grabbable.GetGrabPoint(point).MaxDistanceGrab;
                        bool culled = (bool)method.Invoke(grabbable, new object[] { grabber });

                        if (reachableByFullCheck)
                        {
                            reachable++;
                            Assert.IsFalse(culled, $"Отсечка отрезала досягаемую точку {point} ({grabber.Side}), смещение {offset}");
                        }
                    }
                }
            }

            Assume.That(reachable, Is.GreaterThan(0), "Проверка ни разу не попала в досягаемую зону");

            harness.Left.transform.position = grabbable.transform.position + Vector3.up * 50f;
            Assert.IsTrue((bool)method.Invoke(grabbable, new object[] { harness.Left }), "Рука в 50 м — отсечка должна срабатывать");
        }

        [Test]
        public void Правила_хвата_по_прежнему_решают_для_досягаемой_точки()
        {
            TwoHandGrabCase c = AnyCase();
            using var harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint, grabMain: false);
            Func<UxrGrabbableObject, int, bool> saved = harness.Left.CanGrabDelegate;
            int calls = 0;

            try
            {
                harness.PlaceLeftOnSupport();

                harness.Left.CanGrabDelegate = (g, p) => { calls++; return true; };
                Assert.IsTrue(harness.Grabbable.CanBeGrabbedByGrabber(harness.Left, c.SupportPoint), "Ладонь на точке, правила разрешают");
                Assert.That(calls, Is.EqualTo(1), "Для досягаемой точки правила обязаны спрашиваться");

                harness.Left.CanGrabDelegate = (g, p) => false;
                Assert.IsFalse(harness.Grabbable.CanBeGrabbedByGrabber(harness.Left, c.SupportPoint), "Запрет правил должен действовать");
            }
            finally
            {
                harness.Left.CanGrabDelegate = saved;
            }
        }

        [Test]
        public void Предмет_с_особыми_кнопками_хвата_попадает_в_список_и_уходит_при_выключении()
        {
            TwoHandGrabCase c = AnyCase();
            using var harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint, grabMain: false);
            UxrGrabbableObject grabbable = harness.Grabbable;
            UxrGrabPointInfo point = grabbable.GetGrabPoint(0);
            bool savedDefault = point.UseDefaultGrabButtons;

            // В EditMode Unity не зовёт OnEnable/OnDisable сам — как и харнесс, зовём руками.
            void Call(string method) => typeof(UxrGrabbableObject)
                                        .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                                        .Invoke(grabbable, null);

            try
            {
                point.UseDefaultGrabButtons = false;
                Call("OnEnable");
                Assert.That(UxrGrabbableObject.EnabledWithCustomGrabButtons, Has.Member(grabbable));

                Call("OnDisable");
                Assert.That(UxrGrabbableObject.EnabledWithCustomGrabButtons, Has.No.Member(grabbable));
            }
            finally
            {
                point.UseDefaultGrabButtons = savedDefault;
                UxrGrabbableObject.EnabledWithCustomGrabButtons.Remove(grabbable);
            }
        }

        /// <summary>
        /// Детали со своими кнопками хвата, о которых мы знаем: чека гранаты (<c>Pin</c>, тянется
        /// курком). Рядом с такой деталью контроллер аватара снова делает полный поиск ближайшего
        /// предмета (патч 25) — это нормально, но новая такая деталь должна добавляться сюда осознанно.
        /// </summary>
        private static readonly string[] KnownCustomGrabButtons = { "Pin" };

        [Test]
        public void Предметы_с_особыми_кнопками_хвата_только_известные()
        {
            var custom = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" })
                                         .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                                         .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>)
                                         .Where(go => go != null)
                                         .SelectMany(go => go.GetComponentsInChildren<UxrGrabbableObject>(true))
                                         .Where(g => Enumerable.Range(0, g.GrabPointCount).Any(i => g.GetGrabPoint(i) != null && !g.GetGrabPoint(i).UseDefaultGrabButtons))
                                         .Select(g => g.name)
                                         .Where(n => !KnownCustomGrabButtons.Contains(n))
                                         .Distinct()
                                         .ToList();

            Assert.That(custom, Is.Empty, "Предметы проекта со своими кнопками хвата: " + string.Join(", ", custom));
        }
    }
}
