using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    ///     Mirror-половина класса T-34: <c>[ClientRpc]</c>/<c>[TargetRpc]</c> доходят только до тех, кто
    ///     подключён в момент вызова. Клиент, вошедший позже (посреди матча, после переподключения,
    ///     после смены карты), их не получит никогда. Поэтому RPC может нести только <b>событие</b>
    ///     (эффект, баннер, звук), а всё, что должно быть верно и у опоздавшего, — <c>SyncVar</c>/<c>SyncList</c>
    ///     или снимок UltimateXR.
    ///
    ///     <para>
    ///     Каждый RPC сборки игры записан здесь с ответом на вопрос «почему это не состояние». Новый RPC
    ///     без записи — красный тест: автор обязан ответить на этот вопрос. Реестр держится в тесте, а не
    ///     атрибутом на методе, чтобы решение было видно в одном месте целиком.
    ///     </para>
    /// </summary>
    public class RpcCarriesNoStateTests
    {
        /// <summary>«Тип.Метод» → почему это событие, а не состояние (и где лежит состояние рядом).</summary>
        private static readonly Dictionary<string, string> ReviewedRpcs = new Dictionary<string, string>
        {
            ["NetworkStateRelay.RpcComponentStateChanged"] =
                "транспорт событий UltimateXR; опоздавший получает состояние снимком (TargetLoadInitialState), " +
                "полноту снимка сторожит StateSnapshotCoverageTests",
            ["NetworkStateRelay.TargetLoadInitialState"] =
                "это и есть снимок для опоздавшего — ответ на запрос самого клиента",
            ["PlayerController.RpcOnDied"] =
                "страховка владельца: отпустить предметы из рук в момент гибели; смерть — Life в снимке, наблюдатель — SyncVar",
            ["PlayerController.RpcBecomeCorpse"] =
                "эффект: локальный рэгдолл погибшего, на игру не влияет; выбывание — SyncVar сессии",
            ["PlayerController.RpcReleaseLocalItems"] =
                "событие: владелец отпускает локальный планшет перед уничтожением аватара, состояния нет",
            ["PlayerController.RpcDevTeleport"] =
                "отладочный перенос; позиция едет NetworkTransform",
            ["EliminationMode.RpcOnSidesSwapped"] =
                "баннер HUD; сами стороны — SyncVar _sidesSwapped с хуком",
            ["EliminationMode.RpcOnRoundStarted"] =
                "баннер HUD; номер раунда и фаза — SyncVar",
            ["EliminationMode.RpcOnRoundEnded"] =
                "баннер HUD; счёт — SyncDictionary. Победитель текущего раунда только здесь — открытый вопрос T-34",
            ["GameMode.RpcOnModeStarted"] =
                "баннер HUD; состояние матча — SyncVar MapReferee._currentState",
            ["GameMode.RpcOnModeFinished"] =
                "баннер HUD; итог карты — Series._results (SyncList)",
            ["MapReferee.RpcOnMapFinished"] =
                "только лог; состояние — SyncVar _currentState",
            ["MapReferee.RpcOnStopped"] =
                "только лог; состояние — SyncVar _currentState",
            ["MapReferee.RpcPlayerKilled"] =
                "лента убийств и звук; K/D — SyncVar сессии",
            ["MatchEconomy.RpcTransaction"] =
                "уведомление «+3250 / −2900 TR15» (T-45); сами деньги — SyncDictionary MatchEconomy._money, доход раунда — _roundIncome",
        };

        [Test]
        public void Каждый_RPC_игры_проверен_на_то_что_не_несёт_состояния()
        {
            List<string> found = FindRpcs();

            // Контроль: сканер видит заведомо существующий RPC. Иначе зелёный ничего не значит.
            Assert.That(found, Does.Contain("NetworkStateRelay.RpcComponentStateChanged"),
                        "Сканер не нашёл RPC канала состояния — сломан поиск атрибутов, а не правило.");

            var problems = new List<string>();

            foreach (string rpc in found.Where(r => !ReviewedRpcs.ContainsKey(r)))
                problems.Add($"{rpc}: новый RPC. Опоздавший клиент его не получит — если это состояние, нужен " +
                             "SyncVar/SyncList; если событие — запись в ReviewedRpcs с причиной");

            foreach (string rpc in ReviewedRpcs.Keys.Where(r => !found.Contains(r)))
                problems.Add($"{rpc}: запись есть, RPC нет — убрать запись");

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        private static List<string> FindRpcs()
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            return typeof(NetworkStateRelay).Assembly.GetTypes()
                                            .SelectMany(t => t.GetMethods(all).Select(m => (t, m)))
                                            .Where(x => x.m.IsDefined(typeof(ClientRpcAttribute), false) ||
                                                        x.m.IsDefined(typeof(TargetRpcAttribute), false))
                                            .Select(x => $"{x.t.Name}.{x.m.Name}")
                                            .Distinct()
                                            .ToList();
        }
    }
}
