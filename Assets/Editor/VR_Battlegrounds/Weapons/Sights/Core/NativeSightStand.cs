using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Editor.Gameplay.SightSplit;
using VrBattlegrounds.Weapons.Sights;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Стенд «Родной прицел»: копия префаба ствола с вшитым прицелом, разделённым <see cref="SightPartSplitter"/>
    /// (меши только в памяти, для Scar — подъём рамок). Содержимое префаба загружается во временную preview-сцену,
    /// разделяется, клонируется в сцену стенда и выгружается без сохранения: префаб и меш-ассеты не меняются.
    /// Владеет созданными в памяти мешами до <see cref="Dispose"/>. Решения пользователя 2026-10-12 («Только
    /// инструменты»): запись в префабы и таблицу — отдельная задача.
    /// </summary>
    public sealed class NativeSightStand : IDisposable
    {
        public SightSplitSpec Spec { get; private set; }
        public GameObject Weapon { get; private set; }
        public SightAdjustablePart Rear { get; private set; }
        public SightAdjustablePart Front { get; private set; }
        /// <summary>null — подвижной детали нет (PPK).</summary>
        public SightAdjustablePart Moving { get; private set; }
        public IReadOnlyList<string> Notes => _notes;

        private readonly List<string> _notes = new List<string>();
        private SightSplitApplyResult _split;

        /// <summary>Спецификация родного прицела для префаба ствола; null — ствол не в SightSplitTable.</summary>
        public static SightSplitSpec SpecFor(GameObject weaponPrefab)
        {
            string path = weaponPrefab != null ? AssetDatabase.GetAssetPath(weaponPrefab) : null;
            return string.IsNullOrEmpty(path) ? null : SightSplitTable.All.FirstOrDefault(s => s.PrefabPath == path);
        }

        /// <summary>Собирает стенд: копия под <paramref name="parent"/> (или корнем в <paramref name="scene"/>).</summary>
        public static NativeSightStand Build(SightSplitSpec spec, Scene scene, Transform parent = null)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Стенд — только в Edit Mode");
            var stand = new NativeSightStand { Spec = spec };
            GameObject contents = PrefabUtility.LoadPrefabContents(spec.PrefabPath);
            try
            {
                stand._split = SightPartSplitter.Apply(contents, spec);
                GameObject clone = Object.Instantiate(contents);
                clone.name = contents.name;
                if (parent != null) clone.transform.SetParent(parent, false);
                else SceneManager.MoveGameObjectToScene(clone, scene);
                stand.Weapon = clone;
                SightParts.Find(clone, out var rear, out var front, out var moving);
                stand.Rear = rear; stand.Front = front; stand.Moving = moving;
                stand._notes.AddRange(stand._split.Warnings);
                foreach (SightSplitPartSpec part in spec.Parts.Where(p => p.NodePath.StartsWith("Slide/", StringComparison.Ordinal)))
                    stand._notes.Add($"{(part.Role == SightPartRole.Rear ? "целик" : "мушка")} на подвижной части {part.NodePath}: затвор в позе покоя из префаба");
                foreach (SightNodeRaise raise in spec.Raises)
                    stand._notes.Add($"рамка {raise.NodePath} поднята в рабочее положение");
                return stand;
            }
            catch
            {
                stand.Dispose();
                throw;
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        public void Dispose()
        {
            if (Weapon != null) Object.DestroyImmediate(Weapon);
            Weapon = null;
            _split?.DestroyMeshes();
            _split = null;
        }
    }
}
