// Выполнить как method-body через Unity MCP execute_code (C# 6).
// Снимает только авторский кадр паков. Assets не меняет, SDK pose/retarget не применяет к рукам источника.
// Preview-scaffold даёт владельца изолированной сцены; его геометрия и отчёт полностью заменяются.
if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode||UnityEditor.EditorApplication.isCompiling||UnityEditor.EditorApplication.isUpdating) return "busy";
var cases=Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText("Temp/HandPoseFit/weapon-comparison/pack-catalog.json"));
System.Func<Newtonsoft.Json.Linq.JToken,bool> verified=x=> {string p="Temp/HandPoseFit/weapon-comparison/"+(string)x["Case"]+"/source-render-verified.json";return System.IO.File.Exists(p)&&(int?)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(p))["CaptureVersion"]==2;};
var item=cases.FirstOrDefault(x=>!verified(x));
if(item==null)return "complete";
string id=(string)item["Case"],modelPath=(string)item["ModelPath"],weaponPath=(string)item["WeaponPath"];
bool kinematic=(string)item["Group"]=="kinemation";int side=(int)item["Side"];
var model=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(modelPath);
var clip=UnityEditor.AssetDatabase.LoadAllAssetsAtPath((string)item["ClipPath"]).OfType<UnityEngine.AnimationClip>().Single(c=>c.name==(string)item["ClipName"]);
string clipGuid;long clipId;UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip,out clipGuid,out clipId);
if(clipId!=(long)item["ClipFileId"]) throw new System.Exception("Изменилась идентичность клипа "+id);
var settings=new VrBattlegrounds.Editor.HandPoseReview.HandPoseFitRequest {AvatarPath=modelPath,ObjectPath=weaponPath??modelPath,PoseOverridePath=UnityEditor.AssetDatabase.GetAssetPath(clip),Side=(UltimateXR.Core.UxrHandSide)side,SampleCount=3000,ImageSize=512,CheckSamplingConvergence=true,IncludeOtherHand=false,StateName="authored-animation-frame",SeriesName="original-pack",OutputDirectory="Temp/HandPoseFit/weapon-comparison/"+id};
var scaffold=new VrBattlegrounds.Editor.HandPoseReview.HandPoseFitRequest {AvatarPrefab=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/Avatars/CyborgAvatarExample.prefab").GetComponent<UltimateXR.Avatar.UxrAvatar>(),ObjectPrefab=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/ThirdParty/UltimateXR/Samples/FullScene/Prefabs/ShootingRange/Weapons/Gun.prefab"),Side=UltimateXR.Core.UxrHandSide.Right,IncludeOtherHand=false};
using(var snap=new VrBattlegrounds.Editor.HandPoseReview.HandPoseFitSnapshot(scaffold,new VrBattlegrounds.Editor.HandPoseReview.HandPoseFitReport())) {
snap.Hand.Clear();snap.ContactHand.Clear();snap.ObjectSurface.Clear();snap.ContactObject.Clear();snap.OtherHand.Clear();snap.AnalysisVolume.Clear();
UnityEngine.GameObject character=null,weapon=null;
try {
character=UnityEngine.Object.Instantiate(model);character.hideFlags=UnityEngine.HideFlags.HideAndDontSave;snap.Preview.AddSingleGO(character);
clip.SampleAnimation(character,(float)item["Time"]);
var bones=character.GetComponentsInChildren<UnityEngine.Transform>(true).GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.First());
if(kinematic) {
var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(weaponPath);weapon=UnityEngine.Object.Instantiate(prefab);weapon.hideFlags=UnityEngine.HideFlags.HideAndDontSave;snap.Preview.AddSingleGO(weapon);
weapon.transform.SetParent(bones[VrBattlegrounds.Editor.Avatars.KinemationPoseExtractor.WeaponBone],false);weapon.transform.localPosition=UnityEngine.Vector3.zero;weapon.transform.localRotation=VrBattlegrounds.Editor.Avatars.KinemationPoseExtractor.WeaponRotationOffset(prefab);weapon.transform.localScale=UnityEngine.Vector3.one;
var animator=weapon.GetComponentInChildren<UnityEngine.Animator>(true);string rest=(string)item["RestClipPath"];
if(rest!=null)UnityEditor.AssetDatabase.LoadAllAssetsAtPath(rest).OfType<UnityEngine.AnimationClip>().Single(c=>!c.name.StartsWith("__")).SampleAnimation(animator.gameObject,0);
VrBattlegrounds.Editor.Gameplay.KinemationWeapon.SampleFullMagazines(weapon,animator);
}
string suffix=side==0?"l":"r",catSide=side==0?"L":"R";
string wristName=kinematic?"hand_"+suffix:bones.Keys.Single(n=>n.EndsWith(catSide+"ArmPalm"));
var wrist=bones[wristName];var allowed=new System.Collections.Generic.HashSet<UnityEngine.Transform>(wrist.GetComponentsInChildren<UnityEngine.Transform>(true));
string oppositeWrist=kinematic?"hand_"+(side==0?"r":"l"):bones.Keys.Single(n=>n.EndsWith((side==0?"R":"L")+"ArmPalm"));
var oppositeAllowed=new System.Collections.Generic.HashSet<UnityEngine.Transform>(bones[oppositeWrist].GetComponentsInChildren<UnityEngine.Transform>(true));
var frameRoot=weapon?weapon.transform:character.transform;var frame=UnityEngine.Matrix4x4.TRS(frameRoot.position,frameRoot.rotation,UnityEngine.Vector3.one).inverse;
snap.GripCenter=frame.MultiplyPoint3x4(wrist.position);
System.Func<string,string> zoneOf=n=> {string[] zn={"thumb","index","middle","ring","little"};if(kinematic){for(int f=0;f<5;f++)if(n.StartsWith(f==4?"pinky":zn[f]))return zn[f];}else {var m=System.Text.RegularExpressions.Regex.Match(n,"ArmDigit([1-5])");if(m.Success)return zn[int.Parse(m.Groups[1].Value)-1];}return "palm_wrist";};
System.Func<string,string,string> segmentOf=(n,z)=>{if(z=="palm_wrist")return z;if(n.Contains("metacarpal"))return z+".metacarpal";var m=System.Text.RegularExpressions.Regex.Match(n,kinematic?"_0([1-3])_":"ArmDigit[1-5]([1-3])");return z+"."+(m.Success?new[]{"proximal","intermediate","distal"}[int.Parse(m.Groups[1].Value)-1]:"unknown");};
var rawReader=typeof(VrBattlegrounds.Editor.HandPoseReview.HandPoseFitSnapshot).GetMethod("ReadTriangles",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
var report=new VrBattlegrounds.Editor.HandPoseReview.HandPoseFitReport{CreatedUtc=System.DateTime.UtcNow.ToString("O"),UnityVersion=UnityEngine.Application.unityVersion,Settings=settings,CaptureSource="pack_animation_static",Avatar=VrBattlegrounds.Editor.HandPoseReview.HandPoseFitAnalyzer.Identity(model),Object=VrBattlegrounds.Editor.HandPoseReview.HandPoseFitAnalyzer.Identity(weaponPath!=null?UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(weaponPath):model),Pose=VrBattlegrounds.Editor.HandPoseReview.HandPoseFitAnalyzer.Identity(clip),PoseType="AnimationClip",ObjectScale=frameRoot.lossyScale.x,ObjectScaleAxes=frameRoot.lossyScale,ControllerAlignmentApplied=false,AvatarGuidChain=new string[0]};
foreach(var node in allowed) {string z=zoneOf(node.name);report.Joints.Add(new VrBattlegrounds.Editor.HandPoseReview.FitJointReport{Zone=z,Segment=segmentOf(node.name,z),Path=node.name,PositionMeters=frame.MultiplyPoint3x4(node.position),LocalRotation=node.localRotation});}
foreach(var skin in character.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true)) {
if(!skin.sharedMesh||!skin.enabled||!skin.gameObject.activeInHierarchy)continue;
var mesh=new UnityEngine.Mesh();try {skin.BakeMesh(mesh,false);var v=mesh.vertices;var bs=skin.bones;
var mass=new float[v.Length];var otherMass=new float[v.Length];var dominant=new int[v.Length];
using(var counts=skin.sharedMesh.GetBonesPerVertex())using(var weights=skin.sharedMesh.GetAllBoneWeights()){int offset=0;for(int i=0;i<v.Length;i++){float best=0;dominant[i]=-1;for(int b=0;b<counts[i];b++){var w=weights[offset++];if(w.boneIndex>=bs.Length)continue;if(allowed.Contains(bs[w.boneIndex])){mass[i]+=w.weight;if(w.weight>best){best=w.weight;dominant[i]=w.boneIndex;}}if(oppositeAllowed.Contains(bs[w.boneIndex]))otherMass[i]+=w.weight;}}}
var matrix=frame*skin.localToWorldMatrix;string source=skin.name;var extracted=new System.Collections.Generic.List<VrBattlegrounds.Editor.HandPoseReview.FitTriangle>();
for(int sub=0;sub<mesh.subMeshCount;sub++){var ix=mesh.GetTriangles(sub);for(int j=0;j<ix.Length;j+=3){int a=ix[j],b=ix[j+1],c=ix[j+2];bool hand=(mass[a]+mass[b]+mass[c])/3>=.5f;bool other=(otherMass[a]+otherMass[b]+otherMass[c])/3>=.5f;
if(!hand&&!other)continue;int di=new[]{a,b,c}.Where(x=>dominant[x]>=0).GroupBy(x=>dominant[x]).OrderByDescending(g=>g.Sum(x=>mass[x])).Select(g=>g.Key).DefaultIfEmpty(-1).First();string z=di>=0?zoneOf(bs[di].name):"palm_wrist";
var tri=new VrBattlegrounds.Editor.HandPoseReview.FitTriangle(matrix.MultiplyPoint3x4(v[a]),matrix.MultiplyPoint3x4(v[b]),matrix.MultiplyPoint3x4(v[c]),z){Source=source,SourceTriangle=j/3,SourceSubMesh=sub,FingerSegment=di>=0?segmentOf(bs[di].name,z):z};if(tri.Area<1e-12f)continue;
if(hand){snap.Hand.Add(tri);extracted.Add(tri);}else snap.OtherHand.Add(tri);
}}
if(extracted.Count>0)report.Surfaces.Add(new VrBattlegrounds.Editor.HandPoseReview.FitSurfaceReport{Path=source,Kind="hand",Triangles=extracted.Count,Topology=VrBattlegrounds.Editor.HandPoseReview.HandPoseFitGeometry.Topology(extracted.ToArray())});
}finally{UnityEngine.Object.DestroyImmediate(mesh);}
}
foreach(var skin in (weapon?weapon:character).GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true)) {
if(!skin.sharedMesh||!skin.enabled||!skin.gameObject.activeInHierarchy)continue;
if(!kinematic&&skin.bones.Any(b=>b&&(allowed.Contains(b)||oppositeAllowed.Contains(b))))continue;
// BakeMesh(false) у CAT с масштабом Renderer 6.4459 включил масштаб повторно.
// Мировые вершины GPU: сумма weight * bone.localToWorldMatrix * bindpose * rawVertex.
if(skin.sharedMesh.blendShapeCount>0)throw new System.NotSupportedException("Нужна отдельная обработка blendshape оружия "+skin.name);
var mesh=new UnityEngine.Mesh();try {
UnityEngine.Vector3[] vertices;var submeshes=new System.Collections.Generic.List<int[]>();
using(var data=UnityEditor.MeshUtility.AcquireReadOnlyMeshData(skin.sharedMesh))using(var raw=new Unity.Collections.NativeArray<UnityEngine.Vector3>(skin.sharedMesh.vertexCount,Unity.Collections.Allocator.Temp)){data[0].GetVertices(raw);vertices=raw.ToArray();for(int s=0;s<data[0].subMeshCount;s++){var d=data[0].GetSubMesh(s);if(d.topology!=UnityEngine.MeshTopology.Triangles)throw new System.NotSupportedException("Не треугольный skin "+skin.name);var indices=new int[d.indexCount];if(skin.sharedMesh.indexFormat==UnityEngine.Rendering.IndexFormat.UInt16){var source=data[0].GetIndexData<ushort>();for(int i=0;i<indices.Length;i++)indices[i]=source[d.indexStart+i]+d.baseVertex;}else{var source=data[0].GetIndexData<int>();for(int i=0;i<indices.Length;i++)indices[i]=source[d.indexStart+i]+d.baseVertex;}submeshes.Add(indices);}}
var matrices=skin.bones.Select((bone,i)=>bone.localToWorldMatrix*skin.sharedMesh.bindposes[i]).ToArray();
using(var counts=skin.sharedMesh.GetBonesPerVertex())using(var weights=skin.sharedMesh.GetAllBoneWeights()){int offset=0;for(int i=0;i<vertices.Length;i++){var source=vertices[i];var position=UnityEngine.Vector3.zero;for(int b=0;b<counts[i];b++){var w=weights[offset++];position+=matrices[w.boneIndex].MultiplyPoint3x4(source)*w.weight;}vertices[i]=position;}}
mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.vertices=vertices;mesh.subMeshCount=submeshes.Count;for(int s=0;s<submeshes.Count;s++)mesh.SetTriangles(submeshes[s],s);
snap.ObjectSurface.AddRange((System.Collections.Generic.List<VrBattlegrounds.Editor.HandPoseReview.FitTriangle>)rawReader.Invoke(null,new object[]{mesh,frame,skin.name,"surface"}));
}finally{UnityEngine.Object.DestroyImmediate(mesh);}
}
foreach(var filter in (weapon?weapon:character).GetComponentsInChildren<UnityEngine.MeshFilter>(true)){var renderer=filter.GetComponent<UnityEngine.MeshRenderer>();if(filter.sharedMesh&&renderer&&renderer.enabled&&filter.gameObject.activeInHierarchy)snap.ObjectSurface.AddRange((System.Collections.Generic.List<VrBattlegrounds.Editor.HandPoseReview.FitTriangle>)rawReader.Invoke(null,new object[]{filter.sharedMesh,frame*filter.transform.localToWorldMatrix,filter.name,"surface"}));}
snap.ContactHand.AddRange(snap.Hand);snap.ContactObject.AddRange(snap.ObjectSurface);
if(snap.Hand.Count==0||snap.ObjectSurface.Count==0)throw new System.Exception("Нет исходной геометрии "+id);
var bound=new UnityEngine.Bounds(snap.Hand[0].A,UnityEngine.Vector3.zero);foreach(var t in snap.Hand){bound.Encapsulate(t.A);bound.Encapsulate(t.B);bound.Encapsulate(t.C);}snap.HandBounds=bound;
var samples=VrBattlegrounds.Editor.HandPoseReview.HandPoseFitGeometry.Sample(snap.Hand.ToArray(),3000,42);var otherSurface=new VrBattlegrounds.Editor.HandPoseReview.FitSurface(snap.OtherHand.ToArray());
var pairMetrics=samples.GroupBy(s=>snap.Hand[s.Triangle].Zone).Select(g=>new{Zone=g.Key,AreaMm2=g.Sum(s=>(double)s.Area*1e6),NearOtherHandFraction=g.Count(s=>otherSurface.Distance(s.Position)*1000<=2.0001)/(double)g.Count()}).ToArray();
System.IO.Directory.CreateDirectory(settings.OutputDirectory);System.IO.File.WriteAllText(settings.OutputDirectory+"/other-hand-contact.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{Source="both_hands_in_original_animation_frame",Zones=pairMetrics}));
// Другая кисть не входит в сравнение с объектом и изображения одиночного хвата.
snap.OtherHand.Clear();
foreach(var renderer in character.GetComponentsInChildren<UnityEngine.Renderer>(true))renderer.enabled=false;
if(weapon)foreach(var renderer in weapon.GetComponentsInChildren<UnityEngine.Renderer>(true))renderer.enabled=false;
report.Limitations.Add("Исходный кадр анимационного пака; без ретаргета и SDK выравнивания. Полный исходный оружейный меш может отличаться составом и размером от игрового.");
report.Limitations.Add("Паковый классификатор кисти: >=50% веса треугольника на запястье/пальцах; учитываются все исходные веса костей.");
report.Limitations.Add("Скины оружия сняты по исходным вершинам, bindpose и всем весам костей в мировых координатах; масштаб Renderer не применяется второй раз.");
VrBattlegrounds.Editor.HandPoseReview.HandPoseFitAnalyzer.AnalyzeCaptured(snap,report);
if(report.Error==null)System.IO.File.WriteAllText(settings.OutputDirectory+"/source-render-verified.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{CaptureVersion=2,SourceRenderersDisabled=true,HandTriangles=snap.Hand.Count,ObjectTriangles=snap.ObjectSurface.Count,ClipFileId=clipId}));
return new{caseId=id,error=report.Error,hand=report.HandTriangles,weapon=report.ObjectTriangles,remaining=cases.Count(x=>!verified(x))};
}finally{if(weapon)UnityEngine.Object.DestroyImmediate(weapon);if(character)UnityEngine.Object.DestroyImmediate(character);}
}
