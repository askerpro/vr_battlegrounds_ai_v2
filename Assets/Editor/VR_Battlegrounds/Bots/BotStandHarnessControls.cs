using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using VrBattlegrounds.DevTools.BotCombatStand;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Отрицательные контроли проверяют отчётчик; принятую игровую механику не закрепляют.</summary>
    public static class BotStandHarnessControls
    {
        public static Dictionary<string,object> Run()
        {
            var good=Case(false,false); BotStandMeasurements.Evaluate(good);
            var wall=Case(true,false); BotStandMeasurements.Evaluate(wall);
            var illegal=Case(false,true); BotStandMeasurements.Evaluate(illegal);
            var missing=Case(false,false); missing.Capture=true; BotStandRecorder.ValidateImages(missing);
            var noCapture=Case(false,false); BotStandRecorder.ValidateImages(noCapture);
            bool passed=good.Status=="NeedsReview" && wall.Status=="Failed" && illegal.Status=="Failed" && missing.Status=="InvalidFixture" && noCapture.Status=="NeedsReview";
            var report=new Dictionary<string,object> { {"passed",passed}, {"validObservation",good.Status}, {"wallPenetration",wall.Status}, {"illegalTarget",illegal.Status}, {"missingImages",missing.Status}, {"captureDisabled",noCapture.Status}, {"limit","Synthetic telemetry controls; do not prove bot gameplay"} };
            Directory.CreateDirectory("Docs/tasks/report/bot-combat-stand");
            File.WriteAllText("Docs/tasks/report/bot-combat-stand/harness-controls.json",JsonConvert.SerializeObject(report,Formatting.Indented));
            return report;
        }
        private static BotStandCaseResult Case(bool wall,bool illegal) => new BotStandCaseResult { Id="T03",Status="NeedsReview", Frames=new List<BotStandFrame> {
            new BotStandFrame { BodyId=1, Feet=new[]{0f,0f,0f},LegalTarget=!illegal,InsideSolid=wall,Author=true,Alive=true },
            new BotStandFrame { BodyId=1, Feet=new[]{1f,0f,0f},LegalTarget=!illegal,InsideSolid=wall,Author=true,Alive=true } } };
    }
}
