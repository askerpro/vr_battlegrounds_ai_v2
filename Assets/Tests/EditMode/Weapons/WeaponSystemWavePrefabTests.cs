using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// Стволы волн F1–F4 на хосте <see cref="WeaponSystem"/> (этап waves-f, принят пользователем в шлеме 2026-10-09).
    /// Источник списка — таблица <c>WeaponSystemAuthoring.Weapons</c> (волна, префаб, профиль, категория), включая обзорные
    /// копии <c>SightReview</c>. F5 (Revolver, R08, SDK Shotgun) ещё не переведены и здесь не проверяются.
    ///
    /// Классы ошибок: второй писатель позы/звука/подсказки рядом с хостом (прежние AWSF, WMV, Reminder) или компонент
    /// удалённого скрипта; ствол не своей категории или без ссылки на дефолты (звук и вибрация молчат или чужие);
    /// данные хоста, которые отвергнет настройка в игре; обязательный сигнал без источника. SRM12 — нарезанные клипы
    /// затвора вместо испорченных клипов пака. Сторож уровня — ни один разрешённый звук механизма не «почти тишина».
    ///
    /// <c>WeaponSystemAuthoring</c> и <c>SfxClipLevel</c> живут в Assembly-CSharp-Editor, недоступной прямой ссылкой из
    /// тестового asmdef, — вызываются через отражение: тест читает ту же таблицу и тот же отчёт, что preflight сборщика.
    /// </summary>
    public class WeaponSystemWavePrefabTests
    {
        private static readonly string[] ConvertedWaves = { "F1", "F2", "F3", "F4" };
        private const string Srm12Sfx = "Assets/Audio/SFX/Weapons/Kinemation/SRM12/";
        private const string Srm12BackCut = Srm12Sfx + "SRM12_BoltBack_Cut.wav";
        private const string Srm12ForwardCut = Srm12Sfx + "SRM12_BoltForward_Cut.wav";
        private const string Srm12BackPack = Srm12Sfx + "SRM12_BoltBack.wav";

        /// <summary>Строка таблицы авторинга, прочитанная отражением.</summary>
        public sealed class Row
        {
            public string Name, Prefab, Wave, Profile;
            public WeaponFeedbackCategory Category;
            public override string ToString() => Name;
        }

        // ── Отражение в Assembly-CSharp-Editor ──────────────────────────────────────────────

        private static Type FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);

        private static Type Authoring => FindType("VrBattlegrounds.Editor.Gameplay.WeaponSystemAuthoring");
        private static Type ClipLevel => FindType("VrBattlegrounds.EditorTools.Audio.SfxClipLevel");

        private static IEnumerable<Row> AllRows()
        {
            Type authoring = Authoring;
            if (authoring == null) yield break;
            var weapons = (IEnumerable)authoring.GetField("Weapons", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            foreach (object entry in weapons)
            {
                Type t = entry.GetType();
                string F(string name) => (string)t.GetField(name).GetValue(entry);
                yield return new Row
                {
                    Name = F("Name"), Prefab = F("Prefab"), Wave = F("Wave"), Profile = F("Profile"),
                    Category = (WeaponFeedbackCategory)t.GetField("Category").GetValue(entry)
                };
            }
        }

        public static IEnumerable<TestCaseData> WaveWeapons()
        {
            Row[] rows = AllRows().Where(r => ConvertedWaves.Contains(r.Wave)).ToArray();
            if (rows.Length == 0)
            {
                yield return new TestCaseData(new object[] { null }).SetName("{m}(нет таблицы WeaponSystemAuthoring.Weapons)");
                yield break;
            }
            foreach (Row row in rows) yield return new TestCaseData(row).SetName($"{{m}}({row.Wave}_{row.Name})");
        }

        /// <summary>Все переведённые стволы — пилоты D и волны F1–F4 — для сторожа уровня звука.</summary>
        private static Row[] ConvertedRows() => AllRows().Where(r => r.Wave == "D" || ConvertedWaves.Contains(r.Wave)).ToArray();

        private static WeaponFeedbackDefaults LoadDefaults(WeaponFeedbackCategory category) =>
            (WeaponFeedbackDefaults)Authoring.GetMethod("LoadDefaults", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { category });

        private static List<string> FeedbackReport(WeaponSystem host, List<string> errors, List<string> warnings) =>
            (List<string>)Invoke(Authoring.GetMethod("FeedbackReport", BindingFlags.Public | BindingFlags.Static), host, errors, warnings);

        private static bool TryPeakDbfs(AudioClip clip, out float peak, out string how)
        {
            var args = new object[] { clip, 0f, null };
            bool ok = (bool)Invoke(ClipLevel.GetMethod("TryPeakDbfs", BindingFlags.Public | BindingFlags.Static), args);
            peak = (float)args[1]; how = (string)args[2];
            return ok;
        }

        private static float NearSilenceDbfs => (float)ClipLevel.GetField("NearSilenceDbfs", BindingFlags.Public | BindingFlags.Static).GetValue(null);

        private static bool SameBytes(AudioClip a, AudioClip b) =>
            (bool)Invoke(ClipLevel.GetMethod("SameBytes", BindingFlags.Public | BindingFlags.Static), a, b);

        private static object Invoke(MethodInfo method, params object[] args)
        {
            Assert.That(method, Is.Not.Null, "Контроль: метод редакторского инструмента найден.");
            try { return method.Invoke(null, args); }
            catch (TargetInvocationException exception)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException ?? exception).Throw();
                throw;
            }
        }

        [OneTimeSetUp]
        public void Tools()
        {
            Assert.That(Authoring, Is.Not.Null, "Не загружен WeaponSystemAuthoring (Assembly-CSharp-Editor).");
            Assert.That(ClipLevel, Is.Not.Null, "Не загружен SfxClipLevel (Assembly-CSharp-Editor).");
        }

        // ── Волны F1–F4: хост, категория, preflight ─────────────────────────────────────────

        [TestCaseSource(nameof(WaveWeapons))]
        public void Ствол_волны_на_одном_хосте_без_прежних_компонентов(Row row)
        {
            Assert.That(row, Is.Not.Null, "Таблица стволов WeaponSystemAuthoring.Weapons не прочитана.");
            GameObject prefab = Load(row);
            WeaponSystem[] hosts = prefab.GetComponentsInChildren<WeaponSystem>(true);
            Assert.That(hosts.Length, Is.EqualTo(1), "Ровно один хост WeaponSystem.");
            Assert.That(hosts[0].gameObject, Is.SameAs(prefab), "Хост на корне оружия, рядом с UxrFirearmWeapon.");

            var legacy = new List<string>();
            void Forbid<T>() where T : Component
            {
                foreach (T component in prefab.GetComponentsInChildren<T>(true))
                    legacy.Add($"{typeof(T).Name} на '{component.name}'");
            }
            Forbid<AutomaticWeaponSlideFeedback>();
            Forbid<WeaponMechanismVisuals>();
            Forbid<WeaponChamberingReminder>();
            Assert.That(legacy, Is.Empty, "Второй писатель позы/звука/подсказки рядом с хостом:\n" + string.Join("\n", legacy));

            int missing = prefab.GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            Assert.That(missing, Is.Zero, "На префабе остались компоненты удалённых скриптов.");
        }

        [TestCaseSource(nameof(WaveWeapons))]
        public void Категория_профиль_и_дефолты_как_в_таблице(Row row)
        {
            Assert.That(row, Is.Not.Null, "Таблица стволов WeaponSystemAuthoring.Weapons не прочитана.");
            WeaponSystem host = Load(row).GetComponent<WeaponSystem>();
            Assert.That(host, Is.Not.Null, "Хост WeaponSystem на корне.");

            WeaponFeedbackDefaults expected = LoadDefaults(row.Category);
            Assert.That(expected, Is.Not.Null, $"Контроль: ассет дефолтов категории {row.Category} существует.");
            Assert.That(host.FeedbackDefaults, Is.Not.Null, "У ствола ссылка на дефолты категории (иначе пустые поля молчат).");
            Assert.That(host.FeedbackDefaults, Is.SameAs(expected), $"Ссылка на дефолты не категории {row.Category} из таблицы.");
            Assert.That(host.FeedbackDefaults.Category, Is.EqualTo(row.Category), "Категория в ассете дефолтов совпадает с таблицей.");

            Assert.That(host.Profile, Is.Not.Null, "У хоста задан профиль готовности.");
            Assert.That(AssetDatabase.GetAssetPath(host.Profile), Is.EqualTo(row.Profile), $"Профиль волны {row.Wave} из таблицы.");
        }

        [TestCaseSource(nameof(WaveWeapons))]
        public void Preflight_хоста_и_отклика_без_ошибок(Row row)
        {
            Assert.That(row, Is.Not.Null, "Таблица стволов WeaponSystemAuthoring.Weapons не прочитана.");
            WeaponSystem host = Load(row).GetComponent<WeaponSystem>();
            Assert.That(host, Is.Not.Null, "Хост WeaponSystem на корне.");

            Assert.That(host.TryValidateConfiguration(out string error), Is.True,
                "Данные хоста отвергнет настройка в игре: " + error);

            // Тот же отчёт, что печатает preflight сборщика, но по сохранённому префабу (без повторной записи writer).
            var errors = new List<string>(); var warnings = new List<string>();
            List<string> report = FeedbackReport(host, errors, warnings);
            Assert.That(errors, Is.Empty, $"{row.Name}: ошибки отклика:\n" + string.Join("\n", errors) + "\n— отчёт —\n" + string.Join("\n", report));
        }

        // ── SRM12: нарезанные клипы затвора ─────────────────────────────────────────────────

        [TestCase("SRM12")]
        [TestCase("SRM12_SightReview")]
        public void SRM12_затвор_из_нарезки_и_не_тишина(string weapon)
        {
            Row row = AllRows().FirstOrDefault(r => r.Name == weapon);
            Assert.That(row, Is.Not.Null, $"Строка {weapon} в таблице авторинга.");
            WeaponSystem host = Load(row).GetComponent<WeaponSystem>();
            WeaponAudioSet defaults = host.FeedbackDefaults != null ? host.FeedbackDefaults.Audio : null;

            var wrong = new List<string>();
            AudioClip Expect(WS.WeaponCue cue, string path)
            {
                UxrAudioSample sample = WeaponAudioSet.Resolve(host.Audio, defaults, cue, out WeaponFeedbackSource source);
                string actual = sample?.Clip != null ? AssetDatabase.GetAssetPath(sample.Clip) : null;
                if (source != WeaponFeedbackSource.Override || actual != path)
                    wrong.Add($"{cue}: {actual ?? "пусто"} ({source}), ожидается оверрайд {path}");
                return sample?.Clip;
            }
            AudioClip back = Expect(WS.WeaponCue.ActionBack, Srm12BackCut);
            AudioClip forward = Expect(WS.WeaponCue.ActionForwardChambered, Srm12ForwardCut);
            Expect(WS.WeaponCue.ActionForwardEmpty, Srm12ForwardCut);
            Expect(WS.WeaponCue.ActionReturnPartial, Srm12ForwardCut);
            Assert.That(wrong, Is.Empty, string.Join("\n", wrong));

            foreach (AudioClip clip in new[] { back, forward })
            {
                Assert.That(TryPeakDbfs(clip, out float peak, out string how), Is.True, $"{clip.name}: уровень измерен ({how}).");
                Assert.That(peak, Is.GreaterThanOrEqualTo(NearSilenceDbfs), $"{clip.name}: почти тишина, пик {peak:0.#} dBFS.");
            }
            Assert.That(SameBytes(back, forward), Is.False, "Оттяжка и возврат SRM12 — разные куски цикла, не один файл.");
        }

        [Test]
        public void Контроль_сторожа_клип_пака_SRM12_BoltBack_почти_тишина()
        {
            // Без этого контроля сторож уровня мог бы молча пропускать всё (порог, чтение WAV).
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Srm12BackPack);
            Assert.That(clip, Is.Not.Null, "Исходник пака на месте.");
            Assert.That(TryPeakDbfs(clip, out float peak, out string how), Is.True, $"Уровень измерен ({how}).");
            Assert.That(peak, Is.LessThan(NearSilenceDbfs), $"Исходник пака должен распознаваться как тишина, пик {peak:0.#} dBFS.");
        }

        // ── Сторож уровня: разрешённые звуки механизма переведённых стволов ─────────────────

        private static readonly WS.WeaponCue[] MechanismCues =
        {
            WS.WeaponCue.ActionBack, WS.WeaponCue.ActionForwardChambered, WS.WeaponCue.ActionForwardEmpty, WS.WeaponCue.ActionReturnPartial,
            WS.WeaponCue.ChamberEjected, WS.WeaponCue.SlideLockCatch, WS.WeaponCue.DryFire, WS.WeaponCue.Refusal
        };

        [Test]
        public void Ни_один_разрешённый_звук_механизма_не_почти_тишина()
        {
            Row[] rows = ConvertedRows();
            Assert.That(rows.Length, Is.GreaterThan(2), "Контроль: переведённые стволы прочитаны из таблицы.");
            var silent = new List<string>();
            int measured = 0;
            foreach (Row row in rows)
            {
                WeaponSystem host = Load(row).GetComponent<WeaponSystem>();
                if (host == null) { silent.Add($"{row.Name}: нет хоста"); continue; }
                WeaponAudioSet defaults = host.FeedbackDefaults != null ? host.FeedbackDefaults.Audio : null;
                foreach (WS.WeaponCue cue in MechanismCues)
                {
                    UxrAudioSample sample = WeaponAudioSet.Resolve(host.Audio, defaults, cue, out _);
                    if (sample == null) continue;
                    if (!TryPeakDbfs(sample.Clip, out float peak, out _)) continue; // не WAV и не распакован — не измерен
                    measured++;
                    if (peak < NearSilenceDbfs) silent.Add($"{row.Name} {cue}: {sample.Clip.name}, пик {peak:0.#} dBFS");
                }
            }
            Assert.That(measured, Is.GreaterThan(rows.Length), "Контроль: уровень измерен у большинства звуков (WAV).");
            Assert.That(silent, Is.Empty, "Почти тишина вместо звука механизма:\n" + string.Join("\n", silent));
        }

        [Test]
        public void Дефолты_категорий_полные()
        {
            var wrong = new List<string>();
            foreach (WeaponFeedbackCategory category in Enum.GetValues(typeof(WeaponFeedbackCategory)))
            {
                WeaponFeedbackDefaults defaults = LoadDefaults(category);
                if (defaults == null) { wrong.Add($"{category}: нет ассета"); continue; }
                if (defaults.Category != category) wrong.Add($"{category}: в ассете категория {defaults.Category}");
                foreach (WS.WeaponCue cue in new[] { WS.WeaponCue.ActionBack, WS.WeaponCue.ActionForwardEmpty, WS.WeaponCue.Refusal })
                    if (!WeaponAudioSet.Has(defaults.Audio.For(cue))) wrong.Add($"{category}: нет звука {cue}");
                foreach (WS.WeaponHapticCue cue in new[] { WS.WeaponHapticCue.ActionRear, WS.WeaponHapticCue.NotReady, WS.WeaponHapticCue.Faulted, WS.WeaponHapticCue.RateOfFire })
                    if (!WeaponHapticSet.Has(defaults.Haptics.For(cue))) wrong.Add($"{category}: нет вибрации {cue}");
            }
            Assert.That(wrong, Is.Empty, "Дефолт категории неполон — у стволов без оверрайда сигнал замолчит:\n" + string.Join("\n", wrong));
        }

        private static GameObject Load(Row row)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(row.Prefab);
            Assert.That(prefab, Is.Not.Null, $"Контроль: префаб '{row.Prefab}' загружается.");
            Assert.That(prefab.GetComponent<UxrFirearmWeapon>(), Is.Not.Null, "Контроль: это огнестрельное оружие.");
            return prefab;
        }
    }
}
