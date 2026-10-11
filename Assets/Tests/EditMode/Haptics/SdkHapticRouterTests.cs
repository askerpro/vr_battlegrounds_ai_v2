using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Core.Unique;
using UltimateXR.Haptics;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Haptics;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Haptics
{
    /// <summary>Корень оружия для тестов: играет ли он отдачу сам (контракт haptics-api).</summary>
    public sealed class TestRecoilOwnerProbe : MonoBehaviour, IWeaponRecoilHapticsOwner
    {
        public bool Owns;

        public bool OwnsRecoilHaptics => Owns;
    }

    /// <summary>
    /// Маршрутизатор вибраций UltimateXR (<see cref="SdkHapticRouter" />, SDK-патч 65, <c>tasks/haptics-system/Details.md</c>
    /// §5.3): запрос SDK становится голосом <see cref="HapticService" /> или отбрасывается. Механика принята пользователем в
    /// шлеме 2026-10-11. Проверяются правила, а не подобранные силы и формы: запасные клипы — свои, с различимыми уровнями,
    /// ассет проекта не участвует. Сервис — на подменном моторе, время в EditMode внутри теста стоит.
    /// </summary>
    public class SdkHapticRouterTests
    {
        private sealed class FakeDevice : IHapticDevice
        {
            public readonly List<(UxrHandSide side, float amplitude, float seconds)> Sent = new List<(UxrHandSide, float, float)>();

            public void Send(UxrHandSide side, float amplitude, float seconds) => Sent.Add((side, amplitude, seconds));
            public void Stop(UxrHandSide side) { }
        }

        private const UxrHandSide L = UxrHandSide.Left;
        private const UxrHandSide R = UxrHandSide.Right;

        // Уровни форм различимы: по слышимой амплитуде видно, какой клип звучит.
        private const float ClickLevel = 0.2f;
        private const float ShotLevel = 0.3f;
        private const float ShotBigLevel = 0.4f;
        private const float RawLevel = 0.5f;
        private const float RumbleLevel = 0.6f;
        private const float GameLevel = 0.8f;

        private static readonly FieldInfo ServiceInstance =
            typeof(HapticService).GetField("s_instance", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo ServiceMixer =
            typeof(HapticService).GetField("_mixer", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<Object> _created = new List<Object>();
        private Func<UxrHapticRequest, bool> _previousRouter;
        private FakeDevice _device;
        private SdkHapticFallbacks _fallbacks;
        private SdkHapticRouter _router;

        private bool _local = true;
        private UxrHandSide _heldSide = R;
        private UxrGrabbableObject _held;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(ServiceInstance, "HapticService.s_instance переименован — обновить тест.");
            Assert.IsNotNull(ServiceMixer, "HapticService._mixer переименован — обновить тест.");
            Assert.IsNull(ServiceInstance.GetValue(null), "Сервис вибрации уже поставлен — тест не должен его подменять.");

            _previousRouter = UxrHapticRouting.Router;

            // Сервис без Play Mode и XR: Awake в EditMode не вызывается, мотор подменный.
            _device = new FakeDevice();
            var host = new GameObject(nameof(HapticService)) { hideFlags = HideFlags.HideAndDontSave };
            _created.Add(host);
            HapticService service = host.AddComponent<HapticService>();
            ServiceMixer.SetValue(service, new HapticMixer(_device));
            ServiceInstance.SetValue(null, service);
            UxrHapticRouting.Router = null;

            _fallbacks = ScriptableObject.CreateInstance<SdkHapticFallbacks>();
            _created.Add(_fallbacks);
            _fallbacks.Get(UxrHapticClipType.Click).Waveform = Shape(ClickLevel);
            _fallbacks.Get(UxrHapticClipType.Shot).Waveform = Shape(ShotLevel);
            _fallbacks.Get(UxrHapticClipType.ShotBig).Waveform = Shape(ShotBigLevel);
            _fallbacks.Get(UxrHapticClipType.RumbleFreqNormal).Waveform = Shape(RumbleLevel);
            _fallbacks.Raw.Waveform = Shape(RawLevel);
            // Explosion намеренно без формы: тип без запасного клипа.

            _local = true;
            _heldSide = R;
            _held = null;
            _router = new SdkHapticRouter(_fallbacks, _ => _local, (_, side) => side == _heldSide ? _held : null);
        }

        [TearDown]
        public void TearDown()
        {
            ServiceInstance?.SetValue(null, null);
            UxrHapticRouting.Router = _previousRouter;
            foreach (Object o in _created)
            {
                if (o == null) continue;
                // В EditMode Unity не зовёт OnDestroy у ни разу не активных объектов — id UltimateXR остались бы занятыми.
                if (o is GameObject go)
                    foreach (IUxrUniqueId unique in go.GetComponentsInChildren<IUxrUniqueId>(true))
                        unique.Unregister();
                Object.DestroyImmediate(o);
            }
            _created.Clear();
        }

        // ---------- Помощники ----------

        private UxrHapticWaveform Shape(float level)
        {
            var waveform = ScriptableObject.CreateInstance<UxrHapticWaveform>();
            waveform.Segments.Clear();
            waveform.Segments.Add(new UxrHapticWaveform.Segment(level, 100));
            _created.Add(waveform);
            return waveform;
        }

        private static int Voices(UxrHandSide side)
        {
            HapticService.GetHandState(side, out int voices, out _);
            return voices;
        }

        private static float Amplitude(UxrHandSide side)
        {
            HapticService.GetHandState(side, out _, out float amplitude);
            return amplitude;
        }

        private void AssertSilent(string because)
        {
            Assert.AreEqual(0, Voices(L), because);
            Assert.AreEqual(0, Voices(R), because);
            Assert.IsEmpty(_device.Sent, because);
        }

        private bool Clip(UxrHandSide side, UxrHapticClip clip) => _router.Route(UxrHapticRequest.ForClip(null, side, clip));

        private bool SdkType(UxrHandSide side, UxrHapticClipType type, float amplitude = 1f, float seconds = -1f) =>
            _router.Route(UxrHapticRequest.ForClipType(null, side, type, amplitude, seconds, UxrHapticMode.Mix));

        private bool Raw(UxrHandSide side, float amplitude, float seconds) =>
            _router.Route(UxrHapticRequest.ForRaw(null, side, 160f, amplitude, seconds, UxrHapticMode.Mix));

        private bool Stop(UxrHandSide side) => _router.Route(UxrHapticRequest.ForStop(null, side));

        /// <summary>Рука <see cref="_heldSide" /> держит оружие; <paramref name="owns" /> — играет ли оно отдачу само.</summary>
        private void HoldWeapon(bool owns)
        {
            var root = new GameObject("Weapon");
            _created.Add(root);
            root.AddComponent<TestRecoilOwnerProbe>().Owns = owns;
            var grip = new GameObject("Grip");
            grip.transform.SetParent(root.transform, false);
            _held = grip.AddComponent<UxrGrabbableObject>();
        }

        // ---------- Установка ----------

        [Test]
        public void Без_маршрутизатора_SDK_работает_сам()
        {
            Assert.IsFalse(UxrHapticRouting.TryRoute(UxrHapticRequest.ForStop(null, L)),
                           "Router == null — SDK должен выполнить запрос сам.");

            _router.Install();
            Assert.IsTrue(UxrHapticRouting.TryRoute(UxrHapticRequest.ForStop(null, L)), "Поставленный маршрутизатор принимает запрос.");

            _router.Uninstall();
            Assert.IsNull(UxrHapticRouting.Router);
            Assert.IsFalse(UxrHapticRouting.TryRoute(UxrHapticRequest.ForStop(null, L)));
        }

        [Test]
        public void Снятие_не_трогает_чужой_обработчик()
        {
            Func<UxrHapticRequest, bool> foreign = _ => false;
            UxrHapticRouting.Router = foreign;
            _router.Uninstall();
            Assert.AreSame(foreign, UxrHapticRouting.Router);
        }

        [Test]
        public void Статика_сбрасывается_при_входе_в_Play_Mode()
        {
            MethodInfo reset = typeof(SdkHapticRouter).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(reset, "Нет сброса статики SdkHapticRouter.ResetStatics.");
            var attribute = reset.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
            Assert.IsNotNull(attribute, "Сброс должен вызываться Unity при входе в Play Mode без domain reload.");
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, attribute.loadType);

            _router.Install();
            reset.Invoke(null, null);
            Assert.IsNull(UxrHapticRouting.Router, "Перехват SDK не должен пережить сессию.");
        }

        // ---------- Чья рука ----------

        [Test]
        public void Нелокальный_источник_принят_и_тих()
        {
            _local = false;
            UxrHapticClip withForm = new UxrHapticClip { Waveform = Shape(GameLevel) };

            Assert.IsTrue(Clip(L, withForm), "Запрос бота/чужого игрока принимается, чтобы SDK не тронул мотор.");
            Assert.IsTrue(SdkType(R, UxrHapticClipType.Click));
            Assert.IsTrue(Raw(L, 1f, 1f));
            Assert.IsTrue(Stop(L));
            AssertSilent("Чужой аватар или бот не вибрирует контроллером игрока.");
        }

        [Test]
        public void Без_аватара_принят_и_тих()
        {
            // Маршрутизатор как в игре: локальность по аватару UltimateXR. У запроса без источника аватара нет.
            var router = new SdkHapticRouter(_fallbacks);

            Assert.IsTrue(router.Route(UxrHapticRequest.ForClipType(null, L, UxrHapticClipType.Click, 1f, -1f, UxrHapticMode.Mix)));
            Assert.IsTrue(router.Route(UxrHapticRequest.ForRaw(null, R, 160f, 1f, 1f, UxrHapticMode.Mix)));
            AssertSilent("Запрос без аватара отбрасывается.");
        }

        // ---------- Клипы и типы ----------

        [Test]
        public void Клип_с_формой_звучит_своей_формой()
        {
            var clip = new UxrHapticClip { Waveform = Shape(GameLevel), FallbackClipType = UxrHapticClipType.Click };

            Assert.IsTrue(Clip(L, clip));
            Assert.AreEqual(1, Voices(L));
            Assert.AreEqual(0, Voices(R), "Только рука запроса.");
            Assert.AreEqual(GameLevel, Amplitude(L), 1e-4f, "Звучит форма клипа, а не запасной клип его типа.");
        }

        [Test]
        public void Клип_без_формы_звучит_запасным_клипом_своего_типа()
        {
            var clip = new UxrHapticClip(fallbackClipType: UxrHapticClipType.Click, fallbackAmplitude: 0.5f);

            Assert.IsTrue(Clip(R, clip));
            Assert.AreEqual(1, Voices(R));
            Assert.AreEqual(ClickLevel * 0.5f, Amplitude(R), 1e-4f, "Запасной клип типа × FallbackAmplitude клипа.");
        }

        [Test]
        public void Тип_SDK_звучит_запасным_клипом_с_силой_запроса()
        {
            Assert.IsTrue(SdkType(L, UxrHapticClipType.ShotBig, amplitude: 0.5f));
            Assert.AreEqual(1, Voices(L));
            Assert.AreEqual(ShotBigLevel * 0.5f, Amplitude(L), 1e-4f);
        }

        [Test]
        public void Тип_без_запасного_клипа_принят_и_тих()
        {
            Assert.IsTrue(SdkType(L, UxrHapticClipType.Explosion), "Запрос принимается: иначе SDK сыграл бы его мимо сервиса.");
            Assert.IsTrue(SdkType(L, UxrHapticClipType.None));
            Assert.IsTrue(Clip(L, new UxrHapticClip(fallbackClipType: UxrHapticClipType.Explosion)));
            AssertSilent("Нет запасного клипа — тишина.");
        }

        [Test]
        public void Гул_держится_одним_голосом()
        {
            Assert.IsTrue(SdkType(R, UxrHapticClipType.RumbleFreqNormal, amplitude: 1f, seconds: 0.3f));
            Assert.IsTrue(SdkType(R, UxrHapticClipType.RumbleFreqNormal, amplitude: 1f, seconds: 0.3f));
            Assert.AreEqual(1, Voices(R), "Повтор гула продлевает голос, а не копит новые.");
            Assert.AreEqual(RumbleLevel, Amplitude(R), 1e-4f);
        }

        // ---------- Отдача ----------

        [Test]
        public void Отдача_оружия_которое_играет_её_само_отброшена()
        {
            HoldWeapon(owns: true);

            Assert.IsTrue(SdkType(R, UxrHapticClipType.ShotBig));
            Assert.IsTrue(SdkType(R, UxrHapticClipType.Shot));
            Assert.IsTrue(Clip(R, new UxrHapticClip(fallbackClipType: UxrHapticClipType.ShotBig)));
            Assert.IsTrue(Clip(R, new UxrHapticClip { Waveform = Shape(GameLevel), FallbackClipType = UxrHapticClipType.Shot }));
            Assert.AreEqual(0, Voices(R), "Отдачу играет ствол — второй раз она звучать не должна.");
        }

        [Test]
        public void Отдача_оружия_без_своей_отдачи_звучит()
        {
            HoldWeapon(owns: false);

            Assert.IsTrue(SdkType(R, UxrHapticClipType.ShotBig));
            Assert.AreEqual(1, Voices(R));
            Assert.AreEqual(ShotBigLevel, Amplitude(R), 1e-4f);
        }

        [Test]
        public void Владелец_отдачи_гасит_только_выстрел_своей_руки()
        {
            HoldWeapon(owns: true);

            Assert.IsTrue(SdkType(R, UxrHapticClipType.Click));
            Assert.AreEqual(1, Voices(R), "Не выстрел (клик хвата) звучит и с оружием-владельцем отдачи.");

            Assert.IsTrue(SdkType(L, UxrHapticClipType.ShotBig));
            Assert.AreEqual(1, Voices(L), "Другая рука оружие не держит — её выстрел звучит.");
        }

        // ---------- Сырые запросы ----------

        [Test]
        public void Сырой_запрос_один_голос_повтор_продлевает()
        {
            Assert.IsTrue(Raw(L, 0.5f, 0.2f));
            Assert.AreEqual(1, Voices(L));
            Assert.AreEqual(RawLevel * 0.5f, Amplitude(L), 1e-4f);

            Assert.IsTrue(Raw(L, 1f, 0.2f));
            Assert.AreEqual(1, Voices(L), "Повтор продлевает тот же голос, а не копит новые.");
            Assert.AreEqual(RawLevel, Amplitude(L), 1e-4f, "Продление берёт силу нового запроса.");

            Assert.IsTrue(Raw(R, 1f, 0.2f));
            Assert.AreEqual(1, Voices(R), "Руки независимы.");
            Assert.AreEqual(1, Voices(L));
        }

        [Test]
        public void Сырой_запрос_с_нулевой_силой_или_сроком_завершает_голос()
        {
            Assert.IsTrue(Raw(L, 1f, 0.2f));
            Assert.IsTrue(Raw(L, 0f, 0.2f));
            Assert.AreEqual(0, Voices(L), "Нулевая сила — гула больше не просят.");

            Assert.IsTrue(Raw(L, 1f, 0.2f));
            Assert.AreEqual(1, Voices(L), "После завершения новый запрос начинает голос заново.");
            Assert.IsTrue(Raw(L, 1f, 0f));
            Assert.AreEqual(0, Voices(L), "Нулевой срок — тоже конец.");
        }

        // ---------- Остановка ----------

        [Test]
        public void Остановка_гасит_только_голоса_маршрутизатора_этой_руки()
        {
            var owner = new GameObject("GameSource");
            _created.Add(owner);
            HapticHandle game = HapticService.Begin(new UxrHapticClip { Waveform = Shape(GameLevel) }, L, owner);
            Assert.IsTrue(game.IsActive);

            Assert.IsTrue(SdkType(L, UxrHapticClipType.Click));
            Assert.IsTrue(Raw(L, 1f, 0.2f));
            Assert.IsTrue(Raw(R, 1f, 0.2f));
            Assert.AreEqual(3, Voices(L));

            Assert.IsTrue(Stop(L));
            Assert.AreEqual(1, Voices(L), "Остались бы голоса SDK — остановка SDK их не погасила.");
            Assert.IsTrue(game.IsActive, "Игровой голос не принадлежит маршрутизатору — остановка SDK его не трогает.");
            Assert.AreEqual(1, Voices(R), "Остановка одной руки не трогает другую.");

            Assert.IsTrue(Raw(L, 1f, 0.2f));
            Assert.AreEqual(2, Voices(L), "После остановки сырой запрос начинает новый голос.");
        }
    }
}
