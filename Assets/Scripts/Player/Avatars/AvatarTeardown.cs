using Mirror;
using UltimateXR.Manipulation;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Освобождает аватар перед тем, как сервер его уничтожит: смена скина или команды
    /// (<see cref="AvatarManager.ChangeAvatar"/> — с изъятием снаряжения,
    /// <see cref="ConfiscateBeforeDestroy"/>), смена тела погибшего на призрака
    /// (<see cref="ReleaseBeforeDestroy"/>), отключение игрока (<c>GameNetworkManager.OnServerDisconnect</c>).
    /// Мёртвое тело без смены освобождается так же, только без уничтожения
    /// (<see cref="ReleaseDeadBodyOnServer"/>). Других путей сброса снаряжения при смерти нет.
    ///
    /// <para>
    /// <b>Зачем.</b> Рука UltimateXR, уничтоженная вместе с аватаром, ничего не отпускает:
    /// <c>UxrGrabber</c> зовёт отпускание из <c>OnDisable</c>, но к этому моменту уже выпал из
    /// списка включённых компонентов, и SDK молча пропускает вызов. В
    /// <c>UxrGrabManager</c> остаётся захват с мёртвой рукой, и первый же телепорт
    /// с затемнением обрывался на исключении, оставляя экран чёрным (Issue 17 в
    /// <c>Docs/UltimateXR/known-issues.md</c>). Предметы в кобурах и магазины в кармане —
    /// сетевые объекты Mirror, и уничтожение их как дочерних обходит сеть: у клиентов и в
    /// <c>SyncList</c> кармана оставались висячие <c>netId</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Порядок.</b> Сначала руки (<c>UxrGrabManager.ReleaseObject</c> на каждую держащую руку),
    /// потом снаряжение (<see cref="PlayerLoadoutManager.ServerDropEquipment"/>). Отпускание
    /// на сервере уходит клиентам каналом состояния UltimateXR — тем же надёжным каналом, что
    /// и сообщение Mirror об уничтожении, и раньше него, так что клиенты успевают отпустить
    /// предмет, пока рука ещё жива.
    /// </para>
    ///
    /// <para>
    /// Смена карты сюда не заходит: предметы уничтожаются вместе со сценой, и каждый снимает
    /// свой захват сам (<c>UxrGrabbableObject.GlobalDisabled</c>).
    /// </para>
    /// </summary>
    public static class AvatarTeardown
    {
        /// <summary>
        /// Отпускает всё, что держат руки аватара, и снимает с него снаряжение.
        /// Зовётся на сервере непосредственно перед <c>NetworkServer.Destroy</c> аватара.
        /// </summary>
        /// <param name="avatar">Уничтожаемый аватар; null — ничего не делать.</param>
        /// <param name="reason">Причина для лога: «смена скина», «отключение».</param>
        public static void ReleaseBeforeDestroy(PlayerController avatar, string reason)
        {
            if (avatar == null) return;

            // Локальное (планшет) сервер не видит — его отпускает владелец, до уничтожения.
            if (NetworkServer.active && avatar.isServer) avatar.ServerReleaseLocalItems();

            ReleaseHandsAndEquipment(avatar, reason);

            GameLog.Player.Info($"[AvatarTeardown] {avatar.name}: освобождён перед уничтожением ({reason}).", avatar);
        }

        /// <summary>
        /// То же освобождение, но снаряжение <b>изымается</b> (уничтожается), а не роняется —
        /// смена скина или команды (<see cref="AvatarManager.ChangeAvatar"/>, T-35). Упавший
        /// ствол пережил бы смену и остался ничьим; стена арсенала в разминке сама пополнит
        /// слот, чьё оружие уничтожено. Остальное в руках (планшет, жетон) только отпускается.
        /// </summary>
        public static void ConfiscateBeforeDestroy(PlayerController avatar, string reason)
        {
            if (avatar == null) return;

            EquipmentStrip.ServerStrip(avatar, reason);
            ReleaseBeforeDestroy(avatar, reason);
        }

        /// <summary>
        /// Мёртвое тело без смены (стратегия без призрака — <see cref="AvatarManager"/>): руки отпускают
        /// и снаряжение падает тем же шагом и в том же порядке, что в <see cref="ReleaseBeforeDestroy"/>.
        /// Тело живёт дальше мёртвым и без этого держало бы предмет в руке и оружие в кобуре до
        /// возрождения: ни владелец, ни обработчики смерти больше ничего не отпускают и не роняют
        /// (death-body-defer). Сервер, с верхнего уровня — одно <c>ReleaseObject</c> на руку и снятие
        /// с якоря, которые доходят до клиентов.
        ///
        /// <para>
        /// Планшет и прочее локальное (<see cref="PlayerController.ServerReleaseLocalItems"/>) не
        /// трогается: тело не уничтожается, локальное остаётся владельцу.
        /// </para>
        /// </summary>
        /// <param name="avatar">Мёртвое тело; null — ничего не делать.</param>
        /// <param name="reason">Причина для лога.</param>
        public static void ReleaseDeadBodyOnServer(PlayerController avatar, string reason)
        {
            if (avatar == null || !NetworkServer.active || !StateEventAuthority.IsWorldAuthority) return;

            ReleaseHandsAndEquipment(avatar, reason);
            GameLog.Player.Info($"[AvatarTeardown] {avatar.name}: мёртвое тело без смены — руки отпущены, снаряжение сброшено ({reason}).", avatar);
        }

        /// <summary>
        /// Общий шаг освобождения: сначала руки (<see cref="ReleaseHands"/>), потом снаряжение
        /// (<see cref="PlayerLoadoutManager.ServerDropEquipment"/>) — порядок из описания класса.
        /// </summary>
        private static void ReleaseHandsAndEquipment(PlayerController avatar, string reason)
        {
            ReleaseHands(avatar);

            // Снаряжение — сетевые объекты, трогает их только сервер. Вне сервера
            // (EditMode-тест) [Server]-метод всё равно заглушится, но с шумом в лог.
            if (NetworkServer.active)
            {
                PlayerLoadoutManager loadout = avatar.GetComponent<PlayerLoadoutManager>();
                if (loadout != null) loadout.ServerDropEquipment(reason);
            }
        }

        /// <summary>
        /// Отпускает предметы в руках аватара.
        ///
        /// <para>
        /// Руки берутся прямо из иерархии, а не через <see cref="PlayerGrabManager.ReleaseAllGrabbedObjects"/>:
        /// тот держит список рук, собранный в своём <c>Awake</c>, и молчит, если синглтон
        /// <c>UxrGrabManager</c> Unity-null (в EditMode он всегда такой, и тест этого пути
        /// был бы невозможен). Здесь менеджер зовётся так же, как его зовёт сам SDK в
        /// <c>UxrGrabber.OnDestroy</c>: раз рука что-то держит, менеджер существует.
        /// </para>
        /// </summary>
        private static void ReleaseHands(PlayerController avatar)
        {
            foreach (UxrGrabber grabber in avatar.GetComponentsInChildren<UxrGrabber>(true))
            {
                if (grabber != null && grabber.GrabbedObject != null)
                {
                    UxrGrabManager.Instance.ReleaseObject(grabber, grabber.GrabbedObject, true);
                }
            }
        }
    }
}
