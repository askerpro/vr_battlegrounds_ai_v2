// B2 READ-ONLY: наблюдение сохранённого test job после потери ответа T1; не зовёт TestJobManager/GetJob.
// execute_code требует собственной RUNNING-аренды даже для этого чтения. Перед повтором T1 сначала этот снимок.
// Ключи содержат метаданные последних 10 jobs; полного Result, фильтров и SHA в них нет.
const string Root = "tasks/bots-fix/reports/cc1-round1";
const string ReceiptLabel = "historical-644";
const long HistoricalMin = 1791999021821L; // B1 в644: контроллер уже проверен до единственного вызова T1.
const long HistoricalMax = 1791999366010L; // terminal DONE644; это окно наблюдения, не доказательство результата.
string projectRoot = System.IO.Path.GetFullPath(Application.dataPath + "/..").Replace("\\", "/");
if (projectRoot != "F:/CodexWorktrees/unity-editor-worker/Vr_Battlegrounds_ai")
    return new { executionCompleted = true, readOnly = true, refused = true, projectRoot };
string raw = UnityEditor.SessionState.GetString("MCPForUnity.TestJobsV1", "");
string current = UnityEditor.SessionState.GetString("MCPForUnity.CurrentTestJobIdV1", "");
var snapshot = string.IsNullOrEmpty(raw) ? null : Newtonsoft.Json.Linq.JObject.Parse(raw);
var jobs = snapshot != null ? snapshot["jobs"] as Newtonsoft.Json.Linq.JArray : null;
var rows = new List<object>();
if (jobs != null) foreach (var job in jobs) {
    long? started = (long?)job["started_unix_ms"];
    var failures = job["failures_so_far"] as Newtonsoft.Json.Linq.JArray;
    rows.Add(new {
        jobId = (string)job["job_id"], status = (string)job["status"], mode = (string)job["mode"],
        startedUnixMs = started, finishedUnixMs = (long?)job["finished_unix_ms"],
        lastUpdateUnixMs = (long?)job["last_update_unix_ms"],
        total = (int?)job["total_tests"], completed = (int?)job["completed_tests"],
        currentTest = (string)job["current_test_full_name"], lastTest = (string)job["last_finished_test_full_name"],
        error = (string)job["error"], failuresPresent = failures != null,
        failureCount = failures != null ? failures.Count : -1,
        failureSample = failures != null ? failures.Take(5).Select(f => new {
            fullName = (string)f["FullName"], message = (string)f["Message"] }).ToArray() : null,
        inHistorical644Window = started.HasValue && started.Value >= HistoricalMin && started.Value <= HistoricalMax
    });
}
System.IO.Directory.CreateDirectory(Root);
string reportPath = Root + "/test-receipt-" + ReceiptLabel + "-" + DateTime.UtcNow.Ticks + ".json";
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    executionCompleted = true, readOnly = true, projectRoot, observedUtc = DateTime.UtcNow.ToString("o"),
    currentJob = current, snapshot, historicalWindow = new { min = HistoricalMin, max = HistoricalMax },
    scope = "Только SessionState. Пустой или вытесненный record не доказывает незапуск; полного Result/фильтров/SHA нет."
}, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, readOnly = true, projectRoot, currentJob = current,
    snapshotCurrentJob = snapshot != null ? (string)snapshot["current_job_id"] : null,
    reportPath, jobCount = jobs != null ? jobs.Count : 0, jobs = rows };
