using Mirror;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Сообщение для подключения полноценного участника матча (Player).
    ///
    /// <para>
    /// <b>Про место игрока.</b> Здесь лежала <b>мировая</b> позиция аватара с той карты,
    /// где клиент был последний раз, а сервер применял её как точку спавна дословно и
    /// <b>всем подряд</b> — находка <b>CAL-02</b>. Обе ошибки исправлены на месте:
    /// поза едет в системе координат якорей (<c>PhysicalSpaceAnchorFrame</c>), одинаковой
    /// на всех картах проекта, а применить её или нет решает сервер по признаку
    /// калибровки — тем же правилом, что и при смене карты (T-30):
    /// <b>откалиброванный</b> сохраняет своё физическое место, <b>неоткалиброванный</b>
    /// появляется в зоне своей команды.
    /// </para>
    ///
    /// <para>
    /// Калибровка игрока (пол, рост, признак калибровки по якорям) приходит здесь же, а не командой
    /// сессии: точку спавна сервер выбирает в момент обработки этого сообщения, то есть до того,
    /// как у клиента появится сессия (T-50). Клиент без данных (перезапуск) калибровку не несёт —
    /// тогда сервер берёт снимок отключённой сессии.
    /// </para>
    /// </summary>
    public struct GamePlayerConnectMessage : NetworkMessage
    {
        public string deviceToken; // Уникальный ключ
        public ClientDeviceType deviceType; // VR, ПК
        public int teamId;
        public int avatarId;

        /// <summary>Машина знает калибровку своего игрока (<c>LocalPlayerCalibration.HasValue</c>).</summary>
        public bool hasCalibration;

        /// <summary>
        /// Калибровка своего игрока. Проверяет и обрезает сервер (<c>PlayerSession.ServerAcceptCalibration</c>).
        /// Признаком калибровки по якорям врать невыгодно: соврав, игрок получит своё прежнее место
        /// в арене вместо базы команды.
        /// </summary>
        public PhysicalSpaceUtils.PlayerCalibration calibration;

        /// <summary>Клиенту есть что сказать о своём месте: замер снят и переводим.</summary>
        public bool hasAnchorPlace;

        /// <summary>Позиция аватара в системе координат якорей той карты, где он стоял.</summary>
        public UnityEngine.Vector3 anchorPlacePosition;

        /// <summary>Поворот аватара в той же системе координат.</summary>
        public UnityEngine.Quaternion anchorPlaceRotation;

        /// <summary>Имя карты, на которой снят замер. Нужно только для строки в логе сервера.</summary>
        public string anchorPlaceMap;

        public GamePlayerConnectMessage(string token, ClientDeviceType device, int team, int avatar)
        {
            deviceToken = token;
            deviceType = device;
            teamId = team;
            avatarId = avatar;

            hasCalibration = PhysicalSpaceUtils.LocalPlayerCalibration.HasValue;
            calibration = PhysicalSpaceUtils.LocalPlayerCalibration.Current;
            hasAnchorPlace = false;
            anchorPlacePosition = UnityEngine.Vector3.zero;
            anchorPlaceRotation = UnityEngine.Quaternion.identity;
            anchorPlaceMap = string.Empty;

            PhysicalSpaceUtils.PhysicalSpaceSyncManager sync = PhysicalSpaceUtils.PhysicalSpaceSyncManager.Instance;
            if (sync == null) return;

            UnityEngine.Vector3 place;
            UnityEngine.Quaternion rotation;
            string map;
            if (sync.TryGetSavedAvatarPlace(out place, out rotation, out map))
            {
                hasAnchorPlace = true;
                anchorPlacePosition = place;
                anchorPlaceRotation = rotation;
                anchorPlaceMap = map;
            }
        }
    }
}
