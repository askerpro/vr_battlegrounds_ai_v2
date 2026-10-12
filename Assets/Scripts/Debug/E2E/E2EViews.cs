// Ярус C (два процесса) — обход точек обзора: механизм каркаса, без содержания (точки задаёт файл прогона).
// Отключить из релизной сборки: определить VRBG_NO_E2E в Scripting Define Symbols.
#if !VRBG_NO_E2E
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.DevTools.E2E
{
    /// <summary>
    /// Точка обзора: проекция камеры на пол и направление взгляда по горизонтали. Высота глаз, тангаж и крен
    /// не задаются — они остаются от трекинга (лежащего или стоящего на подставке шлема).
    /// </summary>
    public sealed class E2EViewPoint
    {
        /// <summary>Уникальное имя точки в файле; допустимые символы — как у токенов дирижёра.</summary>
        public string Name = string.Empty;

        /// <summary>Точка на полу, над которой окажется камера. y — уровень пола (если задан, см. <see cref="HasFloorY"/>).</summary>
        public Vector3 Floor;

        /// <summary>
        /// y задан в файле. Нет — при переносе берётся текущий уровень пола локального аватара
        /// (<c>UxrAvatar.LocalAvatar.transform.position.y</c>).
        /// </summary>
        public bool HasFloorY;

        /// <summary>Направление взгляда вокруг мировой вертикали, градусы; 0 — +Z, 90 — +X.</summary>
        public float Yaw;
    }

    /// <summary>
    /// Итог переноса в одну точку. Метод-итератор не может вернуть значение — поэтому отдельный объект,
    /// как <see cref="E2EWaitOutcome"/>.
    /// </summary>
    public sealed class E2EViewOutcome
    {
        /// <summary>Запрошенная точка (как в файле).</summary>
        public E2EViewPoint Point;

        /// <summary>Перенос выполнен и поза снята. Допуски отклонений проверяет сценарий.</summary>
        public bool Succeeded;

        /// <summary>Почему перенос не выполнен или поза не снята; пусто при успехе.</summary>
        public string Error = string.Empty;

        /// <summary>Фактически запрошенная точка пола: y из файла или текущий пол аватара.</summary>
        public Vector3 RequestedFloor;

        /// <summary>Мировая позиция камеры аватара после выдержки.</summary>
        public Vector3 CameraPosition;

        /// <summary>Курс камеры, градусы в [-180, 180): 0 — +Z, 90 — +X.</summary>
        public float CameraYaw;

        /// <summary>Тангаж камеры, градусы: плюс — взгляд вверх. Задаётся трекингом шлема, не переносом.</summary>
        public float CameraPitch;

        /// <summary>Крен камеры, градусы в [-180, 180). Задаётся трекингом шлема.</summary>
        public float CameraRoll;

        /// <summary>Высота камеры над запрошенной точкой пола, м (высота глаз от трекинга).</summary>
        public float EyeHeight;

        /// <summary>Пол аватара после переноса: проекция камеры на уровень аватара (<c>CameraFloorPosition</c>).</summary>
        public Vector3 AvatarFloor;

        /// <summary>Отклонение <see cref="AvatarFloor"/> от <see cref="RequestedFloor"/> по горизонтали, м.</summary>
        public float DeviationXZ;

        /// <summary>Отклонение курса камеры от запрошенного, градусы со знаком (<c>Mathf.DeltaAngle</c>).</summary>
        public float DeviationYaw;

        /// <summary>Отклонение уровня пола аватара от запрошенного, м.</summary>
        public float DeviationFloorY;

        /// <summary>Кадр и время (<see cref="E2EWait.Now"/>) снятия позы.</summary>
        public int Frame;
        public float Realtime;
    }

    /// <summary>
    /// Обход точек обзора локальным аватаром — для замеров «что видно отсюда» на шлеме.
    ///
    /// <para>
    /// <b>Единственный писатель позы — UltimateXR.</b> Перенос делает только
    /// <c>UxrManager.Instance.MoveAvatarTo(UxrAvatar.LocalAvatar, пол, forward)</c> — тот же путь, что игровой
    /// <c>PlayerController.RpcDevTeleport</c>; ни трансформов рига, аватара и камеры, ни трекинга, ввода и калибровки
    /// хелпер не трогает. Вызов — из корутины исполнителя сценария, не из событий <c>UxrManager</c>
    /// (AvatarUpdated/StageUpdated). После переноса — выдержка в кадрах (по умолчанию 3: успевают ноги).
    /// </para>
    ///
    /// <para>
    /// Файл точек: <c>{ "views": [ { "name": "...", "x": 0, "y": -0.01, "z": 0, "yaw": 0 } ] }</c>; y необязателен.
    /// Путь — абсолютный внутри <see cref="Application.persistentDataPath"/> или относительный к нему. Сценарий получает
    /// его параметром <c>-e2eArg views=&lt;путь&gt;</c> (<see cref="TryLoadFromContext"/>).
    /// </para>
    /// </summary>
    public static class E2EViews
    {
        /// <summary>Ключ параметра сценария с путём к файлу точек.</summary>
        public const string ArgKey = "views";

        /// <summary>Допустимые символы имени точки — как <c>$SafeToken</c> дирижёра Run-QuestE2E.ps1.</summary>
        private static readonly Regex SafeName = new Regex("^[A-Za-z0-9_.:/=+@%-]+$", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> PointKeys = new HashSet<string>(StringComparer.Ordinal) { "name", "x", "y", "z", "yaw" };

        // ── Загрузка ─────────────────────────────────────────────────────────

        /// <summary>
        /// Читает файл точек из параметра <c>views</c>. false и <paramref name="error"/> == null — параметр не задан;
        /// false и error — параметр задан, но файл не читается или с ошибкой формата.
        /// </summary>
        public static bool TryLoadFromContext(E2EContext context, out IReadOnlyList<E2EViewPoint> views, out string error)
        {
            views = null;
            error = null;
            string path = context != null ? context.Arg(ArgKey) : null;
            if (string.IsNullOrEmpty(path))
                return false;

            views = Load(path, out error);
            return views != null;
        }

        /// <summary>
        /// Читает и проверяет файл точек. Порядок точек сохраняется, имена уникальны. При ошибке — null и текст
        /// в <paramref name="error"/>; исключений наружу не бросает.
        /// </summary>
        public static IReadOnlyList<E2EViewPoint> Load(string path, out string error)
        {
            string full = ResolvePath(path, out error);
            if (full == null)
                return null;

            string text;
            try
            {
                text = File.ReadAllText(full, Encoding.UTF8);
            }
            catch (Exception e)
            {
                error = $"файл точек '{full}' не читается: {e.GetType().Name}: {e.Message}";
                return null;
            }

            try
            {
                List<E2EViewPoint> points = ParsePoints(text);
                error = null;
                return points;
            }
            catch (FormatException e)
            {
                error = $"файл точек '{full}': {e.Message}";
                return null;
            }
        }

        /// <summary>
        /// Абсолютный нормализованный путь строго внутри persistentDataPath (относительный — от него) или null с причиной.
        /// Правило то же, что у <c>E2EResult.AddArtifact</c>; сама проверка там приватная, поэтому повторена здесь.
        /// </summary>
        private static string ResolvePath(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "путь к файлу точек пуст";
                return null;
            }

            string root;
            string full;
            try
            {
                root = Path.GetFullPath(Application.persistentDataPath)
                           .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string combined = Path.IsPathRooted(path) ? path : Path.Combine(root, path);
                full = Path.GetFullPath(combined).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception e)
            {
                error = $"путь к файлу точек '{path}' не нормализуется: {e.GetType().Name}: {e.Message}";
                return null;
            }

            // На Windows файловая система регистронезависима, на Android — нет.
            StringComparison comparison =
                Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            string prefix = (root + Path.DirectorySeparatorChar).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string candidate = full.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(root) || !candidate.StartsWith(prefix, comparison))
            {
                error = $"файл точек '{path}' вне persistentDataPath ('{root}')";
                return null;
            }

            return full;
        }

        private static List<E2EViewPoint> ParsePoints(string text)
        {
            object rootValue = new JsonReader(text).ReadDocument();
            if (!(rootValue is Dictionary<string, object> root))
                throw new FormatException("корень должен быть объектом { \"views\": [...] }");
            if (!root.TryGetValue("views", out object viewsValue) || !(viewsValue is List<object> items))
                throw new FormatException("нет массива \"views\"");
            if (items.Count == 0)
                throw new FormatException("массив \"views\" пуст");

            var points = new List<E2EViewPoint>(items.Count);
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                if (!(items[i] is Dictionary<string, object> item))
                    throw new FormatException($"views[{i}] не объект");

                foreach (string key in item.Keys)
                {
                    if (!PointKeys.Contains(key))
                        throw new FormatException($"views[{i}]: неизвестное поле \"{key}\" (допустимы name, x, y, z, yaw)");
                }

                if (!item.TryGetValue("name", out object nameValue) || !(nameValue is string name) || name.Length == 0)
                    throw new FormatException($"views[{i}]: нет строки \"name\"");
                if (!SafeName.IsMatch(name))
                    throw new FormatException($"views[{i}]: имя \"{name}\" с недопустимыми символами (разрешены {SafeName})");
                if (!names.Add(name))
                    throw new FormatException($"views[{i}]: имя \"{name}\" повторяется");

                float x = RequireNumber(item, "x", i, name);
                float z = RequireNumber(item, "z", i, name);
                float yaw = RequireNumber(item, "yaw", i, name);
                bool hasY = item.ContainsKey("y");
                float y = hasY ? RequireNumber(item, "y", i, name) : 0f;

                points.Add(new E2EViewPoint { Name = name, Floor = new Vector3(x, y, z), HasFloorY = hasY, Yaw = yaw });
            }

            return points;
        }

        private static float RequireNumber(Dictionary<string, object> item, string key, int index, string name)
        {
            if (!item.TryGetValue(key, out object value))
                throw new FormatException($"views[{index}] \"{name}\": нет числа \"{key}\"");
            if (!(value is double number))
                throw new FormatException($"views[{index}] \"{name}\": \"{key}\" не число");
            if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > 1e6)
                throw new FormatException($"views[{index}] \"{name}\": \"{key}\" вне допустимого диапазона");
            return (float)number;
        }

        // ── Перенос ──────────────────────────────────────────────────────────

        /// <summary>
        /// Переносит локальный аватар в точку штатным <c>UxrManager.MoveAvatarTo</c>, выжидает
        /// <paramref name="settleFrames"/> кадров и снимает фактическую позу камеры в <paramref name="outcome"/>.
        /// Только <c>yield return null</c> — без вложенных корутин; прокручивается исполнителем сценария.
        /// </summary>
        public static IEnumerator GoTo(E2EViewPoint point, E2EViewOutcome outcome, int settleFrames = 3)
        {
            outcome.Point = point;
            outcome.Succeeded = false;
            outcome.Error = string.Empty;

            if (point == null)
            {
                outcome.Error = "точка не задана";
                yield break;
            }

            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            string refusal = CheckAvatar(avatar);
            if (refusal != null)
            {
                outcome.Error = refusal;
                yield break;
            }

            // MoveAvatarTo синхронизируемый (BeginSync): переносит только автор аватара, иначе копии разойдутся
            // по сети от каждой машины (known-issues, Issue 23).
            if (!StateEventAuthority.IsAuthoredHere(avatar))
            {
                outcome.Error = "локальный аватар не авторский на этой машине — синхронизируемый перенос запрещён";
                yield break;
            }

            // MoveAvatarTo совмещает текущий горизонтальный взгляд камеры с запрошенным: при вертикальном взгляде
            // курса нет, и выравнивание выродится.
            if (avatar.ProjectedCameraForward.sqrMagnitude < 1e-6f)
            {
                outcome.Error = "камера смотрит вертикально — курс не определён; поставь шлем ровно";
                yield break;
            }

            Vector3 floor = point.HasFloorY
                ? point.Floor
                : new Vector3(point.Floor.x, avatar.transform.position.y, point.Floor.z);
            Vector3 forward = Quaternion.Euler(0f, point.Yaw, 0f) * Vector3.forward;
            outcome.RequestedFloor = floor;

            string moveError = Move(avatar, floor, forward);
            if (moveError != null)
            {
                outcome.Error = moveError;
                yield break;
            }

            for (int i = 0; i < settleFrames; i++)
                yield return null;

            if (UxrAvatar.LocalAvatar != avatar)
            {
                outcome.Error = "локальный аватар сменился или исчез за время выдержки";
                yield break;
            }

            refusal = CheckAvatar(avatar);
            if (refusal != null)
            {
                outcome.Error = "после выдержки: " + refusal;
                yield break;
            }

            Measure(avatar, outcome);
            outcome.Succeeded = true;
        }

        /// <summary>Единственная запись позы: штатный синхронный перенос UltimateXR. Ошибка — текстом, без исключения.</summary>
        private static string Move(UxrAvatar avatar, Vector3 floor, Vector3 forward)
        {
            try
            {
                UxrManager.Instance.MoveAvatarTo(avatar, floor, forward);
                return null;
            }
            catch (Exception e)
            {
                return $"MoveAvatarTo бросил {e.GetType().Name}: {e.Message}";
            }
        }

        /// <summary>Причина, по которой аватар нельзя переносить, или null.</summary>
        private static string CheckAvatar(UxrAvatar avatar)
        {
            if (avatar == null) return "нет UxrAvatar.LocalAvatar";
            if (avatar.CameraComponent == null) return "у локального аватара нет камеры (CameraComponent)";
            if (UxrManager.Instance == null) return "нет UxrManager.Instance";
            return null;
        }

        private static void Measure(UxrAvatar avatar, E2EViewOutcome outcome)
        {
            Transform cameraTransform = avatar.CameraComponent.transform;
            Vector3 euler = cameraTransform.rotation.eulerAngles;

            outcome.CameraPosition = cameraTransform.position;
            outcome.CameraYaw = Signed(euler.y);
            outcome.CameraPitch = -Signed(euler.x);
            outcome.CameraRoll = Signed(euler.z);
            outcome.EyeHeight = outcome.CameraPosition.y - outcome.RequestedFloor.y;
            outcome.AvatarFloor = avatar.CameraFloorPosition;

            Vector2 deltaXZ = new Vector2(outcome.AvatarFloor.x - outcome.RequestedFloor.x,
                                          outcome.AvatarFloor.z - outcome.RequestedFloor.z);
            outcome.DeviationXZ = deltaXZ.magnitude;
            outcome.DeviationYaw = Mathf.DeltaAngle(outcome.Point.Yaw, outcome.CameraYaw);
            outcome.DeviationFloorY = outcome.AvatarFloor.y - outcome.RequestedFloor.y;
            outcome.Frame = Time.frameCount;
            outcome.Realtime = E2EWait.Now;
        }

        /// <summary>Угол Эйлера [0, 360) → [-180, 180).</summary>
        private static float Signed(float degrees) => Mathf.DeltaAngle(0f, degrees);

        // ── Артефакт ─────────────────────────────────────────────────────────

        /// <summary>
        /// JSON итогов обхода (артефакт views.json): по точке — имя, запрос, фактическая поза камеры, пол аватара,
        /// отклонения. Чистый ASCII, как у <c>E2EResult.ToJson</c>.
        /// </summary>
        public static string ToJson(IReadOnlyList<E2EViewOutcome> outcomes)
        {
            StringBuilder sb = new StringBuilder(512);
            sb.Append("{\n  \"views\": [");
            int count = outcomes != null ? outcomes.Count : 0;
            for (int i = 0; i < count; i++)
            {
                E2EViewOutcome o = outcomes[i];
                E2EViewPoint p = o.Point;
                sb.Append(i == 0 ? "\n" : ",\n");
                sb.Append("    {\n");
                sb.Append("      \"name\": ").Append(Json(p != null ? p.Name : null)).Append(",\n");
                sb.Append("      \"succeeded\": ").Append(o.Succeeded ? "true" : "false").Append(",\n");
                sb.Append("      \"error\": ").Append(Json(o.Error)).Append(",\n");

                if (p != null)
                {
                    sb.Append("      \"requested\": { \"x\": ").Append(Num(p.Floor.x))
                      .Append(", \"y\": ").Append(p.HasFloorY ? Num(p.Floor.y) : "null")
                      .Append(", \"z\": ").Append(Num(p.Floor.z))
                      .Append(", \"yaw\": ").Append(Num(p.Yaw))
                      .Append(", \"floorY\": ").Append(o.Succeeded ? Num(o.RequestedFloor.y) : "null")
                      .Append(", \"floorYFromAvatar\": ").Append(p.HasFloorY ? "false" : "true")
                      .Append(" },\n");
                }
                else
                {
                    sb.Append("      \"requested\": null,\n");
                }

                if (o.Succeeded)
                {
                    sb.Append("      \"camera\": { \"x\": ").Append(Num(o.CameraPosition.x))
                      .Append(", \"y\": ").Append(Num(o.CameraPosition.y))
                      .Append(", \"z\": ").Append(Num(o.CameraPosition.z))
                      .Append(", \"yaw\": ").Append(Num(o.CameraYaw))
                      .Append(", \"pitch\": ").Append(Num(o.CameraPitch))
                      .Append(", \"roll\": ").Append(Num(o.CameraRoll))
                      .Append(", \"eyeHeight\": ").Append(Num(o.EyeHeight))
                      .Append(" },\n");
                    sb.Append("      \"avatarFloor\": { \"x\": ").Append(Num(o.AvatarFloor.x))
                      .Append(", \"y\": ").Append(Num(o.AvatarFloor.y))
                      .Append(", \"z\": ").Append(Num(o.AvatarFloor.z))
                      .Append(" },\n");
                    sb.Append("      \"deviation\": { \"xz\": ").Append(Num(o.DeviationXZ))
                      .Append(", \"yaw\": ").Append(Num(o.DeviationYaw))
                      .Append(", \"floorY\": ").Append(Num(o.DeviationFloorY))
                      .Append(" },\n");
                    sb.Append("      \"frame\": ").Append(o.Frame.ToString(CultureInfo.InvariantCulture))
                      .Append(", \"realtime\": ").Append(Num(o.Realtime)).Append('\n');
                }
                else
                {
                    sb.Append("      \"camera\": null, \"avatarFloor\": null, \"deviation\": null\n");
                }

                sb.Append("    }");
            }

            sb.Append(count > 0 ? "\n  ]\n}" : "]\n}");
            return sb.ToString();
        }

        private static string Num(float value) => value.ToString("0.0####", CultureInfo.InvariantCulture);

        /// <summary>Строка в JSON: всё за пределами печатного ASCII — через \uXXXX (как в E2EResult).</summary>
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

        // ── Минимальный разбор JSON ──────────────────────────────────────────

        /// <summary>
        /// Строгий разбор JSON в Dictionary/List/double/string/bool/null. JsonUtility не отличает отсутствующее поле от
        /// нуля (y необязателен) и молчит об ошибках формата; Newtonsoft в рантайм-сборке не подключён.
        /// Ошибки — <see cref="FormatException"/> с позицией.
        /// </summary>
        private sealed class JsonReader
        {
            private const int MaxDepth = 32;
            private readonly string _text;
            private int _pos;

            public JsonReader(string text)
            {
                _text = text ?? string.Empty;
                // Допускаем BOM: файл могли сохранить в Windows-редакторе.
                if (_text.Length > 0 && _text[0] == '﻿') _pos = 1;
            }

            public object ReadDocument()
            {
                object value = ReadValue(0);
                SkipWhitespace();
                if (_pos < _text.Length) throw Error("лишние символы после JSON");
                return value;
            }

            private object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("слишком глубокая вложенность");
                SkipWhitespace();
                if (_pos >= _text.Length) throw Error("неожиданный конец файла");

                char ch = _text[_pos];
                switch (ch)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': ReadLiteral("true"); return true;
                    case 'f': ReadLiteral("false"); return false;
                    case 'n': ReadLiteral("null"); return null;
                    default:
                        if (ch == '-' || (ch >= '0' && ch <= '9')) return ReadNumber();
                        throw Error($"неожиданный символ '{ch}'");
                }
            }

            private Dictionary<string, object> ReadObject(int depth)
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                _pos++;
                SkipWhitespace();
                if (Peek() == '}') { _pos++; return result; }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"') throw Error("ожидалось имя поля в кавычках");
                    string key = ReadString();
                    SkipWhitespace();
                    if (Peek() != ':') throw Error("ожидалось ':'");
                    _pos++;
                    object value = ReadValue(depth + 1);
                    if (result.ContainsKey(key)) throw Error($"поле \"{key}\" повторяется");
                    result[key] = value;

                    SkipWhitespace();
                    char next = Peek();
                    _pos++;
                    if (next == '}') return result;
                    if (next != ',') throw Error("ожидалось ',' или '}'");
                }
            }

            private List<object> ReadArray(int depth)
            {
                var result = new List<object>();
                _pos++;
                SkipWhitespace();
                if (Peek() == ']') { _pos++; return result; }

                while (true)
                {
                    result.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    char next = Peek();
                    _pos++;
                    if (next == ']') return result;
                    if (next != ',') throw Error("ожидалось ',' или ']'");
                }
            }

            private string ReadString()
            {
                var sb = new StringBuilder();
                _pos++;
                while (true)
                {
                    if (_pos >= _text.Length) throw Error("незакрытая строка");
                    char ch = _text[_pos++];
                    if (ch == '"') return sb.ToString();
                    if (ch < 0x20) throw Error("управляющий символ в строке");
                    if (ch != '\\') { sb.Append(ch); continue; }

                    if (_pos >= _text.Length) throw Error("незакрытая строка");
                    char esc = _text[_pos++];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_pos + 4 > _text.Length ||
                                !int.TryParse(_text.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw Error("неверная последовательность \\u");
                            sb.Append((char)code);
                            _pos += 4;
                            break;
                        default: throw Error($"неверная escape-последовательность \\{esc}");
                    }
                }
            }

            private double ReadNumber()
            {
                int start = _pos;
                while (_pos < _text.Length && "+-0123456789.eE".IndexOf(_text[_pos]) >= 0) _pos++;
                string token = _text.Substring(start, _pos - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw Error($"неверное число '{token}'");
                return value;
            }

            private void ReadLiteral(string literal)
            {
                if (string.CompareOrdinal(_text, _pos, literal, 0, literal.Length) != 0)
                    throw Error($"ожидалось '{literal}'");
                _pos += literal.Length;
            }

            private void SkipWhitespace()
            {
                while (_pos < _text.Length && (_text[_pos] == ' ' || _text[_pos] == '\t' || _text[_pos] == '\n' || _text[_pos] == '\r'))
                    _pos++;
            }

            private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

            private FormatException Error(string message) =>
                new FormatException($"неверный JSON (символ {_pos}): {message}");
        }
    }
}
#endif
