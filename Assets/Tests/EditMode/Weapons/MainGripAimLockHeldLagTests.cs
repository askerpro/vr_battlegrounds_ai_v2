using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Animation.Interpolation;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Tests.Player;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// <see cref="MainGripAimLock" /> запоминает позу «оружие относительно основной руки» только тогда,
    /// когда оружие догнало руку (aimlock-held-lag, принято в шлеме 2026-10-13; bots-fix 4442).
    ///
    /// <para>
    /// <b>Дефект.</b> У своего аватара SDK сглаживает предмет в руке (сопротивление, SDK-патч 73,
    /// <c>UxrGrabManager.FinalizeManipulation</c>): пока рука движется, оружие отстаёт от неё. Раньше
    /// поза одной руки запоминалась в <c>ConstraintsApplied</c> каждый кадр как есть, и если поддержку
    /// брали на ходу, в замок уходила поза последнего кадра с одной рукой — с отставанием. Дальше
    /// <c>MainGripAimLock</c> держал её всё время хвата двумя руками (у PPK 33,9 мм / 2,2°).
    /// </para>
    ///
    /// <para>
    /// <b>Правило.</b> Кандидат из <c>ConstraintsApplied</c> принимается в <c>ConstraintsFinished</c>, только
    /// если после <c>KeepGripsInPlace</c> граббер основной руки остался там, где его поставил трекинг
    /// (<c>UnprocessedGrabberPosition</c>), с точностью 1,5 мм. Значит, после хвата поддержки на ходу и
    /// остановки руки поза оружия относительно руки — поза одной руки в покое (до движения).
    /// </para>
    ///
    /// <para>
    /// <b>Почему старый код падает.</b> Каждый сценарий «на ходу» сначала проверяет контроль: в последнем
    /// кадре с одной рукой поза оружия относительно трекинга руки отличается от позы покоя больше чем
    /// втрое против допуска. Старый код запомнил бы именно её и держал бы после хвата поддержки — итоговая
    /// поза отличалась бы от позы покоя на это отставание, ассерт упал бы.
    /// </para>
    ///
    /// <para>
    /// <b>Шаг времени.</b> <see cref="UxrInterpolator" /> берёт <c>Time.deltaTime</c> без шва, а в EditMode он
    /// любой (0, шаг обновления редактора, у воркера 0,333 с). Основной сценарий не зависит от него вовсе:
    /// отставание создаётся прямо — подписчик <c>ConstraintsApplying</c> сдвигает оружие назад по движению,
    /// сопротивление SDK выключено. Сценарий с настоящим сопротивлением берёт r = <see cref="Resistance" />:
    /// доля шага (1 − r)·dt·20 = 0,2·dt меньше <see cref="MaxInterpolation" /> при любом dt до 2,5 с, а при
    /// dt = 0 оружие просто стоит — отставание тоже есть. Inconclusive — только при dt &gt; 2,5 с.
    /// </para>
    ///
    /// <para>
    /// Харнесс — <see cref="TwoHandGrabHarness" /> (настоящие оружие и аватар). Руки ведёт тест, как
    /// контроллер: каждый кадр ставит им позу трекинга заново (<c>KeepGripInPlace</c> после кадра подтягивает
    /// кисть к оружию). Замер — от позы трекинга, а не от подтянутой кисти.
    /// </para>
    /// </summary>
    public class MainGripAimLockHeldLagTests
    {
        /// <summary>Допуск «оружие догнало руку» в <see cref="MainGripAimLock" />, метры.</summary>
        private const float SettledGap = 0.0015f;

        private const float PositionTolerance = 0.001f;
        private const float AngleTolerance = 0.1f;

        /// <summary>Сопротивление в сценарии с SDK: доля шага сглаживания 0,2·dt.</summary>
        private const float Resistance = 0.99f;

        /// <summary>Выше этой доли шага отставание может оказаться меньше порогов — проверка неубедительна.</summary>
        private const float MaxInterpolation = 0.5f;

        private const int MoveFrames = 6;
        private const int HoldFrames = 5;

        /// <summary>Шаг руки за кадр ≈ 7,5 мм (≈ 0,54 м/с при 72 Гц) и поворот 0,4°: ходьба с поднятым оружием.</summary>
        private static readonly Vector3 MoveStep = new Vector3(0.006f, 0.002f, -0.004f);

        private const float TurnStep = 0.4f;

        /// <summary>Прямое отставание — как у PPK в шлеме (bots-fix 4442).</summary>
        private const float InjectedLagDistance = 0.0339f;

        private const float InjectedLagAngle = 2.2f;

        private static UxrGrabManager Manager => TwoHandGrabHarness.Manager;

        /// <summary>
        /// Основной сценарий, не зависящий от <c>Time.deltaTime</c>: оружие отстаёт от движущейся руки
        /// (сдвиг подписчиком <c>ConstraintsApplying</c>), поддержку берут на ходу, рука останавливается —
        /// поза относительно руки равна позе одной руки в покое.
        /// </summary>
        [Test]
        public void Поддержка_взята_на_ходу_при_отставании_оружия_держит_позу_покоя()
        {
            using var fixture = new LagFixture(UxrAvatarMode.Local);

            fixture.InjectLag = true;
            fixture.MoveOneHand(MoveFrames);
            fixture.InjectLag = false;

            fixture.AssertLaggingAtSupportGrab();
            fixture.GrabSupport();
            fixture.Hold(HoldFrames);

            fixture.AssertRestPose("Поддержку взяли на ходу, когда оружие отставало от основной руки, и после остановки оружие " +
                                   "осталось смещено относительно руки — MainGripAimLock запомнил позу с отставанием " +
                                   "(откат aimlock-held-lag: кандидат принимается без проверки в ConstraintsFinished).");
        }

        /// <summary>
        /// То же с настоящим сопротивлением SDK у локального держателя (патч 73): отставание даёт
        /// <see cref="UxrInterpolator" />, r = <see cref="Resistance" />.
        /// </summary>
        [Test]
        public void Поддержка_взята_на_ходу_с_сопротивлением_SDK_держит_позу_покоя()
        {
            using var fixture = new LagFixture(UxrAvatarMode.Local);

            fixture.EnableResistance();
            RequireEffectiveResistance();

            fixture.MoveOneHand(MoveFrames);

            fixture.AssertLaggingAtSupportGrab();
            fixture.GrabSupport();
            fixture.Hold(HoldFrames);

            fixture.AssertRestPose("Своя рука с сопротивлением SDK, поддержку взяли на ходу, и после остановки оружие осталось " +
                                   "смещено относительно руки — отставание сглаживания попало в замок MainGripAimLock.");
        }

        /// <summary>Контроль: поддержку берут в покое — поза одной руки, оружие не дёргается.</summary>
        [Test]
        public void Поддержка_взята_в_покое_без_рывка()
        {
            using var fixture = new LagFixture(UxrAvatarMode.Local);

            fixture.EnableResistance();
            fixture.Hold(2);

            Pose before = fixture.WeaponPose;
            fixture.GrabSupport();
            fixture.Hold(HoldFrames);

            AssertSame(before, fixture.WeaponPose, "Поддержку взяли в покое, а оружие в мире сдвинулось — рывок при хвате второй рукой.");
            fixture.AssertRestPose("Поддержку взяли в покое, а поза оружия относительно основной руки изменилась.");
        }

        /// <summary>
        /// Без сопротивления (бот, чужой аватар — UpdateExternally, патч 73 ставит предмет жёстко): оружие
        /// догоняет руку каждый кадр, поза запоминается на ходу, поведение прежнее.
        /// </summary>
        [Test]
        public void Внешний_держатель_без_отставания_поддержка_на_ходу_держит_позу_покоя()
        {
            using var fixture = new LagFixture(UxrAvatarMode.UpdateExternally);

            fixture.EnableResistance();
            fixture.MoveOneHand(MoveFrames);

            Assert.LessOrEqual(fixture.LastOneHandGap, SettledGap,
                               $"[TestEnvironment] Держатель UpdateExternally, а оружие отстаёт от руки на {fixture.LastOneHandGap * 1000f:F2} мм — " +
                               "сопротивление снова применяется к удалённой руке (патч 73), сценарий не про это.");

            fixture.GrabSupport();
            fixture.Hold(HoldFrames);

            fixture.AssertRestPose("Держатель без сглаживания (бот/чужой аватар), поддержку взяли на ходу, а поза оружия относительно " +
                                   "руки изменилась — проверка «оружие догнало руку» ломает замок без отставания.");
        }

        /// <summary>Контроль окружения: при текущем <c>Time.deltaTime</c> сопротивление действительно сглаживает.</summary>
        private static void RequireEffectiveResistance()
        {
            MethodInfo method = typeof(UxrInterpolator).GetMethod("GetSmoothInterpolationValue", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(method, "В UxrInterpolator нет GetSmoothInterpolationValue — SDK обновился, поправь тест.");
            float t = (float)method.Invoke(null, new object[] { Resistance, Time.deltaTime });
            TestContext.WriteLine($"[Info] Time.deltaTime={Time.deltaTime:F4} с, доля шага сглаживания {t:F3}.");

            if (t >= MaxInterpolation)
            {
                Assert.Inconclusive($"[TestEnvironment] Time.deltaTime={Time.deltaTime:F3} с: доля шага сглаживания {t:F2} ≥ " +
                                    $"{MaxInterpolation} — сопротивление почти не сглаживает. Регрессию покрывает сценарий с прямым отставанием.");
            }
        }

        private static void AssertSame(Pose expected, Pose actual, string message)
        {
            float distance = Vector3.Distance(expected.position, actual.position);
            float angle = Quaternion.Angle(expected.rotation, actual.rotation);

            Assert.That(distance <= PositionTolerance && angle <= AngleTolerance,
                        $"{message} Отклонение {distance * 1000f:F2} мм, {angle:F2}° (допуск {PositionTolerance * 1000f} мм, {AngleTolerance}°).");
        }

        /// <summary>
        /// Пистолет с <see cref="MainGripAimLock" /> и поддержкой: правая рука на основной точке, левая — на
        /// дополнительной. Предпочитается PPK, на котором дефект замерен в шлеме.
        /// </summary>
        private static TwoHandGrabCase PickCase()
        {
            var cases = TwoHandGrabCases.Collect()
                                        .Where(c => c.HandGap < TwoHandGrabCases.SupportGripMaxGap &&
                                                    AssetDatabase.LoadAssetAtPath<GameObject>(c.WeaponPath).GetComponent<MainGripAimLock>() != null)
                                        .ToList();

            Assert.IsNotEmpty(cases, "[TestEnvironment] Нет пары «пистолет с MainGripAimLock × аватар» с позами двух точек — проверять нечего.");
            return cases.Where(c => Path.GetFileNameWithoutExtension(c.WeaponPath) == "PPK").DefaultIfEmpty(cases[0]).First();
        }

        /// <summary>
        /// Оружие в правой руке аватара в режиме <paramref name="mode" />; поза одной руки в покое установлена
        /// циклом манипуляции без сопротивления. <c>Align To Controller</c> у точек выключен: локальный аватар
        /// брал бы поворот у модели контроллера, которой в EditMode нет.
        /// </summary>
        private sealed class LagFixture : IDisposable
        {
            private readonly TwoHandGrabHarness _harness;
            private readonly UxrAvatar _avatar;
            private readonly bool _subscribed;

            /// <summary>Поза правой руки по трекингу — её тест ставит каждый кадр.</summary>
            private Pose _right;

            /// <summary>Поза левой руки по трекингу относительно правой, пока левая держит поддержку.</summary>
            private Pose _leftInRight;

            private bool _leftHeld;

            /// <summary>Поза оружия относительно трекинга правой руки в покое — поза одной руки.</summary>
            private readonly Pose _rest;

            /// <summary>То же в последнем кадре с одной рукой — её запомнил бы старый код.</summary>
            private Pose _lastOneHand;

            public bool InjectLag { get; set; }

            /// <summary>Расхождение граббера основной руки с трекингом в последнем кадре с одной рукой, метры.</summary>
            public float LastOneHandGap { get; private set; }

            public Pose WeaponPose => PoseOf(_harness.Weapon.transform);

            private UxrGrabbableObject Grabbable => _harness.Grabbable;

            public LagFixture(UxrAvatarMode mode)
            {
                TwoHandGrabCase c = PickCase();
                _harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint);

                try
                {
                    Assert.IsNotNull(_harness.Weapon.GetComponent<MainGripAimLock>(), "[TestEnvironment] На оружии нет MainGripAimLock.");
                    _avatar = _harness.Avatar.GetComponent<UxrAvatar>();

                    for (int i = 0; i < Grabbable.GrabPointCount; ++i)
                    {
                        Grabbable.GetGrabPoint(i).AlignToController = false;
                    }

                    _avatar.AvatarMode = mode;

                    // В игре UxrHandIntegration.Awake подвешивает интеграцию руки (с граббером) к кости кисти, и
                    // KeepGripsInPlace, двигая кость, ставит граббер на снап оружия — по этому зазору MainGripAimLock
                    // видит отставание. Вне Play Mode Awake не зовётся: повторяем только переподчинение, без
                    // регистрации компонента (иначе уникальные id пережили бы тест).
                    foreach (UxrHandIntegration integration in _avatar.GetComponentsInChildren<UxrHandIntegration>(true))
                    {
                        Transform handBone = _avatar.GetHandBone(integration.HandSide);
                        Assert.IsNotNull(handBone, $"[TestEnvironment] У аватара нет кости кисти {integration.HandSide}.");
                        integration.transform.SetParent(handBone, true);
                    }

                    Assert.IsTrue(_harness.Right.transform.IsChildOf(_avatar.GetHandBone(UxrHandSide.Right)),
                                  "[TestEnvironment] Граббер правой руки не на кости кисти — KeepGripsInPlace его не сдвинет.");
                    _harness.AssertManipulationLive();

                    _right = PoseOf(_harness.Right.transform);

                    for (int i = 0; i < 3; ++i)
                    {
                        Step();
                    }

                    _rest = RelativeToTrackedHand();
                    float restGap = MainHandGap();
                    Assert.LessOrEqual(restGap, SettledGap,
                                       $"[TestEnvironment] Рука в покое, а граббер после KeepGripsInPlace в {restGap * 1000f:F2} мм от трекинга — " +
                                       "поза одной руки не запомнится, сценарии ничего не докажут.");

                    Grabbable.ConstraintsApplying += Grabbable_ConstraintsApplying;
                    _subscribed = true;

                    TestContext.WriteLine($"[Info] Фикстура: {Path.GetFileNameWithoutExtension(c.WeaponPath)} × " +
                                          $"{Path.GetFileNameWithoutExtension(c.AvatarPath)}, держатель {mode}, точка поддержки {c.SupportPoint}.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void EnableResistance()
            {
                Grabbable.TranslationResistance = Resistance;
                Grabbable.RotationResistance = Resistance;
                Manager.Features |= UxrManipulationFeatures.ObjectResistance;
            }

            /// <summary>Основная рука движется с постоянной скоростью <paramref name="frames" /> кадров, поддержки нет.</summary>
            public void MoveOneHand(int frames)
            {
                for (int i = 0; i < frames; ++i)
                {
                    _right = new Pose(_right.position + MoveStep, Quaternion.AngleAxis(TurnStep, Vector3.up) * _right.rotation);
                    Step();
                }

                _lastOneHand = RelativeToTrackedHand();
                LastOneHandGap = MainHandGap();
            }

            /// <summary>
            /// Контроль регрессии: в последнем кадре с одной рукой оружие отставало. Граббер отошёл от трекинга
            /// (новый код кандидат отвергнет), а поза относительно руки заметно не равна позе покоя (старый код
            /// запомнил бы её — ассерт сценария упал бы).
            /// </summary>
            public void AssertLaggingAtSupportGrab()
            {
                float distance = Vector3.Distance(_rest.position, _lastOneHand.position);
                float angle = Quaternion.Angle(_rest.rotation, _lastOneHand.rotation);
                TestContext.WriteLine($"[Info] Отставание в момент хвата поддержки: граббер {LastOneHandGap * 1000f:F2} мм от трекинга; " +
                                      $"поза относительно руки — {distance * 1000f:F2} мм, {angle:F2}° от позы покоя (её зафиксировал бы старый код).");

                Assert.Greater(LastOneHandGap, SettledGap * 2f,
                               "[TestEnvironment] Оружие не отстаёт от руки в момент хвата поддержки — сценарий не воспроизводит дефект.");
                Assert.That(distance > PositionTolerance * 3f || angle > AngleTolerance * 3f,
                            "[TestEnvironment] Поза с отставанием почти равна позе покоя — старый код тоже прошёл бы.");
            }

            /// <summary>Левая рука встаёт на дополнительную точку там, где сейчас оружие, и берёт её.</summary>
            public void GrabSupport()
            {
                _harness.PlaceLeftOnSupport();
                Manager.GrabObject(_harness.Left, Grabbable, _harness.SupportPoint, false);
                Assert.IsTrue(Manager.GetGrabbingHand(Grabbable, _harness.SupportPoint, out UxrGrabber holder) && holder == _harness.Left,
                              "Контроль фикстуры: левая рука держит дополнительную точку.");

                Transform left = _harness.Left.transform;
                Quaternion inverse = Quaternion.Inverse(_right.rotation);
                _leftInRight = new Pose(inverse * (left.position - _right.position), inverse * left.rotation);
                _leftHeld = true;
            }

            /// <summary>Руки неподвижны <paramref name="frames" /> кадров.</summary>
            public void Hold(int frames)
            {
                for (int i = 0; i < frames; ++i)
                {
                    Step();
                }
            }

            public void AssertRestPose(string message) => AssertSame(_rest, RelativeToTrackedHand(), message);

            /// <summary>Кадр: руки в позе трекинга, затем кадр манипуляции SDK.</summary>
            private void Step()
            {
                _harness.Right.transform.SetPositionAndRotation(_right.position, _right.rotation);

                if (_leftHeld)
                {
                    _harness.Left.transform.SetPositionAndRotation(_right.position + _right.rotation * _leftInRight.position,
                                                                   _right.rotation * _leftInRight.rotation);
                }

                _harness.UpdateManipulation();

                Assert.IsTrue(Manager.IsBeingGrabbed(Grabbable), "Контроль фикстуры: SDK отпустил оружие.");

                if (_leftHeld)
                {
                    Assert.AreEqual(2, Manager.GetGrabbingHands(Grabbable).Count(), "Контроль фикстуры: оружие держат обе руки.");
                }
            }

            /// <summary>
            /// Отставание как от сглаживания: оружие сдвинуто назад по движению руки и недоповёрнуто. Вызывается
            /// до <c>ConstraintsApplied</c>, то есть до того, как <see cref="MainGripAimLock" /> берёт кандидата.
            /// </summary>
            private void Grabbable_ConstraintsApplying(object sender, UxrApplyConstraintsEventArgs e)
            {
                if (!InjectLag)
                {
                    return;
                }

                Transform weapon = _harness.Weapon.transform;
                weapon.SetPositionAndRotation(weapon.position - MoveStep.normalized * InjectedLagDistance,
                                              Quaternion.AngleAxis(-InjectedLagAngle, Vector3.up) * weapon.rotation);
            }

            private Pose RelativeToTrackedHand()
            {
                Transform weapon = _harness.Weapon.transform;
                Quaternion inverse = Quaternion.Inverse(_right.rotation);
                return new Pose(inverse * (weapon.position - _right.position), inverse * weapon.rotation);
            }

            /// <summary>После <c>KeepGripsInPlace</c> граббер стоит на снапе оружия; расхождение с трекингом — отставание.</summary>
            private float MainHandGap() => Vector3.Distance(_harness.Right.transform.position, _harness.Right.UnprocessedGrabberPosition);

            public void Dispose()
            {
                if (_subscribed && Grabbable != null)
                {
                    Grabbable.ConstraintsApplying -= Grabbable_ConstraintsApplying;
                }

                // Статический UxrAvatar.LocalAvatar не должен пережить тест.
                if (_avatar != null && _avatar.AvatarMode == UxrAvatarMode.Local)
                {
                    _avatar.AvatarMode = UxrAvatarMode.UpdateExternally;
                }

                _harness?.Dispose();
            }

            private static Pose PoseOf(Transform t) => new Pose(t.position, t.rotation);
        }
    }
}
