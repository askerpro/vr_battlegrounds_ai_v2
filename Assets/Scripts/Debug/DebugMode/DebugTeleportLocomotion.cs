using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Locomotion;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Единственный владелец включения телепорта дугой со стика (<see cref="UxrTeleportLocomotionBase"/>,
    /// <c>TeleportLeft</c>/<c>TeleportRight</c> на <c>PlayerBase</c>) у локального аватара. Игра ходит
    /// физически, телепорт — инструмент отладки: он включён только в режиме отладки этого устройства
    /// (<see cref="DebugMode.Enabled"/>) и только пока его никто не приглушил (<see cref="Suppress"/>,
    /// например открытое меню, где стик листает планшет). Права админа на это не влияют.
    ///
    /// <para>
    /// Префаб оставляет компоненты включёнными; служба приводит их к нужному состоянию каждый кадр.
    /// Другие скрипты <c>enabled</c> телепорта не пишут — только просят
    /// <see cref="Suppress"/> / <see cref="Release"/>.
    /// </para>
    /// </summary>
    public sealed class DebugTeleportLocomotion : MonoBehaviour
    {
        private static readonly HashSet<object> Suppressors = new HashSet<object>();

        private readonly List<UxrTeleportLocomotionBase> _teleports = new List<UxrTeleportLocomotionBase>();
        private UxrAvatar _avatar;

        /// <summary>Телепорт дугой сейчас разрешён: режим отладки включён и никто его не приглушил.</summary>
        public static bool Allowed => DebugMode.Enabled && Suppressors.Count == 0;

        /// <summary>Приглушить телепорт на время, пока <paramref name="owner"/> не вызовет <see cref="Release"/>.</summary>
        public static void Suppress(object owner)
        {
            if (owner != null) Suppressors.Add(owner);
        }

        public static void Release(object owner)
        {
            if (owner != null) Suppressors.Remove(owner);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Suppressors.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject(nameof(DebugTeleportLocomotion));
            DontDestroyOnLoad(go);
            go.AddComponent<DebugTeleportLocomotion>();
        }

        private void OnEnable() => UxrAvatar.LocalAvatarStarted += OnLocalAvatarStarted;

        private void OnDisable() => UxrAvatar.LocalAvatarStarted -= OnLocalAvatarStarted;

        private void OnLocalAvatarStarted(object sender, UxrAvatarStartedEventArgs e) => Apply();

        private void Update() => Apply();

        private void Apply()
        {
            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            if (avatar != _avatar)
            {
                _avatar = avatar;
                _teleports.Clear();
                if (avatar != null) avatar.GetComponentsInChildren(true, _teleports);

                // Шаг назад (стик вниз) отключён (решение пользователя 2026-10-25): он искал пол по
                // BlockingTargetLayers без Ground и сквозь пол не находил опоры.
                foreach (UxrTeleportLocomotionBase teleport in _teleports)
                    if (teleport != null) teleport.AllowJoystickBackStep = false;
            }

            bool allowed = Allowed;
            int changed = 0;
            foreach (UxrTeleportLocomotionBase teleport in _teleports)
            {
                if (teleport == null || teleport.enabled == allowed) continue;
                teleport.enabled = allowed;
                changed++;
            }

            if (changed > 0)
                GameLog.Player.Verbose($"[DebugTeleportLocomotion] Телепорт дугой {(allowed ? "включён" : "выключен")} ({changed}): " +
                                       $"режим отладки {(DebugMode.Enabled ? "вкл" : "выкл")}, приглушений {Suppressors.Count}.", this);
        }
    }
}
