using Mirror;
using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.E2E.Input;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Input;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Комплект бота на вводе устройства (контракт tasks/bots-input/contracts/bot-input-design.md): единственный
    /// бот-компонент тела. Подключает к аватару устройство ввода (<see cref="AvatarInputDevice"/>) с источником бота
    /// (<see cref="BotAddOptions.InputSource"/>) — дальше тело ведёт код человека: стадия Update контроллера, хват по Grip,
    /// курок, IK и ноги от головы. Корень не пишет никто.
    ///
    /// <para>
    /// Только сервер под <see cref="StateEventAuthority.IsWorldAuthority"/>, аватар без владельца. На теле с прежним
    /// комплектом (<see cref="BotAvatarDriver"/>) не подключается — два писателя тела запрещены. Тело уничтожено — устройство
    /// снимается; новое тело подключает директор (<see cref="BotDirector"/>, по <c>AvatarSpawned</c>).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotInputKit : MonoBehaviour
    {
        private IAvatarInputSource _source;
        private bool _keepHead;
        private AvatarInputDevice _device;

        /// <summary>Устройство тела; null — не подключено (отказ записан в лог).</summary>
        public AvatarInputDevice Device => _device;

        /// <summary>Источник ввода бота.</summary>
        public IAvatarInputSource Source => _source;

        /// <summary>Причина отказа подключения; null — подключено или ещё не пробовали.</summary>
        public string Refusal { get; private set; }

        /// <summary>Собрать комплект на теле и подключить ввод. Повтор для того же тела ничего не делает.</summary>
        public static BotInputKit Attach(PlayerController avatar, IAvatarInputSource source, bool keepHead)
        {
            if (avatar == null || source == null) return null;
            if (avatar.TryGetComponent(out BotInputKit existing)) return existing;

            BotInputKit kit = avatar.gameObject.AddComponent<BotInputKit>();
            kit._source = source;
            kit._keepHead = keepHead;
            kit.Connect();
            return kit;
        }

        private void Connect()
        {
            if (_device != null || _source == null) return;

            UxrAvatar avatar = GetComponent<UxrAvatar>();
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority)
                Refusal = "не сервер (автор мира)";
            else if (TryGetComponent(out BotAvatarDriver _))
                Refusal = "на теле прежний комплект player-flow (BotAvatarDriver) — второй писатель тела запрещён";
            if (Refusal != null)
            {
                GameLog.Player.Error($"[BotInputKit] {name}: {Refusal}. Ввод не подан.", this);
                return;
            }

            if (_source is IAvatarBoundInputSource bound) bound.Bind(avatar, _keepHead);
            Refusal = AvatarInputDevice.Connect(avatar, _source, out _device);
            if (Refusal == null)
                GameLog.Player.Info($"[BotInputKit] {name}: бот на вводе устройства, источник {_source.GetType().Name}.", this);
        }

        private void OnDestroy()
        {
            if (_device != null) _device.Disconnect();
            _device = null;
        }
    }
}
