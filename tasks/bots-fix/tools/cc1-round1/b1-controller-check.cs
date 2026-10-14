// B1 READ-ONLY проверка ассета контроллера Blaze-прокси после меню генератора
// «Tools/VR Battlegrounds/Bots/Build Blaze Proxy Controller» (BotBlazeProxySetup). Повтор безопасен; пишет controller-<ticks>.json.
// Ожидание: 1 слой без параметров, 6 состояний без motion (Idle по умолчанию, Move, Back, Left, Right, Shoot), .meta папки
// Assets/Resources/Bots и контроллера, Resources.Load по BotCombatDriver.ProxyControllerResource даёт контроллер без клипов.
const string Root = "tasks/bots-fix/reports/cc1-round1";
const string Folder = "Assets/Resources/Bots";
string controllerPath = "Assets/Resources/" + VrBattlegrounds.Bots.BotCombatDriver.ProxyControllerResource + ".controller";
var expected = new[] { "Idle", "Move", "Back", "Left", "Right", "Shoot" };
var blockers = new List<string>();
var states = new List<object>();
string defaultState = null;
int layers = 0, parameters = 0, motions = 0, transitions = 0;
var names = new string[0];
var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath);
if (controller == null) blockers.Add("Нет ассета " + controllerPath);
else {
    layers = controller.layers.Length;
    parameters = controller.parameters.Length;
    var machine = controller.layers[0].stateMachine;
    defaultState = machine.defaultState != null ? machine.defaultState.name : null;
    foreach (var child in machine.states) {
        if (child.state.motion != null) motions++;
        transitions += child.state.transitions.Length;
        states.Add(new { name = child.state.name, motion = child.state.motion != null ? child.state.motion.name : null,
            speed = child.state.speed, writeDefaults = child.state.writeDefaultValues, transitions = child.state.transitions.Length });
    }
    names = machine.states.Select(c => c.state.name).ToArray();
    transitions += machine.anyStateTransitions.Length;
    if (names.Length != expected.Length || expected.Any(n => !names.Contains(n))) blockers.Add("Состояния не те: " + string.Join(",", names));
    if (defaultState != "Idle") blockers.Add("Состояние по умолчанию не Idle: " + defaultState);
    if (layers != 1) blockers.Add("Слоёв " + layers + " (ожидается 1).");
    if (parameters != 0) blockers.Add("Параметров " + parameters + " (ожидается 0).");
    if (motions != 0) blockers.Add("Состояний с motion: " + motions + " (вариант A — без motion).");
}
var runtime = Resources.Load<RuntimeAnimatorController>(VrBattlegrounds.Bots.BotCombatDriver.ProxyControllerResource);
if (runtime == null) blockers.Add("Resources.Load(" + VrBattlegrounds.Bots.BotCombatDriver.ProxyControllerResource + ") = null");
bool folderValid = AssetDatabase.IsValidFolder(Folder);
bool folderMeta = System.IO.File.Exists(Folder + ".meta");
bool controllerMeta = System.IO.File.Exists(controllerPath + ".meta");
if (!folderValid || !folderMeta) blockers.Add("Нет папки/.meta " + Folder);
if (!controllerMeta) blockers.Add("Нет .meta контроллера");
string text = System.IO.File.Exists(controllerPath) ? System.IO.File.ReadAllText(controllerPath) : null;
System.IO.Directory.CreateDirectory(Root);
var reportPath = Root + "/controller-" + DateTime.UtcNow.Ticks + ".json";
var report = new { executionCompleted = true, passed = blockers.Count == 0, blockers, controllerPath, defaultState, layers, parameters, motions,
    transitions, states, runtimeLoaded = runtime != null, runtimeClips = runtime != null ? runtime.animationClips.Length : -1,
    folderValid, folderMeta, controllerMeta,
    folderGuid = AssetDatabase.AssetPathToGUID(Folder), controllerGuid = AssetDatabase.AssetPathToGUID(controllerPath),
    fileBytes = text != null ? text.Length : 0, crlf = text != null && text.Contains("\r\n"),
    scope = "Read-only проверка ассета; ничего не меняет." };
System.IO.File.WriteAllText(reportPath, Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));
return new { executionCompleted = true, passed = blockers.Count == 0, blockers = blockers.ToArray(), defaultState, names, layers, parameters, motions,
    runtimeLoaded = runtime != null, folderMeta, controllerMeta, reportPath };
