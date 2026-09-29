using Mirror;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.UI.Menu
{
    /// <summary>
    /// Единственное правило планшета «показывать ли админку» (раздел «Админ», кнопки админа на
    /// «Обзоре», админские строки «Отладки»). Раньше решалось в четырёх местах по трём правилам, и
    /// шлем-хост плейтеста видел админку сразу.
    ///
    /// <para>
    /// Админка видна, если у машины есть права (<see cref="HasLocalAdminRights"/>) <b>и</b> это
    /// админская сборка-планшет (<see cref="ClientDeviceType.Tablet"/>, <c>BUILD_ROLE_ADMIN</c>)
    /// <b>или</b> включён режим отладки (<see cref="DebugMode"/>). Игроки в VR получают обычные
    /// права; админ сидит на отдельной сборке; на шлеме админка — инструмент отладки.
    /// Права это не проверяет — их проверяет сервер (<c>SessionPermissions</c>).
    /// </para>
    /// </summary>
    public static class MenuPermissions
    {
        /// <summary>Показывать ли админку на этой машине сейчас.</summary>
        public static bool ShowAdminUi() =>
            ShowAdminUi(HasLocalAdminRights(), LocalClientProfile.LocalDeviceType, DebugMode.Enabled);

        /// <summary>Правило (чистое, <c>MenuPermissionsTests</c>).</summary>
        public static bool ShowAdminUi(bool hasRights, ClientDeviceType device, bool debugEnabled) =>
            hasRights && (device == ClientDeviceType.Tablet || debugEnabled);

        /// <summary>Права админа у этой машины: хост, админская сборка или сессия с флагом админа (в т.ч. выданным режимом отладки).</summary>
        public static bool HasLocalAdminRights()
        {
            if (NetworkServer.active) return true;
            if (LocalClientProfile.IsLocalAdmin) return true;
            return PlayerSession.LocalSession != null && PlayerSession.LocalSession.IsAdmin;
        }
    }
}
