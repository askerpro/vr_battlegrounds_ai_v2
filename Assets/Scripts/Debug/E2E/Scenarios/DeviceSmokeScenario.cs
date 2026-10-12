// Ярус C (два процесса) — сценарий device-smoke: самопроверка каркаса прогона на шлеме (контракт quest-device-run).
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Mirror;
using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Managers;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.E2E.Scenarios
{
    /// <summary>
    /// Сценарий <c>device-smoke</c>: проверяет сам каркас прогона на устройстве, а не игровую логику.
    ///
    /// <para>
    /// <b>Топология.</b> Сервер — выделенный Windows-плеер на ПК (<c>-batchmode -nographics</c>), клиент — собранный
    /// плеер на шлеме, подключённый штатным путём игры: Discovery по LAN на изолированной паре портов прогона,
    /// после 30 с тишины — прямое подключение по <see cref="E2EContext.ServerAddress"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Вердикт.</b> Сервер: сеть поднята, пришло хотя бы одно клиентское подключение с сессией. Клиент: подключился
    /// к серверу (в detail — каким путём). С <c>-e2eArg artifact=1</c> каждая сторона пишет маленький файл
    /// <c>persistentDataPath/e2e-smoke/&lt;utc&gt;.txt</c> и объявляет его через <see cref="E2EResult.AddArtifact"/> —
    /// так проверяется сбор артефактов дирижёром с обеих сторон.
    /// </para>
    ///
    /// <para>
    /// <b>Точки обзора.</b> С <c>-e2eArg views=&lt;путь&gt;</c> (файл точек, <see cref="E2EViews"/>) клиент после своей
    /// сессии и аватара обходит точки штатным <c>UxrManager.MoveAvatarTo</c>, пишет
    /// <c>persistentDataPath/e2e-smoke/views-&lt;utc&gt;.json</c> и объявляет его артефактом; проверка «обход точек обзора»
    /// зелёная, если все точки достигнуты с отклонением по XZ меньше <see cref="ViewsMaxDeviationXZ"/> м и по курсу меньше
    /// <see cref="ViewsMaxDeviationYaw"/>°. Сервер только читает тот же файл (имена точек по порядку) — так проверяется
    /// доставка файла обеим сторонам.
    /// </para>
    ///
    /// <para>Без <c>views</c> игровое состояние сценарий не меняет: только наблюдает за сетью и пишет свой файл.</para>
    /// </summary>
    public class DeviceSmokeScenario : IE2EScenario
    {
        public string Name => "device-smoke";

        private const string CheckServerUp = "сервер поднят";
        private const string CheckClientJoined = "клиент подключился";
        private const string CheckArtifact = "файл-артефакт записан и объявлен";
        private const string CheckViews = "обход точек обзора";
        private const string CheckViewsRead = "файл точек обзора прочитан";

        /// <summary>Допуск отклонения пола аватара от точки по горизонтали, м.</summary>
        private const float ViewsMaxDeviationXZ = 0.05f;

        /// <summary>Допуск отклонения курса камеры от запрошенного, градусы.</summary>
        private const float ViewsMaxDeviationYaw = 2f;

        /// <summary>Сколько ждать своего аватара после сессии.</summary>
        private const float AvatarWait = 60f;

        /// <summary>Выдержка после появления аватара: спавн успевает поставить его на место до первого переноса.</summary>
        private const float AvatarSettleHold = 1f;

        /// <summary>Запас до общего таймаута исполнителя: вердикт сценария должен успеть записаться раньше.</summary>
        private const float TimeoutMargin = 10f;

        /// <summary>Сколько ждать Discovery до прямого подключения — как в остальных клиентских сценариях.</summary>
        private const float DiscoveryWait = 30f;

        /// <summary>Выдержка сервера после подключения: клиент успевает записать свой вердикт до выхода сервера.</summary>
        private const float ServerHold = 5f;

        private static float Now => E2EWait.Now;

        public IEnumerator Run(E2EContext context, E2EResult result) =>
            context.IsServerRole ? RunServer(context, result) : RunClient(context, result);

        // ── Сервер ───────────────────────────────────────────────────────────

        private IEnumerator RunServer(E2EContext context, E2EResult result)
        {
            bool wantArtifact = WantsArtifact(context);
            bool wantViews = WantsViews(context);
            result.Declare(CheckServerUp, CheckClientJoined);
            if (wantArtifact) result.Declare(CheckArtifact);
            if (wantViews) result.Declare(CheckViewsRead);

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");
            float deadline = Now + context.Timeout - TimeoutMargin;

            // Серверу из файла точек нужны только имена по порядку; переносить ему нечего.
            if (wantViews) ReadViewNames(context, result);

            var up = new E2EWaitOutcome();
            yield return E2EWait.Until(up, "NetworkServer.active", Mathf.Min(60f, Remaining(deadline)),
                () => NetworkServer.active, DescribeServer);
            result.Set(CheckServerUp, up.Succeeded, up.Diagnosis);
            if (!up.Succeeded) { result.Summary = "сервер не поднялся"; yield break; }

            if (wantArtifact) WriteArtifact(context, result);

            int expected = Math.Max(1, context.ExpectedClients);
            var joined = new E2EWaitOutcome();
            yield return E2EWait.Until(joined, $"клиентских подключений с сессией: {expected}", Remaining(deadline),
                () => ClientConnections() >= expected && SessionCount() >= expected,
                DescribeServer,
                () => NetworkServer.active ? null : "сервер погас");
            result.Set(CheckClientJoined, joined.Succeeded, joined.Diagnosis);
            if (!joined.Succeeded) { result.Summary = "клиент не подключился к серверу"; yield break; }

            // Сервер выходит по вердикту: дать клиенту записать свой до разрыва.
            yield return E2EWait.Hold(Mathf.Min(ServerHold, Remaining(deadline)));
        }

        /// <summary>Подключения удалённых клиентов (на выделенном сервере локального соединения нет).</summary>
        private static int ClientConnections()
        {
            int count = 0;
            foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
            {
                if (connection != null && connection != NetworkServer.localConnection)
                    count++;
            }
            return count;
        }

        private static int SessionCount() => PlayersManager.Instance != null ? PlayersManager.Instance.Sessions.Count : 0;

        private static string DescribeServer() =>
            $"NetworkServer.active={NetworkServer.active}, NetworkClient.active={NetworkClient.active}, " +
            $"подключений={ClientConnections()}, сессий={SessionCount()}";

        // ── Клиент ───────────────────────────────────────────────────────────

        private IEnumerator RunClient(E2EContext context, E2EResult result)
        {
            bool wantArtifact = WantsArtifact(context);
            bool wantViews = WantsViews(context);
            result.Declare(CheckClientJoined);
            if (wantArtifact) result.Declare(CheckArtifact);
            if (wantViews) result.Declare(CheckViews);

            DebugBootstrapGate.Suppress("E2E: дирижёр прогона — сценарий");
            float deadline = Now + context.Timeout - TimeoutMargin;

            if (wantArtifact) WriteArtifact(context, result);

            // Файл точек читается до подключения: ошибка формата видна сразу, подключение проверяется всё равно.
            IReadOnlyList<E2EViewPoint> views = null;
            if (wantViews && !E2EViews.TryLoadFromContext(context, out views, out string viewsError))
            {
                result.Set(CheckViews, false, viewsError);
                result.Summary = "файл точек обзора не прочитан";
            }

            var discovered = new E2EWaitOutcome();
            yield return E2EWait.Until(discovered, "подключение через Discovery", Mathf.Min(DiscoveryWait, Remaining(deadline)),
                () => NetworkClient.isConnected, DescribeClient);

            string via = "Discovery";
            if (!discovered.Succeeded && !string.IsNullOrEmpty(context.ServerAddress))
            {
                via = $"прямое подключение к {context.ServerAddress}";
                GameLog.Debug.Info($"[E2E] Discovery молчит {DiscoveryWait:F0} с, подключаюсь напрямую к {context.ServerAddress}");
                ConnectDirectly(context.ServerAddress);
            }

            // Транспортного соединения мало: сервер считает игрока по сессии. Клиент, вышедший раньше (-e2eQuitOnFinish),
            // оставит сервер без подключения — поэтому ждём свою PlayerSession и держим соединение ещё ClientHold секунд.
            var joined = new E2EWaitOutcome();
            yield return E2EWait.Until(joined, "своя PlayerSession на клиенте", Remaining(deadline),
                () => NetworkClient.isConnected && PlayerSession.LocalSession != null, DescribeClient);
            result.Set(CheckClientJoined, joined.Succeeded, $"путь: {via}. {joined.Diagnosis}");
            if (!joined.Succeeded)
            {
                result.Summary = "клиент не получил сессию на сервере";
                yield break;
            }

            if (views != null)
                yield return WalkViews(views, result, deadline);

            yield return E2EWait.Hold(Mathf.Min(ClientHold, Remaining(deadline)));
        }

        /// <summary>Сколько клиент держит соединение после своей сессии, чтобы сервер успел её засчитать.</summary>
        private const float ClientHold = 2f;

        /// <summary>
        /// Прямое подключение мимо Discovery — через владельца сети (<see cref="GameNetworkDiscovery.RequestClientConnect"/>),
        /// чтобы шов подготовки сети применил пару портов прогона. Прямой StartClient ушёл бы на порт сцены.
        /// </summary>
        private static void ConnectDirectly(string address)
        {
            GameNetworkDiscovery owner = NetworkManager.singleton != null ? NetworkManager.singleton.GetComponent<GameNetworkDiscovery>() : null;
            if (owner == null || !GameNetworkDiscovery.ClientManagerReady)
            {
                GameLog.Error("[E2E] Владелец сети не готов к подключению — прямой путь недоступен.");
                return;
            }

            if (NetworkClient.active)
                return;

            try { owner.RequestClientConnect(address); }
            catch (InvalidOperationException e) { GameLog.Error($"[E2E] Прямое подключение отклонено владельцем сети: {e.Message}"); }
        }

        private static string DescribeClient()
        {
            NetworkManager manager = NetworkManager.singleton;
            return $"адрес менеджера={(manager != null ? manager.networkAddress : "менеджера нет")}, " +
                   $"NetworkClient.active={NetworkClient.active}, isConnected={NetworkClient.isConnected}";
        }

        // ── Точки обзора ─────────────────────────────────────────────────────

        private static bool WantsViews(E2EContext context) => !string.IsNullOrEmpty(context.Arg(E2EViews.ArgKey));

        /// <summary>Сервер: читает файл точек и отчитывается именами по порядку.</summary>
        private static void ReadViewNames(E2EContext context, E2EResult result)
        {
            if (!E2EViews.TryLoadFromContext(context, out IReadOnlyList<E2EViewPoint> views, out string error))
            {
                result.Set(CheckViewsRead, false, error);
                return;
            }

            var names = new string[views.Count];
            for (int i = 0; i < views.Count; i++) names[i] = views[i].Name;
            result.Set(CheckViewsRead, true, $"точек {views.Count}: {string.Join(", ", names)}");
        }

        /// <summary>
        /// Клиент: ждёт свой аватар (аватар сессии и есть <c>UxrAvatar.LocalAvatar</c>), обходит точки через
        /// <see cref="E2EViews.GoTo"/>, пишет views-&lt;utc&gt;.json и выносит проверку по допускам.
        /// </summary>
        private static IEnumerator WalkViews(IReadOnlyList<E2EViewPoint> views, E2EResult result, float deadline)
        {
            var avatarReady = new E2EWaitOutcome();
            yield return E2EWait.Until(avatarReady, "свой аватар сессии с камерой (UxrAvatar.LocalAvatar)",
                Mathf.Min(AvatarWait, Remaining(deadline)), LocalAvatarReady, DescribeAvatar);
            if (!avatarReady.Succeeded)
            {
                result.Set(CheckViews, false, avatarReady.Diagnosis);
                result.Summary = "нет своего аватара — точки обзора не обойдены";
                yield break;
            }

            yield return E2EWait.Hold(Mathf.Min(AvatarSettleHold, Remaining(deadline)));

            var outcomes = new List<E2EViewOutcome>(views.Count);
            foreach (E2EViewPoint point in views)
            {
                var outcome = new E2EViewOutcome();
                yield return E2EViews.GoTo(point, outcome);
                outcomes.Add(outcome);
                GameLog.Debug.Info($"[E2E] Точка обзора '{point.Name}': {DescribeOutcome(outcome)}");
            }

            string path = WriteViews(outcomes, out string writeError);
            bool accepted = path != null && result.AddArtifact(path);

            int reached = 0;
            var failures = new List<string>();
            foreach (E2EViewOutcome o in outcomes)
            {
                bool ok = o.Succeeded &&
                          o.DeviationXZ < ViewsMaxDeviationXZ &&
                          Mathf.Abs(o.DeviationYaw) < ViewsMaxDeviationYaw;
                if (ok) reached++;
                else failures.Add($"'{o.Point.Name}': {DescribeOutcome(o)}");
            }

            var detail = new StringBuilder();
            detail.Append(string.Format(CultureInfo.InvariantCulture,
                "достигнуто {0}/{1} (допуск XZ < {2:F2} м, курс < {3:F0}°)", reached, outcomes.Count,
                ViewsMaxDeviationXZ, ViewsMaxDeviationYaw));
            if (outcomes.Count > 0 && outcomes[0].Succeeded)
                detail.Append("; первая точка: ").Append(DescribeOutcome(outcomes[0]));
            if (failures.Count > 0) detail.Append("; не достигнуты: ").Append(string.Join("; ", failures));
            if (path == null) detail.Append("; views.json не записан: ").Append(writeError);
            else if (!accepted) detail.Append("; views.json записан, но AddArtifact отклонил '").Append(path).Append('\'');
            else detail.Append("; артефакт ").Append(path);

            bool passed = failures.Count == 0 && accepted;
            result.Set(CheckViews, passed, detail.ToString());
            if (!passed) result.Summary = "обход точек обзора не прошёл";
        }

        /// <summary>Свой аватар готов к переносу: LocalAvatar с камерой — это аватар своей сессии.</summary>
        private static bool LocalAvatarReady()
        {
            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            PlayerSession session = PlayerSession.LocalSession;
            return avatar != null && avatar.CameraComponent != null &&
                   session != null && session.ActiveAvatar != null &&
                   session.ActiveAvatar.gameObject == avatar.gameObject;
        }

        private static string DescribeAvatar()
        {
            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            PlayerSession session = PlayerSession.LocalSession;
            return $"LocalAvatar={(avatar != null ? avatar.name : "null")}, " +
                   $"камера={(avatar != null && avatar.CameraComponent != null ? "есть" : "нет")}, " +
                   $"ActiveAvatar сессии={(session != null && session.ActiveAvatar != null ? session.ActiveAvatar.name : "null")}";
        }

        private static string DescribeOutcome(E2EViewOutcome o)
        {
            if (!o.Succeeded) return "ошибка: " + o.Error;
            return string.Format(CultureInfo.InvariantCulture,
                "откл. XZ {0:F3} м, курс {1:F2}°, пол {2:F3} м; камера ({3:F2}, {4:F2}, {5:F2}), курс {6:F1}°, " +
                "тангаж {7:F1}°, крен {8:F1}°, глаза {9:F2} м",
                o.DeviationXZ, o.DeviationYaw, o.DeviationFloorY,
                o.CameraPosition.x, o.CameraPosition.y, o.CameraPosition.z,
                o.CameraYaw, o.CameraPitch, o.CameraRoll, o.EyeHeight);
        }

        /// <summary>Пишет итоги обхода в persistentDataPath/e2e-smoke/views-&lt;utc&gt;.json; путь или null с причиной.</summary>
        private static string WriteViews(IReadOnlyList<E2EViewOutcome> outcomes, out string error)
        {
            error = null;
            string path = Path.Combine(Application.persistentDataPath, "e2e-smoke",
                "views-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + ".json");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, E2EViews.ToJson(outcomes), new UTF8Encoding(false));
                return path;
            }
            catch (Exception e)
            {
                error = $"'{path}': {e.GetType().Name}: {e.Message}";
                return null;
            }
        }

        // ── Артефакт ─────────────────────────────────────────────────────────

        private static bool WantsArtifact(E2EContext context) => context.Arg("artifact") == "1";

        /// <summary>Пишет маленький файл в persistentDataPath/e2e-smoke/ и объявляет его артефактом.</summary>
        private static void WriteArtifact(E2EContext context, E2EResult result)
        {
            DateTime now = DateTime.UtcNow;
            string path = Path.Combine(Application.persistentDataPath, "e2e-smoke",
                now.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + ".txt");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string text = $"scenario={context.Scenario}\nrole={context.Role}\nutc={now.ToString("o", CultureInfo.InvariantCulture)}\n";
                File.WriteAllText(path, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                result.Set(CheckArtifact, false, $"запись '{path}' не удалась: {e.GetType().Name}: {e.Message}");
                return;
            }

            bool accepted = result.AddArtifact(path);
            result.Set(CheckArtifact, accepted, accepted ? path : $"файл записан, но AddArtifact отклонил '{path}'");
        }

        private static float Remaining(float deadline) => Mathf.Max(0f, deadline - Now);
    }
}
#endif
