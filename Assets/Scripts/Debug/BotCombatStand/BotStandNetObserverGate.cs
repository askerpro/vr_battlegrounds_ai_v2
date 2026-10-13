using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>Заявка на клиентского наблюдателя стенда ботов (этап pf-network): куда писать отчёт и до какого времени.</summary>
    [Serializable]
    public sealed class BotStandNetObserverRequest
    {
        /// <summary>Метка прогона (имя пакета/фазы) — попадает в отчёт каждого процесса.</summary>
        public string RunTag;
        /// <summary>Абсолютная папка отчётов клиентов (вне Assets). Клоны MPPM пишут сюда же: путь не зависит от их папки.</summary>
        public string OutputDirectory;
        /// <summary>Срок действия заявки (UTC, ISO 8601). Просроченная заявка наблюдателя не создаёт.</summary>
        public string ExpiresUtc;
        /// <summary>
        /// A/B net-round1 attempt-6 (<see cref="BotStandRunOptions.ClientNoRecoil"/>): наблюдатель вешает
        /// <see cref="BotStandNoRecoilPolicy"/> на локальную копию оружия в руке бота — корневая отдача SDK при повторе
        /// выстрела у этого клиента не применяется. Только копия этого процесса, без Cmd и событий синхронизации.
        /// </summary>
        public bool ClientNoRecoil;
    }

    /// <summary>
    /// Включение клиентского наблюдателя в процессах мультипроцессного стенда (MPPM-клоны получают тот же код, но
    /// команды MCP туда не доходят). Единственный вход — файл заявки в <c>Temp</c> исходного checkout: его пишет payload
    /// главного редактора до <c>PlayLaunch.Play</c> и удаляет после остановки. Без заявки ничего не создаётся.
    /// Наблюдатель только читает состояние (исключение — опция <see cref="BotStandNetObserverRequest.ClientNoRecoil"/>);
    /// на сервере/хосте он бездействует (там замеры ведёт проба стенда).
    /// </summary>
    public static class BotStandNetObserverGate
    {
        /// <summary>Путь заявки относительно корня исходного checkout.</summary>
        public const string RequestRelativePath = "Temp/BotStandNetObserver/request.json";

        /// <summary>
        /// Корень исходного checkout. У клона MPPM <c>Application.dataPath</c> = <c>&lt;root&gt;/Library/VP/&lt;id&gt;/Assets</c> —
        /// часть с <c>/Library/VP/</c> отрезается, поэтому клоны и главный редактор видят одну заявку.
        /// </summary>
        public static string ProjectRoot
        {
            get
            {
                string data = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
                string root = data.EndsWith("/Assets", StringComparison.OrdinalIgnoreCase) ? data.Substring(0, data.Length - "/Assets".Length) : data;
                int clone = root.IndexOf("/Library/VP/", StringComparison.OrdinalIgnoreCase);
                return clone >= 0 ? root.Substring(0, clone) : root;
            }
        }

        public static string RequestPath => Path.Combine(ProjectRoot, RequestRelativePath).Replace('\\', '/');

        /// <summary>Пишет заявку (главный редактор, вне Play). Папка отчётов должна быть абсолютной и вне Assets.</summary>
        public static string WriteRequest(BotStandNetObserverRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.OutputDirectory) || !Path.IsPathRooted(request.OutputDirectory))
                throw new ArgumentException("Нужна абсолютная папка отчётов наблюдателя.");
            string assets = Path.GetFullPath(Path.Combine(ProjectRoot, "Assets")) + Path.DirectorySeparatorChar;
            if ((Path.GetFullPath(request.OutputDirectory) + Path.DirectorySeparatorChar).StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Отчёты наблюдателя нельзя сохранять в Assets.");
            Directory.CreateDirectory(Path.GetDirectoryName(RequestPath));
            File.WriteAllText(RequestPath, JsonConvert.SerializeObject(request, Formatting.Indented), new System.Text.UTF8Encoding(false));
            return RequestPath;
        }

        /// <summary>Удаляет заявку; true — файл был.</summary>
        public static bool ClearRequest()
        {
            if (!File.Exists(RequestPath)) return false;
            File.Delete(RequestPath);
            return true;
        }

        public static bool TryReadRequest(out BotStandNetObserverRequest request, out string reason)
        {
            request = null;
            reason = null;
            if (!File.Exists(RequestPath)) { reason = "нет заявки"; return false; }
            try { request = JsonConvert.DeserializeObject<BotStandNetObserverRequest>(File.ReadAllText(RequestPath)); }
            catch (Exception e) { reason = "заявка не читается: " + e.Message; return false; }
            if (request == null || string.IsNullOrEmpty(request.OutputDirectory) || !Path.IsPathRooted(request.OutputDirectory))
            {
                reason = "в заявке нет абсолютной папки отчётов";
                return false;
            }
            if (!DateTime.TryParse(request.ExpiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out DateTime expires) || expires < DateTime.UtcNow)
            {
                reason = "заявка просрочена";
                return false;
            }
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!TryReadRequest(out BotStandNetObserverRequest request, out string reason)) return;
            if (UnityEngine.Object.FindAnyObjectByType<BotStandNetObserver>() != null) return;
            var go = new GameObject("BotStandNetObserver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<BotStandNetObserver>().Configure(request);
            GameLog.Debug.Info("[BotStandNetObserver] Включён по заявке " + request.RunTag + "; отчёт — " + request.OutputDirectory +
                               "; ClientNoRecoil=" + request.ClientNoRecoil);
        }
    }
}
