string projectRoot = System.IO.Path.GetFullPath(Application.dataPath + "/..").Replace("\\", "/");
if (projectRoot != "F:/CodexWorktrees/unity-editor-worker/Vr_Battlegrounds_ai" || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    return new { executionCompleted = true, refused = true, projectRoot };
const string Attempt = "tasks/bots-fix/reports/cc1-round1/t1-direct-intent.json";
const string Reply = "tasks/bots-fix/reports/cc1-round1/t1-direct-response.json";
if (System.IO.File.Exists(Attempt))
    return new { executionCompleted = true, refused = true, mustNotRepeat = true,
        response = System.IO.File.Exists(Reply) ? System.IO.File.ReadAllText(Reply) : null,
        next = "Читать SessionState MCPForUnity.TestJobsV1; не вызывать RunTests повторно." };
var args = new Newtonsoft.Json.Linq.JObject {
    { "mode", "EditMode" },
    { "groupNames", new Newtonsoft.Json.Linq.JArray(new[] {
        @"^VrBattlegrounds\.Tests\.Bots\.",
        @"^VrBattlegrounds\.Tests\.DevTools\.(BotTeamChoiceTests|BotSessionTests|ServerAuthoredAvatarTests)" }) }
};
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Attempt));
using (var stream = System.IO.File.Open(Attempt, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read))
using (var writer = new System.IO.StreamWriter(stream, new System.Text.UTF8Encoding(false)))
    writer.Write(Newtonsoft.Json.JsonConvert.SerializeObject(new { requestedUtc = System.DateTime.UtcNow.ToString("o"), args }));
var replyTask = MCPForUnity.Editor.Tools.RunTests.HandleCommand(args);
if (replyTask.Status != System.Threading.Tasks.TaskStatus.RanToCompletion)
    return new { executionCompleted = true, responsePending = true, mustNotRepeat = true,
        taskStatus = replyTask.Status.ToString(),
        next = "Читать SessionState MCPForUnity.TestJobsV1; не ждать и не повторять RunTests." };
var response = Newtonsoft.Json.Linq.JObject.FromObject(replyTask.Result);
System.IO.File.WriteAllText(Reply, response.ToString(), new System.Text.UTF8Encoding(false));
return new { executionCompleted = true, mustNotRepeat = true,
    job_id = (string)response.SelectToken("data.job_id"), response = response, responsePath = Reply };

