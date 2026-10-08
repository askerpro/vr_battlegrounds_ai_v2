using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Audio;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;
using WS = VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Пилоты WeaponSystem (этап drive, принят в шлеме 2026-10-09): Herrington и FABARM управляются одним хостом
    /// <see cref="WeaponSystem"/>, данные ствола записал <c>WeaponSystemAuthoring</c>.
    ///
    /// Класс ошибки, который ловит тест: второй писатель канала на префабе (прежние <c>AutomaticWeaponSlideFeedback</c>,
    /// <c>WeaponMechanismVisuals</c>, <c>WeaponChamberingReminder</c>, <c>UxrShotgunPump</c> двигали затвор, играли звук
    /// и включали подсветку параллельно хосту) и пустые данные канала (звук или вибрация молчат, ось не та).
    /// Общие требования к префабам оружия (коллайдеры, <c>OutOfWorldGuard</c>, хваты) проверяют их собственные тесты.
    /// </summary>
    public class WeaponSystemPilotPrefabTests
    {
        public sealed class Pilot
        {
            public string Path;
            public WS.WeaponReleasedAction Released;
            public string ActionBack, ActionForwardChambered, ActionForwardEmpty;

            public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Path);
        }

        private const string RefusalClip = "UI_Error_Subtle_Deep_stereo";

        public static IEnumerable<Pilot> Pilots()
        {
            yield return new Pilot
            {
                Path = "Assets/Prefabs/Weapons/Herrington/Herrington.prefab", Released = WS.WeaponReleasedAction.Spring,
                ActionBack = "Herrington_BoltBack", ActionForwardChambered = "Herrington_BoltForward", ActionForwardEmpty = "Herrington_BoltForward"
            };
            yield return new Pilot
            {
                // Помпа: досылание и закрытие без подачи звучат одинаково — ForwardChambered пуст и берёт ForwardEmpty.
                Path = "Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS.prefab", Released = WS.WeaponReleasedAction.Stay,
                ActionBack = "ShotgunPump01", ActionForwardChambered = null, ActionForwardEmpty = "ShotgunPump02"
            };
        }

        [TestCaseSource(nameof(Pilots))]
        public void Один_хост_WeaponSystem_без_прежних_компонентов(Pilot pilot)
        {
            GameObject prefab = Load(pilot);
            var hosts = prefab.GetComponentsInChildren<WeaponSystem>(true);
            Assert.That(hosts.Length, Is.EqualTo(1), "Ровно один хост WeaponSystem.");
            Assert.That(hosts[0].gameObject, Is.SameAs(prefab), "Хост на корне оружия, рядом с UxrFirearmWeapon.");
            Assert.That(hosts[0].Profile, Is.Not.Null, "У хоста задан профиль готовности.");

            var legacy = new List<string>();
            void Forbid<T>() where T : Component
            {
                foreach (T component in prefab.GetComponentsInChildren<T>(true))
                    legacy.Add($"{typeof(T).Name} на '{component.name}'");
            }
            Forbid<AutomaticWeaponSlideFeedback>();
            Forbid<WeaponMechanismVisuals>();
            Forbid<WeaponChamberingReminder>();
            Forbid<UxrShotgunPump>();
            Assert.That(legacy, Is.Empty, "Второй писатель позы/звука/подсказки рядом с хостом:\n" + string.Join("\n", legacy));

            int missing = prefab.GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Assert.That(missing, Is.Zero, "На префабе остались компоненты удалённых скриптов.");
        }

        [TestCaseSource(nameof(Pilots))]
        public void Механизм_и_ось_ReleasedAction(Pilot pilot)
        {
            GameObject prefab = Load(pilot);
            WeaponMechanismRig rig = prefab.GetComponent<WeaponSystem>().Rig;
            Assert.That(rig.HasAction, Is.True, "Ручка Action и её привязки записаны.");
            Assert.That(rig.Handle.transform.IsChildOf(prefab.transform), Is.True, "Ручка — деталь этого оружия.");
            Assert.That(rig.Body, Is.Not.Null, "Корпус, в пространстве которого меряется ход.");
            Assert.That(WeaponMechanismRig.TryGetTravel(rig.Handle, out _, out _), Is.True,
                "Ход ручки берётся из Translation Limits (Restrict Local Offset).");
            Assert.That(rig.ExtractionGate, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.That(rig.ReleasedAction, Is.EqualTo(pilot.Released),
                "S4: отпущенный затвор Herrington возвращает пружина, помпа FABARM остаётся на месте.");
        }

        [TestCaseSource(nameof(Pilots))]
        public void Звуки_механизма_из_WeaponAudioSet(Pilot pilot)
        {
            WeaponAudioSet audio = Load(pilot).GetComponent<WeaponSystem>().Audio;
            var wrong = new List<string>();
            void Expect(string what, UxrAudioSample sample, string clip)
            {
                string actual = sample?.Clip != null ? sample.Clip.name : null;
                if (actual != clip) wrong.Add($"{what}: '{actual ?? "пусто"}', ожидается '{clip ?? "пусто"}'");
            }
            Expect("ActionBack", audio.ActionBack, pilot.ActionBack);
            Expect("ActionForwardChambered", audio.ActionForwardChambered, pilot.ActionForwardChambered);
            Expect("ActionForwardEmpty", audio.ActionForwardEmpty, pilot.ActionForwardEmpty);
            Expect("Refusal (S2)", audio.Refusal, RefusalClip);
            Assert.That(wrong, Is.Empty, string.Join("\n", wrong));

            // Сигнал досылания всегда звучит: пустой ForwardChambered берёт ForwardEmpty.
            Assert.That(WeaponAudioSet.Has(audio.For(WS.WeaponCue.ActionForwardChambered)), Is.True);
            Assert.That(WeaponAudioSet.Has(audio.For(WS.WeaponCue.ActionBack)), Is.True);
        }

        [TestCaseSource(nameof(Pilots))]
        public void Вибрации_отказов_и_хода_не_пустые(Pilot pilot)
        {
            WeaponHapticSet haptics = Load(pilot).GetComponent<WeaponSystem>().Haptics;
            var silent = new[] { WS.WeaponHapticCue.NotReady, WS.WeaponHapticCue.Faulted, WS.WeaponHapticCue.RateOfFire, WS.WeaponHapticCue.ActionRear }
                .Where(cue => !WeaponHapticSet.Has(haptics.For(cue))).ToList();
            Assert.That(silent, Is.Empty, "Сигналы машины без вибрации: " + string.Join(", ", silent));
        }

        private static GameObject Load(Pilot pilot)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pilot.Path);
            Assert.That(prefab, Is.Not.Null, $"Контроль: префаб '{pilot.Path}' загружается.");
            Assert.That(prefab.GetComponent<UxrFirearmWeapon>(), Is.Not.Null, "Контроль: это огнестрельное оружие.");
            return prefab;
        }
    }
}
