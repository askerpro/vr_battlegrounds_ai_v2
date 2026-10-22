using System;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Editor.Manipulation.HandPoses;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.Editor.GripAuthoring.Analysis
{
    /// <summary>
    /// Режим «Анализ» панели «Автоснап и IK конечных фаланг»: переключатель, колонка результатов,
    /// пересчёт по сигнатуре в <see cref="EditorApplication.update"/> и пробы в SceneView.
    /// Только читает состояние; кости, позы и ассеты не меняет.
    /// </summary>
    internal static class GripAnalysisPanel
    {
        const double MinIntervalSec = 1.0 / 30.0;
        const double ResolveRefreshSec = 0.5;

        static Func<GripAnalysisHost> _hostProvider;
        static Func<UxrGrabbableObject, UxrAvatar, int, UxrHandSide, Transform> _resolveSnap;
        static Func<UxrAvatar, UxrHandSide, UxrGrabber> _resolveGrabber;

        static readonly GripAnalysisEngine Engine = new GripAnalysisEngine();
        static GripAnalysisReport _report;
        static ulong _lastSignature;
        static bool _hasSignature;
        static double _lastComputeTime;
        static bool _updateSubscribed;

        static ulong _resolveKey;
        static double _resolveTime = double.NegativeInfinity;
        static Transform _snap;
        static UxrGrabber _grabber;

        static float _columnTop;
        static GUIStyle _sectionStyle;
        static Vector2 _scroll;

        static GUIStyle _badgeStyle;
        static GUIStyle _detailStyle;
        static GUIStyle _summaryStyle;

        public static bool IsEnabled => GripAnalysisSettings.AnalysisEnabled;

        /// <summary>Подключение к панели автоснапа: поставщик контекста и её же резолверы align/grabber.</summary>
        public static void Bind(
            Func<GripAnalysisHost> hostProvider,
            Func<UxrGrabbableObject, UxrAvatar, int, UxrHandSide, Transform> resolveSnap,
            Func<UxrAvatar, UxrHandSide, UxrGrabber> resolveGrabber)
        {
            _hostProvider = hostProvider;
            _resolveSnap = resolveSnap;
            _resolveGrabber = resolveGrabber;
            AssemblyReloadEvents.beforeAssemblyReload += Unsubscribe;
            if (IsEnabled)
            {
                Subscribe();
            }
        }

        static void Subscribe()
        {
            if (_updateSubscribed)
            {
                return;
            }

            EditorApplication.update += Tick;
            _updateSubscribed = true;
        }

        static void Unsubscribe()
        {
            if (!_updateSubscribed)
            {
                return;
            }

            EditorApplication.update -= Tick;
            _updateSubscribed = false;
        }

        static void Invalidate()
        {
            _hasSignature = false;
        }

        // ---------------------------------------------------------------- Пересчёт

        static void Tick()
        {
            if (!IsEnabled)
            {
                Unsubscribe();
                return;
            }

            if (_hostProvider == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return;
            }

            GripAnalysisHost host = _hostProvider();
            if (host.Window == null)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            ResolveTargets(host, now);

            ulong signature = ComputeSignature(host);
            if (_hasSignature && signature == _lastSignature)
            {
                return;
            }

            // Не чаще ~30 раз/с; если расчёт тяжёлый — не дольше половины времени редактора
            double interval = Math.Max(MinIntervalSec, (_report != null ? _report.ElapsedMs : 0.0) * 2e-3);
            if (now - _lastComputeTime < interval)
            {
                return;
            }

            GripHandProbeProfile profile = host.Avatar != null ? GripHandProbeProfiles.Find(host.Avatar) : null;
            _report = Engine.Compute(host, _snap, _grabber, GripPalmMarking.GetOverride(host, profile), GripRingEditing.GetSettings(host, profile));
            _lastSignature = signature;
            _hasSignature = true;
            _lastComputeTime = now;

            host.Window.Repaint();
            if (GripAnalysisSettings.ShowSceneProbes)
            {
                SceneView.RepaintAll();
            }
        }

        static void ResolveTargets(GripAnalysisHost host, double now)
        {
            var key = new GripHash();
            key.Add(host.Avatar != null ? host.Avatar.GetHashCode() : 0);
            key.Add(host.Grabbable != null ? host.Grabbable.GetHashCode() : 0);
            key.Add(host.GrabPoint);
            key.Add((int)host.Side);

            bool lost = (_snap is object && _snap == null) || (_grabber is object && _grabber == null);
            if (!lost && key.Value == _resolveKey && now - _resolveTime < ResolveRefreshSec)
            {
                return;
            }

            _resolveKey = key.Value;
            _resolveTime = now;
            _snap = host.Grabbable != null && _resolveSnap != null ? _resolveSnap(host.Grabbable, host.Avatar, host.GrabPoint, host.Side) : null;
            _grabber = host.Avatar != null && _resolveGrabber != null ? _resolveGrabber(host.Avatar, host.Side) : null;
        }

        /// <summary>Хеш поворотов костей пальцев выбранной кисти, матриц align/grabber/предмета/запястья, позы, стороны и настроек.</summary>
        static ulong ComputeSignature(GripAnalysisHost host)
        {
            var hash = new GripHash();
            hash.Add(GripAnalysisSettings.Version);
            hash.Add(host.Avatar != null ? host.Avatar.GetHashCode() : 0);
            hash.Add(host.Pose != null ? host.Pose.GetHashCode() : 0);
            hash.Add(host.Grabbable != null ? host.Grabbable.GetHashCode() : 0);
            hash.Add(host.GrabPoint);
            hash.Add((int)host.Side);

            hash.Add(_snap != null ? _snap.GetHashCode() : 0);
            if (_snap != null)
            {
                hash.Add(_snap.localToWorldMatrix);
            }

            hash.Add(_grabber != null ? _grabber.GetHashCode() : 0);
            if (_grabber != null)
            {
                hash.Add(_grabber.transform.localToWorldMatrix);
            }

            if (host.Grabbable != null)
            {
                hash.Add(host.Grabbable.transform.localToWorldMatrix);
            }

            UxrAvatarHand hand = host.Avatar != null ? host.Avatar.GetHand(host.Side) : null;
            if (hand != null && hand.Wrist != null)
            {
                hash.Add(hand.Wrist.localToWorldMatrix);
                foreach (UxrFingerType fingerType in GripAnalysisEngine.FingerTypes)
                {
                    UxrAvatarFinger finger = hand.GetFinger(fingerType);
                    if (finger == null)
                    {
                        hash.Add(-1);
                        continue;
                    }

                    AddBone(ref hash, finger.Metacarpal);
                    AddBone(ref hash, finger.Proximal);
                    AddBone(ref hash, finger.Intermediate);
                    AddBone(ref hash, finger.Distal);
                }
            }

            // Ручная рамка ладони: режим, черновик разметки и профиль (ревизия и значения — Undo тоже меняет сигнатуру)
            hash.Add(GripPalmMarking.Version);
            hash.Add(GripPalmMarking.Active ? 1 : 0);
            hash.Add(GripPalmMarking.HasDraft ? 1 : 0);
            GripHandProbeProfile profile = host.Avatar != null ? GripHandProbeProfiles.Find(host.Avatar) : null;
            hash.Add(profile != null ? profile.GetHashCode() : 0);
            if (profile != null)
            {
                hash.Add(profile.Revision);
                GripPalmProbe palm = profile.Get(host.Side).Palm;
                hash.Add(palm.ManuallySet ? 1 : 0);
                hash.Add(palm.PositionWrist);
                hash.Add(palm.RotationWrist);
                float[] rings = profile.Get(host.Side).GetRingsCopy();
                foreach (float t in rings)
                {
                    hash.Add(t);
                }

                foreach (float radius in profile.Get(host.Side).GetRingRadiiRawCopyMm())
                {
                    hash.Add(radius);
                }

                foreach (Vector2 tilt in profile.Get(host.Side).GetRingTiltsCopyDeg())
                {
                    hash.Add(tilt.x);
                    hash.Add(tilt.y);
                }

                foreach (float azimuth in profile.Get(host.Side).GetRingAzimuthsCopyDeg())
                {
                    hash.Add(azimuth);
                }

                foreach (Vector2 offset in profile.Get(host.Side).GetRingOffsetsCopyMm())
                {
                    hash.Add(offset.x);
                    hash.Add(offset.y);
                }
            }

            // Черновик сдвига колец (во время перетаскивания ручки)
            hash.Add(GripRingEditing.Version);

            return hash.Value;
        }

        static void AddBone(ref GripHash hash, Transform bone)
        {
            if (bone == null)
            {
                hash.Add(0);
                return;
            }

            hash.Add(bone.localRotation);
        }

        // ---------------------------------------------------------------- Окно

        /// <summary>Строка переключателя внутри панели автоснапа.</summary>
        public static void DrawToggle(UxrHandPoseEditorWindow window)
        {
            bool enabled = IsEnabled;
            bool newEnabled = EditorGUILayout.ToggleLeft(
                new GUIContent("Анализ (в реальном времени)", "Показывает проверки качества хвата и пересчитывает их при движении пальцев. Только чтение."),
                enabled);
            if (newEnabled != enabled)
            {
                SetEnabled(window, newEnabled);
            }
        }

        static void SetEnabled(UxrHandPoseEditorWindow window, bool enabled)
        {
            GripAnalysisSettings.AnalysisEnabled = enabled;
            if (enabled)
            {
                Invalidate();
                Subscribe();
            }
            else
            {
                Unsubscribe();
                _report = null;
            }

            window.Repaint();
            SceneView.RepaintAll();
        }

        static void EnsureStyles()
        {
            if (_badgeStyle == null)
            {
                _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
                _badgeStyle.normal.textColor = Color.white;
            }

            if (_detailStyle == null)
            {
                _detailStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true,
                    richText = false
                };
            }

            if (_sectionStyle == null)
            {
                _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    wordWrap = true
                };
            }

            if (_summaryStyle == null)
            {
                _summaryStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true
                };
            }
        }

        /// <summary>
        /// Блок анализа под панелью автоснапа: раздел настроек кисти (профиль) и раздел проверок хвата.
        /// Свой ScrollView занимает остаток высоты окна.
        /// </summary>
        public static void DrawColumn(UxrHandPoseEditorWindow window)
        {
            EnsureStyles();
            if (Event.current.type == EventType.Repaint)
            {
                // Низ панели автоснапа в координатах области диагностики окна
                _columnTop = GUILayoutUtility.GetLastRect().yMax;
            }

            float height = Mathf.Max(220f, window.position.height - _columnTop - 16f);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(height));
            DrawHandSettingsSection();
            EditorGUILayout.Space(4);
            DrawChecksSection();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Раздел «Настройки кисти»: профиль кисти, общий для всех хватов (ладонь, кольца фаланг).</summary>
        static void DrawHandSettingsSection()
        {
            GripAnalysisHost host = _hostProvider != null ? _hostProvider() : default;
            string baseName = "—";
            if (host.Avatar != null)
            {
                GripHandProbeProfile profile = GripHandProbeProfiles.Find(host.Avatar);
                if (profile != null)
                {
                    baseName = profile.AvatarName;
                }
                else if (GripHandProbeProfiles.TryGetNewProfileTarget(host.Avatar, out _, out string targetName, out _))
                {
                    baseName = targetName;
                }
            }

            // Отличается от проверок: синеватая рамка-фон
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.62f, 0.8f, 1f, 1f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUI.backgroundColor = previous;
                bool expanded = EditorGUILayout.Foldout(GripAnalysisSettings.HandSettingsExpanded,
                    new GUIContent($"Настройки кисти — профиль {baseName} (общие для всех хватов)"), true);
                if (expanded != GripAnalysisSettings.HandSettingsExpanded)
                {
                    GripAnalysisSettings.HandSettingsExpanded = expanded;
                }

                if (expanded)
                {
                    DrawPalmBlock();
                    DrawRingsBlock();
                }
            }

            GUI.backgroundColor = previous;
        }

        /// <summary>Раздел «Проверки хвата»: K1…F1, пороги, строка расчёта и показ проб в сцене.</summary>
        static void DrawChecksSection()
        {
            GripAnalysisHost host = _hostProvider != null ? _hostProvider() : default;
            string item = host.Grabbable != null ? host.Grabbable.name : "предмет не выбран";
            string pose = host.Pose != null ? host.Pose.name : "поза не открыта";
            string side = host.Side == UxrHandSide.Left ? "левая" : "правая";

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"Проверки хвата — {item} · {pose} · {side}", _sectionStyle);
                    if (GUILayout.Button(new GUIContent("Пересчитать", "Пересобрать геометрию предмета и пересчитать"), EditorStyles.miniButton, GUILayout.Width(90)))
                    {
                        Engine.InvalidateGeometry();
                        GripHandProbeProfiles.Refresh();
                        Invalidate();
                    }
                }

                if (_report != null)
                {
                    string rebuilt = _report.CacheRebuilt ? $" (пересобраны: {_report.RebuildReason})" : "";
                    EditorGUILayout.LabelField(
                        $"расчёт {_report.ElapsedMs:0.0} мс = кэш {_report.CacheMs:0.0} + запрос {_report.QueryMs:0.0} · треугольников {_report.TriangleCount}{rebuilt} · проб {_report.Queries}",
                        _summaryStyle);
                    EditorGUILayout.LabelField($"знак: W {_report.WindingSigns} · N {_report.NormalSigns} · N? {_report.AmbiguousSigns}", _summaryStyle);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool showScene = EditorGUILayout.ToggleLeft("Показывать пробы в сцене", GripAnalysisSettings.ShowSceneProbes, GUILayout.Width(185));
                    if (showScene != GripAnalysisSettings.ShowSceneProbes)
                    {
                        GripAnalysisSettings.ShowSceneProbes = showScene;
                        SceneView.RepaintAll();
                    }

                    using (new EditorGUI.DisabledScope(!GripAnalysisSettings.ShowSceneProbes))
                    {
                        bool showRing = EditorGUILayout.ToggleLeft(new GUIContent("кольца X1", "Рисовать все 90 точек колец; иначе только проникшие"), GripAnalysisSettings.ShowRingPoints);
                        if (showRing != GripAnalysisSettings.ShowRingPoints)
                        {
                            GripAnalysisSettings.ShowRingPoints = showRing;
                            SceneView.RepaintAll();
                        }
                    }
                }

                if (_report == null)
                {
                    EditorGUILayout.HelpBox("Расчёт ещё не выполнен.", MessageType.None);
                }
                else if (_report.Reason != null)
                {
                    EditorGUILayout.HelpBox(_report.Reason, MessageType.Warning);
                }
                else
                {
                    foreach (GripCheckDefinition check in GripAnalysisSettings.Checks)
                    {
                        _report.Checks.TryGetValue(check.Id, out GripCheckResult result);
                        DrawCheck(check, result);
                    }
                }

                EditorGUILayout.Space(4);
                DrawThresholds();
            }
        }

        /// <summary>Блок «Ладонь»: источник точки P1, разметка рамки ладони и профиль.</summary>
        static void DrawPalmBlock()
        {
            GripAnalysisHost host = _hostProvider != null ? _hostProvider() : default;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Ладонь (P1)", EditorStyles.miniBoldLabel);
                if (host.Avatar == null)
                {
                    EditorGUILayout.LabelField("Нет аватара в Hand Pose Editor.", _summaryStyle);
                    return;
                }

                string sideRu = host.Side == UxrHandSide.Left ? "Левая" : "Правая";
                GripHandProbeProfile profile = GripHandProbeProfiles.Find(host.Avatar);
                if (profile != null)
                {
                    EditorGUILayout.LabelField($"Профиль: {AssetDatabase.GetAssetPath(profile)} (база «{profile.AvatarName}»)", _summaryStyle);
                }
                else if (GripHandProbeProfiles.TryGetNewProfileTarget(host.Avatar, out _, out string targetAvatar, out string newPath))
                {
                    EditorGUILayout.LabelField($"Профиля нет; при сохранении будет создан {newPath}, привязка к аватару «{targetAvatar}»", _summaryStyle);
                }
                else
                {
                    EditorGUILayout.LabelField("Профиль не к чему привязать: у аватара нет GUID prefab.", _summaryStyle);
                }

                GripPalmProbe saved = profile != null ? profile.Get(host.Side).Palm : null;
                bool manualSaved = saved != null && saved.ManuallySet;
                EditorGUILayout.LabelField($"{sideRu} кисть: {(manualSaved ? "ручная рамка сохранена" : "ручной разметки нет")}", _summaryStyle);

                int mode = GUILayout.Toolbar(GripAnalysisSettings.PalmUseManual ? 1 : 0, new[] { "Авто (кожа)", "Вручную" });
                bool useManual = mode == 1;
                if (useManual != GripAnalysisSettings.PalmUseManual)
                {
                    GripAnalysisSettings.PalmUseManual = useManual;
                    SceneView.RepaintAll();
                }

                bool marking = GripPalmMarking.IsFor(host);
                if (!marking)
                {
                    if (GUILayout.Button(new GUIContent("Разметить центр ладони", "Клик по кисти в SceneView ставит рамку ладони на поверхность; затем ручки позиции и поворота")))
                    {
                        GripPalmMarking.Begin(host, profile);
                    }
                }
                else
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!GripPalmMarking.HasDraft))
                        {
                            if (GUILayout.Button("Сохранить в профиль"))
                            {
                                GripPalmMarking.Save(host);
                                GUIUtility.ExitGUI();
                            }
                        }

                        if (GUILayout.Button("Отменить"))
                        {
                            GripPalmMarking.Cancel("Разметка отменена, профиль не изменён.");
                        }
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!manualSaved))
                    {
                        if (GUILayout.Button(new GUIContent("Сбросить на авто", "Снять ручную разметку этой руки в профиле (Undo)")))
                        {
                            GripPalmMarking.Cancel(null);
                            GripPalmMarking.Status = GripHandProbeProfiles.ResetPalm(host.Avatar, host.Side)
                                ? "Ручная разметка снята: P1 снова берёт авто (кожа)."
                                : "Ручной разметки не было.";
                        }
                    }

                    bool hasSource = manualSaved || (marking && GripPalmMarking.HasDraft);
                    using (new EditorGUI.DisabledScope(!hasSource))
                    {
                        if (GUILayout.Button(new GUIContent("Отзеркалить на другую руку", "Отражение поперёк ладони в анатомической рамке запястья; пишет профиль другой руки (Undo)")))
                        {
                            Vector3 position = marking && GripPalmMarking.HasDraft ? GripPalmMarking.DraftPositionWrist : saved.PositionWrist;
                            Quaternion rotation = marking && GripPalmMarking.HasDraft ? GripPalmMarking.DraftRotationWrist : saved.RotationWrist;
                            GripHandProbeProfile written = GripHandProbeProfiles.MirrorPalm(host.Avatar, host.Side, position, rotation, out string error);
                            GripPalmMarking.Status = written != null
                                ? $"Отзеркалено на {(host.Side == UxrHandSide.Left ? "правую" : "левую")} кисть в {AssetDatabase.GetAssetPath(written)}."
                                : "Зеркало не записано: " + error;
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(GripPalmMarking.Status))
                {
                    EditorGUILayout.LabelField(GripPalmMarking.Status, _summaryStyle);
                }
            }
        }

        static int _ringResetFinger;
        static int _ringTable;

        /// <summary>
        /// Блок «Кольца фаланг»: t и радиус колец K1/X1 по руке и фаланге (таблица 5×3 с переключателем «t / радиус»),
        /// ручки в сцене, сброс, зеркало и подгонка радиусов по коже.
        /// </summary>
        static void DrawRingsBlock()
        {
            GripAnalysisHost host = _hostProvider != null ? _hostProvider() : default;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool expanded = EditorGUILayout.Foldout(GripAnalysisSettings.RingsExpanded, "Кольца фаланг (K1/X1)", true);
                if (expanded != GripAnalysisSettings.RingsExpanded)
                {
                    GripAnalysisSettings.RingsExpanded = expanded;
                }

                bool move = EditorGUILayout.ToggleLeft(new GUIContent("Двигать кольца в сцене",
                        "Шар — сдвиг вдоль оси кости (t 0,05..0,95), точка на окружности — радиус (2..20 мм); отпускание ручки пишет профиль (Undo)"),
                    GripAnalysisSettings.RingsMoveInScene);
                if (move != GripAnalysisSettings.RingsMoveInScene)
                {
                    GripAnalysisSettings.RingsMoveInScene = move;
                    SceneView.RepaintAll();
                }

                if (GripAnalysisSettings.RingsMoveInScene)
                {
                    string fingerName = _ringResetFinger == 0 ? "все" : GripAnalysisEngine.FingerNamesRu[Mathf.Clamp(_ringResetFinger - 1, 0, 4)];
                    EditorGUILayout.LabelField(
                        $"ручки: {GripRingEditing.HandleModeName((GripRingEditing.HandleMode)Mathf.Clamp(_ringTable, 0, 3))}, палец: {fingerName} (меняются вкладкой таблицы и списком «Палец для кнопок»)",
                        _summaryStyle);
                }

                if (!expanded)
                {
                    return;
                }

                if (host.Avatar == null)
                {
                    EditorGUILayout.LabelField("Нет аватара в Hand Pose Editor.", _summaryStyle);
                    return;
                }

                GripHandProbeProfile profile = GripHandProbeProfiles.Find(host.Avatar);
                float[] rings = GripRingEditing.GetEffective(host, profile);
                float[] rawRadii = GripRingEditing.GetRawRadiiMm(host, profile);
                float[] radii = GripHandProbeSide.EffectiveRadiiMm(rawRadii);
                string sideRu = host.Side == UxrHandSide.Left ? "левая" : "правая";

                Vector2[] tilts = GripRingEditing.GetTiltsDeg(host, profile);
                float[] azimuths = GripRingEditing.GetAzimuthsDeg(host, profile);
                Vector2[] offsets = GripRingEditing.GetOffsetsMm(host, profile);
                int table = GUILayout.Toolbar(_ringTable, new[] { "t", "радиус, мм", "наклон/азимут, °", "смещение x/y, мм" });
                if (table != _ringTable)
                {
                    _ringTable = table;
                    SceneView.RepaintAll();
                }

                bool radiusTable = _ringTable == 1;
                if (_ringTable == 3)
                {
                    EditorGUILayout.LabelField($"Смещение центра кольца от линии фаланги, мм ({sideRu} кисть): x — вдоль LocalRight кости, y — вдоль LocalUp (тыл); 0 — центр на кости", _summaryStyle);
                    DrawRingOffsetTable(host, offsets);
                }
                else if (_ringTable == 2)
                {
                    EditorGUILayout.LabelField($"Наклон плоскости кольца: pitch — вокруг LocalRight кости, yaw — вокруг LocalUp; азимут — угол точки касания от ладонной стороны ({sideRu} кисть)", _summaryStyle);
                    DrawRingOrientationTable(host, tilts, azimuths);
                }
                else
                {
                    EditorGUILayout.LabelField(radiusTable
                        ? $"Радиус кольца ({sideRu} кисть); по умолчанию 6,8 / 5,8 / 5,0 мм, 0 — сброс на умолчание"
                        : $"t — доля длины сегмента ({sideRu} кисть); 0,5 — середина (не задано)", _summaryStyle);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("", GUILayout.Width(90));
                        foreach (string segment in GripAnalysisEngine.SegmentNamesRu)
                        {
                            GUILayout.Label(segment, EditorStyles.miniLabel, GUILayout.Width(62));
                        }
                    }

                    float[] edited = null;
                    for (int f = 0; f < GripAnalysisEngine.FingerNamesRu.Length; f++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(GripAnalysisEngine.FingerNamesRu[f], EditorStyles.miniLabel, GUILayout.Width(90));
                            for (int s = 0; s < 3; s++)
                            {
                                int index = f * 3 + s;
                                float shown = radiusTable ? radii[index] : rings[index];
                                float value = EditorGUILayout.DelayedFloatField(shown, GUILayout.Width(62));
                                if (Mathf.Approximately(value, shown))
                                {
                                    continue;
                                }

                                if (radiusTable)
                                {
                                    edited = edited ?? (float[])rawRadii.Clone();
                                    edited[index] = value > 0f ? GripHandProbeSide.ClampRingRadiusMm(value) : 0f;
                                }
                                else
                                {
                                    edited = edited ?? (float[])rings.Clone();
                                    edited[index] = GripHandProbeSide.ClampRingT(value);
                                }
                            }
                        }
                    }

                    if (edited != null)
                    {
                        if (radiusTable)
                        {
                            GripRingEditing.SaveRadii(host, edited, "Изменение радиуса кольца фаланги");
                        }
                        else
                        {
                            GripRingEditing.Save(host, edited, "Изменение t кольца фаланги");
                        }

                        GUIUtility.ExitGUI();
                    }
                }

                string[] targets = { "Все пальцы", "Большой", "Указательный", "Средний", "Безымянный", "Мизинец" };
                int selectedFinger = EditorGUILayout.Popup(new GUIContent("Палец для кнопок", "Также ограничивает ручки колец в сцене"), _ringResetFinger,
                    System.Array.ConvertAll(targets, t => new GUIContent(t)));
                if (selectedFinger != _ringResetFinger)
                {
                    _ringResetFinger = selectedFinger;
                    SceneView.RepaintAll();
                }

                bool Selected(int index) => _ringResetFinger == 0 || index / 3 == _ringResetFinger - 1;

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Сбросить t на середину", "t = 0,5 для фаланг выбранного пальца или всех пальцев (Undo)")))
                    {
                        float[] reset = (float[])rings.Clone();
                        for (int i = 0; i < reset.Length; i++)
                        {
                            if (Selected(i)) reset[i] = GripHandProbeSide.DefaultRingT;
                        }

                        GripRingEditing.Save(host, reset, "Сброс колец фаланг на середину");
                        GUIUtility.ExitGUI();
                    }

                    if (GUILayout.Button(new GUIContent("Сбросить радиусы", "Радиусы по умолчанию (6,8 / 5,8 / 5,0 мм) для выбранного пальца или всех (Undo)")))
                    {
                        float[] reset = (float[])rawRadii.Clone();
                        for (int i = 0; i < reset.Length; i++)
                        {
                            if (Selected(i)) reset[i] = 0f;
                        }

                        GripRingEditing.SaveRadii(host, reset, "Сброс радиусов колец фаланг");
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Сбросить наклон", "Наклон и азимут 0° для выбранного пальца или всех (Undo)")))
                    {
                        Vector2[] resetTilts = (Vector2[])tilts.Clone();
                        float[] resetAzimuths = (float[])azimuths.Clone();
                        for (int i = 0; i < resetTilts.Length; i++)
                        {
                            if (!Selected(i)) continue;
                            resetTilts[i] = Vector2.zero;
                            resetAzimuths[i] = 0f;
                        }

                        GripRingEditing.SaveOrientation(host, resetTilts, resetAzimuths, "Сброс наклона колец фаланг");
                        GUIUtility.ExitGUI();
                    }

                    if (GUILayout.Button(new GUIContent("Сбросить смещение", "Центр кольца на линии фаланги для выбранного пальца или всех (Undo)")))
                    {
                        Vector2[] resetOffsets = (Vector2[])offsets.Clone();
                        for (int i = 0; i < resetOffsets.Length; i++)
                        {
                            if (Selected(i)) resetOffsets[i] = Vector2.zero;
                        }

                        GripRingEditing.SaveOffsets(host, resetOffsets, "Сброс смещения колец фаланг");
                        GUIUtility.ExitGUI();
                    }

                    if (GUILayout.Button(new GUIContent("Подогнать наклон по коже",
                            "BakeMesh SMR руки; ладонный луч из центра кольца, внешнее попадание на коже своего пальца; наклон и азимут — чтобы направление на точку касания совпало с нормалью кожи (Undo)")))
                    {
                        GripRingEditing.FitTiltToSkin(host, profile, _ringResetFinger - 1);
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent("Подогнать кольца по коже",
                            "Центр и радиус вместе: BakeMesh SMR руки; 12 лучей в плоскости кольца из текущего центра, внешнее попадание на коже своего пальца, " +
                            "окружность наименьших квадратов, 3 итерации; меньше 6 попаданий — кольцо не меняется (Undo)")))
                    {
                        GripRingEditing.FitRingsToSkin(host, profile, _ringResetFinger - 1);
                        GUIUtility.ExitGUI();
                    }

                    if (GUILayout.Button(new GUIContent("Отзеркалить на другую руку",
                            "Те же t, радиусы, pitch и смещение y; yaw, азимут и смещение x — с обратным знаком (отражение поперёк ладони) — для противоположной кисти (Undo)")))
                    {
                        UxrHandSide other = host.Side == UxrHandSide.Left ? UxrHandSide.Right : UxrHandSide.Left;
                        GripHandProbeProfile written = GripHandProbeProfiles.SaveRings(host.Avatar, other, rings, "Зеркало колец фаланг", out string error);
                        if (written != null)
                        {
                            written = GripHandProbeProfiles.SaveRingRadii(host.Avatar, other, rawRadii, "Зеркало радиусов колец фаланг", out error);
                        }

                        if (written != null)
                        {
                            // Отражение поперёк ладони: поворот вокруг LocalRight сохраняет знак, вокруг LocalUp и оси кольца — меняет
                            var mirroredTilts = new Vector2[tilts.Length];
                            var mirroredAzimuths = new float[azimuths.Length];
                            for (int i = 0; i < tilts.Length; i++)
                            {
                                mirroredTilts[i] = new Vector2(tilts[i].x, -tilts[i].y);
                                mirroredAzimuths[i] = -azimuths[i];
                            }

                            written = GripHandProbeProfiles.SaveRingOrientation(host.Avatar, other, mirroredTilts, mirroredAzimuths, "Зеркало наклона колец фаланг", out error);
                        }

                        if (written != null)
                        {
                            // Смещение: компонента вдоль LocalRight (x) меняет знак, вдоль LocalUp (y) сохраняется
                            var mirroredOffsets = new Vector2[offsets.Length];
                            for (int i = 0; i < offsets.Length; i++)
                            {
                                mirroredOffsets[i] = new Vector2(-offsets[i].x, offsets[i].y);
                            }

                            written = GripHandProbeProfiles.SaveRingOffsets(host.Avatar, other, mirroredOffsets, "Зеркало смещения колец фаланг", out error);
                        }

                        GripRingEditing.Status = written != null
                            ? $"t, радиусы, наклон, азимут и смещение колец отзеркалены на {(other == UxrHandSide.Left ? "левую" : "правую")} кисть в {AssetDatabase.GetAssetPath(written)}."
                            : "Зеркало не записано: " + error;
                        GUIUtility.ExitGUI();
                    }
                }

                if (!string.IsNullOrEmpty(GripRingEditing.Status))
                {
                    EditorGUILayout.LabelField(GripRingEditing.Status, _summaryStyle);
                }
            }
        }

        /// <summary>Таблица 5×3 смещения центра кольца: x, y (мм).</summary>
        static void DrawRingOffsetTable(GripAnalysisHost host, Vector2[] offsets)
        {
            Vector2[] edited = null;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("", GUILayout.Width(90));
                foreach (string segment in GripAnalysisEngine.SegmentNamesRu)
                {
                    GUILayout.Label(segment + " x / y", EditorStyles.miniLabel, GUILayout.Width(84));
                }
            }

            for (int f = 0; f < GripAnalysisEngine.FingerNamesRu.Length; f++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(GripAnalysisEngine.FingerNamesRu[f], EditorStyles.miniLabel, GUILayout.Width(90));
                    for (int s = 0; s < 3; s++)
                    {
                        int index = f * 3 + s;
                        float x = EditorGUILayout.DelayedFloatField(offsets[index].x, GUILayout.Width(40));
                        float y = EditorGUILayout.DelayedFloatField(offsets[index].y, GUILayout.Width(40));
                        if (!Mathf.Approximately(x, offsets[index].x) || !Mathf.Approximately(y, offsets[index].y))
                        {
                            edited = edited ?? (Vector2[])offsets.Clone();
                            edited[index] = GripHandProbeSide.ClampRingOffset(new Vector2(x, y));
                        }
                    }
                }
            }

            if (edited != null)
            {
                GripRingEditing.SaveOffsets(host, edited, "Изменение смещения кольца фаланги");
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>Таблица 5×3 наклона и азимута: в ячейке pitch, yaw, азимут (°).</summary>
        static void DrawRingOrientationTable(GripAnalysisHost host, Vector2[] tilts, float[] azimuths)
        {
            Vector2[] editedTilts = null;
            float[] editedAzimuths = null;
            for (int f = 0; f < GripAnalysisEngine.FingerNamesRu.Length; f++)
            {
                EditorGUILayout.LabelField(GripAnalysisEngine.FingerNamesRu[f], EditorStyles.miniBoldLabel);
                for (int s = 0; s < 3; s++)
                {
                    int index = f * 3 + s;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(GripAnalysisEngine.SegmentNamesRu[s], EditorStyles.miniLabel, GUILayout.Width(50));
                        GUILayout.Label("p", EditorStyles.miniLabel, GUILayout.Width(10));
                        float pitch = EditorGUILayout.DelayedFloatField(tilts[index].x, GUILayout.Width(48));
                        GUILayout.Label("y", EditorStyles.miniLabel, GUILayout.Width(10));
                        float yaw = EditorGUILayout.DelayedFloatField(tilts[index].y, GUILayout.Width(48));
                        GUILayout.Label("аз", EditorStyles.miniLabel, GUILayout.Width(16));
                        float azimuth = EditorGUILayout.DelayedFloatField(azimuths[index], GUILayout.Width(48));
                        if (!Mathf.Approximately(pitch, tilts[index].x) || !Mathf.Approximately(yaw, tilts[index].y))
                        {
                            editedTilts = editedTilts ?? (Vector2[])tilts.Clone();
                            editedTilts[index] = GripHandProbeSide.ClampRingTilt(new Vector2(pitch, yaw));
                        }

                        if (!Mathf.Approximately(azimuth, azimuths[index]))
                        {
                            editedAzimuths = editedAzimuths ?? (float[])azimuths.Clone();
                            editedAzimuths[index] = GripHandProbeSide.ClampRingAzimuth(azimuth);
                        }
                    }
                }
            }

            if (editedTilts != null || editedAzimuths != null)
            {
                GripRingEditing.SaveOrientation(host, editedTilts ?? tilts, editedAzimuths ?? azimuths, "Изменение наклона/азимута кольца фаланги");
                GUIUtility.ExitGUI();
            }
        }

        static void DrawCheck(GripCheckDefinition check, GripCheckResult result)
        {
            GripCheckStatus status = result != null ? result.Status : GripCheckStatus.NotApplicable;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool enabled = EditorGUILayout.Toggle(check.Enabled, GUILayout.Width(16));
                    if (enabled != check.Enabled)
                    {
                        GripAnalysisSettings.SetCheckEnabled(check, enabled);
                        SceneView.RepaintAll();
                    }

                    bool expanded = EditorGUILayout.Foldout(check.DetailsExpanded,
                        new GUIContent($"{check.Id} {check.Title}", check.Description), true);
                    if (expanded != check.DetailsExpanded)
                    {
                        GripAnalysisSettings.SetDetailsExpanded(check, expanded);
                    }

                    DrawBadge(check.Enabled ? status : GripCheckStatus.NotApplicable, check.Enabled ? null : "выкл");
                }

                string summary = !check.Enabled ? "выключена" : result != null ? result.Summary : "нет данных";
                EditorGUILayout.LabelField(summary, _summaryStyle);

                if (check.Enabled && check.DetailsExpanded && result != null)
                {
                    foreach (GripDetailLine line in result.Details)
                    {
                        Color previous = _detailStyle.normal.textColor;
                        if (line.Status != GripCheckStatus.NotApplicable)
                        {
                            _detailStyle.normal.textColor = GripAnalysisEngine.StatusColor(line.Status);
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(10 + line.Indent * 14);
                            GUILayout.Label(line.Text, _detailStyle);
                        }

                        _detailStyle.normal.textColor = previous;
                    }
                }
            }
        }

        static void DrawBadge(GripCheckStatus status, string overrideText)
        {
            string text = overrideText ?? StatusText(status);
            Rect rect = GUILayoutUtility.GetRect(44, 16, GUILayout.Width(44), GUILayout.Height(16));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, GripAnalysisEngine.StatusColor(status));
            }

            GUI.Label(rect, text, _badgeStyle);
        }

        static string StatusText(GripCheckStatus status)
        {
            switch (status)
            {
                case GripCheckStatus.Ok: return "ok";
                case GripCheckStatus.Warn: return "warn";
                case GripCheckStatus.Fail: return "fail";
                default: return "n/a";
            }
        }

        static void DrawThresholds()
        {
            bool expanded = EditorGUILayout.Foldout(GripAnalysisSettings.ThresholdsExpanded, "Пороги", true);
            if (expanded != GripAnalysisSettings.ThresholdsExpanded)
            {
                GripAnalysisSettings.ThresholdsExpanded = expanded;
            }

            if (!expanded)
            {
                return;
            }

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 230f;
            try
            {
                EditorGUILayout.LabelField("Общие (знак и цвет проб)", EditorStyles.miniBoldLabel);
                foreach (GripThreshold threshold in GripAnalysisSettings.CommonThresholds)
                {
                    DrawThreshold(threshold);
                }

                foreach (GripCheckDefinition check in GripAnalysisSettings.Checks)
                {
                    EditorGUILayout.LabelField($"{check.Id} {check.Title}", EditorStyles.miniBoldLabel);
                    foreach (GripThreshold threshold in check.Thresholds)
                    {
                        DrawThreshold(threshold);
                    }
                }

                if (GUILayout.Button("Сбросить пороги по умолчанию", EditorStyles.miniButton))
                {
                    GripAnalysisSettings.ResetThresholds();
                    GUI.FocusControl(null);
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = previousLabelWidth;
            }
        }

        static void DrawThreshold(GripThreshold threshold)
        {
            float value = EditorGUILayout.FloatField(new GUIContent(threshold.Label, threshold.Tooltip), threshold.Value);
            if (!Mathf.Approximately(value, threshold.Value))
            {
                GripAnalysisSettings.SetThreshold(threshold, value);
            }
        }

        // ---------------------------------------------------------------- Сцена

        /// <summary>Пробы анализа в SceneView; вызывается из SceneGUI-хука панели только для её окна.</summary>
        public static void DrawScene(UxrHandPoseEditorWindow window, SceneView sceneView)
        {
            // Разметка рамки ладони обрабатывает все события (клик, ручки, Esc) независимо от показа проб
            if (IsEnabled && GripPalmMarking.Active && _hostProvider != null && Event.current != null)
            {
                GripPalmMarking.OnSceneGUI(_hostProvider(), new GripPreviewMapping(_snap, _grabber != null ? _grabber.transform : null));
            }

            // Ручки колец фаланг вдоль оси кости
            if (IsEnabled && GripAnalysisSettings.RingsMoveInScene && _hostProvider != null && Event.current != null)
            {
                GripAnalysisHost ringHost = _hostProvider();
                GripHandProbeProfile ringProfile = ringHost.Avatar != null ? GripHandProbeProfiles.Find(ringHost.Avatar) : null;
                // Тип ручек = вкладка таблицы, палец = выбор «Палец для кнопок»
                GripRingEditing.OnSceneGUI(ringHost, new GripPreviewMapping(_snap, _grabber != null ? _grabber.transform : null), ringProfile,
                    (GripRingEditing.HandleMode)Mathf.Clamp(_ringTable, 0, 3), _ringResetFinger - 1);
            }

            if (!IsEnabled || !GripAnalysisSettings.ShowSceneProbes || _report == null || _report.Reason != null)
            {
                return;
            }

            if (Event.current == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Color oldColor = Handles.color;
            CompareFunction oldZTest = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.Always;

                foreach (GripProbeLine line in _report.Lines)
                {
                    Handles.color = line.Color;
                    Handles.DrawAAPolyLine(line.Width > 0f ? line.Width : 2.5f, line.From, line.To);
                }

                bool showRing = GripAnalysisSettings.ShowRingPoints;
                foreach (GripProbe probe in _report.Probes)
                {
                    if (probe.Kind == GripProbeKind.Ring && !showRing && probe.State != GripProbeState.Penetration)
                    {
                        continue;
                    }

                    Color color = ProbeColor(probe.State);
                    if (probe.RingManual && probe.Axis.sqrMagnitude > 1e-8f)
                    {
                        // Кольцо со смещённым вручную t — маркер-окружность вокруг оси кости
                        Handles.color = new Color(1f, 0.35f, 0.9f, 1f);
                        Handles.DrawWireDisc(probe.Position - probe.Direction * probe.Radius, probe.Axis, probe.Radius * 1.25f, 2f);
                    }

                    float depth = Mathf.Max(0f, -probe.Signed);
                    float minScreen = HandleUtility.GetHandleSize(probe.Position);
                    switch (probe.Kind)
                    {
                        case GripProbeKind.Tip:
                        {
                            float size = Mathf.Max(0.0035f + depth * 1.5f, minScreen * 0.03f);
                            Handles.color = color;
                            Handles.CubeHandleCap(0, probe.Position, SafeLook(probe.Direction), size, EventType.Repaint);
                            break;
                        }
                        case GripProbeKind.Palm:
                        {
                            float size = Mathf.Max(0.006f, minScreen * 0.04f);
                            Vector3 palmNormal = probe.Direction.sqrMagnitude > 1e-8f ? probe.Direction : Vector3.up;
                            // Ручная рамка ладони — оранжевым, со стрелкой нормали
                            Color manualColor = new Color(1f, 0.55f, 0.1f, 1f);
                            Handles.color = probe.Manual ? manualColor : color;
                            Handles.SphereHandleCap(0, probe.Position, Quaternion.identity, size, EventType.Repaint);
                            Handles.color = probe.Manual ? manualColor : new Color(1f, 0.92f, 0.4f, 0.9f);
                            Handles.DrawWireDisc(probe.Position, palmNormal, 0.012f);
                            if (probe.Manual)
                            {
                                Handles.ArrowHandleCap(0, probe.Position, Quaternion.LookRotation(palmNormal), Mathf.Max(0.02f, minScreen * 0.25f), EventType.Repaint);
                            }
                            break;
                        }
                        default:
                        {
                            float baseSize = probe.Kind == GripProbeKind.Ring ? 0.0013f : 0.0022f;
                            float size = probe.State == GripProbeState.Penetration
                                ? Mathf.Min(baseSize + depth * 1.5f, 0.01f)
                                : baseSize;
                            size = Mathf.Max(size, minScreen * (probe.Kind == GripProbeKind.Ring ? 0.012f : 0.02f));
                            Handles.color = probe.Kind == GripProbeKind.Ring && probe.State != GripProbeState.Penetration
                                ? new Color(color.r, color.g, color.b, 0.55f)
                                : color;
                            Handles.SphereHandleCap(0, probe.Position, Quaternion.identity, size, EventType.Repaint);
                            break;
                        }
                    }
                }
            }
            finally
            {
                Handles.color = oldColor;
                Handles.zTest = oldZTest;
            }
        }

        static Quaternion SafeLook(Vector3 direction)
        {
            return direction.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(direction) : Quaternion.identity;
        }

        static Color ProbeColor(GripProbeState state)
        {
            switch (state)
            {
                case GripProbeState.Contact: return new Color(0.2f, 0.92f, 0.3f, 1f);
                case GripProbeState.Near: return new Color(0.3f, 0.82f, 1f, 1f);
                case GripProbeState.Penetration: return new Color(1f, 0.18f, 0.18f, 1f);
                default: return new Color(0.62f, 0.62f, 0.62f, 0.85f);
            }
        }
    }
}
