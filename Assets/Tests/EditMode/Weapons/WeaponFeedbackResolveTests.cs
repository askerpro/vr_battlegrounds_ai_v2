using System.Collections.Generic;
using NUnit.Framework;
using UltimateXR.Audio;
using UltimateXR.Haptics;
using UnityEngine;
using VrBattlegrounds.Weapons;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Правило подстановки отклика по категориям (этап waves-f, принято пользователем в шлеме 2026-10-09):
    /// <see cref="WeaponAudioSet.Resolve"/> / <see cref="WeaponHapticSet.Resolve"/> — оверрайд ствола важнее дефолта
    /// категории, пустое поле берёт дефолт, сухой щелчок без источника — звук SDK, прочее без источника — тишина.
    /// Частичный ход (<see cref="WeaponCue.ActionReturnPartial"/>) звучит как возврат без подачи.
    ///
    /// Наборы собираются в памяти: тест проверяет правило, а не данные префабов (их — <see cref="WeaponSystemWavePrefabTests"/>).
    /// Каждый случай ломается при откате своего правила: дефолт перекрывает оверрайд, пустое поле не берёт дефолт,
    /// DryFire теряет запасной звук SDK, другие сигналы ошибочно получают SDK, досылание ствола уступает дефолту,
    /// частичный ход играет звук досылания.
    /// </summary>
    public class WeaponFeedbackResolveTests
    {
        private readonly List<Object> _created = new List<Object>();
        private AudioClip _own, _common, _ownChambered, _commonChambered;

        [SetUp]
        public void SetUp()
        {
            _own = Clip("own");
            _common = Clip("common");
            _ownChambered = Clip("own_chambered");
            _commonChambered = Clip("common_chambered");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ── Звук ────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Оверрайд_ствола_важнее_дефолта()
        {
            var overrides = new WeaponAudioSet(); overrides.ActionBack.Clip = _own;
            var defaults = new WeaponAudioSet(); defaults.ActionBack.Clip = _common;

            UxrAudioSample sample = WeaponAudioSet.Resolve(overrides, defaults, WeaponCue.ActionBack, out WeaponFeedbackSource source);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Override));
            Assert.That(sample.Clip, Is.SameAs(_own));
        }

        [TestCase(WeaponCue.ActionBack)]
        [TestCase(WeaponCue.ActionForwardEmpty)]
        [TestCase(WeaponCue.Refusal)]
        [TestCase(WeaponCue.DryFire)]
        [TestCase(WeaponCue.ChamberEjected)]
        [TestCase(WeaponCue.SlideLockCatch)]
        public void Пустое_поле_ствола_берёт_дефолт_категории(WeaponCue cue)
        {
            var overrides = new WeaponAudioSet();
            var defaults = new WeaponAudioSet(); defaults.Field(cue).Clip = _common;

            UxrAudioSample sample = WeaponAudioSet.Resolve(overrides, defaults, cue, out WeaponFeedbackSource source);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Default), $"{cue}: пустое поле ствола — дефолт категории.");
            Assert.That(sample.Clip, Is.SameAs(_common));
        }

        [Test]
        public void Сухой_щелчок_без_источника_звук_SDK()
        {
            UxrAudioSample sample = WeaponAudioSet.Resolve(new WeaponAudioSet(), new WeaponAudioSet(), WeaponCue.DryFire, out WeaponFeedbackSource source);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Sdk), "DryFire без звука в обоих наборах — «нет патронов» спуска SDK.");
            Assert.That(sample, Is.Null, "Клип SDK играет спуск, Resolve клипа не отдаёт.");

            WeaponAudioSet.Resolve(null, null, WeaponCue.DryFire, out source);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Sdk), "Ствол без ссылки на дефолты: тот же запасной звук SDK.");
        }

        [TestCase(WeaponCue.ActionBack)]
        [TestCase(WeaponCue.ActionForwardChambered)]
        [TestCase(WeaponCue.ActionForwardEmpty)]
        [TestCase(WeaponCue.ActionReturnPartial)]
        [TestCase(WeaponCue.Refusal)]
        [TestCase(WeaponCue.ChamberEjected)]
        [TestCase(WeaponCue.SlideLockCatch)]
        public void Прочие_сигналы_без_источника_молчат(WeaponCue cue)
        {
            UxrAudioSample sample = WeaponAudioSet.Resolve(new WeaponAudioSet(), new WeaponAudioSet(), cue, out WeaponFeedbackSource source);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.None), $"{cue}: запасной звук SDK есть только у сухого щелчка.");
            Assert.That(sample, Is.Null);
        }

        [Test]
        public void Досылание_без_своего_звука_берёт_закрытие_ствола_а_не_дефолт()
        {
            var overrides = new WeaponAudioSet(); overrides.ActionForwardEmpty.Clip = _own;
            var defaults = new WeaponAudioSet(); defaults.ActionForwardChambered.Clip = _commonChambered;

            UxrAudioSample sample = WeaponAudioSet.Resolve(overrides, defaults, WeaponCue.ActionForwardChambered, out WeaponFeedbackSource source);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Override), "Сначала набор ствола целиком, затем дефолт.");
            Assert.That(sample.Clip, Is.SameAs(_own), "Собственный звук закрытия важнее звука досылания другой модели.");
        }

        [Test]
        public void Частичный_ход_звучит_как_возврат_без_подачи()
        {
            var set = new WeaponAudioSet();
            Assert.That(set.Field(WeaponCue.ActionReturnPartial), Is.SameAs(set.Field(WeaponCue.ActionForwardEmpty)),
                "Поле частичного хода — поле возврата без подачи (решение пользователя 2026-10-09).");

            // Оверрайд: у ствола свои досылание и закрытие — частичный ход берёт закрытие, не досылание.
            var overrides = new WeaponAudioSet();
            overrides.ActionForwardChambered.Clip = _ownChambered;
            overrides.ActionForwardEmpty.Clip = _own;
            Assert.That(WeaponAudioSet.Resolve(overrides, null, WeaponCue.ActionReturnPartial, out WeaponFeedbackSource source).Clip, Is.SameAs(_own));
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Override));

            // Дефолт: ствол без звуков хода — частичный ход берёт закрытие категории.
            var defaults = new WeaponAudioSet();
            defaults.ActionForwardChambered.Clip = _commonChambered;
            defaults.ActionForwardEmpty.Clip = _common;
            Assert.That(WeaponAudioSet.Resolve(new WeaponAudioSet(), defaults, WeaponCue.ActionReturnPartial, out source).Clip, Is.SameAs(_common));
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Default));
        }

        // ── Вибрация ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Вибрация_оверрайд_важнее_дефолта()
        {
            var overrides = new WeaponHapticSet(); overrides.NotReady.FallbackClipType = UxrHapticClipType.Click;
            var defaults = new WeaponHapticSet(); defaults.NotReady.FallbackClipType = UxrHapticClipType.RumbleFreqNormal;

            UxrHapticClip clip = WeaponHapticSet.Resolve(overrides, defaults, WeaponHapticCue.NotReady, out WeaponFeedbackSource source);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Override));
            Assert.That(clip, Is.SameAs(overrides.NotReady));
        }

        [TestCase(WeaponHapticCue.ActionRear)]
        [TestCase(WeaponHapticCue.NotReady)]
        [TestCase(WeaponHapticCue.Faulted)]
        [TestCase(WeaponHapticCue.RateOfFire)]
        public void Вибрация_пустой_клип_ствола_берёт_дефолт(WeaponHapticCue cue)
        {
            var defaults = new WeaponHapticSet(); defaults.For(cue).FallbackClipType = UxrHapticClipType.RumbleFreqNormal;

            UxrHapticClip clip = WeaponHapticSet.Resolve(new WeaponHapticSet(), defaults, cue, out WeaponFeedbackSource source);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Default));
            Assert.That(clip, Is.SameAs(defaults.For(cue)));
        }

        [TestCase(WeaponHapticCue.NotReady)]
        [TestCase(WeaponHapticCue.Obstructed)]
        public void Вибрация_без_источника_None_без_запасной_SDK(WeaponHapticCue cue)
        {
            UxrHapticClip clip = WeaponHapticSet.Resolve(new WeaponHapticSet(), new WeaponHapticSet(), cue, out WeaponFeedbackSource source);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.None), "У вибрации запасного источника SDK нет.");
            Assert.That(clip, Is.Null);
        }

        private AudioClip Clip(string name)
        {
            AudioClip clip = AudioClip.Create(name, 64, 1, 44100, false);
            _created.Add(clip);
            return clip;
        }
    }
}
