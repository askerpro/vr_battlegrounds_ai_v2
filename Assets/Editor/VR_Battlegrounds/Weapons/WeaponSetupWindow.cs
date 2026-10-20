using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Editor.Arsenal;
using VrBattlegrounds.Editor.Gameplay;
using VrBattlegrounds.Editor.Weapons.Accessories;
using VrBattlegrounds.Editor.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons
{
    /// <summary>
    /// «Настройка оружия» — единый стенд одного ствола (решение 2026-10-20). Вкладки-модули пишут каждая в свой
    /// источник данных, префаб меняют только существующие писатели: прицел — <see cref="SightAutoZeroPanel"/>
    /// (<see cref="WeaponSightTable"/> → <see cref="WeaponSightAuthoring"/>), баланс — <see cref="WeaponInfo"/> →
    /// <see cref="WeaponBalanceApplier"/>, раскладка слота — стенд <see cref="ArsenalLayoutAuthoringStand"/>.
    /// Ассортимент, станции и пересборка из пака остаются в «Редакторе арсенала» (<see cref="ArsenalEditorWindow"/>).
    /// </summary>
    public sealed class WeaponSetupWindow : EditorWindow
    {
        public enum Tab { Sight, Accessories, SlotLayout, Balance }
        private static readonly string[] TabTitles = { "Калибровка прицела", "Аксессуары", "Раскладка в слоте", "Урон и разброс" };
        private const string RegistryPath = "Assets/Data/Weapons/Resources/WeaponRegistry.asset";

        // Поля WeaponInfo вкладки «Урон и разброс»: экономика и баланс CS2. Идентичность, префабы и смещения — не здесь.
        private static readonly string[] BalanceFields =
        {
            "_displayName", "_description", "_icon", "_category", "_price", "_killAward",
            "_damage", "_rangeModifier", "_penetration", "_armorRatio", "_fireRate", "_fullAuto",
            "_magazineSize", "_maxMagazineCount", "_reserveAmmo", "_pellets", "_spread", "_recoilMagnitude",
        };

        [SerializeField] private Tab _tab;
        [SerializeField] private WeaponInfo _weapon;
        [SerializeField] private SightAutoZeroPanel _sight = new SightAutoZeroPanel();
        [SerializeField] private Vector2 _scroll;
        [SerializeField] private string _message = "";
        [NonSerialized] private WeaponInfo[] _weapons = Array.Empty<WeaponInfo>();
        [NonSerialized] private SerializedObject _balance;

        [MenuItem("Tools/VR Battlegrounds/Weapons/Настройка оружия", false, 0)]
        public static void Open() => Open(Tab.Sight, null);

        /// <summary>Открывает окно на вкладке; <paramref name="weapon"/> (если задано) становится выбранным оружием.</summary>
        public static void Open(Tab tab, WeaponInfo weapon)
        {
            var window = GetWindow<WeaponSetupWindow>("Настройка оружия");
            window.minSize = new Vector2(420, 480);
            window._tab = tab;
            if (weapon != null) window._weapon = weapon;
            window.Show();
        }

        private void OnEnable()
        {
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(RegistryPath);
            _weapons = registry != null ? registry.Weapons.Where(w => w != null && w.WeaponPrefab != null).ToArray() : Array.Empty<WeaponInfo>();
            if (_weapon == null && _weapons.Length > 0) _weapon = _weapons[0];
            _sight ??= new SightAutoZeroPanel();
            _sight.Enable(Repaint);
        }

        private void OnDisable() => _sight?.Disable();

        private void OnDestroy() => _sight?.Dispose();

        private void OnGUI()
        {
            DrawWeaponHeader();
            if (_weapon == null) return;
            _tab = (Tab)GUILayout.Toolbar((int)_tab, TabTitles);
            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            switch (_tab)
            {
                case Tab.Sight: _sight.Draw(_weapon); break;
                case Tab.Accessories: DrawAccessories(); break;
                case Tab.SlotLayout: DrawSlotLayout(); break;
                case Tab.Balance: DrawBalance(); break;
            }
            if (!string.IsNullOrEmpty(_message)) EditorGUILayout.HelpBox(_message, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        private void DrawWeaponHeader()
        {
            if (_weapons.Length == 0) { EditorGUILayout.HelpBox("В реестре нет оружия: " + RegistryPath, MessageType.Warning); return; }
            int index = Mathf.Max(0, Array.IndexOf(_weapons, _weapon));
            int next = EditorGUILayout.Popup("Оружие", index, _weapons.Select(w => $"{w.DisplayName} ({w.WeaponId})").ToArray());
            if (next != index || _weapon == null) { _weapon = _weapons[next]; _balance = null; _message = ""; }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(_weapon, typeof(WeaponInfo), false);
                    EditorGUILayout.ObjectField(_weapon.WeaponPrefab, typeof(GameObject), false);
                }
                if (GUILayout.Button("В Редакторе арсенала", GUILayout.Width(150)))
                    ArsenalEditorWindow.OpenWeapon(ArsenalEditorWindow.Tab.Catalog, AssetDatabase.GetAssetPath(_weapon.WeaponPrefab));
            }
        }

        // ── Аксессуары ───────────────────────────────────────────────

        private void DrawAccessories()
        {
            EditorGUILayout.LabelField("Витрина прицелов и аксессуаров", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Витрина строится в активной сцене (удобно рядом со стендом калибровки): коллиматоры, оптика, " +
                                    "механика — только готовые комплекты SightKit_*, глушители, фонари, рукоятки. Детали витрины — " +
                                    "экземпляры исходников пака без игровых компонентов, только для выбора.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Построить витрину в активной сцене"))
                    Run(() =>
                    {
                        Selection.activeGameObject = SightAccessoryShowcase.Build(SceneManager.GetActiveScene());
                        SceneView.lastActiveSceneView?.FrameSelected();
                        return "Витрина построена в сцене " + SceneManager.GetActiveScene().name + ".";
                    });
            EditorGUILayout.HelpBox("Установка аксессуара на оружие с записью в префаб — следующий модуль: нужна запись «набор " +
                                    "аксессуаров ствола» (что, на какую точку, в какой позе) и шаг единственного писателя префаба.",
                MessageType.None);
        }

        // ── Раскладка в слоте ────────────────────────────────────────

        private void DrawSlotLayout()
        {
            EditorGUILayout.LabelField("Раскладка в слотах арсенала", EditorStyles.boldLabel);
            if (_weapon.SlotLayouts.Count == 0)
                EditorGUILayout.LabelField("Своих раскладок нет — ствол стоит по умолчанию слота своего вида.", EditorStyles.wordWrappedLabel);
            foreach (var layout in _weapon.SlotLayouts)
            {
                if (layout == null) continue;
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(layout.SlotKind.ToString(), layout, typeof(ArsenalSlotLayout), false);
                EditorGUILayout.LabelField("Используется: " + ArsenalLayoutAuthoring.Usage(layout), EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.HelpBox("Позы оружия, магазина, карточки, опор и коробки приёма правятся на стенде раскладки: Move/Rotate " +
                                    "ручек, затем «Запечь» в его инспекторе. «Отдельная раскладка» там же заводит стволу свою.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Открыть стенд раскладки с этим оружием")) Run(OpenLayoutStand);
        }

        private string OpenLayoutStand()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Открытие стенда отменено.";
            Scene scene = EditorSceneManager.OpenScene(ArsenalLayoutAuthoringStand.ScenePath, OpenSceneMode.Single);
            var stand = scene.GetRootGameObjects().Select(g => g.GetComponentInChildren<ArsenalLayoutAuthoringStand>(true)).FirstOrDefault(s => s != null);
            if (stand == null) return "На сцене стенда нет ArsenalLayoutAuthoringStand: " + ArsenalLayoutAuthoringStand.ScenePath;
            var slot = stand.Slots.FirstOrDefault(s => s.Weapon == _weapon);
            Selection.activeGameObject = slot != null && slot.Item != null ? slot.Item.gameObject : stand.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            return slot != null
                ? $"Стенд открыт, выбрана ручка оружия {_weapon.WeaponId}. Слот выберите в инспекторе стенда."
                : $"{_weapon.WeaponId} нет в ассортименте стенда ({(stand.Preset != null ? stand.Preset.name : "без ассортимента")}).";
        }

        // ── Урон и разброс ───────────────────────────────────────────

        private void DrawBalance()
        {
            if (_balance == null || _balance.targetObject != _weapon) _balance = new SerializedObject(_weapon);
            _balance.Update();
            EditorGUILayout.LabelField("Паспорт и баланс", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Значения хранятся в WeaponInfo (общие для всех станций и карт). В префаб оружия и магазина их " +
                                    "переносит «Применить баланс»; руками эти поля в префабе не правятся.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                foreach (string name in BalanceFields)
                {
                    SerializedProperty property = _balance.FindProperty(name);
                    if (property != null) EditorGUILayout.PropertyField(property, true);
                }
                if (_balance.ApplyModifiedProperties()) _message = "WeaponInfo изменён (сохраняется с проектом). Префаб — после «Применить баланс».";
                EditorGUILayout.Space();
                using (new EditorGUI.DisabledScope(!_weapon.HasBalance))
                    if (GUILayout.Button("Применить баланс к префабу"))
                        Run(() =>
                        {
                            AssetDatabase.SaveAssetIfDirty(_weapon);
                            return WeaponBalanceApplier.Apply(new[] { _weapon });
                        });
                if (!_weapon.HasBalance) EditorGUILayout.LabelField("Урон 0 — баланс не задан, применять нечего.", EditorStyles.miniLabel);
            }
        }

        private void Run(Func<string> action)
        {
            try { _message = action(); }
            catch (Exception e) { _message = e.Message; }
            Repaint();
        }
    }
}
