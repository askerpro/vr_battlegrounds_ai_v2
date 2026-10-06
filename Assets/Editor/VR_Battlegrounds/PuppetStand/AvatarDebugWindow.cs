using UltimateXR.Avatar.Controllers;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.DevTools.LegsCompare;

namespace VrBattlegrounds.Editor.PuppetStand
{
    /// <summary>
    /// <c>Tools/VR Battlegrounds/Debug/Avatar Debug Window</c> — окно отладки последнего выделенного аватара. Не пропадает,
    /// когда выделен манипулятор (PuppetBody, голова, кисти) или любой не-аватар: такое выделение пропускается.
    /// Строка стенда перемотки (<see cref="ClipScrubStand"/>): управление строкой (клип, отрезок, «Ближайший»),
    /// по разделам — поза сейчас, отличие от цепочки, в Play — ноги живого аватара и режим ног; цепочка — сворачиваемым
    /// разделом. Аватар с ногами UltimateXR — цепочка Legs_Crouch и клипы с весами (<see cref="LegsDebugPanel.Describe"/>).
    /// Обновляется 10 раз в секунду.
    /// </summary>
    public sealed class AvatarDebugWindow : EditorWindow
    {
        // Сериализуются — переживают перезагрузку домена при входе в Play; стенд находится заново.
        [SerializeField] private GameObject _target;
        [SerializeField] private bool _otherAvatar; // последним выделен аватар не со стенда перемотки
        [SerializeField] private bool _ownLegsOpen;
        private ClipScrubStand _stand;
        private Vector2 _scroll;

        private const float KeyWidth = 190f;

        [MenuItem("Tools/VR Battlegrounds/Debug/Avatar Debug Window")]
        public static void Open()
        {
            var window = GetWindow<AvatarDebugWindow>("Отладка аватара");
            window.OnSelectionChange();
        }

        private void OnSelectionChange()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null) return;

            foreach (ClipScrubStand stand in FindObjectsByType<ClipScrubStand>())
            {
                int row = stand.RowOf(go);
                if (row < 0) continue;
                _stand = stand;
                ClipScrubGui.SetInspectedRow(stand, row);
                _otherAvatar = false;
                _target = go;
                Repaint();
                return;
            }

            if (go.GetComponentInParent<UxrStandardAvatarController>() != null)
            {
                _otherAvatar = true;
                _target = go;
                Repaint();
            }
        }

        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            if (_stand == null) _stand = FindAnyObjectByType<ClipScrubStand>();
            int row = !_otherAvatar && _stand != null ? ClipScrubGui.InspectedRow(_stand) : -1;
            if (row < 0 && _target == null)
            {
                EditorGUILayout.HelpBox("Выдели аватар (строку стенда перемотки или любой аватар с ногами UltimateXR). Выделение манипулятора и других объектов окно пропускает — данные последнего аватара остаются.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (row >= 0) DrawRow(row);
            else if (_target != null) DrawAvatar(_target.GetComponentInParent<UxrStandardAvatarController>());
            EditorGUILayout.EndScrollView();
        }

        private void DrawRow(int row)
        {
            ClipScrubStand.RowDebug d = _stand.GetRowDebug(row);

            ClipScrubGui.ViewToolbar(_stand);
            if (row >= _stand.RowCount) return; // вид сменился — дорисуем в следующем кадре
            EditorGUILayout.LabelField(_stand.SavedView ? "Сохранённые комбинации — все в ряд; инспектируемая обведена рамкой" : "Инспектируемый аватар (одна строка; рамка в сцене)", EditorStyles.miniBoldLabel);
            ClipScrubGui.RowSelector(_stand);
            if (_stand.SavedView)
            {
                ClipScrubList.Combo combo = _stand.list.saved[row];
                using (new EditorGUILayout.HorizontalScope())
                {
                    string name = EditorGUILayout.TextField("имя комбинации", combo.label);
                    if (name != combo.label) ClipScrubGui.Change(_stand.list, () => combo.label = name);
                    if (GUILayout.Button("Удалить", GUILayout.Width(70)))
                    {
                        ClipScrubGui.Change(_stand.list, () => _stand.list.saved.RemoveAt(row));
                        return;
                    }
                }

                if (GUILayout.Button("Применить в игру — пересобрать контроллер ног (все аватары)")) ApplyToGame(row);
            }
            else if (GUILayout.Button("Сохранить комбинацию — цепочка, кандидат, кадры, отрезок, зона"))
            {
                ClipScrubGui.SaveCombo(_stand, row);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Выделить стенд")) Selection.activeGameObject = _stand.gameObject;
                if (GUILayout.Button("+ строки для новых клипов sit candidates")) ClipScrubGui.AddMissingSitRows(_stand.list);
            }

            // Общее: камера и управление по камере.
            Section("Камера");
            if (!float.IsNaN(d.Camera))
            {
                Row("камера", $"{d.Camera:0.00} м");
                Row("голова клипа: нужно / сейчас", $"{d.ClipHeadTarget:0.00} / {d.HeadNow:0.00} м");
            }

            Row("таз позы сейчас", $"{d.ClipHips:0.00} м");
            bool follow = EditorGUILayout.ToggleLeft("поза — по высоте камеры (иначе — ползунок просмотра кандидата)", _stand.followHead);
            if (follow != _stand.followHead) SetStand(() => _stand.followHead = follow);

            // Фазы сверху вниз: позы цепочки (общие для всех строк), затем кандидат строки — продолжение той же цепочки.
            Note("Фазы сверху вниз. Полоса — вес фазы и играющий кадр: вычисляется из высоты камеры, изменить нельзя. Под полосой — настройки фазы: кадр позы сдвигает её высоту головы — порог смешивания с соседними фазами.");
            int chainCount = _stand.ChainOf(row).Count;
            for (int k = 0; k < chainCount; k++)
            {
                PhaseHeader($"Фаза {k}: {(d.ChainLabels != null && k < d.ChainLabels.Length ? d.ChainLabels[k] : "поза")}", _stand.SavedView ? "своя у комбинации" : "общая для всех строк");
                if (d.Playing != null && k < d.Playing.Length) WeightRow(d.Playing[k]);
                if (ClipScrubGui.DrawChainPose(_stand, row, k)) return; // состав цепочки изменился — дорисуем в следующем кадре
            }

            if (GUILayout.Button("+ фаза цепочки (перед кандидатом)")) ClipScrubGui.Change(_stand.list, () => _stand.ChainOf(row).Add(new ClipScrubList.KeyPose()));

            PhaseHeader($"Фаза {chainCount}: {(_stand.SavedView ? (_stand.EntryOf(row).clip != null ? _stand.EntryOf(row).clip.name : "—") : d.Label)}", "кандидат этой строки");
            if (d.Playing != null && d.Playing.Length > 0) WeightRow(d.Playing[d.Playing.Length - 1]);
            if (!float.IsNaN(d.CandidateFromCamera)) Row("вступает", $"при камере ниже {d.CandidateFromCamera:0.00} м");
            float zone = EditorGUILayout.Slider(_stand.SavedView ? "зона смешивания перехода, м" : "зона смешивания перехода, м (общая)", _stand.ZoneOf(row), 0f, 0.5f);
            if (!Mathf.Approximately(zone, _stand.ZoneOf(row)))
            {
                if (_stand.SavedView) ClipScrubGui.Change(_stand.list, () => _stand.list.saved[row].blendZone = zone);
                else SetStand(() => _stand.transitionBlendZone = zone);
            }
            ClipScrubGui.DrawEntryControls(_stand, row, false);

            Section("Отличие позы сейчас от последней позы цепочки");
            if (d.HasDelta)
            {
                Row("ноги (RMS мышц, 0 — совпадают)", $"{d.Delta.Legs:0.000}");
                Row("таз", $"{d.Delta.HipsHeight * 100f:+0;-0;0} см");
                Row("корпус", $"{d.Delta.BodyAngle:0}°");
            }
            else
            {
                EditorGUILayout.LabelField("—");
            }

            Section("Play: живой аватар");
            var legs = (LiveLegsMode)EditorGUILayout.EnumPopup("ноги", _stand.liveLegs);
            if (legs != _stand.liveLegs) SetStand(() => _stand.liveLegs = legs);
            bool pelvis = EditorGUILayout.ToggleLeft("таз ног из клипа (CC_Base_Pelvis; план, в игре нет)", _stand.pelvisFromClip);
            if (pelvis != _stand.pelvisFromClip) SetStand(() => _stand.pelvisFromClip = pelvis);
            if (!d.HasLive)
            {
                EditorGUILayout.LabelField(Application.isPlaying ? "у строки нет живого аватара" : "живые аватары — только в Play", EditorStyles.miniLabel);
            }
            else
            {
                Row("таз BodyIK", $"{d.BodyIKHips:0.00} м   (клип {d.ClipHips:0.00} м, разница {(d.BodyIKHips - d.ClipHips) * 100f:+0;-0;0} см)");
                Row($"кость таза ног ({d.PelvisName ?? "нет"})", float.IsNaN(d.PelvisAngle) ? "—" : $"{d.PelvisAngle:0}° от клипа");
                LegRow("левая нога", d.Left);
                LegRow("правая нога", d.Right);
                if (Mathf.Abs(d.FloorOffset) > 0.001f) Row("пол стенда", $"{d.FloorOffset:+0.00;-0.00} м");

                GameObject live = _stand.LiveAvatarOf(row);
                UxrStandardAvatarController c = live != null ? live.GetComponent<UxrStandardAvatarController>() : null;
                if (c != null && c.AnimatedLegs != null)
                {
                    _ownLegsOpen = EditorGUILayout.Foldout(_ownLegsOpen, "Собственные ноги UltimateXR аватара (стенд перекрывает их после всех стадий)", true);
                    if (_ownLegsOpen) Lines(LegsDebugPanel.Describe(c));
                }
            }
        }

        /// <summary>
        /// Перенос сохранённой комбинации в игру: поза 1 цепочки — поза приседа (<c>Legs_Crouch</c> 1), позы цепочки ниже и
        /// кандидат (поза — один кадр, переход — 4 кадра от входа до самой низкой головы) — позы ниже приседа. Пишет ассет
        /// <c>AvatarLegsCrouchConfig</c> и пересобирает контроллер (генератор в общей редакторской сборке — через отражение).
        /// </summary>
        private void ApplyToGame(int row)
        {
            System.Collections.Generic.List<ClipScrubList.KeyPose> chain = _stand.ChainOf(row);
            if (chain.Count < 2 || chain[1].clip == null)
            {
                EditorUtility.DisplayDialog("Применить в игру", "В цепочке нет позы приседа (поза 1).", "OK");
                return;
            }

            var clips = new System.Collections.Generic.List<AnimationClip>();
            var frames = new System.Collections.Generic.List<float>();
            for (int k = 2; k < chain.Count; k++)
            {
                if (chain[k].clip == null) continue;
                clips.Add(chain[k].clip);
                frames.Add(chain[k].frame);
            }

            ClipScrubList.Entry e = _stand.EntryOf(row);
            if (e.clip != null)
            {
                foreach (float f in _stand.CandidateFramesForGame(row, 4))
                {
                    clips.Add(e.clip);
                    frames.Add(f);
                }
            }

            string below = string.Join(", ", System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0, clips.Count), i => $"{clips[i].name}@{frames[i]:0}"));
            string label = _stand.RowLabel(row);
            if (!EditorUtility.DisplayDialog("Применить в игру",
                    $"Комбинация «{label}».\n\nПрисед (Legs_Crouch 1): {chain[1].clip.name}@{chain[1].frame:0}\nНиже приседа: {(below.Length > 0 ? below : "—")}\n\n" +
                    "Стоя, повороты и ходьба не меняются. Контроллер ног общий — изменится у всех аватаров с ногами UltimateXR. Ассет AvatarLegsCrouch.asset под git — откат через него.",
                    "Применить", "Отмена"))
            {
                return;
            }

            System.Type type = System.Type.GetType("VrBattlegrounds.Editor.Avatars.AvatarLegsCrouchConfig, Assembly-CSharp-Editor");
            var apply = type?.GetMethod("Apply", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (apply == null)
            {
                EditorUtility.DisplayDialog("Применить в игру", "Не найден AvatarLegsCrouchConfig.Apply (редакторская сборка).", "OK");
                return;
            }

            string result = (string)apply.Invoke(null, new object[] { chain[1].clip, chain[1].frame, clips.ToArray(), frames.ToArray(), label });
            EditorUtility.DisplayDialog("Применить в игру", result, "OK");
        }

        private void SetStand(System.Action change)
        {
            Undo.RecordObject(_stand, "Clip Scrub Stand");
            change();
            EditorUtility.SetDirty(_stand);
        }

        private static void DrawAvatar(UxrStandardAvatarController c)
        {
            if (c == null)
            {
                EditorGUILayout.HelpBox("Аватар удалён.", MessageType.Warning);
                return;
            }

            Section(c.name);
            if (!Application.isPlaying)
            {
                EditorGUILayout.LabelField("ноги UltimateXR работают в Play", EditorStyles.miniLabel);
                return;
            }

            Lines(LegsDebugPanel.Describe(c));
        }

        /// <summary>Клип позы: полоса веса, роль, клип, кадр, высота головы позы.</summary>
        private static void WeightRow(ClipScrubStand.ActiveClip clip)
        {
            Rect r = EditorGUILayout.GetControlRect(false, 18f);
            string head = float.IsNaN(clip.HeadHeight) ? "" : $", голова {clip.HeadHeight:0.00} м";
            EditorGUI.ProgressBar(r, clip.Weight, $"{clip.Weight:P0}   {clip.Role}: {clip.ClipName}, кадр {clip.Frame:0.#}{head}");
        }

        /// <summary>Заголовок фазы: название и пометка (общая / кандидат строки), полоса-разделитель.</summary>
        private static void PhaseHeader(string title, string note)
        {
            EditorGUILayout.Space(8f);
            Rect line = GUILayoutUtility.GetRect(1f, 2f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(0.35f, 0.55f, 0.9f, 0.8f));
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(note, EditorStyles.miniLabel, GUILayout.Width(150));
            }
        }

        private static void Note(string text) => EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);

        private static void LegRow(string name, ClipScrubStand.LegDebug leg)
        {
            if (!leg.Valid)
            {
                Row(name, "—");
                return;
            }

            string kneeFromClip = float.IsNaN(leg.KneeFromClipCm) ? "" : $", от колена клипа {leg.KneeFromClipCm:0} см";
            Row(name, $"подошва {leg.SoleCm:+0;-0;0} см над полом, колено {leg.Knee:0.00} м{kneeFromClip}");
        }

        private static void Section(string title)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }

        private static void Row(string key, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(key, EditorStyles.miniLabel, GUILayout.Width(KeyWidth));
                EditorGUILayout.LabelField(value, EditorStyles.wordWrappedLabel);
            }
        }

        /// <summary>Многострочный текст — строками, без слияния в абзац.</summary>
        private static void Lines(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                if (line.Length > 0) EditorGUILayout.LabelField(line, EditorStyles.label);
            }
        }
    }
}
