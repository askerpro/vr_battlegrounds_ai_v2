using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Gameplay.SightSplit;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Автосведение механических прицелов (решения 2026-10-11/12). Два режима стенда (несохраняемая сцена):
    /// <list type="bullet">
    /// <item>комплект SightKit_* под MeshContainer копии оружия — пользователь ставит корень на планку и раздвигает
    /// половины вдоль планки (локальная Z);</item>
    /// <item>«Родной прицел» — вшитый прицел ствола из <see cref="SightSplitTable"/>, разделённый
    /// <see cref="SightPartSplitter"/> на копии (<see cref="NativeSightStand"/>, меши в памяти; уже разделённый префаб
    /// копия сначала возвращает к источнику и делит заново).</item>
    /// </list>
    /// «Свести» двигает подвижную деталь только по её локальной Y. У комплекта-коллиматора (SightKit_XPS2) подвижной
    /// детали нет: «Свести» привязывает сетку конечного фокуса к ShotSource и дистанции пристрелки и показывает, где
    /// точка пристрелки видна в окне (<see cref="SolveOptic"/>).
    /// Ствол со своим прицелом (<see cref="SightSplitTable"/>) калибруется только им — комплекты для него недоступны
    /// (раунд 1, 2026-10-20). «Сохранить в таблицу» и «Применить к префабу» включены для комплектов и (раунд 2) для
    /// родного прицела — запись с Kit = null, применение = постоянное разделение + сдвиг (<see cref="WeaponSightAuthoring"/>).
    /// Панель вкладки «Калибровка прицела» окна «Настройка оружия»: оружие выбирает окно, панель хранит стенд.
    /// </summary>
    [Serializable]
    public sealed class SightAutoZeroPanel
    {
        private const string SettingsPath = WeaponSightAuthoring.SettingsPath;
        private const string NativeOption = "Родной прицел";
        // Решение пользователя 2026-10-20: запись таблицы и применение к префабу включены для комплектов SightKit_*.
        private static readonly bool SaveUnlocked = true;
        private static readonly bool ApplyUnlocked = true;
        /// <summary>Расстояние глаза за окном коллиматора для отчёта и «Вида через прицел», м.</summary>
        private const float OpticEyeRelief = .10f;
        private const float RailTolerance = .00001f; // 0,01 мм
        private const float ViewDistance = .18f;

        [NonSerialized] private GameObject[] _kits = Array.Empty<GameObject>();
        [SerializeField] private int _kitIndex;
        [SerializeField] private GameObject _standWeapon, _standKit, _standKitPrefab;
        [SerializeField] private WeaponInfo _standInfo;
        [SerializeField] private bool _standNative;
        [SerializeField] private string _message = "";
        [NonSerialized] private SightAutoZeroResult _last;
        [NonSerialized] private string _opticReport;
        /// <summary>Поверхности оружия на стенде в системе ShotSource (без комплекта) — для посадки в паз планки.</summary>
        [NonSerialized] private SurfaceProbe _railProbe;
        /// <summary>Допуск посадки в паз: контакт и ось — 0,3 мм, упор от центра паза — 1 мм.</summary>
        private const float SeatTolerance = .0003f, LugTolerance = .001f;
        /// <summary>Наибольшее перекрытие губки с планкой с одной стороны, м: штатная посадка XPS2 на TR15 в паке — 2 мм.</summary>
        private const float LipOverlapLimit = .002f;
        [NonSerialized] private NativeSightStand _native;
        [NonSerialized] private Action _repaint;

        /// <summary>Вызывает окно-хозяин в своём OnEnable; <paramref name="repaint"/> перерисовывает окно.</summary>
        public void Enable(Action repaint)
        {
            _repaint = repaint;
            _kits = AssetDatabase.FindAssets("t:Prefab SightKit_", new[] { SightKitBuilder.PrefabFolder })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => System.IO.Path.GetFileName(p).StartsWith("SightKit_", StringComparison.Ordinal))
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>).Where(g => g != null).ToArray();
            SceneView.duringSceneGui += DrawLines;
        }

        public void Disable() => SceneView.duringSceneGui -= DrawLines;

        public void Dispose() => DisposeNative();

        /// <summary>Объект, внутри которого ищутся половины: комплект или (в режиме родного прицела) копия ствола.</summary>
        private GameObject Scope => _standNative ? _standWeapon : _standKit;

        /// <summary>На стенде комплект-коллиматор (без механики).</summary>
        private bool StandOptic => !_standNative && _standKitPrefab != null && SightParts.IsOpticKit(_standKitPrefab);

        private bool StandAlive => _standWeapon != null && _standInfo != null
            && (_standNative ? _standWeapon.GetComponentsInChildren<SightAdjustablePart>(true).Length == 2 : _standKit != null && _standKitPrefab != null);

        /// <summary>Рисует вкладку для оружия, выбранного в окне (null — оружие не выбрано).</summary>
        public void Draw(WeaponInfo selected)
        {
            EditorGUILayout.LabelField("Автосведение механического прицела", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Стенд — временная сцена, не сохраняйте её. Комплект: поставьте корень на планку, раздвиньте " +
                "Rear/Front только вдоль планки (локальная Z корня). «Родной прицел»: вшитый прицел ствола уже разделён на копии. " +
                "«Свести» — только по высоте ходом подвижной детали; боковое расхождение показывается, но не исправляется.", MessageType.Info);
            var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath);
            EditorGUILayout.LabelField("Пристрелка Z", settings != null ? settings.DefaultZeroDistance + " м (настройки калибровки)" : "нет настроек");

            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
            {
                SightSplitSpec nativeSpec = selected != null ? NativeSightStand.SpecFor(selected.WeaponPrefab) : null;
                WeaponSightTable table = WeaponSightTable.Load();
                var entry = selected != null && table != null ? table.Find(selected) : null;

                // Список: ствол со своим прицелом (SightSplitTable) — только «Родной прицел»; иначе — комплекты SightKit_*.
                bool nativeSelected = false;
                GameObject kitSelected = null;
                if (nativeSpec != null)
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.Popup("Прицел", 0, new[] { NativeOption });
                    nativeSelected = true;
                }
                else if (_kits.Length > 0)
                {
                    _kitIndex = EditorGUILayout.Popup("Прицел", Mathf.Clamp(_kitIndex, 0, _kits.Length - 1), _kits.Select(k => k.name).ToArray());
                    kitSelected = _kits[Mathf.Clamp(_kitIndex, 0, _kits.Length - 1)];
                }
                else EditorGUILayout.HelpBox("Нет префабов SightKit_* в " + SightKitBuilder.PrefabFolder, MessageType.Warning);
                if (nativeSpec != null)
                    EditorGUILayout.HelpBox("У ствола свой прицел — он и калибруется; комплекты для него недоступны.", MessageType.None);
                if (nativeSpec != null) EditorGUILayout.LabelField("Родной прицел", nativeSpec.Note, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("В таблице", entry == null ? "записи нет" :
                    $"{(entry.Kit != null ? entry.Kit.name : "встроенный")}, " +
                    (SightParts.IsOpticKit(entry.Kit) ? "коллиматор" : $"сдвиг {entry.MovingOffset * 1000:+0.00;-0.00;0.00} мм") + $", {entry.ZeroDistance} м");
                // Намеренная смена оси выстрела (пак, рецепт, обвес): без записи пересборка сведёт родной прицел сама.
                if (nativeSpec != null && entry != null
                    && GUILayout.Button(new GUIContent("Сбросить запись", "Удалить запись родного прицела из таблицы: следующая пересборка ствола сведёт его заново (автосведение). Префаб не меняется")))
                    Run(() => ResetEntry(selected));

                using (new EditorGUI.DisabledScope(selected == null || (!nativeSelected && kitSelected == null)))
                    if (GUILayout.Button("Открыть стенд"))
                        Run(() => { if (nativeSelected) OpenNativeStand(selected, nativeSpec); else OpenStand(selected, kitSelected, entry); });

                if (StandAlive)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Стенд", _standNative ? $"{_standInfo.DisplayName} · родной прицел" : $"{_standInfo.DisplayName} + {_standKitPrefab.name}");
                    if (_standNative && _native != null && _native.Notes.Count > 0)
                        EditorGUILayout.HelpBox(string.Join("\n", _native.Notes), MessageType.None);
                    if (!_standNative)
                    {
                        if (GUILayout.Button("Выделить корень комплекта")) { Selection.activeGameObject = _standKit; SceneView.lastActiveSceneView?.FrameSelected(); }
                        if (GUILayout.Button(new GUIContent("Посадить на планку (в паз)", "Контакт канала — на вершины зубцов, ось — по оси планки, упор/центр крепления — в ближайший поперечный паз")))
                            Run(SeatOnRail);
                        if (StandOptic) { if (GUILayout.Button("Вернуть масштаб комплекта")) Run(ResetHalves); }
                        else if (GUILayout.Button("Вернуть половины на планку (X/Y и поворот как в префабе)")) Run(ResetHalves);
                    }
                    else if (GUILayout.Button("Вернуть деталь в нейтраль")) Run(ResetPartsToNeutral);
                    if (GUILayout.Button("Свести")) Run(() => { if (StandOptic) SolveOptic(settings, true); else Solve(settings, true); });
                    if (GUILayout.Button("Вид через прицел")) Run(ViewThroughSights);
                    if (StandOptic) { if (!string.IsNullOrEmpty(_opticReport)) EditorGUILayout.HelpBox(_opticReport, MessageType.None); }
                    else DrawResult();
                    EditorGUILayout.Space();
                    // PPK: подвижной детали нет — «Свести» только измеряет, записать можно сдвиг 0.
                    bool solved = StandOptic ? _opticReport != null
                        : _last != null && (_last.Ok || (_standNative && _native != null && _native.Moving == null));
                    using (new EditorGUI.DisabledScope(!SaveUnlocked || !solved))
                        if (GUILayout.Button(new GUIContent("Сохранить в таблицу", _standNative ? "Сдвиг родного прицела → WeaponSightTable (Kit = null)" : "Поза комплекта и сведение → WeaponSightTable")))
                            Run(() => SaveToTable(settings));
                    using (new EditorGUI.DisabledScope(!ApplyUnlocked))
                        if (GUILayout.Button(new GUIContent("Применить к префабу", _standNative
                                ? "Постоянное разделение родного прицела (меш-ассеты) + сдвиг из таблицы (WeaponSightAuthoring)"
                                : "Запись таблицы → префаб оружия (WeaponSightAuthoring)")))
                            Run(() => _message = WeaponSightAuthoring.Apply(_standInfo));
                    if (!_standNative && GUILayout.Button("Проверить запись таблицы (без записи)")) Run(() => _message = WeaponSightAuthoring.Preflight(_standInfo));
                }
            }
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.None);
        }

        private void DrawResult()
        {
            if (_last == null) return;
            if (!_last.Ok) EditorGUILayout.HelpBox(_last.Error, MessageType.Warning);
            double z = _last.ZeroDistance;
            EditorGUILayout.LabelField("Результат", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Требуемый сдвиг", $"{SightAutoZeroSolver.Mm(_last.RequiredWorldShift)} мм (итог {SightAutoZeroSolver.Mm(_last.RequiredOffset)} мм)");
            EditorGUILayout.LabelField("Ход детали", $"−{_last.TravelDown * 1000:0.00} … +{_last.TravelUp * 1000:0.00} мм");
            EditorGUILayout.LabelField($"Вертикаль на {z} м", $"до {SightAutoZeroSolver.Mm(_last.Before.VerticalAt(z))} → после {SightAutoZeroSolver.Mm(_last.After.VerticalAt(z))} мм");
            EditorGUILayout.LabelField($"Горизонталь на {z} м", $"{SightAutoZeroSolver.Mm(_last.After.HorizontalAt(z))} мм (не исправляется)");
            var distances = SightAutoZeroSolver.ReportDistances.Concat(new[] { z }).Distinct().OrderBy(d => d);
            foreach (double d in distances)
                EditorGUILayout.LabelField($"  {d} м", $"верт. {SightAutoZeroSolver.Mm(_last.After.VerticalAt(d))}, гор. {SightAutoZeroSolver.Mm(_last.After.HorizontalAt(d))}, |e| {_last.After.TotalAt(d) * 1000:0.00} мм");
        }

        private void Run(Action action)
        {
            try { action(); }
            catch (Exception e) { _message = e.Message; }
            _repaint?.Invoke();
            SceneView.RepaintAll();
        }

        // ── Стенд ───────────────────────────────────────────────

        private GameObject NewStandScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            DisposeNative();
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            _standWeapon = null; _standKit = null; _standKitPrefab = null; _standInfo = null; _last = null; _opticReport = null; _railProbe = null;
            return new GameObject("SightAutoZero_Stand (не сохранять)");
        }

        private void DisposeNative()
        {
            _native?.Dispose();
            _native = null;
        }

        private void OpenStand(WeaponInfo info, GameObject kitPrefab, WeaponSightTable.Entry entry)
        {
            GameObject stand = NewStandScene();
            if (stand == null) return;
            // Обычные копии, не экземпляры префабов: со стенда нельзя применить overrides в оружие или комплект.
            GameObject weapon = UnityEngine.Object.Instantiate(info.WeaponPrefab, stand.transform, false);
            weapon.name = info.WeaponPrefab.name;
            Transform container = WeaponSightAuthoring.FindContainer(weapon.transform);
            foreach (Transform child in container.Cast<Transform>().ToArray())
                if (child.name.StartsWith("SightKit_", StringComparison.Ordinal)) UnityEngine.Object.DestroyImmediate(child.gameObject); // прежний комплект заменяется
            if (weapon.GetComponentsInChildren<SightAdjustablePart>(true).Length > 0 || weapon.GetComponentsInChildren<WeaponOpticView>(true).Length > 0)
                throw new InvalidOperationException($"{weapon.name}: у оружия уже есть прицел вне комплекта (механика или коллиматор) — стенд комплекта не открыт");
            GameObject kit = UnityEngine.Object.Instantiate(kitPrefab, container, false);
            kit.name = kitPrefab.name;
            SightKitScale.Apply(kit.transform); // реальный размер при любом масштабе корня оружия
            Transform muzzle = SightParts.FindShotSource(weapon);
            bool optic = SightParts.IsOpticKit(kitPrefab);
            if (entry != null && entry.Kit == kitPrefab)
            {
                kit.transform.localPosition = entry.KitLocalPosition;
                kit.transform.localRotation = entry.KitLocalRotation;
                if (!optic)
                {
                    SetZ(kit.transform.Find("Rear"), entry.RearAlongRail);
                    SetZ(kit.transform.Find("Front"), entry.FrontAlongRail);
                    SightAdjustablePart moving = WeaponSightAuthoring.Moving(kit);
                    moving.transform.localPosition = moving.NeutralLocalPosition + Vector3.up * entry.MovingOffset;
                }
            }
            else PlaceAboveBore(weapon, kit.transform, muzzle);
            _standWeapon = weapon; _standKit = kit; _standKitPrefab = kitPrefab; _standInfo = info; _standNative = false;
            Selection.activeGameObject = kit;
            SceneView.lastActiveSceneView?.FrameSelected();
            _message = (entry != null && entry.Kit == kitPrefab ? "Стенд открыт с позой из таблицы." : "Стенд открыт: комплект поставлен над осью ствола — посадите его на планку.")
                + (optic ? " Коллиматор: после посадки «Свести» привяжет сетку к ShotSource (до этого сетка выключена)." : "");
        }

        /// <summary>Стенд родного прицела: копия ствола, разделённая SightPartSplitter (меши в памяти, префаб не меняется).</summary>
        private void OpenNativeStand(WeaponInfo info, SightSplitSpec spec)
        {
            GameObject stand = NewStandScene();
            if (stand == null) return;
            _native = NativeSightStand.Build(spec, stand.scene, stand.transform);
            _standWeapon = _native.Weapon; _standInfo = info; _standNative = true;
            // Сдвиг из таблицы, если запись родного прицела ещё годна (тот же ShotSource): стенд показывает префаб как есть.
            WeaponSightTable table = WeaponSightTable.Load();
            var entry = table != null ? table.Find(info) : null;
            if (entry != null && entry.Kit == null && _native.Moving != null && _native.Moving.IsWithinTravel(entry.MovingOffset)
                && entry.ShotSource.Compare(ShotSourceFingerprint.Capture(SightParts.FindShotSource(_standWeapon), _standWeapon.transform)) == null)
                _native.Moving.transform.localPosition = _native.Moving.NeutralLocalPosition + Vector3.up * entry.MovingOffset;
            Selection.activeGameObject = _native.Moving != null ? _native.Moving.gameObject : _native.Front.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            _message = "Стенд родного прицела открыт: " + spec.Note + (_native.Moving == null ? ". Подвижной детали нет — «Свести» только покажет ошибку." : ".");
        }

        /// <summary>Начальная поза нового комплекта: по оси ShotSource, над серединой оружия, на верхней границе bounds.</summary>
        private static void PlaceAboveBore(GameObject weapon, Transform kit, Transform muzzle)
        {
            kit.rotation = Quaternion.LookRotation(muzzle.forward, muzzle.up);
            var renderers = weapon.GetComponentsInChildren<Renderer>(true).Where(r => !r.transform.IsChildOf(kit)).ToArray();
            if (renderers.Length == 0) { kit.position = muzzle.position; return; }
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float along = Vector3.Dot(b.center - muzzle.position, muzzle.forward), top = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                top = Mathf.Max(top, Vector3.Dot(corner - muzzle.position, muzzle.up));
            }
            kit.position = muzzle.position + muzzle.forward * along + muzzle.up * top;
        }

        private static void SetZ(Transform half, float z)
        {
            if (half == null) throw new InvalidOperationException("У комплекта нет половины Rear/Front");
            half.localPosition = new Vector3(half.localPosition.x, half.localPosition.y, z);
        }

        private void RequireStand()
        {
            if (!StandAlive) throw new InvalidOperationException("Стенд не открыт или объекты удалены — откройте стенд заново");
        }

        private void ResetHalves()
        {
            RequireStand();
            Undo.RecordObject(_standKit.transform, "Вернуть масштаб комплекта");
            SightKitScale.Apply(_standKit.transform);
            _opticReport = null;
            if (StandOptic) return;
            foreach (string name in new[] { "Rear", "Front" })
            {
                Transform half = _standKit.transform.Find(name), source = _standKitPrefab.transform.Find(name);
                Undo.RecordObject(half, "Вернуть половину прицела на планку");
                half.localPosition = new Vector3(source.localPosition.x, source.localPosition.y, half.localPosition.z);
                half.localRotation = source.localRotation;
                half.localScale = source.localScale;
            }
            ResetParts(false);
        }

        private void ResetPartsToNeutral()
        {
            RequireStand();
            ResetParts(true);
        }

        /// <summary>Детали — в собранную позицию (подвижная сохраняет сдвиг по Y, если <paramref name="includeMoving"/> = false).</summary>
        private void ResetParts(bool includeMoving)
        {
            foreach (var part in Scope.GetComponentsInChildren<SightAdjustablePart>(true))
            {
                Undo.RecordObject(part.transform, "Вернуть деталь прицела");
                Vector3 n = part.NeutralLocalPosition;
                part.transform.localPosition = new Vector3(n.x, part.IsAdjustable && !includeMoving ? part.transform.localPosition.y : n.y, n.z);
                part.transform.localRotation = Quaternion.identity;
                part.transform.localScale = Vector3.one;
            }
            _last = null;
        }

        /// <summary>
        /// Проверка посадки. Комплект: корень в реальном размере, половины сдвинуты только по Z. Оба режима: детали
        /// не сдвинуты вручную (подвижная — только по Y).
        /// </summary>
        private string LayoutProblem()
        {
            if (!_standNative)
            {
                string scale = SightKitScale.Problem(_standKit.transform);
                if (scale != null) return scale + (StandOptic ? " («Вернуть масштаб комплекта» восстановит)" : " («Вернуть половины на планку» восстановит)");
                string seat = SeatProblem(out _);
                if (seat != null) return seat;
                if (StandOptic) return null;
                foreach (string name in new[] { "Rear", "Front" })
                {
                    Transform half = _standKit.transform.Find(name), source = _standKitPrefab.transform.Find(name);
                    if (half == null || source == null) return "У комплекта нет половины " + name;
                    if (Mathf.Abs(half.localPosition.x - source.localPosition.x) > RailTolerance || Mathf.Abs(half.localPosition.y - source.localPosition.y) > RailTolerance
                        || Quaternion.Angle(half.localRotation, source.localRotation) > .01f || (half.localScale - source.localScale).sqrMagnitude > 1e-10f)
                        return $"{name} сдвинута не вдоль планки: двигайте половины только по локальной Z корня («Вернуть половины на планку»)";
                }
            }
            foreach (var part in Scope.GetComponentsInChildren<SightAdjustablePart>(true))
            {
                Vector3 p = part.transform.localPosition, n = part.NeutralLocalPosition;
                if (Mathf.Abs(p.x - n.x) > RailTolerance || Mathf.Abs(p.z - n.z) > RailTolerance || (!part.IsAdjustable && Mathf.Abs(p.y - n.y) > RailTolerance)
                    || Quaternion.Angle(part.transform.localRotation, Quaternion.identity) > .01f)
                    return $"Деталь {part.name} сдвинута вручную: подвижная деталь двигается только сведением по Y" +
                           (_standNative ? " («Вернуть деталь в нейтраль»)" : "");
            }
            return null;
        }

        // ── Посадка в паз планки (раунд 3, решение 2026-10-20 «Посадка в паз») ─────────

        private Matrix4x4 MuzzleFrame()
        {
            Transform muzzle = SightParts.FindShotSource(_standWeapon);
            return Matrix4x4.TRS(muzzle.position, muzzle.rotation, Vector3.one).inverse;
        }

        private SurfaceProbe RailProbe()
        {
            if (_railProbe != null) return _railProbe;
            Matrix4x4 frame = MuzzleFrame();
            var probe = new SurfaceProbe();
            foreach (var filter in _standWeapon.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !filter.gameObject.activeInHierarchy || filter.GetComponent<MeshRenderer>() == null) continue;
                if (filter.name.Contains("Highlight") || (_standKit != null && filter.transform.IsChildOf(_standKit.transform))) continue;
                if (filter.GetComponentInParent<SightAdjustablePart>(true) != null) continue;
                probe.Add(filter.sharedMesh, frame * filter.transform.localToWorldMatrix);
            }
            return _railProbe = probe;
        }

        /// <summary>Узлы крепления комплекта: (половина или корень, узел RailMount).</summary>
        private List<(Transform half, Transform mount)> Mounts()
        {
            var result = new List<(Transform, Transform)>();
            Transform[] halves = StandOptic ? new[] { _standKit.transform } : new[] { _standKit.transform.Find("Rear"), _standKit.transform.Find("Front") };
            foreach (Transform half in halves)
            {
                Transform mount = half != null ? half.Find(SightRailMount.MountName) : null;
                if (mount == null) throw new InvalidOperationException($"{_standKitPrefab.name}: нет узла крепления {SightRailMount.MountName} — пересоберите комплекты (Build Iron/Optic Sight Kits)");
                result.Add((half, mount));
            }
            return result;
        }

        /// <summary>Канал крепления половины (ширина между губками, глубина) — из её меша, как у SightKitBuilder.</summary>
        private static SightMountInfo MountGeometry(Transform half, bool optic)
        {
            var probe = new SurfaceProbe();
            MeshFilter filter = optic ? half.GetComponentsInChildren<MeshFilter>(true).First() : half.GetComponent<MeshFilter>();
            Matrix4x4 toHalf = half.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            probe.Add(filter.sharedMesh, toHalf);
            Bounds b = filter.sharedMesh.bounds, bounds = new Bounds(toHalf.MultiplyPoint3x4(b.min), Vector3.zero);
            for (int i = 0; i < 8; i++)
                bounds.Encapsulate(toHalf.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z)));
            return SightRailMount.FindMount(probe, bounds, half.name);
        }

        /// <summary>null — каждое крепление сидит в пазу планки; иначе причина. <paramref name="report"/> — зазоры и смещения.</summary>
        private string SeatProblem(out string report)
        {
            report = null;
            if (_standNative) return null;
            Matrix4x4 frame = MuzzleFrame();
            var lines = new List<string>();
            string problem = null;
            foreach (var (half, mount) in Mounts())
            {
                Vector3 p = frame.MultiplyPoint3x4(mount.position);
                SightRailInfo rail = SightRailMount.FindRail(RailProbe(), p.z);
                string name = StandOptic ? _standKitPrefab.name : half.name;
                if (rail == null) { problem ??= $"{name}: под креплением нет планки"; lines.Add(name + ": планки нет"); continue; }
                float gap = p.y - rail.TopY, side = p.x - rail.AxisX;
                SightMountInfo channel = MountGeometry(half, StandOptic);
                float railWidth = SightRailMount.RailWidthAt(RailProbe(), rail.AxisX, p.z, rail.TopY - channel.ChannelDepth + .0002f);
                float lips = (channel.ChannelWidth - railWidth) * .5f;
                int slot = rail.NearestSlot(p.z);
                float lug = slot >= 0 ? p.z - rail.SlotCenters[slot] : float.NaN;
                lines.Add($"{name}: контакт {gap * 1000:+0.00;-0.00;0.00} мм от вершин зубцов, ось {side * 1000:+0.00;-0.00;0.00} мм, " +
                          (slot >= 0 ? $"упор от центра паза {lug * 1000:+0.00;-0.00;0.00} мм (паз {rail.SlotWidths[slot] * 1000:0.0} мм)" : "пазов под креплением нет") +
                          $", губки: канал {channel.ChannelWidth * 1000:0.0} мм, планка на их глубине {railWidth * 1000:0.0} мм, зазор с боку {lips * 1000:+0.0;-0.0;0.0} мм; планка: {rail}");
                if (gap > SeatTolerance) problem ??= $"{name} висит над планкой на {gap * 1000:0.00} мм — «Посадить на планку»";
                else if (gap < -SeatTolerance) problem ??= $"{name} врезан в планку на {-gap * 1000:0.00} мм — «Посадить на планку»";
                else if (Mathf.Abs(side) > SeatTolerance) problem ??= $"{name} смещён с оси планки на {side * 1000:0.00} мм — «Посадить на планку»";
                else if (slot >= 0 && Mathf.Abs(lug) > LugTolerance) problem ??= $"{name}: упор не в пазу ({lug * 1000:0.00} мм) — «Посадить на планку»";
                // Губки моделей паков упрощены: перекрытие с планкой до 2 мм с боку (штатная посадка XPS2 на TR15) только
                // показывается; больше — комплект на эту планку не садится.
                else if ((rail.TopWidth - channel.ChannelWidth) * .5f > LipOverlapLimit)
                    problem ??= $"{name}: канал крепления {channel.ChannelWidth * 1000:0.0} мм уже верха планки {rail.TopWidth * 1000:0.0} мм — комплект на эту планку не садится";
            }
            report = string.Join("\n", lines);
            return problem;
        }

        /// <summary>
        /// «Посадить на планку»: корень — по осям ShotSource; первое крепление (Rear или коллиматор) — контакт на вершины
        /// зубцов, на ось планки, упор в ближайший паз; у механики Front сдвигается вдоль планки к своему ближайшему пазу.
        /// </summary>
        private void SeatOnRail()
        {
            RequireStand();
            if (_standNative) throw new InvalidOperationException("Родной прицел на планку не сажается");
            Transform muzzle = SightParts.FindShotSource(_standWeapon);
            Undo.RecordObject(_standKit.transform, "Посадить комплект на планку");
            _standKit.transform.rotation = muzzle.rotation;
            SightKitScale.Apply(_standKit.transform);
            Matrix4x4 frame = MuzzleFrame(), back = frame.inverse;
            var mounts = Mounts();
            Vector3 p = frame.MultiplyPoint3x4(mounts[0].mount.position);
            SightRailInfo rail = SightRailMount.FindRail(RailProbe(), p.z) ?? throw new InvalidOperationException("Под креплением нет планки — поставьте комплект над планкой и повторите");
            int slot = rail.NearestSlot(p.z);
            var target = new Vector3(rail.AxisX, rail.TopY, slot >= 0 ? rail.SlotCenters[slot] : p.z);
            _standKit.transform.position += back.MultiplyVector(target - p);
            if (mounts.Count > 1)
            {
                Transform front = mounts[1].half;
                Vector3 f = frame.MultiplyPoint3x4(mounts[1].mount.position);
                SightRailInfo frontRail = SightRailMount.FindRail(RailProbe(), f.z) ?? throw new InvalidOperationException("Под мушкой нет планки — сдвиньте Front вдоль планки и повторите");
                int frontSlot = frontRail.NearestSlot(f.z);
                if (frontSlot >= 0)
                {
                    Undo.RecordObject(front, "Посадить мушку в паз");
                    // Корень стоит по осям ShotSource: локальная Z корня = z системы ShotSource (масштаб корня мира 1).
                    front.localPosition += Vector3.forward * (frontRail.SlotCenters[frontSlot] - f.z);
                }
            }
            _last = null; _opticReport = null;
            string problem = SeatProblem(out string report);
            _message = (problem == null ? "Посажено в паз планки.\n" : "Посадка с замечанием: " + problem + "\n") + report;
        }

        private SightAutoZeroResult Solve(WeaponSightCalibrationSettings settings, bool write)
        {
            RequireStand();
            if (settings == null) throw new InvalidOperationException("Нет настроек калибровки: " + SettingsPath);
            string problem = LayoutProblem();
            if (problem != null) throw new InvalidOperationException(problem);
            SightParts.Find(Scope, out var rear, out var front, out var moving);
            Transform muzzle = SightParts.FindShotSource(_standWeapon);
            var result = SightAutoZeroSolver.Solve(SightParts.Input(rear, front, moving, muzzle, settings.DefaultZeroDistance));
            if (write)
            {
                _last = result;
                if (result.Ok && moving != null)
                {
                    Undo.RecordObject(moving.transform, "Свести прицел");
                    Vector3 n = moving.NeutralLocalPosition;
                    moving.transform.localPosition = new Vector3(n.x, n.y + (float)result.RequiredOffset, n.z);
                    _message = "Сведено: подвижная деталь сдвинута по высоте. Проверьте «Вид через прицел».";
                }
                else _message = moving == null ? "Подвижной детали нет: показана ошибка на дистанциях." : "Не сведено: " + result.Error;
            }
            return result;
        }

        /// <summary>
        /// «Свести» коллиматор: подвижной детали нет, сетка конечного фокуса рисует точку пристрелки T = ShotSource +
        /// forward·Z в мире, поэтому сведение — привязка WeaponOpticView экземпляра к ShotSource и Z. Отчёт: угол оси
        /// окна к T и где точка видна в окне глазу на оси за <see cref="OpticEyeRelief"/>. <paramref name="write"/> = false —
        /// только проверка (для «Сохранить в таблицу»).
        /// </summary>
        private void SolveOptic(WeaponSightCalibrationSettings settings, bool write)
        {
            RequireStand();
            if (settings == null) throw new InvalidOperationException("Нет настроек калибровки: " + SettingsPath);
            string problem = LayoutProblem();
            if (problem != null) throw new InvalidOperationException(problem);
            WeaponOpticView view = SightParts.FindOptic(_standKit);
            Transform muzzle = SightParts.FindShotSource(_standWeapon);
            var source = _standWeapon.GetComponentsInChildren<UltimateXR.Mechanics.Weapons.UxrProjectileSource>(true).Single();
            float z = settings.DefaultZeroDistance;
            if (write)
            {
                Undo.RecordObject(view, "Свести коллиматор");
                SightParts.BindOptic(view, source, _standInfo, settings, z);
            }
            else
            {
                string binding = SightParts.OpticBindingProblem(view, source, _standInfo, z);
                if (binding != null) throw new InvalidOperationException("Не сведено: " + binding + " — нажмите «Свести»");
            }
            Vector3 lens = SightParts.OpticLensCenter(view), axis = _standKit.transform.forward, target = muzzle.position + muzzle.forward * z;
            if (Vector3.Dot(target - lens, axis) <= 0f) throw new InvalidOperationException("Окно коллиматора смотрит не к точке пристрелки");
            Vector3 toTarget = (target - lens).normalized;
            float vertical = Vector3.Dot(toTarget, _standKit.transform.up), horizontal = Vector3.Dot(toTarget, _standKit.transform.right);
            float tilt = Vector3.Angle(_standKit.transform.up, muzzle.up);
            // Глаз на оси окна за ним: точка T видна в окне со смещением ≈ угол·eye relief (для конечного фокуса без параллакса оси).
            _opticReport = $"Сетка привязана: точка пристрелки {z} м (ShotSource + forward·Z), сдвига деталей нет.\n" +
                           $"Ось окна → точка: верт. {vertical * 1000:+0.0;-0.0;0.0} мрад, гор. {horizontal * 1000:+0.0;-0.0;0.0} мрад; " +
                           $"в окне при глазе в {OpticEyeRelief * 100:0} см: {vertical * OpticEyeRelief * 1000:+0.0;-0.0;0.0} / {horizontal * OpticEyeRelief * 1000:+0.0;-0.0;0.0} мм от центра. " +
                           $"Окно над осью ствола: {Vector3.Dot(lens - muzzle.position, muzzle.up) * 1000:0.0} мм. Крен комплекта {tilt:0.00}°.";
            if (write) _message = "Коллиматор сведён (сетка привязана к ShotSource). Проверьте «Вид через прицел».";
        }

        /// <summary>
        /// Сброс записи родного прицела после намеренной смены оси выстрела: префаб не меняется, следующая пересборка
        /// ствола сводит прицел заново (<see cref="WeaponSightAuthoring.ApplyNative"/>).
        /// </summary>
        private void ResetEntry(WeaponInfo info)
        {
            if (!EditorUtility.DisplayDialog("Сбросить запись прицела",
                    $"{info.DisplayName}: удалить запись родного прицела из таблицы?\n\nПрефаб не меняется. Следующая пересборка ствола " +
                    "сведёт прицел заново тем же решателем, что «Свести».", "Сбросить", "Отмена"))
                return;
            WeaponSightTable table = WeaponSightTable.Load();
            if (table == null) throw new InvalidOperationException("Нет таблицы " + WeaponSightTable.AssetPath);
            Undo.RecordObject(table, "Сбросить запись прицела");
            if (!table.Remove(info)) throw new InvalidOperationException(info.WeaponId + ": записи в таблице нет");
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssetIfDirty(table);
            GameLog.WeaponSystem.Info($"[SightAutoZero] {info.WeaponId}: запись родного прицела сброшена — следующая пересборка сведёт заново");
            _message = $"Запись {info.WeaponId} сброшена. Пересоберите ствол — родной прицел сведётся автоматически.";
        }

        /// <summary>Запись позы комплекта и сведения в таблицу. Префаб оружия не меняется.</summary>
        private void SaveToTable(WeaponSightCalibrationSettings settings)
        {
            bool optic = StandOptic;
            // Повторный расчёт без записи: состояние стенда должно быть сведённым прямо сейчас.
            if (optic) SolveOptic(settings, false);
            else
            {
                var check = Solve(settings, false);
                bool measureOnly = _standNative && _native != null && _native.Moving == null;
                if (!check.Ok && !measureOnly) throw new InvalidOperationException("Не сведено: " + check.Error);
                if (!measureOnly && Math.Abs(check.RequiredWorldShift) > 1e-6) throw new InvalidOperationException("Стенд изменился после сведения — нажмите «Свести» ещё раз");
            }
            Transform muzzleAsset = SightParts.FindShotSource(_standInfo.WeaponPrefab);
            var fingerprint = ShotSourceFingerprint.Capture(muzzleAsset, _standInfo.WeaponPrefab.transform);
            string drift = fingerprint.Compare(ShotSourceFingerprint.Capture(SightParts.FindShotSource(_standWeapon), _standWeapon.transform));
            if (drift != null) throw new InvalidOperationException("Копия оружия на стенде отличается от префаба (" + drift + ") — откройте стенд заново");
            if (_standNative)
            {
                SightParts.Find(_standWeapon, out _, out _, out var nativeMoving);
                var native = new WeaponSightTable.Entry
                {
                    Weapon = _standInfo, Kit = null, KitLocalPosition = Vector3.zero, KitLocalRotation = Quaternion.identity,
                    MovingOffset = nativeMoving != null ? nativeMoving.Offset : 0f, ZeroDistance = settings.DefaultZeroDistance, ShotSource = fingerprint
                };
                WeaponSightTable nativeTable = WeaponSightTable.LoadOrCreate();
                Undo.RecordObject(nativeTable, "Сохранить сведение прицела");
                nativeTable.Set(native);
                EditorUtility.SetDirty(nativeTable);
                AssetDatabase.SaveAssetIfDirty(nativeTable);
                _message = $"Сохранено в таблицу: {_standInfo.WeaponId}, родной прицел, сдвиг {native.MovingOffset * 1000:+0.00;-0.00;0.00} мм. " +
                           "Префаб не менялся — «Применить к префабу» разделит прицел постоянно.";
                return;
            }
            SightAdjustablePart moving = optic ? null : WeaponSightAuthoring.Moving(_standKit);
            var entry = new WeaponSightTable.Entry
            {
                Weapon = _standInfo, Kit = _standKitPrefab,
                KitLocalPosition = _standKit.transform.localPosition, KitLocalRotation = _standKit.transform.localRotation,
                RearAlongRail = optic ? 0f : _standKit.transform.Find("Rear").localPosition.z,
                FrontAlongRail = optic ? 0f : _standKit.transform.Find("Front").localPosition.z,
                MovingOffset = optic ? 0f : moving.Offset, ZeroDistance = settings.DefaultZeroDistance, ShotSource = fingerprint
            };
            WeaponSightTable table = WeaponSightTable.LoadOrCreate();
            Undo.RecordObject(table, "Сохранить сведение прицела");
            table.Set(entry);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssetIfDirty(table);
            _message = $"Сохранено в таблицу: {_standInfo.WeaponId}, {_standKitPrefab.name}" +
                       (optic ? "" : $", сдвиг {entry.MovingOffset * 1000:+0.00;-0.00;0.00} мм") + ". Префаб оружия не менялся — «Применить к префабу».";
        }

        private void ViewThroughSights()
        {
            RequireStand();
            if (StandOptic)
            {
                // Глаз на оси окна коллиматора за ним, взгляд на точку пристрелки.
                Vector3 lens = SightParts.OpticLensCenter(SightParts.FindOptic(_standKit));
                Transform shot = SightParts.FindShotSource(_standWeapon);
                var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath);
                Vector3 target = shot.position + shot.forward * (settings != null ? settings.DefaultZeroDistance : 15f);
                var sceneView = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
                sceneView.orthographic = false;
                sceneView.LookAtDirect(lens, Quaternion.LookRotation((target - lens).normalized, shot.up), .1f);
                sceneView.size *= (OpticEyeRelief + .05f) / Mathf.Max(sceneView.cameraDistance, .0001f);
                sceneView.Focus();
                sceneView.Repaint();
                return;
            }
            SightParts.Find(Scope, out var rearPart, out var frontPart, out _);
            Vector3 rear = rearPart.transform.position, front = frontPart.transform.position;
            Transform muzzle = SightParts.FindShotSource(_standWeapon);
            Vector3 forward = front - rear;
            if (forward.sqrMagnitude < 1e-10f || Vector3.Dot(forward, muzzle.forward) <= 0)
                throw new InvalidOperationException("Мушка не впереди целика вдоль оси выстрела");
            var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            view.orthographic = false;
            // Камера Scene View — на ViewDistance позади R по линии R→F; пивот орбиты — точка R.
            view.LookAtDirect(rear, Quaternion.LookRotation(forward.normalized, muzzle.up), .1f);
            view.size *= ViewDistance / Mathf.Max(view.cameraDistance, .0001f);
            view.Focus();
            view.Repaint();
        }

        private void DrawLines(SceneView view)
        {
            if (!StandAlive) return;
            var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath);
            if (settings == null) return;
            Transform muzzle;
            SightAdjustablePart rear = null, front = null;
            WeaponOpticView optic = null;
            try
            {
                muzzle = SightParts.FindShotSource(_standWeapon);
                if (StandOptic) optic = SightParts.FindOptic(_standKit);
                else SightParts.Find(Scope, out rear, out front, out _);
            }
            catch (InvalidOperationException) { return; }
            float z = settings.DefaultZeroDistance;
            Vector3 target = muzzle.position + muzzle.forward * z;
            Color colour = Handles.color;
            var depth = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.Always;
                Handles.color = Color.green;
                Handles.DrawLine(muzzle.position, target, 2f);
                Handles.DrawWireDisc(target, muzzle.forward, .01f);
                if (optic != null)
                {
                    Vector3 lens;
                    try { lens = SightParts.OpticLensCenter(optic); }
                    catch (InvalidOperationException) { return; }
                    Handles.color = Color.cyan;
                    Handles.DrawLine(lens, target, 2f);
                    Handles.DrawWireDisc(lens, muzzle.forward, .003f);
                    return;
                }
                Vector3 dir = front.transform.position - rear.transform.position;
                float du = Vector3.Dot(dir, muzzle.forward);
                if (du <= 0f) return;
                float t = (z - Vector3.Dot(rear.transform.position - muzzle.position, muzzle.forward)) / du;
                Vector3 hit = rear.transform.position + dir * t;
                Handles.color = Color.cyan;
                Handles.DrawLine(rear.transform.position, hit, 2f);
                Handles.DrawWireDisc(rear.transform.position, muzzle.forward, .001f);
                Handles.DrawWireDisc(front.transform.position, muzzle.forward, .001f);
                Handles.DrawWireDisc(hit, muzzle.forward, .006f);
            }
            finally { Handles.color = colour; Handles.zTest = depth; }
        }
    }
}
