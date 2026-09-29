using System.Collections.Generic;
using TMPro;
using UltimateXR.Avatar;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Метки попаданий режима отладки: в точке попадания по игроку — «✕ урон · зона · кто» на
    /// <see cref="Lifetime"/> секунд, повёрнутая к камере. Включается кнопкой «Показать попадания»
    /// (экран «Отладка»), гаснет с выключением режима.
    ///
    /// <para>
    /// Данные — событие урона на этой машине (<see cref="PlayerController.HitReceivedLocal"/>): пули
    /// симулирует каждая машина, так что метки видны и у клиента. Зона — по хитбоксу (<see cref="Hitbox"/>,
    /// T-36); урон, отменённый правилом режима (разминка), помечается «отменён». Своя табличка на
    /// <see cref="TextMeshPro"/>, без материалов — как <c>PerfOverlay</c>.
    /// </para>
    /// </summary>
    public sealed class DebugHitMarkers : MonoBehaviour
    {
        public const float Lifetime = 3f;
        private const int MaxMarkers = 24;

        private static DebugHitMarkers _instance;

        private readonly List<(TextMeshPro text, float dieAt)> _markers = new List<(TextMeshPro, float)>();

        public static bool Visible { get; private set; }

        public static void SetVisible(bool visible)
        {
            if (Visible == visible) return;
            Visible = visible;
            GameLog.Debug.Info($"[DebugMode] Метки попаданий {(visible ? "включены" : "выключены")}.");

            if (visible)
            {
                PlayerController.HitReceivedLocal += OnHit;
                DebugMode.Changed += OnDebugModeChanged;
            }
            else
            {
                PlayerController.HitReceivedLocal -= OnHit;
                DebugMode.Changed -= OnDebugModeChanged;
                if (_instance != null) _instance.ClearMarkers();
            }
        }

        /// <summary>Текст метки. Чистая функция — проверяется тестом.</summary>
        public static string Describe(float damage, bool canceled, bool hasZone, HitZone zone, string target)
        {
            string zoneText = hasZone ? ZoneName(zone) : "без зоны";
            string damageText = canceled ? $"{damage:0.#} (отменён)" : $"{damage:0.#}";
            return $"✕ {damageText} · {zoneText} · {target}";
        }

        public static string ZoneName(HitZone zone)
        {
            switch (zone)
            {
                case HitZone.Head: return "голова";
                case HitZone.Torso: return "торс";
                case HitZone.Arm: return "рука";
                case HitZone.Leg: return "нога";
                default: return zone.ToString();
            }
        }

        private static void OnDebugModeChanged(bool enabled)
        {
            if (!enabled) SetVisible(false);
        }

        private static void OnHit(PlayerController target, UxrDamageEventArgs e)
        {
            if (!Visible || target == null || e == null) return;

            Vector3 point = e.DamageType == UxrDamageType.ProjectileHit && e.RaycastHit.collider != null
                ? e.RaycastHit.point
                : target.transform.position + Vector3.up * 1.5f;
            bool hasZone = Hitbox.TryGetPart(e.RaycastHit.collider, out HitZone zone);

            GetOrCreate().Add(point, Describe(e.Damage, e.IsCanceled, hasZone, zone, target.AvatarPlayerName));
        }

        private static DebugHitMarkers GetOrCreate()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("DebugHitMarkers");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<DebugHitMarkers>();
            return _instance;
        }

        private void Add(Vector3 point, string label)
        {
            if (_markers.Count >= MaxMarkers)
            {
                if (_markers[0].text != null) Destroy(_markers[0].text.gameObject);
                _markers.RemoveAt(0);
            }

            var go = new GameObject("HitMarker");
            go.transform.SetParent(transform, false);
            go.transform.position = point;

            var text = go.AddComponent<TextMeshPro>();
            text.text = label;
            text.fontSize = 0.12f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = new Color(1f, 0.35f, 0.25f);
            text.rectTransform.pivot = new Vector2(0f, 0.5f);
            text.rectTransform.sizeDelta = new Vector2(0.8f, 0.1f);

            _markers.Add((text, Time.unscaledTime + Lifetime));
        }

        private void ClearMarkers()
        {
            foreach ((TextMeshPro text, float _) in _markers)
                if (text != null) Destroy(text.gameObject);
            _markers.Clear();
        }

        private void LateUpdate()
        {
            Transform camera = UxrAvatar.LocalAvatar != null ? UxrAvatar.LocalAvatar.CameraTransform
                             : Camera.main != null ? Camera.main.transform : null;

            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                (TextMeshPro text, float dieAt) = _markers[i];
                if (text == null || Time.unscaledTime > dieAt)
                {
                    if (text != null) Destroy(text.gameObject);
                    _markers.RemoveAt(i);
                    continue;
                }

                if (camera != null)
                    text.transform.rotation = Quaternion.LookRotation(text.transform.position - camera.position);
            }
        }
    }
}
