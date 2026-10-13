// Ярус C (два процесса) — машиночитаемый вердикт сценария.
// Отключить из релизной сборки: определить VRBG_NO_E2E в Scripting Define Symbols.
#if !VRBG_NO_E2E
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using VrBattlegrounds.Core;

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
    /// Известный шум сценария: ошибка лога, которую сценарий ожидает и считает безвредной.
    /// <see cref="Regex"/> сверяет дирижёр с текстом «первая строка + "\n" + начало стека» записи лога.
    /// </summary>
    public sealed class E2EKnownNoise
    {
        public string Id = string.Empty;
        public string Regex = string.Empty;
        public string Reason = string.Empty;
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

        // ── Условия сборки процесса: дирижёр переносит их в run.json (контракт quest-device-run) ──

        /// <summary><see cref="Application.buildGUID"/>; в редакторе пусто.</summary>
        public string BuildGuid = string.Empty;
        /// <summary><see cref="Application.version"/>.</summary>
        public string AppVersion = string.Empty;
        /// <summary><see cref="Application.unityVersion"/>.</summary>
        public string UnityVersion = string.Empty;
        /// <summary>Development-сборка (<c>Debug.isDebugBuild</c>).</summary>
        public bool DevelopmentBuild;
        /// <summary><see cref="Application.platform"/>.</summary>
        public string Platform = string.Empty;

        /// <summary>Имя красной проверки, которую ставит <see cref="AddArtifact"/> на путь вне persistentDataPath.</summary>
        public const string CheckArtifactOutside = "артефакт вне persistentDataPath";

        private readonly List<E2ECheck> _checks = new List<E2ECheck>();
        private readonly List<string> _artifacts = new List<string>();
        private readonly List<E2EKnownNoise> _knownNoise = new List<E2EKnownNoise>();
        private readonly E2ELogCapture _logCapture = new E2ELogCapture();

        public IReadOnlyList<E2ECheck> Checks => _checks;

        /// <summary>
        /// Захват ошибок лога процесса (контракт quest-device-run r4). Подписывается первой записью заглушки
        /// (<see cref="StatusStarted"/>) и снимается финальной записью (любой другой статус) — см. <see cref="WriteTo"/>.
        /// На <see cref="Passed"/> не влияет: вердикт по логам выносит дирижёр.
        /// </summary>
        public E2ELogCapture LogCapture => _logCapture;

        /// <summary>Известный шум, объявленный сценарием через <see cref="DeclareKnownNoise"/>.</summary>
        public IReadOnlyList<E2EKnownNoise> KnownNoise => _knownNoise;

        /// <summary>
        /// Файлы и каталоги, которые дирижёр заберёт в папку прогона после вердикта. Только абсолютные пути
        /// внутри <see cref="Application.persistentDataPath"/>; пополняется только через <see cref="AddArtifact"/>,
        /// чтобы правило пути нельзя было обойти.
        /// </summary>
        public IReadOnlyList<string> Artifacts => _artifacts;

        /// <summary>
        /// Снимает условия сборки текущего процесса. Вызывается исполнителем на главном потоке до первой записи файла.
        /// </summary>
        public void CaptureBuildConditions()
        {
            BuildGuid = Application.buildGUID ?? string.Empty;
            AppVersion = Application.version ?? string.Empty;
            UnityVersion = Application.unityVersion ?? string.Empty;
            DevelopmentBuild = UnityEngine.Debug.isDebugBuild;
            Platform = Application.platform.ToString();
        }

        /// <summary>
        /// Объявляет артефакт прогона — файл или каталог на устройстве. Принимается только абсолютный путь строго
        /// внутри <see cref="Application.persistentDataPath"/> (после нормализации); иначе путь в список не попадает,
        /// а в результат добавляется красная проверка <see cref="CheckArtifactOutside"/>.
        /// </summary>
        /// <returns>true — артефакт принят.</returns>
        public bool AddArtifact(string path)
        {
            string normalized = NormalizeInsidePersistentData(path, out string reason);
            if (normalized == null)
            {
                RejectArtifact($"'{path}': {reason}");
                return false;
            }

            if (!_artifacts.Contains(normalized))
                _artifacts.Add(normalized);
            return true;
        }

        private void RejectArtifact(string detail)
        {
            GameLog.Error($"[E2E] Артефакт отклонён: {detail}");
            E2ECheck check = _checks.Find(c => c.Name == CheckArtifactOutside);
            if (check == null)
            {
                _checks.Add(new E2ECheck { Name = CheckArtifactOutside, Passed = false, Detail = detail });
                return;
            }

            check.Passed = false;
            check.Detail = string.IsNullOrEmpty(check.Detail) ? detail : check.Detail + "; " + detail;
        }

        /// <summary>Нормализованный абсолютный путь внутри persistentDataPath или null с причиной.</summary>
        private static string NormalizeInsidePersistentData(string path, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                reason = "пустой путь";
                return null;
            }

            if (!Path.IsPathRooted(path))
            {
                reason = "путь не абсолютный";
                return null;
            }

            string root;
            string full;
            try
            {
                root = Path.GetFullPath(Application.persistentDataPath)
                           .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception e)
            {
                reason = $"путь не нормализуется: {e.GetType().Name}: {e.Message}";
                return null;
            }

            // На Windows файловая система регистронезависима, на Android — нет.
            StringComparison comparison =
                Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            string prefix = root + Path.DirectorySeparatorChar;
            string candidate = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(root) ||
                !candidate.StartsWith(prefix.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar), comparison))
            {
                reason = $"вне persistentDataPath ('{root}')";
                return null;
            }

            return full;
        }

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

        /// <summary>
        /// Объявляет известный шум сценария: ошибку лога, которую дирижёр отнесёт к known, а не к неожиданным.
        /// <paramref name="regex"/> (.NET, с учётом регистра) сверяется с текстом «первая строка + "\n" + начало
        /// стека» каждой записи лога этого процесса. <paramref name="reason"/> обязан объяснять, почему ошибка
        /// безвредна и откуда это известно. Повтор <paramref name="id"/> заменяет прежнее объявление.
        /// Пустой id/regex/reason или неверный regex не принимаются: <c>GameLog.Error</c>, и эта ошибка
        /// сама попадёт в вердикт по логам как неожиданная.
        /// </summary>
        /// <returns>true — шум объявлен.</returns>
        public bool DeclareKnownNoise(string id, string regex, string reason)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrEmpty(regex) || string.IsNullOrWhiteSpace(reason))
            {
                GameLog.Error($"[E2E] Шум сценария '{id}' не объявлен: id, regex и reason обязательны.");
                return false;
            }

            try
            {
                // Только проверка синтаксиса: сверяет дирижёр, тем же движком .NET.
                _ = new Regex(regex, RegexOptions.CultureInvariant);
            }
            catch (ArgumentException e)
            {
                GameLog.Error($"[E2E] Шум сценария '{id}' не объявлен: неверный regex '{regex}': {e.Message}");
                return false;
            }

            var noise = new E2EKnownNoise { Id = id, Regex = regex, Reason = reason };
            int index = _knownNoise.FindIndex(n => n.Id == id);
            if (index >= 0)
                _knownNoise[index] = noise;
            else
                _knownNoise.Add(noise);
            return true;
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
            sb.Append("  \"buildGuid\": ").Append(Json(BuildGuid)).Append(",\n");
            sb.Append("  \"appVersion\": ").Append(Json(AppVersion)).Append(",\n");
            sb.Append("  \"unityVersion\": ").Append(Json(UnityVersion)).Append(",\n");
            sb.Append("  \"developmentBuild\": ").Append(DevelopmentBuild ? "true" : "false").Append(",\n");
            sb.Append("  \"platform\": ").Append(Json(Platform)).Append(",\n");
            sb.Append("  \"artifacts\": [");
            for (int i = 0; i < _artifacts.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Json(_artifacts[i]));
            }
            sb.Append("],\n");
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

            sb.Append("  ],\n");

            // Вердикт по логам (r4): поля добавлены в конец — прежние потребители JSON их не замечают.
            List<E2ELogIssue> issues = _logCapture.Snapshot();
            sb.Append("  \"logIssues\": [\n");
            for (int i = 0; i < issues.Count; i++)
            {
                E2ELogIssue issue = issues[i];
                sb.Append("    { \"type\": ").Append(Json(issue.Type))
                  .Append(", \"message\": ").Append(Json(issue.Message))
                  .Append(", \"stack\": ").Append(Json(issue.Stack))
                  .Append(", \"count\": ").Append(issue.Count.ToString(CultureInfo.InvariantCulture))
                  .Append(", \"firstAtSeconds\": ").Append(issue.FirstAtSeconds.ToString("F2", CultureInfo.InvariantCulture))
                  .Append(" }");
                if (i < issues.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ],\n");
            sb.Append("  \"logIssuesDropped\": ").Append(_logCapture.Dropped.ToString(CultureInfo.InvariantCulture)).Append(",\n");

            sb.Append("  \"knownNoise\": [\n");
            for (int i = 0; i < _knownNoise.Count; i++)
            {
                E2EKnownNoise noise = _knownNoise[i];
                sb.Append("    { \"id\": ").Append(Json(noise.Id))
                  .Append(", \"regex\": ").Append(Json(noise.Regex))
                  .Append(", \"reason\": ").Append(Json(noise.Reason))
                  .Append(" }");
                if (i < _knownNoise.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ]\n}");
            return sb.ToString();
        }

        /// <summary>
        /// Атомарно (насколько это возможно) пишет вердикт в файл.
        /// Исключения глушатся: упавшая запись результата не должна уронить процесс,
        /// но обязана попасть в лог.
        ///
        /// Заодно ведёт захват лога (<see cref="LogCapture"/>): запись заглушки <see cref="StatusStarted"/>
        /// подписывает его (повторно — без эффекта), финальная запись (completed/error/timeout) снимает
        /// подписку до сериализации — в файл уходит итог, ошибки после вердикта в него уже не попадут.
        /// </summary>
        public void WriteTo(string path)
        {
            if (Status == StatusStarted)
                _logCapture.Install();
            else
                _logCapture.Uninstall();

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
                GameLog.Error($"[E2E] Не удалось записать результат в '{path}': {e.Message}");
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
