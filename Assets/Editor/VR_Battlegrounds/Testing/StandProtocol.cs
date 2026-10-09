using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace VrBattlegrounds.EditorTools.TestStand
{
    /// <summary>Адрес и данные одной команды. Роль устройства не является адресом.</summary>
    [Serializable]
    public sealed class StandRequest
    {
        public string RunId;
        public string ParticipantId;
        public string ProcessSessionId;
        public string RequestId;
        public string Action;
        public string MarkerName;
        public int TimeBudgetMs = 5000;
        public int ExpectedConnectionEpoch;
        public uint ExpectedAvatarNetId;
        public string Scenario;
        public int ScenarioTimeoutSeconds = 240;
    }

    /// <summary>Фактический получатель и исход команды, отдельно от успеха вызова MCP.</summary>
    [Serializable]
    public sealed class StandReply
    {
        public bool Passed;
        public string Code;
        public string Message;
        public string RunId;
        public string ParticipantId;
        public string ProcessSessionId;
        public string RequestId;
        public int ProcessId;
        public string MarkerName;
        public bool MarkerExists;
        public int MarkerCount;
        public string Data;

        public static StandReply Ok(string code, string marker = null, int count = 0, bool exists = false)
        {
            return new StandReply { Passed = true, Code = code, MarkerName = marker, MarkerCount = count, MarkerExists = exists };
        }

        public static StandReply Error(string code, string message = null)
        {
            return new StandReply { Code = code, Message = message };
        }

        internal StandReply Copy() => (StandReply)MemberwiseClone();
    }

    /// <summary>
    /// Единственный допуск команд участника. Ошибки адреса проверяются до кэша и эффекта.
    /// Выполненные RequestId не вытесняются: при заполнении запуск отказывает новым запросам.
    /// </summary>
    public sealed class StandRequestGate
    {
        private sealed class Entry
        {
            public string Fingerprint;
            public StandReply Reply;
        }

        private readonly string _run;
        private readonly string _participant;
        private readonly string _session;
        private readonly int _capacity;
        private readonly int _processId;
        private readonly Dictionary<string, Entry> _completed = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private bool _active = true;

        public StandRequestGate(string run, string participant, string session, int capacity = 256, int processId = 0)
        {
            if (string.IsNullOrEmpty(run) || string.IsNullOrEmpty(participant) || string.IsNullOrEmpty(session) || capacity < 1)
                throw new ArgumentException("Для допуска нужны полный адрес и положительная ёмкость.");
            _run = run;
            _participant = participant;
            _session = session;
            _capacity = capacity;
            _processId = processId;
        }

        public void Revoke()
        {
            _active = false;
            _completed.Clear();
        }

        public StandReply Execute(StandRequest request, Func<StandRequest, StandReply> execute)
        {
            string error = Validate(request);
            if (error != null) return Address(StandReply.Error(error), request);
            string fingerprint = request.Action + "|" + request.MarkerName + "|" + request.TimeBudgetMs.ToString(CultureInfo.InvariantCulture) + "|" + request.ExpectedConnectionEpoch + "|" + request.ExpectedAvatarNetId + "|" + request.Scenario + "|" + request.ScenarioTimeoutSeconds;
            if (_completed.TryGetValue(request.RequestId, out Entry old))
            {
                if (old.Fingerprint != fingerprint) return Address(StandReply.Error("request-conflict"), request);
                return old.Reply.Copy();
            }
            if (_completed.Count >= _capacity) return Address(StandReply.Error("capacity"), request);

            StandReply reply;
            var watch = Stopwatch.StartNew();
            try
            {
                reply = execute(request) ?? StandReply.Error("execution-error", "Исполнитель не вернул результат.");
                if (watch.ElapsedMilliseconds > request.TimeBudgetMs)
                    reply = StandReply.Error("execution-timeout", "Команда завершилась после бюджета; повтор не исполняет её заново.");
            }
            catch (Exception e)
            {
                reply = StandReply.Error("execution-error", e.GetType().Name + ": " + e.Message);
            }
            reply = Address(reply, request);
            _completed.Add(request.RequestId, new Entry { Fingerprint = fingerprint, Reply = reply.Copy() });
            return reply;
        }

        private string Validate(StandRequest request)
        {
            if (!_active) return "inactive";
            if (request == null || string.IsNullOrEmpty(request.RequestId) || request.RequestId.Length > 128) return "invalid-request";
            if (request.RunId != _run) return "wrong-run";
            if (request.ParticipantId != _participant) return "wrong-participant";
            if (request.ProcessSessionId != _session) return "wrong-session";
            if (request.TimeBudgetMs < 1 || request.TimeBudgetMs > 30000) return "invalid-budget";
            bool network = request.Action == "disconnect" || request.Action == "reconnect";
            bool e2e = request.Action == "run-e2e";
            if (request.Action != "create-marker" && request.Action != "read-marker" && request.Action != "remove-marker" && request.Action != "state" && !network && !e2e) return "invalid-action";
            if (e2e && (!ValidMarker(request.Scenario) || request.ScenarioTimeoutSeconds < 1 || request.ScenarioTimeoutSeconds > 3600)) return "scenario-invalid";
            if (network && request.ExpectedConnectionEpoch < 1) return "connection-epoch-required";
            if (!network && !e2e && request.Action != "state" && !ValidMarker(request.MarkerName)) return "invalid-marker";
            if ((network || e2e || request.Action == "state") && !string.IsNullOrEmpty(request.MarkerName)) return "invalid-marker";
            return null;
        }

        private static bool ValidMarker(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char c in value)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
            return true;
        }

        private StandReply Address(StandReply reply, StandRequest request)
        {
            reply.RunId = _run;
            reply.ParticipantId = _participant;
            reply.ProcessSessionId = _session;
            reply.RequestId = request?.RequestId;
            reply.ProcessId = _processId;
            return reply;
        }
    }
}
