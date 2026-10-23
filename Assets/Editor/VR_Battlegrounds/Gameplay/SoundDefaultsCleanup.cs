using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Weapons;
using WS = VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Снимает с префабов копии звуковых дефолтов, которые раньше писали сборщики константами (решение пользователя
    /// 2026-10-23: дефолты — данные в ассетах, подстановка в момент проигрывания), чтобы объект звучал ассетом и подбор на
    /// слух был правкой одного ассета. Снимается только точное совпадение с тем, что дал бы дефолт, поэтому звучание не
    /// меняется; свой клип объекта (нарезка пака, чужой выбор) — оверрайд, остаётся.
    ///
    /// <list type="bullet">
    /// <item>Префабы вылета (<see cref="WeaponSystemAuthoring.EjectaPrefabPaths"/>): <c>WeaponEjecta._impactClips</c>, если набор
    /// совпадает с <see cref="WeaponFeedbackDefaults.EjectaImpacts"/> каждой категории, чей ствол выпускает этот префаб
    /// (дефолты вылета категорий и оверрайды таблицы хоста).</item>
    /// <item>Гнёзда магазинов и окна приёма патрона стволов реестра: свой клип <see cref="AnchorSound"/>, равный
    /// <see cref="AnchorSound.DefaultInsertClip"/>/<see cref="AnchorSound.DefaultTakeOutClip"/> (категория ствола → роль).</item>
    /// <item>Оружие и магазины реестра: <see cref="ImpactSound"/> получает вид (<c>_kind</c>), свои клипы снимаются при
    /// совпадении с набором вида в <see cref="ImpactSoundDefaults"/>.</item>
    /// </list>
    ///
    /// <para>
    /// Порядок: сначала заполнить ассеты дефолтов (текущими клипами — тогда звук не меняется), затем <see cref="DryRun"/>,
    /// отчёт, <see cref="Apply"/>. Меню нет — вызов из кода (как <see cref="AnchorSoundRoleDefaultsCleanup"/>). Поля правятся
    /// прямо в ассете и сохраняются <c>SavePrefabAsset</c>: иерархия не пересобирается, UxrUniqueId и assetId не меняются.
    /// </para>
    /// </summary>
    public static class SoundDefaultsCleanup
    {
        private const string Registry = "Assets/Data/Weapons/Resources/WeaponRegistry.asset";

        public enum Action
        {
            /// <summary>Копия дефолта снята (или будет снята): объект звучит ассетом.</summary>
            Clear,
            /// <summary>Свой клип отличается от дефолта — оверрайд, оставлен.</summary>
            KeepOverride,
            /// <summary>Дефолта нет (ассет не заполнен или префаб никто не выпускает) — оставлен, иначе стало бы тихо.</summary>
            KeepNoDefault,
            /// <summary>Записан вид удара (<c>ImpactSound._kind</c>).</summary>
            SetKind
        }

        public readonly struct Entry
        {
            public readonly string PrefabPath, ObjectPath, What, Note;
            public readonly Action Action;

            public Entry(string prefabPath, string objectPath, string what, Action action, string note = null)
            {
                PrefabPath = prefabPath; ObjectPath = objectPath; What = what; Action = action; Note = note;
            }

            public override string ToString() =>
                $"{Action,-14} {What,-22} {PrefabPath} :: {ObjectPath}{(string.IsNullOrEmpty(Note) ? "" : " — " + Note)}";
        }

        public sealed class Report
        {
            public bool Applied;
            public readonly List<Entry> Entries = new List<Entry>();
            public readonly List<string> SavedPrefabs = new List<string>();

            public int Count(Action action) => Entries.Count(e => e.Action == action);

            public override string ToString()
            {
                var text = new StringBuilder();
                text.AppendLine($"[SoundDefaultsCleanup] {(Applied ? "применено" : "сухой прогон")}: снять {Count(Action.Clear)}, " +
                                $"оверрайдов {Count(Action.KeepOverride)}, без дефолта {Count(Action.KeepNoDefault)}, " +
                                $"вид удара {Count(Action.SetKind)}; префабов сохранено {SavedPrefabs.Count}.");
                foreach (Entry entry in Entries) text.AppendLine("  " + entry);
                return text.ToString();
            }
        }

        /// <summary>Отчёт без изменений.</summary>
        public static Report DryRun() => Run(false);

        /// <summary>Снимает копии дефолтов и пишет вид удара; пересохраняет только изменённые префабы.</summary>
        public static Report Apply() => Run(true);

        private static Report Run(bool apply)
        {
            var report = new Report { Applied = apply };
            var changed = new HashSet<GameObject>();
            Ejecta(apply, report, changed);

            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(Registry);
            if (registry == null) GameLog.Error("[SoundDefaultsCleanup] Нет реестра " + Registry + " — гнёзда и удары не проверены.");
            else
                foreach (WeaponInfo info in registry.Weapons)
                {
                    if (info == null) continue;
                    if (info.WeaponPrefab != null)
                    {
                        Receivers(info.WeaponPrefab, apply, report, changed);
                        Impact(info.WeaponPrefab, ImpactSoundKind.Weapon, apply, report, changed);
                    }
                    if (info.MagazinePrefab != null) Impact(info.MagazinePrefab, ImpactSoundKind.Magazine, apply, report, changed);
                }

            if (apply)
                foreach (GameObject prefab in changed)
                {
                    PrefabUtility.SavePrefabAsset(prefab);
                    report.SavedPrefabs.Add(AssetDatabase.GetAssetPath(prefab));
                }
            GameLog.Debug.Info(report.ToString());
            return report;
        }

        // ── Вылет ─────────────────────────────────────────────────────────────────────────────

        private static void Ejecta(bool apply, Report report, HashSet<GameObject> changed)
        {
            Dictionary<GameObject, List<WeaponFeedbackDefaults>> users = EjectaUsers();
            foreach (string path in WeaponSystemAuthoring.EjectaPrefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                WeaponEjecta ejecta = prefab != null ? prefab.GetComponent<WeaponEjecta>() : null;
                if (ejecta == null || ejecta.ImpactClips == null || ejecta.ImpactClips.Length == 0) continue;
                const string what = "касание вылета";
                if (!users.TryGetValue(prefab, out List<WeaponFeedbackDefaults> categories) || categories.Count == 0)
                {
                    report.Entries.Add(new Entry(path, prefab.name, what, Action.KeepNoDefault, "префаб не выпускает ни одна категория"));
                    continue;
                }
                WeaponFeedbackDefaults differs = categories.FirstOrDefault(d => !ImpactSoundInstaller.SameSet(ejecta.ImpactClips, d.EjectaImpacts));
                if (differs != null)
                {
                    report.Entries.Add(new Entry(path, prefab.name, what,
                        differs.EjectaImpacts.Length == 0 ? Action.KeepNoDefault : Action.KeepOverride, "отличается от " + differs.name));
                    continue;
                }
                if (apply)
                {
                    var data = new SerializedObject(ejecta);
                    data.FindProperty("_impactClips").arraySize = 0;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    changed.Add(prefab);
                }
                report.Entries.Add(new Entry(path, prefab.name, what, Action.Clear, string.Join(", ", categories.Select(d => d.name).Distinct())));
            }
        }

        /// <summary>Префаб вылета → ассеты категорий, чьи стволы его выпускают: дефолт вылета категории и оверрайды таблицы хоста.</summary>
        private static Dictionary<GameObject, List<WeaponFeedbackDefaults>> EjectaUsers()
        {
            var users = new Dictionary<GameObject, List<WeaponFeedbackDefaults>>();
            void Add(GameObject prefab, WeaponFeedbackDefaults defaults)
            {
                if (prefab == null || defaults == null) return;
                if (!users.TryGetValue(prefab, out List<WeaponFeedbackDefaults> list)) users[prefab] = list = new List<WeaponFeedbackDefaults>();
                if (!list.Contains(defaults)) list.Add(defaults);
            }

            foreach (WeaponFeedbackCategory category in Enum.GetValues(typeof(WeaponFeedbackCategory)))
            {
                WeaponFeedbackDefaults defaults = WeaponSystemAuthoring.LoadDefaults(category);
                if (defaults == null) continue;
                Add(defaults.Ejection.LiveRound.Prefab, defaults);
                Add(defaults.Ejection.SpentCasing.Prefab, defaults);
            }
            foreach (WeaponSystemAuthoring.Entry entry in WeaponSystemAuthoring.Weapons)
            {
                WeaponFeedbackDefaults defaults = WeaponSystemAuthoring.LoadDefaults(entry.Category);
                foreach (WS.WeaponCue cue in new[] { WS.WeaponCue.ChamberEjected, WS.WeaponCue.CasingEjected })
                {
                    string name = WeaponSystemAuthoring.EjectionOverrideOf(entry.Name, cue);
                    if (name != null) Add(AssetDatabase.LoadAssetAtPath<GameObject>(WeaponSystemAuthoring.EjectionFolder + "/" + name + ".prefab"), defaults);
                }
            }
            return users;
        }

        // ── Гнёзда магазинов и окна приёма ────────────────────────────────────────────────────

        private static void Receivers(GameObject weapon, bool apply, Report report, HashSet<GameObject> changed)
        {
            string path = AssetDatabase.GetAssetPath(weapon);
            foreach (AnchorSound sound in weapon.GetComponentsInChildren<AnchorSound>(true))
            {
                if (!sound.IsWeaponAmmoReceiver) continue;
                var anchor = sound.GetComponent<UltimateXR.Manipulation.UxrGrabbableObjectAnchor>();
                // Якорь вложенного префаба проекта (гнездо магазина внутри вложенного магазина) чистится в своём префабе.
                if (anchor == null || !AnchorSoundInstaller.OwnedBy(anchor, path)) continue;
                string objectPath = PathOf(sound.transform, weapon.transform);

                AudioClip ownTake = sound.OwnTakeOutClip;
                if (ownTake != null)
                {
                    AudioClip common = sound.DefaultTakeOutClip;
                    Action action = common == null ? Action.KeepNoDefault : common == ownTake ? Action.Clear : Action.KeepOverride;
                    if (action == Action.Clear && apply)
                    {
                        var data = new SerializedObject(sound);
                        data.FindProperty("_takeOutClip").objectReferenceValue = null;
                        data.ApplyModifiedPropertiesWithoutUndo();
                        changed.Add(weapon);
                    }
                    report.Entries.Add(new Entry(path, objectPath, "снятие (гнездо)", action, ownTake.name));
                }

                AudioClip ownInsert = sound.OwnInsertClip;
                if (ownInsert != null)
                {
                    // Без своего клипа источник сначала спросил бы дефолт; сравнение — с тем, что прозвучит после снятия.
                    AudioClip common = sound.DefaultInsertClip;
                    Action action = common == null ? Action.KeepNoDefault : common == ownInsert ? Action.Clear : Action.KeepOverride;
                    if (action == Action.Clear && apply)
                    {
                        sound.Source.clip = null;
                        EditorUtility.SetDirty(sound.Source);
                        changed.Add(weapon);
                    }
                    report.Entries.Add(new Entry(path, objectPath, "вставка (гнездо)", action, ownInsert.name));
                }
            }
        }

        // ── Падение ────────────────────────────────────────────────────────────────────────────

        private static void Impact(GameObject prefab, ImpactSoundKind kind, bool apply, Report report, HashSet<GameObject> changed)
        {
            ImpactSound impact = prefab.GetComponent<ImpactSound>();
            if (impact == null) return;
            string path = AssetDatabase.GetAssetPath(prefab);
            var data = new SerializedObject(impact);
            bool dirty = false;
            if (impact.Kind != kind)
            {
                data.FindProperty("_kind").intValue = (int)kind;
                dirty = true;
                report.Entries.Add(new Entry(path, prefab.name, "вид удара", Action.SetKind, kind.ToString()));
            }

            AudioClip[] own = impact.OwnClips;
            if (own != null && own.Length > 0)
            {
                ImpactSoundDefaults defaults = ImpactSoundDefaults.Instance;
                AudioClip[] common = defaults != null ? defaults.For(kind) : Array.Empty<AudioClip>();
                Action action = common.Length == 0 ? Action.KeepNoDefault :
                    ImpactSoundInstaller.SameSet(own, common) ? Action.Clear : Action.KeepOverride;
                if (action == Action.Clear)
                {
                    data.FindProperty("_clips").arraySize = 0;
                    dirty = true;
                }
                report.Entries.Add(new Entry(path, prefab.name, "удар о мир", action, kind.ToString()));
            }

            if (dirty && apply)
            {
                data.ApplyModifiedPropertiesWithoutUndo();
                changed.Add(prefab);
            }
        }

        private static string PathOf(Transform t, Transform stop)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != stop; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
