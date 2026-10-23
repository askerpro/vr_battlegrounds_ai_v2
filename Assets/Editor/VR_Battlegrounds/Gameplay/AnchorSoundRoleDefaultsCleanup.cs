using System.Collections.Generic;
using System.Linq;
using System.Text;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Снимает с якорей префабов проекта общую пару клипов, которую раньше раскладывал <see cref="AnchorSoundInstaller" />
    /// (<see cref="AnchorSoundInstaller.PlaceClip" /> — вставка, <see cref="AnchorSoundInstaller.TakeClip" /> — доставание),
    /// чтобы якорь звучал дефолтом своей роли из <see cref="MagazineAnchorSoundDefaults" />. Клип снимается, только
    /// если у роли задан дефолт этого же звука, иначе якорь остался бы без звука или с общим щелчком магазина.
    ///
    /// <para>
    /// По состоянию на 2026-10-23 пара лежит в пяти префабах: слот арсенала <c>Arsenal/Slots/FireArmSlotPrefab</c>
    /// (<c>WeaponAnchor</c>, <c>MagAnchor</c>), карманы <c>Player/Equipment/Pockets/Anchor_Back</c>,
    /// <c>Anchor_Hip_R</c>, <c>MagazinePocket</c> и распакованные копии этих трёх карманов в
    /// <c>Player/Avatars/CyborgHands/Cyborg/PlayerControllersCyborgAvatar</c> (зона avatar-ik). Аватары MEF вкладывают
    /// карманы префабами и наследуют очистку. Сцены не правятся (см. <see cref="AnchorSoundInstaller" />).
    /// </para>
    ///
    /// <para>
    /// Вызов — из кода (меню нет): сначала <see cref="DryRun" />, отчёт, затем <see cref="Apply" />. Порядок
    /// префабов не важен: правка базового префаба доходит до вложенных экземпляров без переопределения. Чужие клипы
    /// (свой звук гнезда, жетона, слота) не трогаются: снимается только точное совпадение с парой установщика.
    /// </para>
    /// </summary>
    public static class AnchorSoundRoleDefaultsCleanup
    {
        private const string Prefabs = "Assets/Prefabs";

        /// <summary>Что будет (или было) сделано с одним клипом одного якоря.</summary>
        public enum Action
        {
            /// <summary>Клип снят (или будет снят): якорь звучит дефолтом роли.</summary>
            Clear,
            /// <summary>У роли нет дефолта этого звука — клип оставлен.</summary>
            KeepNoRoleDefault,
            /// <summary>Якорь из вложенного префаба проекта с переопределением клипа у родителя — снять вручную.</summary>
            KeepInstanceOverride
        }

        public readonly struct Entry
        {
            public readonly string PrefabPath;
            public readonly string AnchorPath;
            public readonly AnchorRoleKind Role;
            public readonly bool TakeOut;
            public readonly Action Action;

            public Entry(string prefabPath, string anchorPath, AnchorRoleKind role, bool takeOut, Action action)
            {
                PrefabPath = prefabPath; AnchorPath = anchorPath; Role = role; TakeOut = takeOut; Action = action;
            }

            public override string ToString() =>
                $"{Action,-20} {(TakeOut ? "доставание" : "вставка   ")} роль {Role,-14} {PrefabPath} :: {AnchorPath}";
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
                text.AppendLine($"[AnchorSoundRoleDefaultsCleanup] {(Applied ? "применено" : "сухой прогон")}: " +
                                $"снять {Count(Action.Clear)}, оставить без дефолта роли {Count(Action.KeepNoRoleDefault)}, " +
                                $"переопределения экземпляров {Count(Action.KeepInstanceOverride)}; префабов сохранено {SavedPrefabs.Count}.");
                foreach (Entry entry in Entries) text.AppendLine("  " + entry);
                return text.ToString();
            }
        }

        /// <summary>Отчёт без изменений: какие клипы будут сняты и какие останутся (нет дефолта роли).</summary>
        public static Report DryRun() => Run(false);

        /// <summary>Снимает клипы пары установщика там, где у роли есть дефолт; пересохраняет только изменённые префабы.</summary>
        public static Report Apply() => Run(true);

        private static Report Run(bool apply)
        {
            var report = new Report { Applied = apply };
            MagazineAnchorSoundDefaults defaults = MagazineAnchorSoundDefaults.Instance;
            var place = AssetDatabase.LoadAssetAtPath<AudioClip>(AnchorSoundInstaller.PlaceClip);
            var take = AssetDatabase.LoadAssetAtPath<AudioClip>(AnchorSoundInstaller.TakeClip);
            if (defaults == null || place == null && take == null)
            {
                GameLog.Error($"[AnchorSoundRoleDefaultsCleanup] Нет Resources/MagazineAnchorSoundDefaults.asset или клипов пары " +
                              $"{AnchorSoundInstaller.PlaceClip} / {AnchorSoundInstaller.TakeClip} — нечего сравнивать.");
                return report;
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { Prefabs }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;

                // Правка полей прямо в ассете и SavePrefabAsset, без LoadPrefabContents/SaveAsPrefabAsset: иерархия не
                // пересобирается, и у аватара (PlayerControllersCyborgAvatar) не перегенерируются UxrUniqueId и _assetId
                // (так портился GhostAvatar.prefab). Меняются только ссылки на клипы.
                if (Inspect(asset, path, defaults, place, take, apply ? ClearClip : null, report.Entries) && apply)
                {
                    PrefabUtility.SavePrefabAsset(asset);
                    report.SavedPrefabs.Add(path);
                }
            }

            GameLog.Debug.Info(report.ToString());
            return report;
        }

        /// <summary>
        /// Находит клипы пары установщика на якорях <paramref name="root" />; с <paramref name="clear" /> — снимает те,
        /// у роли которых есть дефолт. Возвращает, есть ли что снять.
        /// </summary>
        private static bool Inspect(GameObject root, string path, MagazineAnchorSoundDefaults defaults, AudioClip place, AudioClip take,
                                    System.Action<AnchorSound, bool> clear, List<Entry> entries)
        {
            bool any = false;
            foreach (UxrGrabbableObjectAnchor anchor in root.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                AnchorSound sound = anchor.GetComponent<AnchorSound>();
                if (sound == null) continue;

                AnchorRoleKind role = AnchorRole.Get(anchor);
                bool owned = AnchorSoundInstaller.OwnedBy(anchor, path);
                var settings = new SerializedObject(sound);
                bool placeMatch = place != null && sound.Source != null && sound.Source.clip == place;
                bool takeMatch = take != null && settings.FindProperty("_takeOutClip").objectReferenceValue == take;

                if (placeMatch) any |= Decide(sound, false, role, owned, defaults.RolePlaceClip(role) != null, path, root, clear, entries);
                if (takeMatch) any |= Decide(sound, true, role, owned, defaults.RoleTakeOutClip(role) != null, path, root, clear, entries);
            }
            return any;
        }

        private static bool Decide(AnchorSound sound, bool takeOut, AnchorRoleKind role, bool owned, bool hasRoleDefault, string path,
                                   GameObject root, System.Action<AnchorSound, bool> clear, List<Entry> entries)
        {
            string anchorPath = PathOf(sound.transform, root.transform);
            if (!hasRoleDefault)
            {
                entries.Add(new Entry(path, anchorPath, role, takeOut, Action.KeepNoRoleDefault));
                return false;
            }

            if (!owned)
            {
                // Якорь вложенного префаба проекта чистится в своём префабе. Здесь он попадает в отчёт, только если
                // клип переопределён у родителя: иначе он унаследует очистку.
                if (IsOverridden(sound, takeOut)) entries.Add(new Entry(path, anchorPath, role, takeOut, Action.KeepInstanceOverride));
                return false;
            }

            clear?.Invoke(sound, takeOut);
            entries.Add(new Entry(path, anchorPath, role, takeOut, Action.Clear));
            return true;
        }

        private static void ClearClip(AnchorSound sound, bool takeOut)
        {
            if (!takeOut)
            {
                sound.Source.clip = null;
                EditorUtility.SetDirty(sound.Source);
                return;
            }

            var settings = new SerializedObject(sound);
            settings.FindProperty("_takeOutClip").objectReferenceValue = null;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool IsOverridden(AnchorSound sound, bool takeOut)
        {
            if (takeOut) return new SerializedObject(sound).FindProperty("_takeOutClip").prefabOverride;
            // Unity 6 хранит clip источника в m_Resource, прежние версии — в m_audioClip.
            var source = new SerializedObject(sound.Source);
            SerializedProperty clip = source.FindProperty("m_Resource") ?? source.FindProperty("m_audioClip");
            return clip != null && clip.prefabOverride;
        }

        private static string PathOf(Transform t, Transform stop)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null && p != stop; p = p.parent) path = p.name + "/" + path;
            return path;
        }
    }
}
