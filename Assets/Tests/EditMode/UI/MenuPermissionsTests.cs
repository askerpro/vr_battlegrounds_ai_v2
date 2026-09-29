using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.UI.Menu;

namespace VrBattlegrounds.Tests.UI
{
    /// <summary>
    /// Правило «показывать ли админку на планшете» (<see cref="MenuPermissions"/>) и сторож класса:
    /// ни один файл меню не решает это сам. Раньше решалось в четырёх местах по трём правилам, и
    /// шлем-хост плейтеста видел раздел «Админ» без режима отладки.
    /// </summary>
    public class MenuPermissionsTests
    {
        [Test]
        public void Игроку_в_VR_админка_только_в_режиме_отладки()
        {
            Assert.IsFalse(MenuPermissions.ShowAdminUi(hasRights: true, ClientDeviceType.VR, debugEnabled: false),
                "Шлем-хост (права есть) без режима отладки видит админку.");
            Assert.IsTrue(MenuPermissions.ShowAdminUi(true, ClientDeviceType.VR, true));
        }

        [Test]
        public void Админская_сборка_планшет_видит_админку_всегда()
        {
            Assert.IsTrue(MenuPermissions.ShowAdminUi(true, ClientDeviceType.Tablet, false));
        }

        [Test]
        public void Без_прав_админки_нет_ни_при_каком_режиме()
        {
            Assert.IsFalse(MenuPermissions.ShowAdminUi(false, ClientDeviceType.VR, true),
                "Режим отладки без выданных сервером прав — админку не показывать.");
            Assert.IsFalse(MenuPermissions.ShowAdminUi(false, ClientDeviceType.Tablet, true));
        }

        /// <summary>
        /// Сторож класса: признаки «я админ» (хост, профиль, флаг сессии, серверная проверка прав)
        /// в коде меню читает только <see cref="MenuPermissions"/>.
        /// </summary>
        [Test]
        public void Меню_решает_про_админку_только_через_MenuPermissions()
        {
            string root = Path.Combine(Application.dataPath, "Scripts/UI/Menu");
            string[] markers = { "LocalClientProfile.IsLocalAdmin", "SessionPermissions.IsAdmin(", ".IsAdmin)", "LocalSession.IsAdmin" };

            var offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => Path.GetFileName(f) != "MenuPermissions.cs")
                .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, i)))
                .Where(x => !x.line.TrimStart().StartsWith("///") && !x.line.TrimStart().StartsWith("//")
                            && markers.Any(m => x.line.Contains(m)))
                .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
                .ToArray();

            Assert.IsEmpty(offenders, "Проверка «админ ли» в обход MenuPermissions:\n" + string.Join("\n", offenders));
        }
    }
}
