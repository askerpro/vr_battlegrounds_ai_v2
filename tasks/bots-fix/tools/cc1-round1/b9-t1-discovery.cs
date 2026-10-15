// B9: одна штатная discovery существующих EditMode fixtures, без запуска тестов.
// Полный список leaf tests сохраняется до run_tests; этот intent не повторять.
const string Root = "tasks/bots-fix/reports/cc1-round1";
System.IO.Directory.CreateDirectory(Root);
string intentPath = Root + "/discovery-intent.json";
string resultPath = Root + "/discovery-result.json";
if (System.IO.File.Exists(intentPath))
    return new { executionCompleted = true, refused = true, reason = "Discovery intent уже существует", resultPath };
var expected = new[] {
    "VrBattlegrounds.Tests.Bots.BotAuthorityTests",
    "VrBattlegrounds.Tests.Bots.BotBuyPlanTests",
    "VrBattlegrounds.Tests.Bots.BotMatchPlanTests",
    "VrBattlegrounds.Tests.Bots.BotOrdersTests",
    "VrBattlegrounds.Tests.Bots.BotRouteTests",
    "VrBattlegrounds.Tests.DevTools.BotTeamChoiceTests",
    "VrBattlegrounds.Tests.DevTools.BotSessionTests",
    "VrBattlegrounds.Tests.DevTools.ServerAuthoredAvatarTests"
};
System.IO.File.WriteAllText(intentPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
    requestedUtc = DateTime.UtcNow.ToString("o"), mode = "EditMode", expected,
    route = "TestRunnerApi.RetrieveTestList; без Execute" }));
var api = ScriptableObject.CreateInstance<UnityEditor.TestTools.TestRunner.Api.TestRunnerApi>();
try {
    api.RetrieveTestList(UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode, root => {
        try {
            var found = new HashSet<string>();
            var counts = expected.ToDictionary(t => t, t => 0);
            var leaves = new List<object>();
            int notRunnable = 0;
            Action<UnityEditor.TestTools.TestRunner.Api.ITestAdaptor> visit = null;
            visit = node => {
                if (node.IsSuite && expected.Contains(node.FullName)) found.Add(node.FullName);
                if (!node.IsSuite) {
                    string fixture = expected.FirstOrDefault(t => node.FullName.StartsWith(t + ".", StringComparison.Ordinal));
                    if (fixture != null) {
                        string state = node.RunState.ToString();
                        if (state != "Runnable") notRunnable++;
                        counts[fixture]++;
                        leaves.Add(new { id = node.Id, fullName = node.FullName, fixture, runState = state });
                    }
                }
                foreach (var child in node.Children) visit(child);
            };
            visit(root);
            bool passed = found.SetEquals(expected) && counts.All(p => p.Value > 0) && notRunnable == 0;
            System.IO.File.WriteAllText(resultPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
                completedUtc = DateTime.UtcNow.ToString("o"), passed, expected, found = found.OrderBy(t => t).ToArray(),
                counts, total = leaves.Count, notRunnable, tests = leaves }, Newtonsoft.Json.Formatting.Indented));
        } catch (Exception error) {
            System.IO.File.WriteAllText(resultPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
                passed = false, error = error.ToString(), expected }));
        } finally { UnityEngine.Object.DestroyImmediate(api); }
    });
} catch (Exception error) {
    UnityEngine.Object.DestroyImmediate(api);
    System.IO.File.WriteAllText(resultPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
        passed = false, error = error.ToString(), expected }));
    throw;
}
return new { executionCompleted = true, started = true, resultPath,
    note = "Ожидать полный discovery-result.json; тесты не запускались" };
