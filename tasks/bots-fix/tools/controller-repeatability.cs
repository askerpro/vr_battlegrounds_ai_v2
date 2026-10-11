var paths = new[] {
    "Assets/Resources/Bots/BotPistol.controller",
    "Assets/Resources/Bots/BotRifle.controller",
    "Assets/Resources/Bots/BotUnarmed.controller",
    "Assets/Resources/Bots/BotCombatAssets.asset"
};
VrBattlegrounds.EditorTools.BotCombatSetup.Build();
var first = paths.ToDictionary(p => p, p => System.IO.File.ReadAllBytes(p));
var ids = paths.Where(p => p.EndsWith(".controller")).ToDictionary(p => p,
    p => UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(p)
        .layers[0].stateMachine.states.ToDictionary(s => s.state.name,
            s => UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(s.state).ToString()));
VrBattlegrounds.EditorTools.BotCombatSetup.Build();
var checks = paths.Select(p => new {
    path = p,
    bytesStable = first[p].SequenceEqual(System.IO.File.ReadAllBytes(p)),
    statesStable = !p.EndsWith(".controller") || UnityEditor.AssetDatabase
        .LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(p).layers[0].stateMachine.states
        .All(s => ids[p].TryGetValue(s.state.name, out var id) &&
            id == UnityEditor.GlobalObjectId.GetGlobalObjectIdSlow(s.state).ToString())
}).ToArray();
var report = new { passed = checks.All(c => c.bytesStable && c.statesStable), checks = checks };
var root = "tasks/bots-fix/reports/controller-repeatability";
System.IO.Directory.CreateDirectory(root);
System.IO.File.WriteAllText(root + "/result.json",
    Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
return report;
