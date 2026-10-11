// Один пакет authoring: calibration не запускается на неуспешной composition.
var folder = "tasks/bots-fix/reports/calibration-rebuild";
VrBattlegrounds.EditorTools.BotCombatSetup.Build();
var composition = VrBattlegrounds.EditorTools.BotClipComposition.Build(folder);
if (!(bool)composition["passed"])
    return new { executionCompleted = true, composition, calibrationExecuted = false };
var calibration = VrBattlegrounds.EditorTools.BotClipComposition.BuildCalibration(folder);
return new { executionCompleted = true, composition, calibration, calibrationExecuted = true };
