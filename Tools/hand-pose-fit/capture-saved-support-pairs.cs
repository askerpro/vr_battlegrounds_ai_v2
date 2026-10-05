// Анализ двух независимо сохранённых трансформов хвата. Не симулирует IK/SDK двухручного удержания.
if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode||UnityEditor.EditorApplication.isCompiling||UnityEditor.EditorApplication.isUpdating) return "busy";
var requests=Newtonsoft.Json.JsonConvert.DeserializeObject<VrBattlegrounds.Editor.HandPoseReview.HandPoseFitRequest[]>(System.IO.File.ReadAllText("Temp/HandPoseFit/weapon-comparison/requests.json"),VrBattlegrounds.Editor.HandPoseReview.FitUnityJsonConverter.Settings);
var item=requests.FirstOrDefault(r=>r.AvatarPath.Contains("MEF")&&r.GrabPoint==1&&r.GrabbableIndexPath==""&&!System.IO.File.Exists(r.OutputDirectory+"/other-hand-contact.json"));
if(item==null)return "complete";
var main=item.Copy();main.GrabPoint=0;main.Side=item.Side==UltimateXR.Core.UxrHandSide.Left?UltimateXR.Core.UxrHandSide.Right:UltimateXR.Core.UxrHandSide.Left;
System.Func<VrBattlegrounds.Editor.HandPoseReview.HandPoseFitRequest,VrBattlegrounds.Editor.HandPoseReview.HandPoseFitSnapshot> build=r=> {r.AvatarPrefab=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(r.AvatarPath).GetComponent<UltimateXR.Avatar.UxrAvatar>();r.ObjectPrefab=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(r.ObjectPath);return new VrBattlegrounds.Editor.HandPoseReview.HandPoseFitSnapshot(r,new VrBattlegrounds.Editor.HandPoseReview.HandPoseFitReport());};
using(var primary=build(main))using(var support=build(item)) {
var surface=new VrBattlegrounds.Editor.HandPoseReview.FitSurface(primary.Hand.ToArray());
var samples=VrBattlegrounds.Editor.HandPoseReview.HandPoseFitGeometry.Sample(support.Hand.ToArray(),3000,42);
var zones=samples.GroupBy(s=>support.Hand[s.Triangle].Zone).Select(g=>new{Zone=g.Key,AreaMm2=g.Sum(s=>(double)s.Area*1e6),NearOtherHandFraction=g.Count(s=>surface.Distance(s.Position)*1000<=2.0001)/(double)g.Count()}).ToArray();
System.IO.File.WriteAllText(item.OutputDirectory+"/other-hand-contact.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{Source="two_independent_saved_grip_transforms_without_two_hand_ik",Zones=zones}));
return new{folder=item.OutputDirectory,measured=true};
}
