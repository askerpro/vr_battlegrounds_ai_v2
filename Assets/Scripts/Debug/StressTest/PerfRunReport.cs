using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Итог прогона на одной машине — уходит в <c>summary.json</c>. Клиент и выделенный
    /// сервер пишут каждый свой.
    /// </summary>
    [Serializable]
    public sealed class PerfRunReport
    {
        public string startedAt;
        public string role;
        public string device;
        public string gpu;
        public string unityVersion;
        public bool   developmentBuild;
        public string scene;
        public float  refreshRate;
        public string display;        // FFR и OVRPlugin — только запись, см. OculusPerfStats
        public int    puppetCount;
        public int    clutterCount;
        public string puppetSkins = "";   // префабы кукол за весь прогон, через запятую
        public bool   completed;
        public string endReason;
        public List<PerfPhaseReport> phases = new List<PerfPhaseReport>();

        /// <summary>Каталог прогона: <c>persistentDataPath/perf/&lt;время&gt;</c>.</summary>
        public string Directory => Path.Combine(Application.persistentDataPath, "perf", startedAt);

        public static PerfRunReport Create(string role, int puppets, int clutter)
        {
            return new PerfRunReport
            {
                startedAt        = UniqueStartedAt(DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)),
                role             = role,
                device           = SystemInfo.deviceModel,
                gpu              = SystemInfo.graphicsDeviceName,
                unityVersion     = Application.unityVersion,
                developmentBuild = UnityEngine.Debug.isDebugBuild,
                scene            = SceneManager.GetActiveScene().name,
                refreshRate      = ReadRefreshRate(),
                display          = OculusPerfStats.DescribeDisplay(),
                puppetCount      = puppets,
                clutterCount     = clutter,
            };
        }

        /// <summary>
        /// Имя каталога прогона, свободное на этой машине. Выделенный сервер и клиент на одном ПК
        /// (редактор + <c>VrBattlegroundsServer.exe</c>) делят <c>persistentDataPath</c> и начинают
        /// прогон в одну секунду — один <c>perf.log</c> на двоих давал IOException у второго, а
        /// исключение в обработчике сообщения Mirror рвёт соединение клиента.
        /// </summary>
        private static string UniqueStartedAt(string stamp)
        {
            string root = Path.Combine(Application.persistentDataPath, "perf");
            string prefix = stamp + "_" + RoleTag() + "_";
            string name = prefix + "1";

            // Номер экземпляра — первый свободный: несколько клиентов (Multiplayer Play Mode) на одной
            // машине в ту же секунду получают _1, _2, …
            for (int i = 2; System.IO.Directory.Exists(Path.Combine(root, name)); i++)
            {
                name = prefix + i.ToString(CultureInfo.InvariantCulture);
            }

            // Занять сразу: второй процесс, начавший в ту же секунду, увидит каталог и возьмёт суффикс.
            System.IO.Directory.CreateDirectory(Path.Combine(root, name));
            return name;
        }

        /// <summary>Роль приложения для имени каталога: server, host или client.</summary>
        private static string RoleTag()
        {
            bool server = Mirror.NetworkServer.active;
            bool client = Mirror.NetworkClient.active;
            return server && client ? "host" : server ? "server" : "client";
        }

        public float BudgetMs => PerfStats.BudgetMs(refreshRate);

        public string BuildHeader()
        {
            string build = developmentBuild ? "Development" : "Release";
            return "Стресс-тест VR Battlegrounds\n" +
                   $"Роль: {role}. Начат: {startedAt}. Сцена: {scene}.\n" +
                   $"Устройство: {device}, GPU: {gpu}, Unity {unityVersion}, {build}-сборка.\n" +
                   FormattableString.Invariant($"Частота дисплея: {refreshRate:F0} Гц.") +
                   // Клиент заводит лог до первой фазы и числа нагрузки ещё не знает — они придут с фазами.
                   (puppetCount > 0 ? $" Кукол: {puppetCount}, предметов: {clutterCount}." : "") +
                   (!string.IsNullOrEmpty(puppetSkins) ? $" Скины кукол: {puppetSkins}." : "") +
                   "\n" + display;
        }

        /// <summary>
        /// Дописывает в <see cref="puppetSkins"/> имена из списка через запятую, без повторов:
        /// в прогоне по скинам каждая фаза приносит свой.
        /// </summary>
        public void AddSkins(string skins)
        {
            if (string.IsNullOrEmpty(skins)) return;

            var known = new List<string>();
            if (!string.IsNullOrEmpty(puppetSkins))
            {
                foreach (string raw in puppetSkins.Split(',')) known.Add(raw.Trim());
            }

            foreach (string raw in skins.Split(','))
            {
                string name = raw.Trim();
                if (name.Length == 0 || known.Contains(name)) continue;
                known.Add(name);
            }

            puppetSkins = string.Join(", ", known);
        }

        /// <summary>Строки итога по измеряемым фазам — для лога, таблички и history.log.</summary>
        public string BuildResultText()
        {
            var sb = new StringBuilder();
            foreach (PerfPhaseReport phase in phases)
            {
                PerfSummary dt = phase.Get("dt");
                sb.Append(FormattableString.Invariant(
                    $"{phase.name}: кадр p50 {dt.p50:F1} p95 {dt.p95:F1} p99 {dt.p99:F1} мс · вне бюджета {phase.overBudgetShare * 100f:F0}% · всплесков {phase.spikes}"))
                  .Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>Пишет <c>summary.json</c> и строку в <c>perf/history.log</c>.</summary>
        public void Save()
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(Path.Combine(Directory, "summary.json"), JsonUtility.ToJson(this, true), new UTF8Encoding(false));

                string history = Path.Combine(Application.persistentDataPath, "perf", "history.log");
                File.AppendAllText(history,
                    $"{startedAt} · {role} · {scene} · кукол {puppetCount} · предметов {clutterCount}" +
                    (!string.IsNullOrEmpty(puppetSkins) ? $" · скины {puppetSkins}" : "") +
                    $" · {(completed ? "полный" : "прерван: " + endReason)}\n{BuildResultText()}\n\n",
                    new UTF8Encoding(false));
            }
            catch (IOException e)
            {
                GameLog.Perf.Warning($"[StressTest] Отчёт не записан: {e.Message}");
            }
        }

        private static float ReadRefreshRate()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (XRDisplaySubsystem display in displays)
            {
                if (display.running && display.TryGetDisplayRefreshRate(out float rate) && rate > 1f) return rate;
            }

            double screenRate = Screen.currentResolution.refreshRateRatio.value;
            return screenRate > 1.0 ? (float)screenRate : 72f;
        }
    }
}
