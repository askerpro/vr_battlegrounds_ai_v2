// Ярус C — захват ошибок лога процесса для вердикта по логам (контракт quest-device-run r4).
// Отключить из релизной сборки: определить VRBG_NO_E2E в Scripting Define Symbols.
#if !VRBG_NO_E2E
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace VrBattlegrounds.DevTools.E2E
{
    /// <summary>
    /// Одна агрегированная ошибка лога: все повторы с тем же типом и той же первой строкой сообщения.
    /// </summary>
    public sealed class E2ELogIssue
    {
        /// <summary><c>Error</c>, <c>Exception</c> или <c>Assert</c>.</summary>
        public string Type = string.Empty;

        /// <summary>Первая строка сообщения (обрезана до <see cref="E2ELogCapture.MaxMessageLength"/>).</summary>
        public string Message = string.Empty;

        /// <summary>
        /// Начало стека первого появления: продолжение многострочного сообщения, затем первые кадры —
        /// не больше <see cref="E2ELogCapture.StackLines"/> строк.
        /// </summary>
        public string Stack = string.Empty;

        /// <summary>Сколько раз ошибка пришла за время захвата.</summary>
        public int Count;

        /// <summary>Секунды от создания захвата (≈ создания результата) до первого появления.</summary>
        public double FirstAtSeconds;

        internal E2ELogIssue Copy()
        {
            return new E2ELogIssue
            {
                Type = Type,
                Message = Message,
                Stack = Stack,
                Count = Count,
                FirstAtSeconds = FirstAtSeconds
            };
        }
    }

    /// <summary>
    /// Собирает Error/Exception/Assert процесса в список <see cref="E2ELogIssue"/> для результата e2e.
    /// Вердикт по ним выносит дирижёр (Tools/e2e/E2ELogVerdict.psm1), а не сценарий:
    /// <see cref="E2EResult.Passed"/> от захвата не зависит.
    ///
    /// Обработчик висит на <see cref="Application.logMessageReceivedThreaded"/> и зовётся с любого потока.
    /// Условие perf-зоны: Log и Warning отбрасываются первой проверкой по <see cref="LogType"/> — без
    /// блокировок и аллокаций, поэтому захват не влияет на замер. Внутри обработчика ничего не логируется
    /// (рекурсия в тот же обработчик).
    ///
    /// В процессе подписан не больше чем один захват: установка нового снимает прежний, повторная установка
    /// того же — без эффекта. Подписка ставится и снимается владельцем — <see cref="E2EResult"/>.
    /// </summary>
    public sealed class E2ELogCapture
    {
        /// <summary>Предел уникальных записей по умолчанию; сверх него ошибки только считаются.</summary>
        public const int DefaultMaxIssues = 200;

        /// <summary>Сколько строк стека хранится у записи.</summary>
        public const int StackLines = 5;

        /// <summary>Предел длины одной строки стека.</summary>
        public const int MaxStackLineLength = 300;

        /// <summary>Предел длины первой строки сообщения.</summary>
        public const int MaxMessageLength = 500;

        private const string TypeError = "Error";
        private const string TypeException = "Exception";
        private const string TypeAssert = "Assert";

        // Подписанный сейчас захват процесса. Статика переживает Play без перезагрузки домена — см. ResetOnPlay.
        private static readonly object InstallLock = new object();
        private static E2ELogCapture _installed;

        private readonly object _lock = new object();
        private readonly Dictionary<string, E2ELogIssue> _byKey = new Dictionary<string, E2ELogIssue>(StringComparer.Ordinal);
        private readonly List<E2ELogIssue> _issues = new List<E2ELogIssue>();
        private readonly int _maxIssues;
        private readonly long _startTimestamp;
        private readonly Application.LogCallback _handler;
        private int _dropped;

        public E2ELogCapture() : this(DefaultMaxIssues)
        {
        }

        public E2ELogCapture(int maxIssues)
        {
            _maxIssues = Math.Max(1, maxIssues);
            _startTimestamp = Stopwatch.GetTimestamp();
            // Делегат создаётся один раз: подписка и отписка идут одним экземпляром.
            _handler = Handle;
        }

        /// <summary>Захват сейчас подписан на лог процесса.</summary>
        public bool IsInstalled
        {
            get
            {
                lock (InstallLock)
                    return _installed == this;
            }
        }

        /// <summary>
        /// Ошибки, не вошедшие в лимит уникальных записей (каждое появление), плюс сбои самого обработчика.
        /// </summary>
        public int Dropped
        {
            get
            {
                lock (_lock)
                    return _dropped;
            }
        }

        /// <summary>Подписывает захват на лог процесса. Идемпотентно; прежний захват процесса снимается.</summary>
        public void Install()
        {
            lock (InstallLock)
            {
                if (_installed == this)
                    return;

                if (_installed != null)
                    Application.logMessageReceivedThreaded -= _installed._handler;

                Application.logMessageReceivedThreaded += _handler;
                _installed = this;
            }
        }

        /// <summary>Снимает подписку, если она этого захвата. Собранные записи сохраняются.</summary>
        public void Uninstall()
        {
            lock (InstallLock)
            {
                if (_installed != this)
                    return;

                Application.logMessageReceivedThreaded -= _handler;
                _installed = null;
            }
        }

        /// <summary>
        /// Play без перезагрузки домена (Enter Play Mode Options) сохраняет статику и подписку прошлого Play:
        /// снимаем её до загрузки сцен, чтобы два захвата не писали в разные результаты.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            lock (InstallLock)
            {
                if (_installed == null)
                    return;

                Application.logMessageReceivedThreaded -= _installed._handler;
                _installed = null;
            }
        }

        /// <summary>Обработчик лога. Публичен для проверок харнесса; в игре его зовёт только Unity.</summary>
        public void Handle(string condition, string stackTrace, LogType type)
        {
            // Первая проверка — условие perf-зоны: Log и Warning уходят без работы и без аллокаций.
            if (type == LogType.Log || type == LogType.Warning)
                return;

            try
            {
                Record(type, condition, stackTrace);
            }
            catch
            {
                // Логировать отсюда нельзя (рекурсия в этот же обработчик): сбой виден в счётчике отброшенных.
                lock (_lock)
                    _dropped++;
            }
        }

        /// <summary>Копия собранных записей в порядке первого появления.</summary>
        public List<E2ELogIssue> Snapshot()
        {
            lock (_lock)
            {
                var copy = new List<E2ELogIssue>(_issues.Count);
                for (int i = 0; i < _issues.Count; i++)
                    copy.Add(_issues[i].Copy());
                return copy;
            }
        }

        private void Record(LogType type, string condition, string stackTrace)
        {
            string typeName = TypeName(type);
            string text = condition ?? string.Empty;
            int firstEnd = LineEnd(text, 0);
            string message = Clip(text.Substring(0, firstEnd), MaxMessageLength);
            // Ключ без меняющихся идентификаторов: иначе одна ошибка с разными GUID/netId дробится на записи и
            // исчерпывает лимит. В записи остаётся исходный текст первого появления.
            string key = typeName + "\n" + NormalizeKey(message);
            double at = (Stopwatch.GetTimestamp() - _startTimestamp) / (double)Stopwatch.Frequency;

            lock (_lock)
            {
                if (_byKey.TryGetValue(key, out E2ELogIssue issue))
                {
                    issue.Count++;
                    return;
                }

                if (_issues.Count >= _maxIssues)
                {
                    _dropped++;
                    return;
                }

                issue = new E2ELogIssue
                {
                    Type = typeName,
                    Message = message,
                    Stack = StackHead(text, firstEnd, stackTrace),
                    Count = 1,
                    FirstAtSeconds = at
                };
                _byKey.Add(key, issue);
                _issues.Add(issue);
            }
        }

        private static string TypeName(LogType type)
        {
            switch (type)
            {
                case LogType.Exception: return TypeException;
                case LogType.Assert: return TypeAssert;
                default: return TypeError;
            }
        }

        /// <summary>Продолжение сообщения после первой строки, затем стек — первые непустые строки.</summary>
        private static string StackHead(string condition, int firstEnd, string stackTrace)
        {
            var sb = new StringBuilder(256);
            int lines = 0;
            AppendLines(sb, condition, firstEnd, ref lines);
            AppendLines(sb, stackTrace ?? string.Empty, 0, ref lines);
            return sb.ToString();
        }

        private static void AppendLines(StringBuilder sb, string text, int start, ref int lines)
        {
            int pos = start;
            while (pos < text.Length && lines < StackLines)
            {
                int end = LineEnd(text, pos);
                string line = text.Substring(pos, end - pos).Trim();
                if (line.Length > 0)
                {
                    if (sb.Length > 0)
                        sb.Append('\n');
                    sb.Append(Clip(line, MaxStackLineLength));
                    lines++;
                }

                pos = end + 1;
            }
        }

        private static int LineEnd(string text, int start)
        {
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '\n' || text[i] == '\r')
                    return i;
            }

            return text.Length;
        }

        /// <summary>
        /// Ключ группировки: каждое слово из букв и цифр, где есть цифра и все символы шестнадцатеричные
        /// (числа, части GUID, хеши), заменяется на <c>#</c>. Имена вида <c>Spine01</c> остаются как есть.
        /// </summary>
        internal static string NormalizeKey(string message)
        {
            var sb = new StringBuilder(message.Length);
            int i = 0;
            while (i < message.Length)
            {
                if (!char.IsLetterOrDigit(message[i]))
                {
                    sb.Append(message[i]);
                    i++;
                    continue;
                }

                int start = i;
                bool hasDigit = false;
                bool allHex = true;
                while (i < message.Length && char.IsLetterOrDigit(message[i]))
                {
                    char c = message[i];
                    if (c >= '0' && c <= '9') hasDigit = true;
                    else if (!((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) allHex = false;
                    i++;
                }

                if (hasDigit && allHex) sb.Append('#');
                else sb.Append(message, start, i - start);
            }

            return sb.ToString();
        }

        private static string Clip(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }
    }
}
#endif
