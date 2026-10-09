using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Weapons;
using WS = VrBattlegrounds.Weapons.Core;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Weapons
{
    /// <summary>
    /// Вылет патрона и гильзы (этап ejection, принят пользователем в шлеме 2026-10-09).
    ///
    /// <para>
    /// Машина: живой патрон (<see cref="WS.WeaponCue.ChamberEjected"/>) — на фиксацию Extract, если в патроннике был патрон;
    /// гильза (<see cref="WS.WeaponCue.CasingEjected"/>) — на фиксацию Shot у самозарядного (Semi/Auto), у ручного цикла нет.
    /// Сигналы приходят по фиксации учёта, поэтому у наблюдателя те же, что у автора: вылет видят все игроки без сети.
    /// Классы ошибок: гильза у помпы/болта при выстреле, живой патрон из пустого патронника, наблюдатель без вылета.
    /// </para>
    /// <para>
    /// Данные: оверрайд ствола важнее дефолта категории, пустой оверрайд — дефолт, пусто везде — вылета нет. Префабы:
    /// у каждого ствола D–F4 окно выброса внутри ствола и вылет на каждый его сигнал; MKR9 — пистолетная гильза (оверрайд);
    /// SniperRifle (AX-50) и SRM12 (Desert Tech SRS) — категория «снайперская», снайперский патрон по дефолту категории;
    /// префабы вылета — только визуал (слой Ignore Raycast, Rigidbody, без граббабла).
    /// </para>
    /// Пул (<c>WeaponEjectaPool</c>) здесь не проверяется: он работает только в Play (вне Play вылет не спавнится).
    /// </summary>
    public class WeaponEjectionTests
    {
        private const int Mag = 1;
        private const string EjectaLayer = "Ignore Raycast";
        private static readonly string[] ConvertedWaves = { "D", "F1", "F2", "F3", "F4" };

        // ── Машина ──────────────────────────────────────────────────────────────────────────

        private static WS.WeaponProfileAxes Axes(WS.WeaponFireMode mode) => new WS.WeaponProfileAxes(
            mode, WS.WeaponAmmoCapability.DetachableMagazineChamber, WS.WeaponPhysicalCapability.ActionTravel,
            WS.WeaponChamberPolicy.ManualReturn, WS.WeaponEmptyPose.ReturnToRest, extractionGate: 0.8f, epsilon: 0.02f,
            springReturnSpeed: 4f, autoReturnSpeed: 2f, hasFireClip: true, fireClipDuration: 0.3f,
            releasedAction: WS.WeaponReleasedAction.Spring);

        private static WS.LedgerView Ready() => new WS.LedgerView(initialized: true, chamber: true, magazinePresent: true,
            magazineRounds: 3, capacity: 5, magazineToken: Mag);

        private static WS.LedgerView EmptyChambering() => new WS.LedgerView(initialized: true, magazinePresent: true,
            magazineRounds: 3, capacity: 5, magazineToken: Mag);

        private static WS.WeaponContext Context(bool observer) =>
            observer ? new WS.WeaponContext(WS.WeaponRole.Observer, false) : new WS.WeaponContext(WS.WeaponRole.Author, true);

        private static readonly WS.ActionSample IdleAtRest = new WS.ActionSample(false, false, 0f, 0f, true, false);

        private static IEnumerable<TestCaseData> Shots()
        {
            foreach (bool observer in new[] { false, true })
            {
                string role = observer ? "Observer" : "Author";
                foreach (WS.WeaponFireMode mode in new[] { WS.WeaponFireMode.Semi, WS.WeaponFireMode.Auto, WS.WeaponFireMode.Manual })
                foreach (bool chamberAfter in mode == WS.WeaponFireMode.Manual ? new[] { false } : new[] { true, false })
                    yield return new TestCaseData(observer, mode, chamberAfter, mode != WS.WeaponFireMode.Manual)
                        .SetName($"{{m}}({role}_{mode}_{(chamberAfter ? "ДосланСледующий" : "Последний")})");
            }
        }

        [TestCaseSource(nameof(Shots))]
        public void Выстрел_даёт_гильзу_только_у_самозарядного(bool observer, WS.WeaponFireMode mode, bool chamberAfter, bool casing)
        {
            var machine = new WS.WeaponStateMachine(Axes(mode));
            var output = new Recorder();
            machine.Step(WS.WeaponEvent.Committed(WS.LedgerOp.Shot, true, chamberAfter), chamberAfter ? Ready() : EmptyChambering(),
                IdleAtRest, Context(observer), output);

            Assert.That(output.Violations, Is.Zero, $"Нарушение таблицы в строке {machine.LastRowId}.");
            Assert.That(output.Cues.Count(cue => cue == WS.WeaponCue.CasingEjected), Is.EqualTo(casing ? 1 : 0),
                $"{mode}: гильза при выстреле — ровно одна у самозарядного, ни одной у ручного цикла.");
            Assert.That(output.Cues, Has.No.Member(WS.WeaponCue.ChamberEjected), "Выстрел не выбрасывает живой патрон.");
        }

        private static IEnumerable<TestCaseData> Extracts()
        {
            foreach (bool observer in new[] { false, true })
            foreach (bool chamberBefore in new[] { true, false })
                yield return new TestCaseData(observer, chamberBefore)
                    .SetName($"{{m}}({(observer ? "Observer" : "Author")}_{(chamberBefore ? "СПатроном" : "Пустой")})");
        }

        [TestCaseSource(nameof(Extracts))]
        public void Извлечение_выбрасывает_живой_патрон_только_из_заряженного_патронника(bool observer, bool chamberBefore)
        {
            var machine = new WS.WeaponStateMachine(Axes(WS.WeaponFireMode.Semi));
            var output = new Recorder();
            machine.Step(WS.WeaponEvent.Committed(WS.LedgerOp.Extract, chamberBefore, false), EmptyChambering(), IdleAtRest,
                Context(observer), output);

            Assert.That(output.Violations, Is.Zero, $"Нарушение таблицы в строке {machine.LastRowId}.");
            Assert.That(output.Cues.Count(cue => cue == WS.WeaponCue.ChamberEjected), Is.EqualTo(chamberBefore ? 1 : 0),
                "Живой патрон вылетает ровно один раз и только если он был в патроннике.");
            Assert.That(output.Cues, Has.No.Member(WS.WeaponCue.CasingEjected), "Извлечение не выбрасывает гильзу (ручной цикл — отдельный этап).");
        }

        private sealed class Recorder : WS.IWeaponOutput
        {
            public readonly List<WS.WeaponCue> Cues = new List<WS.WeaponCue>();
            public int Violations;

            public void Command(in WS.LedgerCommand command) { }
            public void Pose(in WS.PoseTarget target) { }
            public void Cue(WS.WeaponCue cue, WS.WeaponNotReadyReason reason) => Cues.Add(cue);
            public void Haptic(WS.WeaponHapticCue cue) { }
            public void Hint(bool on) { }

            public void Report(in WS.WeaponReport report)
            {
                if (report.Kind == WS.WeaponReportKind.TableViolation || report.Kind == WS.WeaponReportKind.Unhandled) Violations++;
            }
        }

        // ── Данные: подстановка ─────────────────────────────────────────────────────────────

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created) if (created != null) Object.DestroyImmediate(created);
            _created.Clear();
        }

        private GameObject Prefab(string name)
        {
            var prefab = new GameObject(name);
            _created.Add(prefab);
            return prefab;
        }

        private static void SetPrefab(WeaponEjectile ejectile, GameObject prefab) =>
            typeof(WeaponEjectile).GetField("_prefab", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(ejectile, prefab);

        [TestCase(WS.WeaponCue.ChamberEjected)]
        [TestCase(WS.WeaponCue.CasingEjected)]
        public void Оверрайд_ствола_важнее_дефолта_категории(WS.WeaponCue cue)
        {
            var overrides = new WeaponEjectionSet(); var defaults = new WeaponEjectionSet();
            GameObject own = Prefab("own"), common = Prefab("common");
            SetPrefab(overrides.For(cue), own); SetPrefab(defaults.For(cue), common);

            WeaponEjectile result = WeaponEjectionSet.Resolve(overrides, defaults, cue, out WeaponFeedbackSource source);
            Assert.That(result.Prefab, Is.SameAs(own));
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Override));
        }

        [TestCase(WS.WeaponCue.ChamberEjected)]
        [TestCase(WS.WeaponCue.CasingEjected)]
        public void Пустой_оверрайд_берёт_дефолт_категории(WS.WeaponCue cue)
        {
            var defaults = new WeaponEjectionSet();
            GameObject common = Prefab("common");
            SetPrefab(defaults.For(cue), common);

            WeaponEjectile result = WeaponEjectionSet.Resolve(new WeaponEjectionSet(), defaults, cue, out WeaponFeedbackSource source);
            Assert.That(result.Prefab, Is.SameAs(common));
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Default));
        }

        [Test]
        public void Пусто_везде_и_не_сигнал_вылета_дают_отсутствие_вылета()
        {
            Assert.That(WeaponEjectionSet.Resolve(new WeaponEjectionSet(), new WeaponEjectionSet(), WS.WeaponCue.CasingEjected,
                out WeaponFeedbackSource source), Is.Null);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.None));
            Assert.That(WeaponEjectionSet.Resolve(null, null, WS.WeaponCue.ChamberEjected, out source), Is.Null);

            var full = new WeaponEjectionSet();
            SetPrefab(full.LiveRound, Prefab("live")); SetPrefab(full.SpentCasing, Prefab("spent"));
            foreach (WS.WeaponCue cue in new[] { WS.WeaponCue.ActionBack, WS.WeaponCue.DryFire, WS.WeaponCue.SlideLockCatch })
                Assert.That(WeaponEjectionSet.Resolve(full, full, cue, out _), Is.Null, $"{cue} — не сигнал вылета.");
        }

        // ── Ассеты: дефолты, префабы вылета, стволы ─────────────────────────────────────────

        private static Type FindType(string fullName) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(fullName, false)).FirstOrDefault(t => t != null);

        private static Type Authoring => FindType("VrBattlegrounds.Editor.Gameplay.WeaponSystemAuthoring");

        private static object Call(string method, params object[] args)
        {
            MethodInfo info = Authoring?.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
            Assert.That(info, Is.Not.Null, "Контроль: метод WeaponSystemAuthoring." + method + " найден.");
            return info.Invoke(null, args);
        }

        /// <summary>Стволы D–F4 из таблицы авторинга: (имя, путь префаба, категория).</summary>
        public static IEnumerable<TestCaseData> ConvertedWeapons()
        {
            Type authoring = Authoring;
            var weapons = authoring?.GetField("Weapons", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IEnumerable;
            if (weapons == null)
            {
                yield return new TestCaseData(null, null, WeaponFeedbackCategory.Rifle).SetName("{m}(нет таблицы WeaponSystemAuthoring.Weapons)");
                yield break;
            }
            foreach (object entry in weapons)
            {
                Type t = entry.GetType();
                string name = (string)t.GetField("Name").GetValue(entry), wave = (string)t.GetField("Wave").GetValue(entry);
                if (!ConvertedWaves.Contains(wave)) continue;
                yield return new TestCaseData(name, (string)t.GetField("Prefab").GetValue(entry),
                    (WeaponFeedbackCategory)t.GetField("Category").GetValue(entry)).SetName($"{{m}}({wave}_{name})");
            }
        }

        [TestCase(WeaponFeedbackCategory.Rifle)]
        [TestCase(WeaponFeedbackCategory.Pistol)]
        [TestCase(WeaponFeedbackCategory.Shotgun)]
        [TestCase(WeaponFeedbackCategory.Sniper)]
        public void Дефолты_категории_заполнены_патроном_и_гильзой(WeaponFeedbackCategory category)
        {
            var defaults = (WeaponFeedbackDefaults)Call("LoadDefaults", category);
            Assert.That(defaults, Is.Not.Null, $"Нет ассета дефолтов {category}.");
            Assert.That(WeaponEjectile.Has(defaults.Ejection.LiveRound), $"{category}: нет живого патрона.");
            Assert.That(WeaponEjectile.Has(defaults.Ejection.SpentCasing), $"{category}: нет гильзы.");
            Assert.That(defaults.Ejection.LiveRound.Lifetime, Is.InRange(1f, 30f), "Патрон исчезает через секунды.");
            Assert.That(defaults.Ejection.SpentCasing.Lifetime, Is.InRange(1f, 30f), "Гильза исчезает через секунды.");
        }

        [Test]
        public void Префабы_вылета_только_визуал()
        {
            var paths = ((IEnumerable<string>)(Authoring?.GetProperty("EjectaPrefabPaths", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) ?? Enumerable.Empty<string>())).ToArray();
            Assert.That(paths, Is.Not.Empty, "Список префабов вылета WeaponSystemAuthoring.EjectaPrefabPaths не прочитан.");
            int layer = LayerMask.NameToLayer(EjectaLayer);
            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, "Нет префаба вылета " + path);
                Assert.That(prefab.GetComponent<Rigidbody>(), Is.Not.Null, path + ": без Rigidbody.");
                Assert.That(prefab.GetComponent<WeaponEjecta>(), Is.Not.Null, path + ": без WeaponEjecta.");
                Assert.That(prefab.GetComponentInChildren<Collider>(true), Is.Not.Null, path + ": без коллайдера.");
                Assert.That(prefab.GetComponentsInChildren<UxrGrabbableObject>(true), Is.Empty, path + ": вылет не должен хвататься.");
                Assert.That(prefab.GetComponentsInChildren<Mirror.NetworkIdentity>(true), Is.Empty, path + ": вылет не сетевой.");
                foreach (Transform part in prefab.GetComponentsInChildren<Transform>(true))
                    Assert.That(part.gameObject.layer, Is.EqualTo(layer), $"{path}/{part.name}: слой не {EjectaLayer}.");
            }
        }

        [TestCaseSource(nameof(ConvertedWeapons))]
        public void Ствол_с_окном_выброса_и_вылетом_на_каждый_сигнал(string name, string path, WeaponFeedbackCategory category)
        {
            Assert.That(name, Is.Not.Null, "Таблица стволов WeaponSystemAuthoring.Weapons не прочитана.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            WeaponSystem host = prefab != null ? prefab.GetComponent<WeaponSystem>() : null;
            Assert.That(host, Is.Not.Null, path + ": нет хоста WeaponSystem.");

            WeaponEjectionPort port = host.EjectionPort;
            Assert.That(port.IsValid && port.Anchor.IsChildOf(prefab.transform), name + ": окно выброса вне ствола.");

            var cues = new List<WS.WeaponCue>();
            if (host.Profile.PhysicalCapability == WeaponPhysicalCapability.ActionTravel) cues.Add(WS.WeaponCue.ChamberEjected);
            var cycle = new SerializedObject(host.GetComponent<UltimateXR.Mechanics.Weapons.UxrFirearmWeapon>())
                .FindProperty("_triggers.Array.data[0]._cycleType");
            if (cycle != null && cycle.intValue != (int)UltimateXR.Mechanics.Weapons.UxrShotCycle.ManualReload) cues.Add(WS.WeaponCue.CasingEjected);
            Assert.That(cues, Is.Not.Empty, name + ": ни одного сигнала вылета.");

            Assert.That(host.FeedbackDefaults, Is.Not.Null, name + ": нет дефолтов категории.");
            Assert.That(host.FeedbackDefaults.Category, Is.EqualTo(category), name + ": дефолты не своей категории.");
            foreach (WS.WeaponCue cue in cues)
            {
                WeaponEjectile ejectile = WeaponEjectionSet.Resolve(host.Ejection, host.FeedbackDefaults.Ejection, cue, out WeaponFeedbackSource source);
                Assert.That(ejectile, Is.Not.Null, $"{name}: нет вылета {cue}.");
                var wanted = (string)Call("EjectionOverrideOf", name, cue);
                if (wanted != null)
                {
                    Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Override), $"{name}: {cue} — ждём оверрайд {wanted}.");
                    Assert.That(ejectile.Prefab.name, Is.EqualTo(wanted), $"{name}: {cue}.");
                }
                else Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Default), $"{name}: {cue} — без оверрайда берётся дефолт категории.");
            }
        }

        [TestCase("MKR9", WS.WeaponCue.CasingEjected, "Pistol_Casing")]
        [TestCase("MKR9_SightReview", WS.WeaponCue.CasingEjected, "Pistol_Casing")]
        public void Калибр_ствола_вне_категории_задан_оверрайдом(string name, WS.WeaponCue cue, string ejecta)
        {
            Assert.That(Call("EjectionOverrideOf", name, cue), Is.EqualTo(ejecta),
                "Решение пользователя 2026-10-09: MKR9 (9 мм) в категории «автомат» — пистолетная гильза.");
        }

        [TestCase("SniperRifle", WS.WeaponCue.ChamberEjected)]
        [TestCase("SniperRifle_SightReview", WS.WeaponCue.ChamberEjected)]
        [TestCase("SRM12", WS.WeaponCue.ChamberEjected)]
        [TestCase("SRM12_SightReview", WS.WeaponCue.ChamberEjected)]
        public void Снайперские_стволы_в_своей_категории_и_вылет_по_дефолту(string name, WS.WeaponCue cue)
        {
            Assert.That(Call("EjectionOverrideOf", name, cue), Is.Null, "Снайперский вылет — дефолт категории, не оверрайд.");
            string path = name.EndsWith("_SightReview")
                ? $"Assets/Prefabs/Weapons/SightReview/{name}.prefab" : $"Assets/Prefabs/Weapons/{name}/{name}.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            WeaponSystem host = prefab != null ? prefab.GetComponent<WeaponSystem>() : null;
            Assert.That(host, Is.Not.Null, path + ": нет хоста.");
            Assert.That(host.FeedbackDefaults != null && host.FeedbackDefaults.Category == WeaponFeedbackCategory.Sniper,
                "Решение пользователя 2026-10-09: SniperRifle (AX-50) и SRM12 (Desert Tech SRS) — категория «снайперская винтовка».");
            WeaponEjectile ejectile = WeaponEjectionSet.Resolve(host.Ejection, host.FeedbackDefaults.Ejection, cue, out WeaponFeedbackSource source);
            Assert.That(source, Is.EqualTo(WeaponFeedbackSource.Default));
            Assert.That(ejectile.Prefab.name, Is.EqualTo("Sniper_Round"), name + ": снайперский патрон.");
        }
    }
}
