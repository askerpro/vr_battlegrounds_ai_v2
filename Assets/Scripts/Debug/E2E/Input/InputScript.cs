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
    ///       <c>own-spawn-zone</c> (до возрождения в своей зоне), <c>own-wall</c> (к точке своей стены арсенала);</item>
    /// <item><c>turn</c> — поворот курса на <c>degrees</c>;</item>
    /// <item><c>choose-team</c> — команда по индексу клиента тем же Command, что у планшета;</item>
    /// <item><c>grab-wall-item</c> — подвести кисть <c>hand</c> к самому дешёвому доступному предмету своей стены и
    ///       нажать Grip;</item>
    /// <item><c>expect-held</c>, <c>expect-peers-holding</c> — проверки реплицированного состояния;</item>
    /// <item><c>release</c>, <c>hold</c> (<c>seconds</c>);</item>
    /// <item><c>step</c> / <c>wait-server</c> — барьер шагов e2e (<c>name</c>, <c>timeoutS</c>).</item>
    /// </list>
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
            "release", "hold", "expect-held", "expect-peers-holding", "step", "wait-server"
        };

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

        /// <summary>Имя проверки в результате процесса: номер и суть шага.</summary>
        public string Label(int index)
        {
            string detail = !string.IsNullOrEmpty(name) ? name : !string.IsNullOrEmpty(target) ? target : null;
            return detail == null ? $"{index + 1:00} {op}" : $"{index + 1:00} {op} {detail}";
        }
    }
}
#endif
