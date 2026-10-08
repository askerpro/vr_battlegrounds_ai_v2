using System.Collections.Generic;
using System.Text;
using TMPro;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Дебаг-панель состояния оружия в шлеме (этапы C/D WeaponSystem): над каждым огнестрелом в радиусе
    /// <see cref="Radius"/> от головы локального игрока — мелкий world-space текст, повёрнутый к камере HMD:
    /// имя и где лежит ствол, учёт SDK (фаза, патронник, запас/ёмкость, ревизия, барьер приёма), ход Action и хват,
    /// у ствола на машине (<see cref="WeaponSystem"/>) — жест, эпизод, клип, строка таблицы, цель позы и отказы учёта
    /// (красным, если были). Ствол без учёта готовности показывает патроны SDK.
    ///
    /// Только редактор (<see cref="WeaponStatePanelSettings"/>). Префабы и сцены не меняются: один объект-менеджер
    /// создаётся во время игры в DontDestroyOnLoad (без HideFlags.DontSave — такие объекты переживают выход из Play), панели — его дети, не дети оружия: хват, прицел и физика
    /// ствола не затронуты. Коллайдеров нет, слой <c>Ignore Raycast</c>. Текст пересобирается 8 раз в секунду
    /// в переиспользуемый <see cref="StringBuilder"/> и передаётся в TMP только при изменении.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class WeaponStatePanels : MonoBehaviour
    {
        public const float Radius = 3f;
        private const float TextInterval = 0.125f;
        private const float ScanInterval = 0.5f;
        private const float FontSize = 0.2f;
        private static readonly Vector3 Offset = new Vector3(0f, 0.17f, 0f);

        private static WeaponStatePanels s_instance;

        private sealed class Panel
        {
            public UxrFirearmWeapon Weapon;
            public WeaponSystem Host;
            public UxrFirearmMag Store;
            public string Name;
            public TextMeshPro Text;
            public readonly StringBuilder Current = new StringBuilder(256);
            public readonly StringBuilder Shown = new StringBuilder(256);
        }

        private readonly Dictionary<UxrFirearmWeapon, Panel> _panels = new Dictionary<UxrFirearmWeapon, Panel>();
        private readonly List<UxrFirearmWeapon> _removed = new List<UxrFirearmWeapon>();
        private float _nextText, _nextScan;
        private int _layer;

        /// <summary>Сколько панелей сейчас видно (для проб).</summary>
        public static int VisibleCount
        {
            get
            {
                if (s_instance == null) return 0;
                int count = 0;
                foreach (Panel panel in s_instance._panels.Values) if (panel.Text != null && panel.Text.gameObject.activeSelf) count++;
                return count;
            }
        }

        /// <summary>Текст панели ствола (для проб), null — панели нет.</summary>
        public static string TextOf(UxrFirearmWeapon weapon) =>
            s_instance != null && weapon != null && s_instance._panels.TryGetValue(weapon, out Panel panel) && panel.Text != null ? panel.Text.text : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => s_instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            WeaponStatePanelSettings.Changed -= OnSettingChanged;
            WeaponStatePanelSettings.Changed += OnSettingChanged;
            if (WeaponStatePanelSettings.Enabled) Ensure();
        }

        private static void OnSettingChanged(bool enabled)
        {
            if (!Application.isPlaying) return;
            if (enabled) Ensure();
            else if (s_instance != null) Destroy(s_instance.gameObject);
        }

        private static void Ensure()
        {
            if (s_instance != null) return;
            var go = new GameObject("WeaponStatePanels");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<WeaponStatePanels>();
        }

        private void Awake()
        {
            int layer = LayerMask.NameToLayer("Ignore Raycast");
            _layer = layer >= 0 ? layer : 0;
            gameObject.layer = _layer;
        }

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        private void LateUpdate()
        {
            if (!WeaponStatePanelSettings.Enabled) { Destroy(gameObject); return; }
            Transform head = UxrAvatar.LocalAvatar != null ? UxrAvatar.LocalAvatar.CameraTransform : null;
            if (head == null) { SetAllVisible(false); return; }

            float now = Time.unscaledTime;
            if (now >= _nextScan) { _nextScan = now + ScanInterval; Scan(head.position); }
            bool rebuild = now >= _nextText;
            if (rebuild) _nextText = now + TextInterval;

            float radiusSq = Radius * Radius;
            _removed.Clear();
            foreach (KeyValuePair<UxrFirearmWeapon, Panel> pair in _panels)
            {
                Panel panel = pair.Value;
                if (pair.Key == null || panel.Text == null) { _removed.Add(pair.Key); continue; }
                Vector3 anchor = pair.Key.transform.position + Offset;
                bool visible = pair.Key.isActiveAndEnabled && (anchor - head.position).sqrMagnitude <= radiusSq;
                if (panel.Text.gameObject.activeSelf != visible) panel.Text.gameObject.SetActive(visible);
                if (!visible) continue;
                Transform t = panel.Text.transform;
                t.position = anchor;
                Vector3 look = anchor - head.position;
                if (look.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(look, Vector3.up);
                if (rebuild) Refresh(panel, now);
            }
            foreach (UxrFirearmWeapon weapon in _removed)
            {
                if (_panels.TryGetValue(weapon, out Panel panel) && panel.Text != null) Destroy(panel.Text.gameObject);
                _panels.Remove(weapon);
            }
        }

        private void Scan(Vector3 head)
        {
            float radiusSq = (Radius + 0.5f) * (Radius + 0.5f);
            foreach (UxrFirearmWeapon weapon in FindObjectsByType<UxrFirearmWeapon>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (_panels.ContainsKey(weapon) || (weapon.transform.position - head).sqrMagnitude > radiusSq) continue;
                _panels.Add(weapon, Create(weapon));
            }
        }

        private Panel Create(UxrFirearmWeapon weapon)
        {
            var panel = new Panel
            {
                Weapon = weapon,
                Host = weapon.GetComponent<WeaponSystem>(),
                Name = weapon.name.Replace("_instance", "").Replace("(Clone)", "")
            };
            foreach (UxrFirearmMag mag in weapon.GetComponentsInChildren<UxrFirearmMag>(true))
                if (mag.IsFixedAmmoStore && mag.FixedStoreWeapon == weapon) { panel.Store = mag; break; }
            var go = new GameObject("WeaponStatePanel " + panel.Name) { layer = _layer };
            go.transform.SetParent(transform, false);
            TextMeshPro text = go.AddComponent<TextMeshPro>();
            text.font = TMP_Settings.defaultFontAsset; // общий материал шрифта: без него TMP, созданный из кода, рисуется ошибочным шейдером
            if (text.font != null) text.fontSharedMaterial = text.font.material;
            text.fontSize = FontSize;
            text.alignment = TextAlignmentOptions.Bottom;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.richText = true;
            text.raycastTarget = false;
            text.color = new Color(0.85f, 1f, 0.85f);
            text.rectTransform.pivot = new Vector2(0.5f, 0f);
            text.rectTransform.sizeDelta = new Vector2(0.6f, 0.12f);
            panel.Text = text;
            return panel;
        }

        private static void Refresh(Panel panel, float now)
        {
            UxrFirearmWeapon weapon = panel.Weapon;
            StringBuilder sb = panel.Current;
            sb.Clear();
            sb.Append(panel.Name).Append("  ").Append(Where(weapon)).Append('\n');

            if (weapon.UsesReadinessLedger(0))
            {
                UxrFirearmReadinessState s = weapon.GetReadinessState(0);
                sb.Append(Phase(panel, weapon, s)).Append("  C").Append(s != null && s.ChamberRound ? 1 : 0)
                  .Append("  M").Append(weapon.GetMagazineRounds(0)).Append('/').Append(Capacity(panel, weapon))
                  .Append("  r").Append(s != null ? s.Revision : 0u);
                if (weapon.IsAmmoAdmissionPending(0)) sb.Append("  <color=#FFD040>барьер</color>");
                sb.Append('\n');
                // Расхождения учёта процесса (C2): дельта M, форк ревизии, поправки. Должно быть пусто.
                int delta = WeaponLedgerIntegrity.DeltaMismatchTotal, fork = WeaponLedgerIntegrity.ForkTotal,
                    fixes = WeaponLedgerIntegrity.CorrectionsPublished + WeaponLedgerIntegrity.CorrectionsApplied,
                    notify = WeaponLedgerIntegrity.NotificationFailures;
                if (delta + fork + fixes + notify > 0)
                    sb.Append("<color=#FF5040>учёт: ΔM ").Append(delta).Append("  форк ").Append(fork)
                      .Append("  попр ").Append(fixes).Append("  увед ").Append(notify).Append("</color>\n");
            }
            else sb.Append("SDK: патроны ").Append(weapon.GetAmmoLeft(0)).Append('/').Append(weapon.GetAmmoCapacity(0)).Append('\n');

            WeaponSystem host = panel.Host;
            if (host != null && host.IsConfigured && host.Rig.HasAction)
            {
                sb.Append("ход ").Append(Mathf.RoundToInt(host.ActionProgress * 100f)).Append('%');
                if (host.Rig.IsHandleHeld) sb.Append("  рука");
                sb.Append(host.IsActionAtRest ? "  покой" : host.IsActionAtValidatedRear ? "  задержка" : "");
                sb.Append('\n');
            }
            if (host != null && host.IsConfigured)
            {
                VrBattlegrounds.Weapons.Core.WeaponMachineState state = host.MachineState;
                sb.Append("машина: ").Append(state.Gesture).Append(' ').Append(state.Episode);
                if (state.Clip != VrBattlegrounds.Weapons.Core.ClipKind.None) sb.Append(' ').Append(state.Clip);
                sb.Append("  ").Append(host.LastRowId ?? "—").Append("  поза ").Append(host.LastPose.Action);
                if (host.HintOn) sb.Append("  подсказка");
                if (host.CommandsRejected + host.TableViolations > 0)
                    sb.Append("\n<color=#FF5040>отказы учёта ").Append(host.CommandsRejected)
                      .Append("  ошибки таблицы ").Append(host.TableViolations).Append("</color>");
            }

            if (Same(sb, panel.Shown)) return;
            panel.Shown.Clear();
            panel.Shown.Append(sb);
            panel.Text.SetText(sb);
        }

        private static string Where(UxrFirearmWeapon weapon)
        {
            if (!weapon.TryGetTriggerGrip(0, out UxrGrabbableObject grip, out _) || grip == null) return "";
            if (UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(grip)) return "в руке";
            UxrGrabbableObjectAnchor anchor = grip.CurrentAnchor;
            return anchor != null && anchor.GetComponentInParent<UxrAvatar>() != null ? "в кармане" : anchor != null ? "в гнезде" : "лежит";
        }

        private static string Phase(Panel panel, UxrFirearmWeapon weapon, UxrFirearmReadinessState s)
        {
            if (s == null || !s.ReadinessInitialized) return "Uninit";
            if (weapon.IsReadinessCyclePending(0)) return s.ChamberRound ? "CycleLoaded" : "CycleCleared";
            if (s.ActionOpen) return "OpenIdle";
            if (s.ChamberRound) return "Ready";
            if (s.PostShotEmptyAction)
                return panel.Host != null && panel.Host.Profile != null && panel.Host.Profile.EmptyPose == WeaponEmptyPose.HoldOpen &&
                       panel.Host.Profile.PhysicalCapability == WeaponPhysicalCapability.ActionTravel
                    ? "HoldOpen" : "EmptyAwaitRest";
            return "Empty";
        }

        private static int Capacity(Panel panel, UxrFirearmWeapon weapon)
        {
            if (panel.Store != null) return panel.Store.Capacity;
            if (!weapon.TryGetTriggerMagazineAnchor(0, out UxrGrabbableObjectAnchor anchor) || anchor == null || anchor.CurrentPlacedObject == null) return 0;
            UxrFirearmMag mag = anchor.CurrentPlacedObject.GetComponent<UxrFirearmMag>();
            return mag != null ? mag.Capacity : 0;
        }

        private static bool Same(StringBuilder a, StringBuilder b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private void SetAllVisible(bool visible)
        {
            foreach (Panel panel in _panels.Values)
                if (panel.Text != null && panel.Text.gameObject.activeSelf != visible) panel.Text.gameObject.SetActive(visible);
        }
    }
}
