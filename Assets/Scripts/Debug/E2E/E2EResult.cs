// Ярус C (два процесса) — машиночитаемый вердикт сценария.
// Отключить из релизной сборки: определить VRBG_NO_E2E в Scripting Define Symbols.
#if !VRBG_NO_E2E
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace VrBattlegrounds.DevTools.E2E
{
    /// <summary>
    /// Одно утверждение сценария. Имя — на русском, человекочитаемо;
    /// <see cref="Detail"/> обязан объяснять, почему проверка провалилась,
    /// чтобы агенту не пришлось открывать логи руками.
    /// </summary>
    public class E2ECheck
    {
        public string Name;
        public bool Passed;
        public string Detail = string.Empty;
    }

    /// <summary>
    /// Результат прогона одного сценария в одном процессе.
    ///
    /// Пишется в файл <b>всегда</b>: сразу при старте (заглушка со статусом
    /// <c>started</c>), затем поверх — при завершении, таймауте или исключении.
    /// Иначе дирижёр не отличит «сценарий провалился» от «процесс не запустился».
    ///
    /// JSON выводится в чистом ASCII (кириллица уходит в \uXXXX), поэтому файл
    /// читается любым парсером независимо от кодировки консоли Windows.
    /// </summary>
    public class E2EResult
    {
        /// <summary>Сценарий завершился штатно и вынес вердикт.</summary>
        public const string StatusCompleted = "completed";

        /// <summary>Файл создан на старте; процесс до конца не дошёл.</summary>
        public const string StatusStarted = "started";

        /// <summary>Сценарий не уложился в общий таймаут.</summary>
        public const string StatusTimeout = "timeout";

        /// <summary>Внутри сценария вылетело исключение.</summary>
        public const string StatusError = "error";

        public string Scenario = string.Empty;
        public string Role = string.Empty;
        public string Status = StatusStarted;
        public string Summary = string.Empty;
        public DateTime StartedUtc = DateTime.UtcNow;
        public double DurationSeconds;

        private readonly List<E2ECheck> _checks = new List<E2ECheck>();

        public IReadOnlyList<E2ECheck> Checks => _checks;

        /// <summary>
        /// Вердикт: сценарий дошёл до конца и все объявленные проверки зелёные.
        /// Пустой список проверок вердиктом не считается — это признак того,
        /// что сценарий ничего не проверил.
        /// </summary>
        public bool Passed
        {
            get
            {
                return Status == StatusCompleted && AllChecksGreen;
            }
        }

        /// <summary>
        /// Все объявленные проверки зелёные — без учёта <see cref="Status"/>.
        /// Нужно сценарию: он формулирует итоговую строку до того, как
        /// <see cref="E2ERunner"/> выставит статус <c>completed</c>.
        /// </summary>
        public bool AllChecksGreen
        {
            get
            {
                if (_checks.Count == 0)
                    return false;

                for (int i = 0; i < _checks.Count; i++)
                {
                    if (!_checks[i].Passed)
                        return false;
                }

                return true;
            }
        }

        /// <summary>
        /// Объявляет полный список проверок заранее. Каждая заводится красной
        /// с пометкой «не выполнялась» — если сценарий оборвётся на середине,
        /// в файле всё равно будет видно, до чего он не дошёл.
        /// </summary>
        public void Declare(params string[] names)
        {
            foreach (string name in names)
            {
                _checks.Add(new E2ECheck
                {
                    Name = name,
                    Passed = false,
                    Detail = "не выполнялась — сценарий до неё не дошёл"
                });
            }
        }

        /// <summary>Проставляет результат ранее объявленной проверки.</summary>
        public void Set(string name, bool passed, string detail)
        {
            E2ECheck check = _checks.Find(c => c.Name == name);
            if (check == null)
            {
                check = new E2ECheck { Name = name };
                _checks.Add(check);
            }

            check.Passed = passed;
            check.Detail = detail ?? string.Empty;
        }

        /// <summary>
        /// Помечает все ещё не выполненные проверки причиной обрыва.
        /// Вызывается при таймауте и при исключении.
        /// </summary>
        public void AbortPending(string reason)
        {
            for (int i = 0; i < _checks.Count; i++)
            {
                if (!_checks[i].Passed && _checks[i].Detail.StartsWith("не выполнялась", StringComparison.Ordinal))
                    _checks[i].Detail = "не выполнялась — " + reason;
            }
        }

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder(1024);
            sb.Append("{\n");
            sb.Append("  \"scenario\": ").Append(Json(Scenario)).Append(",\n");
            sb.Append("  \"role\": ").Append(Json(Role)).Append(",\n");
            sb.Append("  \"passed\": ").Append(Passed ? "true" : "false").Append(",\n");
            sb.Append("  \"status\": ").Append(Json(Status)).Append(",\n");
            sb.Append("  \"summary\": ").Append(Json(Summary)).Append(",\n");
            sb.Append("  \"startedUtc\": ").Append(Json(StartedUtc.ToString("o", CultureInfo.InvariantCulture))).Append(",\n");
            sb.Append("  \"durationSeconds\": ")
              .Append(DurationSeconds.ToString("F2", CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"checks\": [\n");

            for (int i = 0; i < _checks.Count; i++)
            {
                E2ECheck c = _checks[i];
                sb.Append("    { \"name\": ").Append(Json(c.Name))
                  .Append(", \"passed\": ").Append(c.Passed ? "true" : "false")
                  .Append(", \"detail\": ").Append(Json(c.Detail))
                  .Append(" }");
                if (i < _checks.Count - 1) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("  ]\n}");
            return sb.ToString();
        }

        /// <summary>
        /// Атомарно (насколько это возможно) пишет вердикт в файл.
        /// Исключения глушатся: упавшая запись результата не должна уронить процесс,
        /// но обязана попасть в лог.
        /// </summary>
        public void WriteTo(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"[E2E] Не удалось записать результат в '{path}': {e.Message}");
            }
        }

        /// <summary>Строка в JSON: экранируем всё за пределами ASCII через \uXXXX.</summary>
        private static string Json(string value)
        {
            if (value == null) return "null";

            StringBuilder sb = new StringBuilder(value.Length + 16);
            sb.Append('"');

            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (ch < 0x20 || ch > 0x7E)
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }
    }
}
#endif
