using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools
{
    /// <summary>
    /// Сообщает серверу состояние режима отладки этого устройства (<see cref="DebugModeNetwork.RequestAdmin"/>):
    /// при переключении режима и при каждой новой локальной сессии — после переподключения
    /// или смены сервера права выдаются заново, старые остались у прежней сессии.
    ///
    /// <para>
    /// Опрос раз в кадр двух полей, а не подписка на появление сессии: запрос можно слать
    /// только когда клиент готов (<c>NetworkClient.ready</c>), а сессия приходит отдельно;
    /// сравнение «что отправлено» с «что есть» закрывает любой порядок событий.
    /// </para>
    /// </summary>
    public sealed class DebugClientSync : MonoBehaviour
    {
        private uint _sentSessionNetId;
        private bool _sentState;

        private void Update()
        {
            PlayerSession session = PlayerSession.LocalSession;
            if (session == null)
            {
                _sentSessionNetId = 0;
                _sentState = false;
                return;
            }

            bool state = DebugMode.Enabled;
            bool sameSession = session.netId == _sentSessionNetId;

            // Новая сессия с выключенным режимом — сказать нечего: прав у неё и так нет.
            if (!sameSession && !state)
            {
                _sentSessionNetId = session.netId;
                _sentState = false;
                return;
            }

            if (sameSession && state == _sentState) return;

            if (DebugModeNetwork.RequestAdmin(state))
            {
                _sentSessionNetId = session.netId;
                _sentState = state;
            }
        }
    }
}
