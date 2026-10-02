using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Кейс хвата двумя руками: правая рука на основной точке, левая — на дополнительной.
    /// </summary>
    public readonly struct TwoHandGrabCase
    {
        public readonly string WeaponPath;
        public readonly string AvatarPath;
        public readonly int SupportPoint;

        /// <summary>Расстояние между местами ладоней: правой на основной и левой на дополнительной.</summary>
        public readonly float HandGap;

        public TwoHandGrabCase(string weaponPath, string avatarPath, int supportPoint, float handGap)
        {
            WeaponPath = weaponPath;
            AvatarPath = avatarPath;
            SupportPoint = supportPoint;
            HandGap = handGap;
        }
    }

    /// <summary>
    /// Откуда берутся кейсы хвата двумя руками: ничего не называется по пути.
    ///
    /// <para>
    /// <b>Оружие</b> — префабы из ассетов <see cref="WeaponInfo" />, то есть всё, что можно
    /// взять в арсенале. Подходит оружие, у корневого <see cref="UxrGrabbableObject" /> которого
    /// включены <c>Allow Multi Grab</c> и <c>First Grab Point Is Main</c> и больше одной точки.
    /// </para>
    ///
    /// <para>
    /// <b>Аватары</b> — все из <see cref="AvatarRegistry" />. Пара берётся, только если у аватара
    /// для обеих точек есть своя поза хвата с местом ладони нужной руки: без неё UltimateXR
    /// подставляет пустую позу по умолчанию, и проверять нечего. Отсутствие поз ловит
    /// <c>AvatarLoadoutTests</c>.
    /// </para>
    /// </summary>
    public static class TwoHandGrabCases
    {
        public const int MainPoint = 0;

        /// <summary>
        /// Дополнительная точка ближе этого к основной — поддержка (пистолет): вторая рука обнимает
        /// первую и направление не задаёт. Дальше — цевьё (винтовка), которое наводит ствол.
        /// </summary>
        public const float SupportGripMaxGap = 0.10f;

        public static IEnumerable<TestCaseData> All() => Collect().Select(ToTestCase);

        public static IEnumerable<TestCaseData> SupportGrips() => Collect().Where(c => c.HandGap < SupportGripMaxGap).Select(ToTestCase);

        public static List<TwoHandGrabCase> Collect()
        {
            var cases = new List<TwoHandGrabCase>();

            List<UxrAvatar> avatars = LoadAll<AvatarRegistry>().SelectMany(r => r.avatars)
                                                               .Where(d => d != null && d.prefab != null)
                                                               .Select(d => d.prefab.GetComponent<UxrAvatar>())
                                                               .Where(a => a != null)
                                                               .Distinct()
                                                               .ToList();

            foreach (GameObject weapon in LoadAll<WeaponInfo>().Select(w => w.WeaponPrefab).Where(p => p != null).Distinct())
            {
                UxrGrabbableObject grabbable = weapon.GetComponent<UxrGrabbableObject>();

                if (grabbable == null || !grabbable.AllowMultiGrab || !grabbable.FirstGrabPointIsMain || grabbable.GrabPointCount < 2)
                {
                    continue;
                }

                foreach (UxrAvatar avatar in avatars)
                {
                    Transform main = AlignTransform(grabbable, MainPoint, avatar, UxrHandSide.Right);

                    for (int support = 1; support < grabbable.GrabPointCount; ++support)
                    {
                        Transform supportAlign = AlignTransform(grabbable, support, avatar, UxrHandSide.Left);

                        if (main != null && supportAlign != null)
                        {
                            cases.Add(new TwoHandGrabCase(AssetDatabase.GetAssetPath(weapon), AssetDatabase.GetAssetPath(avatar.gameObject), support,
                                                          Vector3.Distance(main.position, supportAlign.position)));
                        }
                    }
                }
            }

            return cases;
        }

        internal static Transform AlignTransform(UxrGrabbableObject grabbable, int point, UxrAvatar avatar, UxrHandSide side)
        {
            UxrGrabPointInfo info = grabbable.GetGrabPoint(point);
            UxrGripPoseInfo grip = info.GetGripPoseInfo(avatar);

            if (grip == null || ReferenceEquals(grip, info.DefaultGripPoseInfo))
            {
                return null;
            }

            return side == UxrHandSide.Left ? grip.GripAlignTransformHandLeft : grip.GripAlignTransformHandRight;
        }

        private static TestCaseData ToTestCase(TwoHandGrabCase c)
        {
            string weapon = System.IO.Path.GetFileNameWithoutExtension(c.WeaponPath);
            string avatar = System.IO.Path.GetFileNameWithoutExtension(c.AvatarPath);
            return new TestCaseData(c.WeaponPath, c.AvatarPath, c.SupportPoint).SetName($"{{m}}({weapon}, {avatar}, точка {c.SupportPoint})");
        }

        internal static List<T> LoadAll<T>() where T : Object
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                                .Select(AssetDatabase.GUIDToAssetPath)
                                .Where(p => !p.StartsWith("Assets/ThirdParty/"))
                                .Select(AssetDatabase.LoadAssetAtPath<T>)
                                .Where(a => a != null)
                                .ToList();
        }
    }

    /// <summary>
    /// Настоящие оружие и аватар в сцене редактора, правая рука держит основную точку.
    ///
    /// <para>
    /// Вне Play Mode <c>Awake</c>/<c>OnEnable</c> не зовутся, а руки регистрируются в
    /// <c>UxrGrabber.EnabledComponents</c> именно в <c>Awake</c> — без этого
    /// <c>GetGrabbingHand</c> не видит держащую руку. Цикл манипуляции
    /// <c>UxrGrabManager.UpdateManipulation</c> приватный. Всё это зовётся рефлексией.
    /// Плавные переходы выключены: их таймеры идут от <c>Time.deltaTime</c>, которого в EditMode нет.
    /// Сопротивление манипуляции (<c>Translation/Rotation Resistance</c>) — тоже: оно сглаживает
    /// движение через тот же <c>deltaTime</c>, и в редакторе без фокуса оружие с сопротивлением
    /// (M16, <c>Gun_real</c>, <c>Shotgun_real</c>) не двигалось вовсе — контроль
    /// <see cref="AssertManipulationLive" /> падал нестабильно, в зависимости от фокуса окна.
    /// </para>
    /// </summary>
    public sealed class TwoHandGrabHarness : IDisposable
    {
        private readonly GameObject _avatar;
        private readonly UxrGrabber[] _grabbers;
        private readonly MonoBehaviour[] _weaponBehaviours;
        private readonly UxrManipulationFeatures _savedFeatures;

        public GameObject Avatar => _avatar;
        public GameObject Weapon { get; }
        public UxrGrabbableObject Grabbable { get; }
        public UxrGrabber Right { get; }
        public UxrGrabber Left { get; }
        public int SupportPoint { get; }

        public static UxrGrabManager Manager => UxrGrabManager.Instance;

        public TwoHandGrabHarness(string weaponPath, string avatarPath, int supportPoint, bool grabMain = true)
        {
            TestEnvironmentContract.ResetDestroyedSingleton<UxrGrabManager>();
            _savedFeatures = Manager.Features;
            Assert.That(Manager, Is.SameAs(TestEnvironmentContract.ExactlyOneInScene<UxrGrabManager>()),
                "[TestEnvironment] Singleton захватов не совпадает с менеджером сцены.");
            TestEnvironmentContract.IsActive(Manager);
            Manager.Features &= ~(UxrManipulationFeatures.SmoothTransitions | UxrManipulationFeatures.ObjectResistance);

            _avatar = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(avatarPath));
            Weapon = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(weaponPath));
            Grabbable = Weapon.GetComponent<UxrGrabbableObject>();
            SupportPoint = supportPoint;

            _grabbers = _avatar.GetComponentsInChildren<UxrGrabber>(true);
            Assert.That(_grabbers.Count(g => g.Side == UxrHandSide.Left), Is.EqualTo(1),
                "[TestEnvironment] У аватара должна быть ровно одна левая рука.");
            Assert.That(_grabbers.Count(g => g.Side == UxrHandSide.Right), Is.EqualTo(1),
                "[TestEnvironment] У аватара должна быть ровно одна правая рука.");
            Right = _grabbers.First(g => g.Side == UxrHandSide.Right);
            Left = _grabbers.First(g => g.Side == UxrHandSide.Left);

            foreach (UxrGrabber grabber in _grabbers)
            {
                Invoke(grabber, "Awake");
            }

            // У оружия с Align To Controller локальный аватар берёт поворот не у руки, а у модели
            // контроллера из ControllerInput (UxrGrabManager.PlaceObjectInHand). Какой ввод активен,
            // зависит от того, что оставили другие тесты, а харнесс крутит только руку — оружие
            // стояло на месте, и проверки поворота молча зеленели. Внешне управляемый аватар
            // (как чужой по сети) берёт позу руки как есть.
            _avatar.GetComponent<UxrAvatar>().AvatarMode = UxrAvatarMode.UpdateExternally;

            // Игровые компоненты оружия (подписки на события SDK) — тоже Awake/OnEnable руками.
            _weaponBehaviours = Weapon.GetComponents<MonoBehaviour>().Where(b => b != null && b.GetType().Namespace?.StartsWith("VrBattlegrounds") == true).ToArray();

            foreach (MonoBehaviour behaviour in _weaponBehaviours)
            {
                InvokeIfExists(behaviour, "Awake");
                InvokeIfExists(behaviour, "OnEnable");
            }

            if (!grabMain)
            {
                return;
            }

            Manager.GrabObject(Right, Grabbable, TwoHandGrabCases.MainPoint, false);
            Assert.IsTrue(Manager.GetGrabbingHand(Grabbable, TwoHandGrabCases.MainPoint, out UxrGrabber holder) && holder == Right,
                          "Контроль харнесса: правая рука держит основную точку.");
        }

        /// <summary>Ставит левую ладонь ровно туда, куда её ставит дополнительная точка.</summary>
        public void PlaceLeftOnSupport()
        {
            Grabbable.ComputeRequiredGrabberTransform(Left, SupportPoint, out Vector3 position, out Quaternion rotation, false);
            Left.transform.SetPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Кадр манипуляции, как в <c>UxrGrabManager.UpdateManager</c>: сначала
        /// <c>InitializeManipulationFrame</c> (положение рук с контроллера — <c>UnprocessedGrabberPosition</c>,
        /// счёт деталей, наводящих родителя), потом <c>UpdateManipulation</c>. Без первого шага
        /// деталь, зависящая от оружия (помпа), решалась неверно — уходила на метр от места.
        /// </summary>
        public void UpdateManipulation()
        {
            Invoke(Manager, "InitializeManipulationFrame");
            Invoke(Manager, "UpdateManipulation");
        }

        /// <summary>
        /// Контроль харнесса: цикл манипуляции действительно двигает оружие за рукой. Без него
        /// тест «оружие не повернулось» зелёный и тогда, когда оружие вообще не двигается —
        /// такое наблюдалось в раннере при неизвестном состоянии редактора.
        /// </summary>
        public void AssertManipulationLive()
        {
            UpdateManipulation();
            Quaternion relative = Quaternion.Inverse(Right.transform.rotation) * Weapon.transform.rotation;
            Quaternion weaponBefore = Weapon.transform.rotation;

            Right.transform.Rotate(0f, 30f, 0f);
            UpdateManipulation();

            float followError = Quaternion.Angle(relative, Quaternion.Inverse(Right.transform.rotation) * Weapon.transform.rotation);
            float weaponMoved = Quaternion.Angle(weaponBefore, Weapon.transform.rotation);

            Right.transform.Rotate(0f, -30f, 0f);
            UpdateManipulation();

            Assert.Less(followError, 0.5f,
                        $"Контроль харнесса: основная рука повернулась на 30°, а оружие — на {weaponMoved:F1}°. Цикл манипуляции " +
                        $"не двигает оружие, проверки поворота ничего не докажут. Features={Manager.Features}, " +
                        $"grabbed={Manager.IsBeingGrabbed(Grabbable)}, enabled={Grabbable.isActiveAndEnabled}.");
        }

        /// <summary>
        /// Уничтожает аватар так, как это происходит в Play Mode. Там рука сначала выключается,
        /// а потом её <c>OnDestroy</c> зовёт отпускание — и SDK его пропускает: выключенной руки
        /// уже нет в <c>UxrGrabber.EnabledComponents</c>. В EditMode колбэки не зовутся сами,
        /// а <c>isActiveAndEnabled</c> у руки истинно, поэтому рука выключается руками — иначе
        /// отпускание сработало бы и скрыло дефект. <c>OnDestroy</c> заодно снимает руку с учёта
        /// <c>UxrComponent</c>, иначе мёртвая запись ломала бы соседние тесты.
        /// </summary>
        public void DestroyAvatarLikePlayMode()
        {
            foreach (UxrGrabber grabber in _grabbers)
            {
                if (grabber == null) continue;

                grabber.enabled = false;
                Invoke(grabber, "OnDestroy");
            }

            Object.DestroyImmediate(_avatar);
        }

        public void Dispose()
        {
            if (Grabbable != null && Manager.IsBeingGrabbed(Grabbable))
            {
                Manager.ReleaseObject(null, Grabbable, false);
            }

            foreach (MonoBehaviour behaviour in _weaponBehaviours ?? Array.Empty<MonoBehaviour>())
            {
                if (behaviour != null)
                {
                    InvokeIfExists(behaviour, "OnDisable");
                }
            }

            foreach (UxrGrabber grabber in _grabbers ?? Array.Empty<UxrGrabber>())
            {
                if (grabber != null)
                {
                    Invoke(grabber, "OnDestroy");
                }
            }

            if (_avatar != null) Object.DestroyImmediate(_avatar);
            if (Weapon != null) Object.DestroyImmediate(Weapon);

            Manager.Features = _savedFeatures;
        }

        private static void Invoke(object target, string method)
        {
            Assert.IsTrue(InvokeIfExists(target, method), $"У {target.GetType().Name} нет метода {method}.");
        }

        private static bool InvokeIfExists(object target, string method)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                MethodInfo info = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly,
                                                 null, Type.EmptyTypes, null);

                if (info != null)
                {
                    info.Invoke(target, null);
                    return true;
                }
            }

            return false;
        }
    }
}
