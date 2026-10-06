using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.LegsCompare;

namespace VrBattlegrounds.Editor.PuppetStand
{
    /// <summary>
    /// Общее рисование стенда перемотки (<see cref="ClipScrubStand"/>) для его инспектора и окна «Отладка аватара»:
    /// цепочка поз аватара и управление строкой кандидата (клип, кадр входа, отрезок, «Ближайший»). Правки — в ассет
    /// списка с Undo.
    /// </summary>
    public static class ClipScrubGui
    {
        private const string InspectedRowKey = "VrBattlegrounds.ClipScrub.InspectedRow";

        private static readonly Dictionary<string, AnimationClip[]> s_folderClips = new Dictionary<string, AnimationClip[]>();

        static ClipScrubGui()
        {
            EditorApplication.projectChanged += () => s_folderClips.Clear(); // скачали/удалили клип — список папки заново
        }

        /// <summary>Клипы папки: главный клип каждой модели (FBX) и отдельные .anim, по имени.</summary>
        public static AnimationClip[] ClipsIn(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return new AnimationClip[0];
            if (s_folderClips.TryGetValue(folder, out AnimationClip[] cached)) return cached;
            var clips = new List<AnimationClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip != null && !clips.Contains(clip)) clips.Add(clip);
            }

            AnimationClip[] result = clips.OrderBy(c => c.name).ToArray();
            s_folderClips[folder] = result;
            return result;
        }

        /// <summary>
        /// Выпадающий список клипов папки рядом с полем клипа: выбор — новый клип. Текущий клип не из папки — первым пунктом
        /// «(свой) имя».
        /// </summary>
        public static AnimationClip FolderPopup(string title, AnimationClip current, string folder)
        {
            AnimationClip[] clips = ClipsIn(folder);
            bool inFolder = current != null && clips.Contains(current);
            var names = new List<string>();
            if (!inFolder) names.Add(current != null ? $"(свой) {current.name}" : "(нет клипа)");
            names.AddRange(clips.Select(c => c.name));
            int index = inFolder ? System.Array.IndexOf(clips, current) : 0;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, GUILayout.Width(110));
                if (clips.Length == 0)
                {
                    EditorGUILayout.LabelField($"папка пуста: {Path.GetFileName(folder)}", EditorStyles.miniLabel);
                    return current;
                }

                int chosen = EditorGUILayout.Popup(index, names.ToArray());
                if (chosen == index) return current;
                int clipIndex = inFolder ? chosen : chosen - 1;
                return clipIndex >= 0 ? clips[clipIndex] : current;
            }
        }

        /// <summary>Строки для клипов папки сидения, которых ещё нет в списке. Возвращает число добавленных.</summary>
        public static int AddMissingSitRows(ClipScrubList list)
        {
            var missing = ClipsIn(list.sitCandidatesFolder).Where(c => !list.entries.Any(e => e.clip == c)).ToList();
            if (missing.Count == 0) return 0;
            Change(list, () =>
            {
                foreach (AnimationClip clip in missing) list.entries.Add(new ClipScrubList.Entry { label = clip.name, clip = clip });
            });
            return missing.Count;
        }

        /// <summary>
        /// Инспектируемая строка стенда — одна на стенд и окно «Отладка аватара» (SessionState редактора, в сцену не пишется);
        /// всегда в пределах списка (нет выделения — строка 0).
        /// </summary>
        public static int InspectedRow(ClipScrubStand stand)
        {
            int count = stand != null ? stand.RowCount : 0;
            int row = count == 0 ? -1 : Mathf.Clamp(SessionState.GetInt(InspectedRowKey, 0), 0, count - 1);
            if (stand != null) stand.InspectedRow = row;
            return row;
        }

        public static void SetInspectedRow(ClipScrubStand stand, int row)
        {
            SessionState.SetInt(InspectedRowKey, row);
            if (stand != null) stand.InspectedRow = row;
            SceneView.RepaintAll();
        }

        /// <summary>Выбор инспектируемой строки: ◀ список ▶.</summary>
        public static int RowSelector(ClipScrubStand stand)
        {
            int row = InspectedRow(stand);
            if (row < 0) return row;
            var names = new string[stand.RowCount];
            for (int i = 0; i < names.Length; i++) names[i] = $"{i}: {stand.RowLabel(i)}";

            using (new EditorGUILayout.HorizontalScope())
            {
                int next = row;
                if (GUILayout.Button("◀", GUILayout.Width(26)) && row > 0) next = row - 1;
                next = EditorGUILayout.Popup(next, names);
                if (GUILayout.Button("▶", GUILayout.Width(26)) && row + 1 < names.Length) next = row + 1;
                if (next != row) SetInspectedRow(stand, row = next);
            }

            return row;
        }

        /// <summary>
        /// Управление строкой: подпись, клип, отрезок клипа (ползунок с двумя ручками), «Ближайший» — верх отрезка к
        /// последней позе цепочки, вход (вычисляется), без следования за камерой — кадр просмотра в отрезке. true — строка удалена.
        /// </summary>
        public static bool DrawEntryControls(ClipScrubStand stand, int i, bool allowRemove)
        {
            ClipScrubList list = stand.list;
            ClipScrubList.Entry e = stand.EntryOf(i);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(i.ToString(), GUILayout.Width(24));
            string label = EditorGUILayout.TextField(e.label);
            AnimationClip clip = (AnimationClip)EditorGUILayout.ObjectField(e.clip, typeof(AnimationClip), false);
            if (allowRemove && GUILayout.Button("✕", GUILayout.Width(22)))
            {
                int removed = i;
                if (stand.SavedView) Change(list, () => list.saved.RemoveAt(removed));
                else Change(list, () => list.entries.RemoveAt(removed));
                EditorGUILayout.EndHorizontal();
                return true;
            }

            EditorGUILayout.EndHorizontal();
            clip = FolderPopup("из sit candidates", clip, list.sitCandidatesFolder);

            float last = ClipScrubStand.LastFrame(clip);
            float from = Mathf.Clamp(e.rangeFrom, 0f, last);
            float to = e.rangeTo > 0f ? Mathf.Clamp(e.rangeTo, 0f, last) : last;
            if (to < from) (from, to) = (to, from);
            float frame = e.frame;

            int mode = GUILayout.Toolbar(e.poseBlend ? 1 : 0, new[] { "переход по кадрам", "поза: смешивание с цепочкой (как игра)" });
            bool poseBlend = mode == 1;

            if (poseBlend)
            {
                // Поза — неподвижный кадр клипа; вес от последней позы цепочки до неё — по высоте головы (blend tree игры).
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("кадр позы", GUILayout.Width(70));
                if (GUILayout.Button("◀", GUILayout.Width(26))) frame = Mathf.Max(0f, Mathf.Round(frame) - 1f);
                frame = EditorGUILayout.Slider(frame, 0f, Mathf.Max(last, 1f));
                if (GUILayout.Button("▶", GUILayout.Width(26))) frame = Mathf.Min(last, Mathf.Round(frame) + 1f);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("вес позы растёт от 0 на высоте головы последней позы цепочки до 1 на высоте головы этой позы", EditorStyles.wordWrappedMiniLabel);
            }
            else
            {
                // Отрезок клипа — один ползунок с двумя ручками: всё между ними — клип кандидата.
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("отрезок", GUILayout.Width(56));
                from = Mathf.Round(EditorGUILayout.FloatField(from, GUILayout.Width(44)));
                EditorGUILayout.MinMaxSlider(ref from, ref to, 0f, Mathf.Max(last, 1f));
                to = Mathf.Round(EditorGUILayout.FloatField(to, GUILayout.Width(44)));
                from = Mathf.Round(Mathf.Clamp(from, 0f, last));
                to = Mathf.Round(Mathf.Clamp(to, from, last));
                if (GUILayout.Button("Ближайший", GUILayout.Width(80)) && stand.TryFindNearestFrame(i, out float best, out ClipScrubStand.PoseDelta found))
                {
                    // Верх отрезка (конец с высокой головой — стык с цепочкой) — на кадр, ближайший к последней позе цепочки.
                    bool topIsStart = stand.TopIsRangeStart(i);
                    Change(list, () =>
                    {
                        if (topIsStart) e.rangeFrom = best;
                        else e.rangeTo = best;
                        if (e.rangeTo > 0f && e.rangeTo < e.rangeFrom) (e.rangeFrom, e.rangeTo) = (e.rangeTo, e.rangeFrom);
                    });
                    GameLog.Debug.Info($"[ClipScrubStand] {i}: верх отрезка — кадр {best} (ноги Δ {found.Legs:0.000}, таз {found.HipsHeight * 100f:+0;-0;0} см, корпус {found.BodyAngle:0}°)");
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField($"вход (кадр отрезка с самой высокой головой): {stand.EntryFrame(i):0}; вес набирается за {stand.transitionBlendZone:0.00} м ниже цепочки", EditorStyles.miniLabel);

                if (!stand.followHead || stand.head == null)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("просмотр", GUILayout.Width(56));
                    if (GUILayout.Button("◀", GUILayout.Width(26))) frame = Mathf.Max(from, Mathf.Round(frame) - 1f);
                    frame = EditorGUILayout.Slider(Mathf.Clamp(frame, from, to), from, Mathf.Max(to, from + 1f));
                    if (GUILayout.Button("▶", GUILayout.Width(26))) frame = Mathf.Min(to, Mathf.Round(frame) + 1f);
                    EditorGUILayout.EndHorizontal();
                }
            }

            float storedTo = Mathf.Approximately(to, last) && e.rangeTo <= 0f ? 0f : to;
            if (label != e.label || clip != e.clip || poseBlend != e.poseBlend || !Mathf.Approximately(frame, e.frame) ||
                !Mathf.Approximately(from, e.rangeFrom) || !Mathf.Approximately(storedTo, e.rangeTo))
            {
                // Новый клип — подпись по нему (старая «текущее сидение» и т.п. вводила бы в заблуждение).
                if (clip != e.clip) label = clip != null ? clip.name : "";
                Change(list, () =>
                {
                    e.poseBlend = poseBlend;
                    e.label = label;
                    e.clip = clip;
                    e.frame = Mathf.Clamp(frame, 0f, ClipScrubStand.LastFrame(clip));
                    e.rangeFrom = from;
                    e.rangeTo = storedTo;
                });
            }

            return false;
        }

        /// <summary>Цепочка поз строки сверху вниз: название, клип, кадр позы; добавить, убрать, сдвинуть.</summary>
        public static void DrawChain(ClipScrubStand stand, int row)
        {
            List<ClipScrubList.KeyPose> chain = stand.ChainOf(row);
            for (int k = 0; k < chain.Count; k++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                bool changed = DrawChainPose(stand, row, k);
                EditorGUILayout.EndVertical();
                if (changed) break;
            }

            if (GUILayout.Button("+ Поза цепочки (внизу)")) Change(stand.list, () => chain.Add(new ClipScrubList.KeyPose()));
        }

        /// <summary>
        /// Одна поза цепочки строки (подбор — общая, сохранённые — своя у комбинации): название, клип, ▲▼✕, кадр позы.
        /// true — состав цепочки изменился (сдвиг или удаление) — рисование прервать до следующего кадра.
        /// </summary>
        public static bool DrawChainPose(ClipScrubStand stand, int row, int k)
        {
            ClipScrubList list = stand.list;
            List<ClipScrubList.KeyPose> chain = stand.ChainOf(row);
            ClipScrubList.KeyPose pose = chain[k];
            EditorGUILayout.BeginHorizontal();
            string label = EditorGUILayout.TextField(pose.label);
            AnimationClip clip = (AnimationClip)EditorGUILayout.ObjectField(pose.clip, typeof(AnimationClip), false);
            int index = k;
            bool structure = false;
            if (GUILayout.Button("▲", GUILayout.Width(22)) && k > 0) { Change(list, () => Swap(chain, index, index - 1)); structure = true; }
            if (!structure && GUILayout.Button("▼", GUILayout.Width(22)) && k + 1 < chain.Count) { Change(list, () => Swap(chain, index, index + 1)); structure = true; }
            if (!structure && GUILayout.Button("✕", GUILayout.Width(22))) { Change(list, () => chain.RemoveAt(index)); structure = true; }
            EditorGUILayout.EndHorizontal();
            if (structure) return true;
            if (k > 0) clip = FolderPopup("из crouch candidates", clip, list.crouchCandidatesFolder);

            float frame = EditorGUILayout.Slider("кадр позы", pose.frame, 0f, Mathf.Max(ClipScrubStand.LastFrame(clip), 1f));
            if (label != pose.label || clip != pose.clip || !Mathf.Approximately(frame, pose.frame))
            {
                Change(list, () =>
                {
                    pose.label = label;
                    pose.clip = clip;
                    pose.frame = Mathf.Clamp(frame, 0f, ClipScrubStand.LastFrame(clip));
                });
            }

            return false;
        }

        private static void Swap(List<ClipScrubList.KeyPose> chain, int a, int b) => (chain[a], chain[b]) = (chain[b], chain[a]);

        /// <summary>
        /// Сохранить комбинацию строки подбора: снимок цепочки (клипы, кадры), кандидата (клип, режим, отрезок, кадр позы)
        /// и зоны смешивания — в сохранённые. Возвращает номер комбинации.
        /// </summary>
        public static int SaveCombo(ClipScrubStand stand, int row)
        {
            ClipScrubList list = stand.list;
            var combo = new ClipScrubList.Combo
            {
                chain = stand.ChainOf(row).Select(ClipScrubList.Copy).ToList(),
                entry = ClipScrubList.Copy(stand.EntryOf(row)),
                blendZone = stand.ZoneOf(row),
            };
            ClipScrubList.KeyPose last = combo.chain.Count > 0 ? combo.chain[combo.chain.Count - 1] : null;
            string mid = last != null && last.clip != null ? $"{last.clip.name}@{last.frame:0}" : "—";
            string cand = combo.entry.clip != null ? combo.entry.clip.name : "—";
            string how = combo.entry.poseBlend ? $"поза @{combo.entry.frame:0}" : $"{combo.entry.rangeFrom:0}–{(combo.entry.rangeTo > 0f ? combo.entry.rangeTo.ToString("0") : "конец")}";
            combo.label = $"{mid} → {cand} {how}";
            Change(list, () => list.saved.Add(combo));
            GameLog.Debug.Info($"[ClipScrubStand] Сохранена комбинация {list.saved.Count - 1}: {combo.label}");
            return list.saved.Count - 1;
        }

        /// <summary>Вид стенда: подбор / сохранённые (число). Смена вида — пересборка стенда.</summary>
        public static void ViewToolbar(ClipScrubStand stand)
        {
            int view = GUILayout.Toolbar((int)stand.view, new[] { "Подбор", $"Сохранённые ({stand.list.saved.Count})" });
            if (view == (int)stand.view) return;
            Undo.RecordObject(stand, "Clip Scrub View");
            stand.view = (ClipScrubView)view;
            EditorUtility.SetDirty(stand);
            SetInspectedRow(stand, 0);
        }

        public static void Change(ClipScrubList list, System.Action change)
        {
            Undo.RecordObject(list, "Clip Scrub");
            change();
            EditorUtility.SetDirty(list);
        }
    }
}
