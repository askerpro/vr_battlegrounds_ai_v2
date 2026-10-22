// Ярус C (несколько процессов) — формат сценария скриптового клиента (контракт
// tasks/bots-control-modes/contracts/xr-input.md). Отключить из сборки: VRBG_NO_E2E.
#if !VRBG_NO_E2E
using System;
using System.IO;
using UnityEngine;

namespace VrBattlegrounds.DevTools.E2E.Input
{
    /// <summary>
    /// Сценарий скриптового клиента: список шагов JSON. Шаги — намерения игрока (иди, возьми, нажми) и ожидания
    /// событий игры; ни один шаг не вызывает действий SDK и не меняет игровое состояние напрямую.
    ///
    /// <para>Операции (<see cref="InputStep.op"/>):</para>
    /// <list type="bullet">
    /// <item><c>activate</c> — подать скриптовый ввод своему локальному аватару;</item>
    /// <item><c>measure-start</c> / <c>measure-check</c> — замер точности поз (голова ±1 см/0,1°, запястья ±1 мм);</item>
    /// <item><c>walk</c> — ходьба головой: <c>target</c> = <c>forward</c> (на <c>meters</c> по курсу),
    ///       <c>own-spawn-zone</c> (до возрождения в своей зоне), <c>own-spawn-point</c> (то же и встать в точку спавна
    ///       зоны), <c>own-wall</c> (к точке своей стены арсенала);</item>
    /// <item><c>turn</c> — поворот курса на <c>degrees</c>;</item>
    /// <item><c>choose-team</c> — команда по индексу клиента тем же Command, что у планшета;</item>
    /// <item><c>grab-wall-item</c> — подвести кисть <c>hand</c> к самому дешёвому доступному предмету своей стены и
    ///       нажать Grip; <c>item</c> = <c>pistol</c> — только пистолет с одной пулей на выстрел;</item>
    /// <item><c>grab-dog-tag</c> — снять жетон своей стены рукой <c>hand</c> (Grip) и отпустить: готовность к раунду;</item>
    /// <item><c>carry</c> — рука <c>hand</c> с предметом опускается и идёт за головой, Grip держится;</item>
    /// <item><c>shoot-enemy</c> — подойти к живому противнику на <c>meters</c> м, навести ствол руки <c>hand</c> на
    ///       его торс и жать Trigger до попадания (<c>until</c> = <c>hit</c>) или до его смерти (<c>kill</c>), не
    ///       больше <c>shots</c> нажатий;</item>
    /// <item><c>expect-eliminated</c> — свой игрок выбыл, ввод перенесён на новый аватар (призрак);</item>
    /// <item><c>admin-rights</c> — права админа этому клиенту (режим отладки, права выдаёт сервер);</item>
    /// <item><c>admin-command</c> — команда админа <c>target</c> (<c>GoLive</c>, <c>Pause</c>, <c>Resume</c>, <c>Stop</c>,
    ///       <c>NextMap</c>) тем же Command, что кнопка планшета; <c>admin-command-denied</c> — то же от не-админа:
    ///       сервер отказывает, состояние карты не меняется (<c>seconds</c> ожидания); <c>repeat</c> — сколько раз
    ///       отправить команду подряд в одном кадре (повтор кнопки во время перехода карты);</item>
    /// <item><c>admin-start-series</c> — «Новая серия» планшета: режим <c>target</c>, карты <c>maps</c> по порядку;
    ///       шаг ждёт реплицированную серию;</item>
    /// <item><c>expect-map</c> — карта <c>target</c> загружена заново (новый оркестратор карты), разминка, ввод подан
    ///       аватару на ней;</item>
    /// <item><c>expect-held</c>, <c>expect-peers-holding</c> — проверки реплицированного состояния;</item>
    /// <item><c>release</c>, <c>hold</c> (<c>seconds</c>);</item>
    /// <item><c>step</c> / <c>wait-server</c> — барьер шагов e2e (<c>name</c>, <c>timeoutS</c>).</item>
    /// </list>
    ///
    /// <para>Шаг с <c>client</c> = N исполняет только клиент N (роль <c>client-N</c>); 0 — все клиенты.</para>
    /// </summary>
    [Serializable]
    public sealed class InputScript
    {
        public string name;
        public InputStep[] steps;

        /// <summary>Каталог встроенных сценариев в проекте (редактор).</summary>
        public const string ProjectFolder = "Scripts/Debug/E2E/Input/Scripts";

        /// <summary>Операции, которые исполняет <see cref="ScriptedClientRunner"/>.</summary>
        public static readonly string[] Operations =
        {
            "activate", "measure-start", "measure-check", "walk", "turn", "choose-team", "grab-wall-item",
            "grab-dog-tag", "carry", "shoot-enemy", "expect-eliminated", "release", "hold", "expect-held",
            "expect-peers-holding", "admin-rights", "admin-command", "admin-command-denied", "admin-start-series",
            "expect-map", "step", "wait-server"
        };

        /// <summary>Команды админа (<c>MapCommand</c>) для <c>admin-command</c> и <c>admin-command-denied</c>.</summary>
        public static readonly string[] AdminCommands = { "GoLive", "Pause", "Resume", "Stop", "NextMap" };

        /// <summary>Фильтры предмета <see cref="InputStep.item"/> для <c>grab-wall-item</c>.</summary>
        public static readonly string[] ItemFilters = { "", "pistol" };

        /// <summary>Цели стрельбы <see cref="InputStep.until"/> для <c>shoot-enemy</c>.</summary>
        public static readonly string[] ShootGoals = { "hit", "kill" };

        /// <summary>
        /// Загружает сценарий: <paramref name="path"/> — файл (плеер, <c>-e2eArg script=</c>), иначе встроенный
        /// <paramref name="builtInName"/> из <see cref="ProjectFolder"/>. Ошибка — в <paramref name="error"/>.
        /// </summary>
        public static InputScript Load(string builtInName, string path, out string error)
        {
            error = null;
            string file = !string.IsNullOrEmpty(path)
                ? path
                : Path.Combine(Application.dataPath, ProjectFolder, builtInName + ".json");
            try
            {
                if (!File.Exists(file))
                {
                    error = $"файл сценария не найден: {file}";
                    return null;
                }

                InputScript script = JsonUtility.FromJson<InputScript>(File.ReadAllText(file));
                if (script == null || script.steps == null || script.steps.Length == 0)
                {
                    error = $"в сценарии {file} нет шагов";
                    return null;
                }

                for (int i = 0; i < script.steps.Length; i++)
                {
                    if (script.steps[i] == null || string.IsNullOrEmpty(script.steps[i].op))
                    {
                        error = $"шаг {i + 1} сценария {file} без op";
                        return null;
                    }

                    if (Array.IndexOf(Operations, script.steps[i].op) < 0)
                    {
                        error = $"шаг {i + 1} сценария {file}: неизвестная операция '{script.steps[i].op}'";
                        return null;
                    }

                    if (Array.IndexOf(ItemFilters, script.steps[i].item ?? "") < 0)
                    {
                        error = $"шаг {i + 1} сценария {file}: неизвестный фильтр предмета '{script.steps[i].item}'";
                        return null;
                    }

                    if (Array.IndexOf(ShootGoals, script.steps[i].until ?? "") < 0)
                    {
                        error = $"шаг {i + 1} сценария {file}: неизвестная цель стрельбы '{script.steps[i].until}'";
                        return null;
                    }

                    if (script.steps[i].op.StartsWith("admin-command", StringComparison.Ordinal) &&
                        Array.IndexOf(AdminCommands, script.steps[i].target ?? "") < 0)
                    {
                        error = $"шаг {i + 1} сценария {file}: неизвестная команда админа '{script.steps[i].target}'";
                        return null;
                    }

                    if (script.steps[i].op == "admin-start-series" &&
                        (string.IsNullOrEmpty(script.steps[i].target) || script.steps[i].maps == null ||
                         script.steps[i].maps.Length == 0))
                    {
                        error = $"шаг {i + 1} сценария {file}: admin-start-series без режима (target) или карт (maps)";
                        return null;
                    }

                    if (script.steps[i].op == "expect-map" && string.IsNullOrEmpty(script.steps[i].target))
                    {
                        error = $"шаг {i + 1} сценария {file}: expect-map без сцены (target)";
                        return null;
                    }

                    if (script.steps[i].repeat < 1)
                    {
                        error = $"шаг {i + 1} сценария {file}: repeat {script.steps[i].repeat} < 1";
                        return null;
                    }

                    if (script.steps[i].client < 0)
                    {
                        error = $"шаг {i + 1} сценария {file}: client {script.steps[i].client} < 0";
                        return null;
                    }
                }

                return script;
            }
            catch (Exception e)
            {
                error = $"сценарий {file} не прочитан: {e.Message}";
                return null;
            }
        }
    }

    /// <summary>Шаг сценария. Поля, не нужные операции, остаются по умолчанию.</summary>
    [Serializable]
    public sealed class InputStep
    {
        public string op;
        public string name;
        public string target;
        public string hand = "right";
        public float meters;
        public float degrees;
        public float seconds;
        public float speed = 1.0f;
        public float timeoutS = 60f;
        public string item = "";
        public int shots = 3;
        public string until = "hit";

        /// <summary>Сцены карт серии по порядку (<c>admin-start-series</c>).</summary>
        public string[] maps;

        /// <summary>Сколько раз подряд отправить команду (<c>admin-command</c>).</summary>
        public int repeat = 1;

        /// <summary>Номер клиента, который исполняет шаг; 0 — все.</summary>
        public int client;

        /// <summary>Шаг исполняет клиент с индексом <paramref name="clientIndex"/>.</summary>
        public bool AppliesTo(int clientIndex) => client == 0 || client == clientIndex;

        /// <summary>Имя проверки в результате процесса: номер и суть шага.</summary>
        public string Label(int index)
        {
            string detail = !string.IsNullOrEmpty(name) ? name : !string.IsNullOrEmpty(target) ? target : null;
            return detail == null ? $"{index + 1:00} {op}" : $"{index + 1:00} {op} {detail}";
        }
    }
}
#endif
