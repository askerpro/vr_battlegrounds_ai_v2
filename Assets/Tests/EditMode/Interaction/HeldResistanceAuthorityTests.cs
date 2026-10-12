using System;
using System.Collections;
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
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Interaction
{
    /// <summary>
    /// SDK-патч 73 (<c>tasks/grab-authority/changelog/2026-10-12-sdk-73.md</c>): сопротивление
    /// удерживаемого предмета (<c>TranslationResistance</c>/<c>RotationResistance</c>, сглаживание
    /// <see cref="UxrInterpolator" />) применяется, только если каждый держатель-аватар —
    /// <see cref="UxrAvatarMode.Local" />. Рука бота и чужого игрока (<see cref="UxrAvatarMode.UpdateExternally" />)
    /// ставит предмет в целевую позу жёстко. Держатель без аватара решение не меняет.
    ///
    /// <para>
    /// Проверяется правило, а не подобранные числа: «жёстко» — предмет в целевой позе с точностью
    /// <see cref="RigidPosition" />/<see cref="RigidAngle" />, «с сопротивлением» — отстаёт больше
    /// <see cref="LagPosition" />/<see cref="LagAngle" />. Целевая поза — поза предмета, которую цикл
    /// манипуляции дал без сопротивления, перенесённая тем же жёстким движением, что и руки.
    /// </para>
    ///
    /// <para>
    /// <b>Шага времени нет.</b> <see cref="UxrInterpolator" /> берёт <c>Time.deltaTime</c> без шва, а в
    /// EditMode он не задаётся (обычно 0, в редакторе с фокусом — шаг его обновления). Доля шага
    /// t = (1 − r)·dt·20; при r = <see cref="Resistance" /> и любом dt &lt; 0,25 с t &lt; 0,5, и локальный
    /// предмет отстаёт минимум на половину движения руки. Контроль <see cref="RequireEffectiveResistance" />
    /// делает тест неубедительным (Inconclusive), если редактор дал такой dt, при котором сопротивление
    /// ничего не сглаживает, — тогда откат патча не был бы виден. Числа при dt = 1/72 метрика считает
    /// формулой самого <see cref="UxrInterpolator" /> (<c>GetSmoothInterpolationValue</c>).
    /// </para>
    ///
    /// <para>
    /// <b>Что ловит откат патча 73</b> (<c>UxrGrabManager.Manipulation.cs</c>, метка <c>VR Battlegrounds patch 73</c>):
    /// гейт <c>AllowsHeldResistance</c> в <c>FinalizeManipulation</c> убран — падают
    /// <see cref="Внешне_управляемый_держатель_ставит_предмет_жёстко" />,
    /// <see cref="Метрика_вращения_90_градусов_в_секунду" /> и
    /// <see cref="Смешанные_держатели_локальный_и_внешний_без_сопротивления" />; гейт инвертирован
    /// (сопротивление только у удалённых) — падают <see cref="Локальный_держатель_сохраняет_сопротивление" />
    /// и контроли «локальный отстаёт»; в <c>AreAvatarHoldersLocal</c> правило «все» заменено на «хотя бы один
    /// локальный» — падает тест смешанных держателей; убрана проверка <c>grabber.Avatar != null</c>
    /// (держатель без аватара считается удалённым или бросает NRE) — падает
    /// <see cref="Держатель_без_аватара_сохраняет_сопротивление" />; гейт убран из
    /// <c>SetPositionAndRotationUsingConstraintsInternal</c> — падает
    /// <see cref="SetPositionAndRotationUsingConstraints_у_внешнего_держателя_жёстко" />.
    /// Ветка look-at детей (<c>DirectChildrenLookAts</c>) здесь не покрыта.
    /// </para>
    /// </summary>
    public class HeldResistanceAuthorityTests
    {
        /// <summary>Сопротивление предмета в фикстуре: t = (1 − r)·dt·20 = 2·dt.</summary>
        private const float Resistance = 0.9f;

        /// <summary>Выше этой доли шага сопротивление почти не сглаживает — проверка неубедительна.</summary>
        private const float MaxEffectiveInterpolation = 0.5f;

        private const float RigidPosition = 1e-4f;
        private const float RigidAngle = 0.01f;
        private const float LagPosition = 1e-3f;
        private const float LagAngle = 0.5f;

        /// <summary>Шаг кадра шлема для метрики.</summary>
        private const float HeadsetDeltaTime = 1f / 72f;

        private const float MetricAngularSpeed = 90f;
        private const int MetricFrames = 72;

        /// <summary>
        /// Жёсткое движение рук: поворот вокруг основной руки и сдвиг. Малое по сдвигу, чтобы
        /// отставание не превысило <c>LockedGrabReleaseDistance</c> (0,4 м) и SDK не отпустил хват.
        /// </summary>
        private static readonly Quaternion MoveRotation = Quaternion.AngleAxis(20f, new Vector3(0.3f, 1f, 0.2f).normalized);

        private static readonly Vector3 MoveTranslation = new Vector3(0.04f, 0.02f, -0.03f);

        private static UxrGrabManager Manager => TwoHandGrabHarness.Manager;

        [Test]
        public void Есть_что_проверять()
        {
            Assert.IsNotEmpty(TwoHandGrabCases.Collect(),
                              "Нет ни одной пары «оружие арсенала × аватар» с позами двух точек — тесты сопротивления " +
                              "удерживаемого предмета молча ничего не проверяют.");
        }

        /// <summary>Рука бота/чужого игрока: предмет точно в целевой позе, хотя сопротивление предмета &gt; 0.</summary>
        [Test]
        public void Внешне_управляемый_держатель_ставит_предмет_жёстко()
        {
            using var fixture = new HeldResistanceFixture(UxrAvatarMode.UpdateExternally);

            fixture.MoveHandsAndUpdate(MoveRotation, MoveTranslation);

            fixture.AssertRigid("Держит аватар UpdateExternally (бот на сервере, чужой игрок у наблюдателя), а предмет " +
                                "отстаёт от кисти — сопротивление снова применяется к удалённой руке (откат гейта " +
                                "AllowsHeldResistance в FinalizeManipulation, патч 73).");
        }

        /// <summary>Своя рука: прежнее поведение — предмет отстаёт от целевой позы.</summary>
        [Test]
        public void Локальный_держатель_сохраняет_сопротивление()
        {
            using var fixture = new HeldResistanceFixture(UxrAvatarMode.Local);

            fixture.MoveHandsAndUpdate(MoveRotation, MoveTranslation);

            fixture.AssertLagging("Держит локальный аватар, а предмет встал в целевую позу жёстко — ощущение веса своего " +
                                  "предмета пропало: гейт патча 73 отключает сопротивление и локальной руке.");
        }

        /// <summary>
        /// Кисть вращается 90°/с <see cref="MetricFrames" /> кадров: отставание у UpdateExternally ≈ 0,
        /// у Local &gt; 0. Числа — в выводе теста; ассерт только на «≈ 0» и «&gt; 0».
        /// </summary>
        [Test]
        public void Метрика_вращения_90_градусов_в_секунду()
        {
            float step = MetricAngularSpeed * HeadsetDeltaTime;
            float externalLag;
            float localLag;
            float prefabResistance;

            using (var fixture = new HeldResistanceFixture(UxrAvatarMode.UpdateExternally))
            {
                prefabResistance = fixture.PrefabRotationResistance;
                externalLag = fixture.RotateMainHand(step, MetricFrames, true);
            }

            using (var fixture = new HeldResistanceFixture(UxrAvatarMode.Local))
            {
                localLag = fixture.RotateMainHand(step, MetricFrames, false);
            }

            float modelTest = ModelRotationLag(Resistance, step, MetricFrames);
            float modelPrefab = ModelRotationLag(prefabResistance, step, MetricFrames);

            TestContext.WriteLine($"[Info] Отставание поворота предмета при вращении кисти {MetricAngularSpeed}°/с, {MetricFrames} кадров:");
            TestContext.WriteLine($"[Info]   цикл SDK в EditMode (Time.deltaTime={Time.deltaTime:F4} с, r={Resistance}): " +
                                  $"UpdateExternally max {externalLag:F4}°, Local в конце {localLag:F2}°");
            TestContext.WriteLine($"[Info]   формула UxrInterpolator при dt=1/72: r={Resistance} → Local {modelTest:F2}°; " +
                                  $"сопротивление поворота префаба r={prefabResistance} → Local {modelPrefab:F2}°; UpdateExternally 0° (патч 73)");

            Assert.Less(externalLag, RigidAngle,
                        $"Кисть бота/чужого игрока вращается {MetricAngularSpeed}°/с, а предмет отстаёт до {externalLag:F3}° — " +
                        "сопротивление снова применяется к удалённой руке (патч 73).");
            Assert.Greater(localLag, LagAngle,
                           $"Своя кисть вращается {MetricAngularSpeed}°/с, а предмет отстаёт лишь на {localLag:F3}° — " +
                           "сопротивление локального держателя пропало.");
        }

        /// <summary>
        /// Две руки разных аватаров: основная — Local, дополнительная — UpdateExternally. Сопротивления нет:
        /// сглаживание предмета, который ведёт удалённая рука, только добавило бы отставание от неё.
        /// </summary>
        [Test]
        public void Смешанные_держатели_локальный_и_внешний_без_сопротивления()
        {
            using var fixture = new HeldResistanceFixture(UxrAvatarMode.Local, SupportHolder.OtherAvatarExternal);

            fixture.MoveHandsAndUpdate(MoveRotation, MoveTranslation);

            fixture.AssertRigid("Предмет держат локальная рука и рука UpdateExternally, а он отстаёт — сопротивление " +
                                "осталось при смешанных держателях. Правило патча 73: сопротивление, только если КАЖДЫЙ " +
                                "держатель-аватар локальный (AreAvatarHoldersLocal).");
        }

        /// <summary>Контроль к смешанным держателям: две свои руки — сопротивление есть, двуручный путь его применяет.</summary>
        [Test]
        public void Две_локальные_руки_сохраняют_сопротивление()
        {
            using var fixture = new HeldResistanceFixture(UxrAvatarMode.Local, SupportHolder.SameAvatar);

            fixture.MoveHandsAndUpdate(MoveRotation, MoveTranslation);

            fixture.AssertLagging("Предмет держат две руки локального аватара и встаёт в целевую позу жёстко — двуручный " +
                                  "хват не применяет сопротивление вовсе, и тест смешанных держателей ничего не доказывает.");
        }

        /// <summary>
        /// Держатель без аватара решение не меняет: поведение SDK прежнее, с сопротивлением.
        ///
        /// <para>
        /// Кадр манипуляции с такой рукой SDK не проводит вовсе: <c>UxrGrabber.HandBone</c> — это
        /// <c>Avatar.GetHandBone</c>, и <c>KeepGripInPlace</c> бросает NRE (без патча 73 так же). Поэтому
        /// решение гейта проверяется напрямую — приватный <c>AllowsHeldResistance</c> на настоящей
        /// манипуляции, где рука сначала принадлежит аватару UpdateExternally (контроль: сопротивления нет),
        /// а потом вынесена из аватара.
        /// </para>
        /// </summary>
        [Test]
        public void Держатель_без_аватара_сохраняет_сопротивление()
        {
            using var fixture = new HeldResistanceFixture(UxrAvatarMode.UpdateExternally);

            Assert.IsFalse(fixture.AllowsHeldResistance(),
                           "Контроль: рука аватара UpdateExternally, а гейт патча 73 разрешает сопротивление.");

            fixture.DetachMainHandFromAvatar();

            Assert.IsTrue(fixture.AllowsHeldResistance(),
                          "Предмет держит рука без аватара, а гейт патча 73 запрещает сопротивление — AreAvatarHoldersLocal " +
                          "считает хват без аватара удалённым (правило: такой хват решение не меняет).");
        }

        /// <summary>Публичный Set*UsingConstraints с useResistance = true у предмета в руке UpdateExternally — жёстко.</summary>
        [Test]
        public void SetPositionAndRotationUsingConstraints_у_внешнего_держателя_жёстко()
        {
            using var fixture = new HeldResistanceFixture(UxrAvatarMode.UpdateExternally);

            fixture.SetPoseUsingConstraints(MoveRotation, MoveTranslation);

            fixture.AssertRigid("SetPositionAndRotationUsingConstraints(useResistance: true) у предмета в руке UpdateExternally " +
                                "сгладил позу — гейт патча 73 пропал из SetPositionAndRotationUsingConstraintsInternal.");
        }

        /// <summary>Контроль к Set*UsingConstraints: у локального держателя useResistance по-прежнему сглаживает.</summary>
        [Test]
        public void SetPositionAndRotationUsingConstraints_у_локального_держателя_сглаживает()
        {
            using var fixture = new HeldResistanceFixture(UxrAvatarMode.Local);

            fixture.SetPoseUsingConstraints(MoveRotation, MoveTranslation);

            fixture.AssertLagging("SetPositionAndRotationUsingConstraints(useResistance: true) у предмета в своей руке поставил " +
                                  "его жёстко — useResistance перестал работать, тест внешнего держателя ничего не доказывает.");
        }

        /// <summary>
        /// Отставание поворота после <paramref name="frames" /> кадров вращения с шагом <paramref name="step" />
        /// по формуле <see cref="UxrInterpolator.SmoothDampRotation" /> при dt = 1/72: каждый кадр предмет
        /// проходит долю t оставшегося угла.
        /// </summary>
        private static float ModelRotationLag(float resistance, float step, int frames)
        {
            float t = Mathf.Clamp01(SmoothInterpolation(resistance, HeadsetDeltaTime));
            float lag = 0f;

            for (int i = 0; i < frames; ++i)
            {
                lag = (lag + step) * (1f - t);
            }

            return lag;
        }

        private static float SmoothInterpolation(float resistance, float deltaTime)
        {
            MethodInfo method = typeof(UxrInterpolator).GetMethod("GetSmoothInterpolationValue", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(method, "В UxrInterpolator нет GetSmoothInterpolationValue — SDK обновился, поправь тест.");
            return (float)method.Invoke(null, new object[] { resistance, deltaTime });
        }

        /// <summary>
        /// Контроль окружения: при текущем <c>Time.deltaTime</c> сопротивление действительно сглаживает.
        /// Иначе и локальный предмет встал бы жёстко, и откат патча не был бы виден.
        /// </summary>
        private static void RequireEffectiveResistance()
        {
            float t = SmoothInterpolation(Resistance, Time.deltaTime);

            if (t >= MaxEffectiveInterpolation)
            {
                Assert.Inconclusive($"[TestEnvironment] Time.deltaTime={Time.deltaTime:F3} с: доля шага сглаживания {t:F2} ≥ " +
                                    $"{MaxEffectiveInterpolation} — сопротивление почти не сглаживает, правило патча 73 не проверить.");
            }
        }

        private enum SupportHolder
        {
            None,
            SameAvatar,
            OtherAvatarExternal
        }

        /// <summary>
        /// Настоящие оружие и аватар (<see cref="TwoHandGrabHarness" />): правая рука держит основную точку,
        /// по желанию — левая рука того же или второго аватара держит дополнительную. Позы установлены
        /// циклом манипуляции без сопротивления, потом сопротивление включается.
        ///
        /// <para>
        /// Руки ведёт тест, как контроллер: каждый кадр ставит им позу заново. Цикл SDK
        /// (<c>KeepGripInPlace</c>) после кадра подтягивает кисть к отстающему предмету — замер идёт от
        /// заданной позы руки, а не от подтянутой. <c>Align To Controller</c> у точек выключен: локальный
        /// аватар брал бы поворот у модели контроллера, которой в EditMode нет.
        /// </para>
        /// </summary>
        private sealed class HeldResistanceFixture : IDisposable
        {
            private readonly TwoHandGrabHarness _harness;
            private readonly UxrAvatar _avatar;
            private readonly GameObject _otherAvatar;
            private readonly UxrGrabber[] _otherGrabbers = Array.Empty<UxrGrabber>();
            private readonly UxrGrabber _support;

            private Pose _mainHand;
            private Pose _supportHand;
            private Pose _weapon;
            private Pose _expected;
            private Transform _detachedParent;

            public float PrefabRotationResistance { get; }

            private UxrGrabbableObject Grabbable => _harness.Grabbable;

            public HeldResistanceFixture(UxrAvatarMode mode, SupportHolder support = SupportHolder.None)
            {
                TwoHandGrabCase c = PickCase();
                _harness = new TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint);

                try
                {
                    _avatar = _harness.Avatar.GetComponent<UxrAvatar>();
                    PrefabRotationResistance = Grabbable.RotationResistance;

                    for (int i = 0; i < Grabbable.GrabPointCount; ++i)
                    {
                        Grabbable.GetGrabPoint(i).AlignToController = false;
                    }

                    _avatar.AvatarMode = mode;

                    // До второй руки: контроль харнесса крутит основную руку и ждёт, что оружие повернётся следом.
                    _harness.AssertManipulationLive();

                    if (support == SupportHolder.SameAvatar)
                    {
                        _support = _harness.Left;
                    }
                    else if (support == SupportHolder.OtherAvatarExternal)
                    {
                        _otherAvatar = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(c.AvatarPath));
                        _otherAvatar.GetComponent<UxrAvatar>().AvatarMode = UxrAvatarMode.UpdateExternally;
                        _otherGrabbers = _otherAvatar.GetComponentsInChildren<UxrGrabber>(true);

                        foreach (UxrGrabber grabber in _otherGrabbers)
                        {
                            Invoke(grabber, "Awake");
                        }

                        _support = _otherGrabbers.Single(g => g.Side == UxrHandSide.Left);
                    }

                    if (_support != null)
                    {
                        Grabbable.ComputeRequiredGrabberTransform(_support, c.SupportPoint, out Vector3 position, out Quaternion rotation, false);
                        _support.transform.SetPositionAndRotation(position, rotation);
                        Manager.GrabObject(_support, Grabbable, c.SupportPoint, false);
                        Assert.IsTrue(Manager.GetGrabbingHand(Grabbable, c.SupportPoint, out UxrGrabber holder) && holder == _support,
                                      "Контроль фикстуры: вторая рука держит дополнительную точку.");
                    }

                    Settle();

                    Grabbable.TranslationResistance = Resistance;
                    Grabbable.RotationResistance = Resistance;
                    Manager.Features |= UxrManipulationFeatures.ObjectResistance;

                    RequireEffectiveResistance();

                    TestContext.WriteLine($"[Info] Фикстура: {System.IO.Path.GetFileNameWithoutExtension(c.WeaponPath)} × " +
                                          $"{System.IO.Path.GetFileNameWithoutExtension(c.AvatarPath)}, держатель {mode}, вторая рука {support}, " +
                                          $"Time.deltaTime={Time.deltaTime:F4} с, доля шага сглаживания {SmoothInterpolation(Resistance, Time.deltaTime):F3}.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            /// <summary>
            /// Кадры без сопротивления, пока поза предмета не установится; потом запоминаются позы рук
            /// и предмета — от них считается целевая поза.
            /// </summary>
            private void Settle()
            {
                for (int i = 0; i < 3; ++i)
                {
                    _harness.UpdateManipulation();
                }

                _mainHand = PoseOf(_harness.Right.transform);
                _supportHand = _support != null ? PoseOf(_support.transform) : default;
                _weapon = PoseOf(_harness.Weapon.transform);
                AssertHeld();
            }

            /// <summary>Руки сдвигаются одним жёстким движением, затем один кадр манипуляции.</summary>
            public void MoveHandsAndUpdate(Quaternion rotation, Vector3 translation)
            {
                Vector3 pivot = _mainHand.position;
                SetPose(_harness.Right.transform, Apply(_mainHand, pivot, rotation, translation));

                if (_support != null)
                {
                    SetPose(_support.transform, Apply(_supportHand, pivot, rotation, translation));
                }

                _expected = Apply(_weapon, pivot, rotation, translation);
                _harness.UpdateManipulation();
                AssertHeld();
            }

            /// <summary>Предмет переносится публичным Set*UsingConstraints с useResistance вне цикла манипуляции.</summary>
            public void SetPoseUsingConstraints(Quaternion rotation, Vector3 translation)
            {
                _expected = Apply(_weapon, _mainHand.position, rotation, translation);
                Manager.SetPositionAndRotationUsingConstraints(Grabbable, _expected.position, _expected.rotation, true, false);
                AssertHeld();
            }

            /// <summary>
            /// Основная рука вращается вокруг вертикали на <paramref name="step" /> градусов за кадр.
            /// Возвращает отставание поворота предмета от целевого: максимум за все кадры
            /// (<paramref name="maximum" />) или в последнем кадре.
            /// </summary>
            public float RotateMainHand(float step, int frames, bool maximum)
            {
                Quaternion relative = Quaternion.Inverse(_mainHand.rotation) * _weapon.rotation;
                float result = 0f;

                for (int i = 1; i <= frames; ++i)
                {
                    Quaternion hand = Quaternion.AngleAxis(step * i, Vector3.up) * _mainHand.rotation;
                    _harness.Right.transform.SetPositionAndRotation(_mainHand.position, hand);
                    _harness.UpdateManipulation();

                    float lag = Quaternion.Angle(hand * relative, _harness.Weapon.transform.rotation);
                    result = maximum ? Mathf.Max(result, lag) : lag;
                }

                AssertHeld();
                return result;
            }

            /// <summary>
            /// Основная рука перестаёт принадлежать аватару: кисть выносится из иерархии аватара,
            /// кэш <c>UxrAvatarComponent._avatar</c> сбрасывается. В Dispose всё возвращается.
            /// </summary>
            public void DetachMainHandFromAvatar()
            {
                Transform hand = _harness.Right.transform;
                _detachedParent = hand.parent;
                hand.SetParent(null, true);
                ResetAvatarCache(_harness.Right);

                Assert.IsNull(_harness.Right.Avatar, "[TestEnvironment] Рука вынесена из аватара, а UxrGrabber.Avatar не пуст.");
                Assert.IsTrue(Manager.IsBeingGrabbed(Grabbable), "Контроль фикстуры: рука без аватара держит предмет.");
            }

            /// <summary>Решение гейта патча 73 для текущей манипуляции предмета (приватный AllowsHeldResistance).</summary>
            public bool AllowsHeldResistance()
            {
                FieldInfo field = typeof(UxrGrabManager).GetField("_currentManipulations", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo gate = typeof(UxrGrabManager).GetMethod("AllowsHeldResistance", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(field, "В UxrGrabManager нет _currentManipulations — SDK обновился, поправь тест.");
                Assert.IsNotNull(gate, "В UxrGrabManager нет AllowsHeldResistance — патч 73 откатан или переименован.");

                object manipulation = ((IDictionary)field.GetValue(Manager))[Grabbable];
                Assert.IsNotNull(manipulation, "Контроль фикстуры: у предмета нет текущей манипуляции.");

                try
                {
                    return (bool)gate.Invoke(Manager, new[] { Grabbable, manipulation });
                }
                catch (TargetInvocationException e)
                {
                    Assert.Fail($"AllowsHeldResistance бросил {e.InnerException?.GetType().Name}: {e.InnerException?.Message} — " +
                                "хват без аватара не должен ломать гейт патча 73.");
                    throw;
                }
            }

            public void AssertRigid(string message)
            {
                Measure(out float distance, out float angle);
                Assert.That(distance < RigidPosition && angle < RigidAngle,
                            $"{message} Смещение от целевой позы {distance * 1000f:F3} мм, угол {angle:F3}° " +
                            $"(жёстко — меньше {RigidPosition * 1000f} мм и {RigidAngle}°).");
            }

            public void AssertLagging(string message)
            {
                Measure(out float distance, out float angle);
                Assert.That(distance > LagPosition && angle > LagAngle,
                            $"{message} Смещение от целевой позы {distance * 1000f:F3} мм, угол {angle:F3}° " +
                            $"(с сопротивлением — больше {LagPosition * 1000f} мм и {LagAngle}°).");
            }

            private void Measure(out float distance, out float angle)
            {
                Transform weapon = _harness.Weapon.transform;
                distance = Vector3.Distance(_expected.position, weapon.position);
                angle = Quaternion.Angle(_expected.rotation, weapon.rotation);

                // Контроль: движение заметно больше порогов, иначе «отстаёт» и «жёстко» неотличимы.
                Assert.Greater(Vector3.Distance(_expected.position, _weapon.position), LagPosition * 10f, "[TestEnvironment] Целевая поза почти не сдвинута.");
                Assert.Greater(Quaternion.Angle(_expected.rotation, _weapon.rotation), LagAngle * 10f, "[TestEnvironment] Целевая поза почти не повёрнута.");
            }

            private void AssertHeld()
            {
                Assert.IsTrue(Manager.IsBeingGrabbed(Grabbable), "Контроль фикстуры: SDK отпустил предмет (отставание дальше LockedGrabReleaseDistance?).");

                if (_support != null)
                {
                    Assert.AreEqual(2, Manager.GetGrabbingHands(Grabbable).Count(), "Контроль фикстуры: предмет держат обе руки.");
                }
            }

            public void Dispose()
            {
                if (_detachedParent != null && _harness.Right != null)
                {
                    _harness.Right.transform.SetParent(_detachedParent, true);
                    ResetAvatarCache(_harness.Right);
                    _detachedParent = null;
                }

                // Статический UxrAvatar.LocalAvatar не должен пережить тест.
                if (_avatar != null && _avatar.AvatarMode == UxrAvatarMode.Local)
                {
                    _avatar.AvatarMode = UxrAvatarMode.UpdateExternally;
                }

                if (_otherAvatar != null)
                {
                    foreach (UxrGrabber grabber in _otherGrabbers)
                    {
                        if (grabber != null && grabber.GrabbedObject != null)
                        {
                            Manager.ReleaseObject(grabber, grabber.GrabbedObject, false);
                        }
                    }

                    foreach (UxrGrabber grabber in _otherGrabbers)
                    {
                        if (grabber != null)
                        {
                            Invoke(grabber, "OnDestroy");
                        }
                    }

                    Object.DestroyImmediate(_otherAvatar);
                }

                _harness?.Dispose();
            }

            /// <summary>
            /// Один кейс достаточно: правило не зависит от оружия. Предпочитается цевьё (рука на
            /// дополнительной точке задаёт направление) — двуручный путь решения нагружен сильнее.
            /// </summary>
            private static TwoHandGrabCase PickCase()
            {
                var cases = TwoHandGrabCases.Collect();
                Assert.IsNotEmpty(cases, "[TestEnvironment] Нет пары «оружие × аватар» с позами двух точек.");
                return cases.Where(c => c.HandGap >= TwoHandGrabCases.SupportGripMaxGap).DefaultIfEmpty(cases[0]).First();
            }

            private static Pose PoseOf(Transform t) => new Pose(t.position, t.rotation);

            private static void SetPose(Transform t, Pose pose) => t.SetPositionAndRotation(pose.position, pose.rotation);

            private static Pose Apply(Pose pose, Vector3 pivot, Quaternion rotation, Vector3 translation)
            {
                return new Pose(pivot + rotation * (pose.position - pivot) + translation, rotation * pose.rotation);
            }

            private static void ResetAvatarCache(UxrGrabber grabber)
            {
                for (Type type = grabber.GetType(); type != null; type = type.BaseType)
                {
                    FieldInfo field = type.GetField("_avatar", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

                    if (field != null)
                    {
                        field.SetValue(grabber, null);
                        return;
                    }
                }

                Assert.Fail("В UxrAvatarComponent нет поля _avatar — SDK обновился, поправь тест.");
            }

            private static void Invoke(object target, string method)
            {
                for (Type type = target.GetType(); type != null; type = type.BaseType)
                {
                    MethodInfo info = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly,
                                                     null, Type.EmptyTypes, null);

                    if (info != null)
                    {
                        info.Invoke(target, null);
                        return;
                    }
                }

                Assert.Fail($"У {target.GetType().Name} нет метода {method}.");
            }
        }
    }
}
