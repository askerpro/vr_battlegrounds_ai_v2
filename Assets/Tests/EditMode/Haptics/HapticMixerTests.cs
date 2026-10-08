using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Haptics;
using UnityEngine;
using VrBattlegrounds.Haptics;

namespace VrBattlegrounds.Tests.Haptics
{
    /// <summary>
    /// Смешивание вибрации на моторе руки (<see cref="HapticMixer" />, <c>tasks/haptics-system/Details.md</c>, п. 2): на Quest
    /// каждый вызов мотора обрывает текущую вибрацию, поэтому порядок и сила звучащего решаются здесь. Время подаётся
    /// явно, мотор — подменный, шлем и Unity-время не нужны. Механика карманов принята пользователем 2026-10-09; правила
    /// приоритетов — утверждённый дизайн.
    /// </summary>
    public class HapticMixerTests
    {
        private sealed class FakeDevice : IHapticDevice
        {
            public readonly List<(UxrHandSide side, float amplitude, float seconds)> Sent = new List<(UxrHandSide, float, float)>();
            public int Stops;

            public void Send(UxrHandSide side, float amplitude, float seconds) => Sent.Add((side, amplitude, seconds));
            public void Stop(UxrHandSide side) => Stops++;
        }

        private const UxrHandSide L = UxrHandSide.Left;
        private readonly List<Object> _created = new List<Object>();
        private FakeDevice _device;
        private HapticMixer _mixer;

        [SetUp]
        public void SetUp()
        {
            _device = new FakeDevice();
            _mixer = new HapticMixer(_device);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private UxrHapticWaveform Shape(params float[] amplitudeMs)
        {
            var waveform = ScriptableObject.CreateInstance<UxrHapticWaveform>();
            waveform.Segments.Clear();
            for (int i = 0; i < amplitudeMs.Length; i += 2)
                waveform.Segments.Add(new UxrHapticWaveform.Segment(amplitudeMs[i], (int)amplitudeMs[i + 1]));
            _created.Add(waveform);
            return waveform;
        }

        private static UxrHapticClip Clip(UxrHapticWaveform shape, UxrHapticPriority priority, float gain = 1f, int gapMs = 0,
            float cooldown = 0f) => new UxrHapticClip
        {
            Waveform = shape, Priority = priority, WaveformGain = gain, RepeatGapMs = gapMs, CooldownSeconds = cooldown
        };

        [Test]
        public void Разовый_клип_звучит_по_форме_и_гаснет()
        {
            _mixer.Play(Clip(Shape(1f, 100), UxrHapticPriority.Normal), L, 1f, false, 0f);

            Assert.AreEqual(1f, _device.Sent[0].amplitude, 1e-4f, "Отдача должна уйти в мотор сразу, без ожидания тика.");
            Assert.AreEqual(1f, _mixer.Evaluate(L, 0.05f), 1e-4f);
            _mixer.Tick(0.11f);
            Assert.AreEqual(0f, _mixer.Evaluate(L, 0.11f));
            Assert.AreEqual(1, _device.Stops, "Закончившийся клип гасит мотор.");
        }

        [Test]
        public void Сила_клипа_и_вызова_перемножаются()
        {
            _mixer.Play(Clip(Shape(1f, 100), UxrHapticPriority.Normal, gain: 0.5f), L, 0.5f, false, 0f);
            Assert.AreEqual(0.25f, _mixer.Evaluate(L, 0.01f), 1e-4f);
        }

        [Test]
        public void Высший_приоритет_глушит_непрерывный_низший_и_тот_возвращается()
        {
            _mixer.Play(Clip(Shape(1f, 100), UxrHapticPriority.Low, gain: 0.1f), L, 1f, true, 0f);
            _mixer.Play(Clip(Shape(1f, 170), UxrHapticPriority.High, gain: 0.8f), L, 1f, false, 0.05f);

            Assert.AreEqual(0.8f, _mixer.Evaluate(L, 0.1f), 1e-4f, "Отказ слышен вместо кармана.");
            _mixer.Tick(0.3f);
            Assert.AreEqual(0.1f, _mixer.Evaluate(L, 0.3f), 1e-4f, "После отказа карман снова слышен.");
        }

        [Test]
        public void Физика_складывается_по_максимуму()
        {
            _mixer.Play(Clip(Shape(1f, 200), UxrHapticPriority.Normal, gain: 0.3f), L, 1f, false, 0f);
            _mixer.Play(Clip(Shape(1f, 200), UxrHapticPriority.Normal, gain: 0.7f), L, 1f, false, 0f);
            _mixer.Play(Clip(Shape(1f, 200), UxrHapticPriority.Normal, gain: 0.5f), L, 1f, false, 0f);
            Assert.AreEqual(0.7f, _mixer.Evaluate(L, 0.1f), 1e-4f, "Очередь автомата не должна рваться последним слабым импульсом.");
        }

        [Test]
        public void Разовый_низшего_приоритета_не_откладывается()
        {
            _mixer.Play(Clip(Shape(1f, 170), UxrHapticPriority.High), L, 1f, false, 0f);
            _mixer.Play(Clip(Shape(1f, 50), UxrHapticPriority.Normal, gain: 0.6f), L, 1f, false, 0.01f);

            _mixer.Tick(0.2f);
            Assert.AreEqual(0f, _mixer.Evaluate(L, 0.2f), "Опоздавшая вибрация теряет причинность — после отказа её нет.");
        }

        [Test]
        public void Пауза_внутри_формы_занимает_мотор()
        {
            _mixer.Play(Clip(Shape(1f, 1000), UxrHapticPriority.Normal, gain: 0.5f), L, 1f, false, 0f);
            _mixer.Play(Clip(Shape(0.8f, 50, 0f, 70, 0.8f, 50), UxrHapticPriority.High), L, 1f, false, 0f);

            Assert.AreEqual(0f, _mixer.Evaluate(L, 0.08f), "Пауза двойного импульса не заполняется физикой — иначе рисунок стирается.");
            Assert.AreEqual(0.8f, _mixer.Evaluate(L, 0.13f), 1e-4f);
        }

        [Test]
        public void Пауза_повтора_непрерывного_не_занимает_мотор()
        {
            _mixer.Play(Clip(Shape(1f, 1000), UxrHapticPriority.Normal, gain: 0.5f), L, 1f, false, 0f);
            _mixer.Play(Clip(Shape(0.8f, 50, 0f, 70, 0.8f, 50), UxrHapticPriority.High, gapMs: 500), L, 1f, true, 0f);

            Assert.AreEqual(0.5f, _mixer.Evaluate(L, 0.3f), 1e-4f, "Между повторами предупреждения отдача слышна.");
            Assert.AreEqual(0.8f, _mixer.Evaluate(L, 0.69f), 1e-4f, "Следующий повтор начинается через длину формы + паузу.");
        }

        [Test]
        public void Непрерывный_ровный_держится_пока_не_остановлен()
        {
            int voice = _mixer.Play(Clip(Shape(1f, 100), UxrHapticPriority.Low, gain: 0.08f), L, 1f, true, 0f);

            for (float t = 0.05f; t < 1f; t += 0.05f)
            {
                _mixer.Tick(t);
                Assert.AreEqual(0.08f, _mixer.Evaluate(L, t), 1e-4f, $"Карман должен вибрировать ровно и в {t:0.00} с.");
            }

            _mixer.End(voice, 1f);
            Assert.AreEqual(0f, _mixer.Evaluate(L, 1f));
            Assert.AreEqual(1, _device.Stops, "Рука ушла от кармана — мотор гаснет.");
        }

        [Test]
        public void Внутри_приоритета_разовый_поверх_непрерывного_затем_новый()
        {
            _mixer.Play(Clip(Shape(1f, 100), UxrHapticPriority.Low, gain: 0.1f), L, 1f, true, 0f);
            _mixer.Play(Clip(Shape(1f, 100), UxrHapticPriority.Low, gain: 0.3f), L, 1f, false, 0.01f);
            Assert.AreEqual(0.3f, _mixer.Evaluate(L, 0.05f), 1e-4f, "Разовый поверх непрерывного того же приоритета.");

            _mixer.Play(Clip(Shape(1f, 100), UxrHapticPriority.High, gain: 0.6f), L, 1f, false, 0.2f);
            _mixer.Play(Clip(Shape(1f, 100), UxrHapticPriority.High, gain: 0.9f), L, 1f, false, 0.21f);
            Assert.AreEqual(0.9f, _mixer.Evaluate(L, 0.25f), 1e-4f, "Новый отказ заменяет старый.");
        }

        [Test]
        public void Кулдаун_отбрасывает_повтор_на_той_же_руке()
        {
            UxrHapticClip clip = Clip(Shape(1f, 50), UxrHapticPriority.High, cooldown: 0.4f);

            Assert.AreNotEqual(0, _mixer.Play(clip, L, 1f, false, 0f));
            Assert.AreEqual(0, _mixer.Play(clip, L, 1f, false, 0.1f), "Повтор раньше кулдауна отбрасывается.");
            Assert.AreNotEqual(0, _mixer.Play(clip, UxrHandSide.Right, 1f, false, 0.1f), "Кулдаун — на руку, другая рука свободна.");
            Assert.AreNotEqual(0, _mixer.Play(clip, L, 1f, false, 0.5f));
        }

        [Test]
        public void Клип_без_формы_и_нулевая_сила_не_играют()
        {
            Assert.AreEqual(0, _mixer.Play(new UxrHapticClip(), L, 1f, false, 0f), "Клип без формы — прежний путь SDK, не сервис.");
            Assert.AreEqual(0, _mixer.Play(Clip(Shape(1f, 100), UxrHapticPriority.Normal, gain: 0f), L, 1f, false, 0f));
            Assert.IsEmpty(_device.Sent);
        }

        [Test]
        public void Руки_независимы()
        {
            _mixer.Play(Clip(Shape(1f, 100), UxrHapticPriority.High, gain: 0.8f), L, 1f, false, 0f);
            Assert.AreEqual(0f, _mixer.Evaluate(UxrHandSide.Right, 0.05f), "Вибрация левой руки не попадает на правую.");
        }

        [Test]
        public void Правка_формы_слышна_сразу()
        {
            UxrHapticWaveform shape = Shape(0.2f, 100);
            _mixer.Play(Clip(shape, UxrHapticPriority.Low), L, 1f, true, 0f);

            shape.Segments[0] = new UxrHapticWaveform.Segment(0.6f, 100);
            _mixer.Refresh(0.01f);
            Assert.AreEqual(0.6f, _mixer.Evaluate(L, 0.01f), 1e-4f, "Звучащий голос читает форму заново — подбор в инспекторе действует сразу.");
        }
    }
}
