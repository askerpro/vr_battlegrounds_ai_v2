using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Все изображения получены из тех же треугольников, что использованы для измерений.</summary>
    public static class HandPoseFitRenderer
    {
        static readonly Color HandColor=new Color(.15f,.72f,.95f),ObjectColor=new Color(.72f,.73f,.76f);
        public static void Render(HandPoseFitSnapshot snapshot,HandPoseFitReport report,FitSurface contact,FitSurface full,FitTopology topology)
        {
            var r=report.Settings;Shader shader=Shader.Find("Hidden/VRBattlegrounds/HandPoseFit");
            if(!shader) throw new InvalidOperationException("Не импортирован shader анализатора.");
            // Кадр по bounds кисти: наибольший габарит + 30 мм с каждой стороны, поправка на полосы подписей.
            float frame=r.FrameSizeMeters,labelShift=0;
            if(r.FrameToHandBounds) {Vector3 size=snapshot.HandBounds.size;frame=(Mathf.Max(size.x,Mathf.Max(size.y,size.z))+.06f)*r.ImageSize/(r.ImageSize-90f);labelShift=frame*15f/r.ImageSize;}
            Camera camera=snapshot.Preview.camera;camera.orthographic=true;camera.orthographicSize=frame*.5f;camera.nearClipPlane=.001f;camera.farClipPlane=20;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.10f,.13f);camera.cullingMask=-1;
            Vector3 center=r.OverrideFrameCenter?r.FrameCenterMeters:r.FrameToHandBounds?snapshot.HandBounds.center:snapshot.GripCenter;
            string[] names={"front","back","left","right","top","bottom"};
            Vector3[] dirs={Vector3.forward,Vector3.back,Vector3.left,Vector3.right,Vector3.up,Vector3.down};
            var resources=new List<Object>();var objects=new List<GameObject>();
            using var xray=HandPoseXrayRenderer.CreateMaterials();
            try {
                Material solid=Material(shader,false,false),ghost=Material(shader,true,false),line=Material(shader,true,true);resources.Add(solid);resources.Add(ghost);resources.Add(line);
                Material markers=Material(shader,true,true);markers.renderQueue=3002;resources.Add(markers);
                Mesh hand=Mesh(snapshot.Hand,_=>HandColor),weapon=Mesh(snapshot.ObjectSurface,_=>ObjectColor),wire=Wire(snapshot.ObjectSurface,new Color(.85f,.86f,.89f,.32f));resources.Add(hand);resources.Add(weapon);resources.Add(wire);
                Mesh intersection=BuildIntersectionMesh(snapshot.Hand,full);if(intersection) resources.Add(intersection);
                var selected=new HashSet<(string,int,int)>(snapshot.ContactHand.Select(t=>(t.Source,t.SourceSubMesh,t.SourceTriangle)));
                Mesh heat=Mesh(snapshot.Hand,t=>{
                    if(full.IntersectionCount(t)>0) return new Color(1,.2f,.8f);
                    if(!selected.Contains((t.Source,t.SourceSubMesh,t.SourceTriangle))) return new Color(.35f,.35f,.4f);
                    Vector3 p=(t.A+t.B+t.C)/3;
                    bool? inside=HandPoseFitGeometry.IsInside(p,full,topology);
                    if(inside==true) return new Color(1,.1f,.1f);
                    float d=contact.Distance(p)*1000;Color tint=Color.Lerp(new Color(.1f,.8f,.95f),new Color(1,.55f,.05f),Mathf.Clamp01(d/(r.ProximityMm*5)));
                    return inside.HasValue?tint:Color.Lerp(tint,new Color(.5f,.2f,.9f),.5f);
                });resources.Add(heat);
                GameObject hg=Add(snapshot,hand,solid),wg=Add(snapshot,weapon,solid),wireg=Add(snapshot,wire,line),heatg=Add(snapshot,heat,solid);objects.AddRange(new[]{hg,wg,wireg,heatg});
                // Режим pads: маска подушечек и знак расстояния в центре грани (визуальная проверка метода).
                GameObject padsg=null;var metrics=report.ContactMetrics;
                if(metrics!=null&&metrics.PadTriangleIndices.Count>0) {
                    var colors=new Dictionary<(string,int,int),Color>();
                    for(int i=0;i<snapshot.Hand.Count;i++) {
                        var t=snapshot.Hand[i];var key=(t.Source,t.SourceSubMesh,t.SourceTriangle);
                        if(metrics.PadTriangleIndices.Contains(i)) {
                            Vector3 p=(t.A+t.B+t.C)/3;double mm=full.Distance(p)*1000;
                            colors[key]=full.WindingNumber(p)>=.5?new Color(1,.12f,.1f):mm<=metrics.WindowMm?new Color(.15f,.95f,.3f):Color.Lerp(new Color(.45f,.75f,1f),new Color(.1f,.15f,.75f),Mathf.Clamp01((float)mm/20f));
                        } else if(metrics.DistalTriangleIndices.Contains(i)) colors[key]=new Color(.95f,.85f,.2f);
                    }
                    Mesh pads=Mesh(snapshot.Hand,t=>colors.TryGetValue((t.Source,t.SourceSubMesh,t.SourceTriangle),out var c)?c:new Color(.4f,.4f,.45f));resources.Add(pads);padsg=Add(snapshot,pads,solid);objects.Add(padsg);
                }
                string[] modeNames=padsg?new[]{"normal","xray","distance","section","pads"}:new[]{"normal","xray","distance","section"};
                GameObject otherg=null,volumeg=null,regionsg=null;Mesh other=null;
                if(snapshot.OtherHand.Count>0) {other=Mesh(snapshot.OtherHand,_=>new Color(.65f,.35f,.9f));resources.Add(other);otherg=Add(snapshot,other,solid);objects.Add(otherg);}
                if(snapshot.AnalysisVolume.Count>0) {Mesh volume=Wire(snapshot.AnalysisVolume,new Color(1,.65f,.15f,.8f));resources.Add(volume);volumeg=Add(snapshot,volume,line);objects.Add(volumeg);}
                if(r.ContactRegions.Length>0) {Mesh bounds=RegionBounds(r.ContactRegions);resources.Add(bounds);regionsg=Add(snapshot,bounds,line);objects.Add(regionsg);}
                for(int view=0;view<6;view++) {
                    Vector3 dir=dirs[view],up=Mathf.Abs(Vector3.Dot(dir,Vector3.up))>.9f?Vector3.forward:Vector3.up;
                    camera.transform.position=center+dir*2+up*labelShift;camera.transform.rotation=Quaternion.LookRotation(-dir,up);
                    for(int mode=0;mode<modeNames.Length;mode++) {
                        string modeName=modeNames[mode];
                        hg.SetActive(mode==0);wg.SetActive(mode!=3&&mode!=1);wireg.SetActive(mode==2||mode==4);heatg.SetActive(mode==2);
                        if(padsg) padsg.SetActive(mode==4);
                        if(otherg) otherg.SetActive(mode==0);
                        if(volumeg) volumeg.SetActive(mode==1||mode==2);
                        if(regionsg) regionsg.SetActive(mode==1||mode==2);
                        wg.GetComponent<MeshRenderer>().sharedMaterial=mode==1||mode==2||mode==4?ghost:solid;
                        var sectionObjects=new List<GameObject>();var sectionMeshes=new List<Mesh>();
                        try {
                            if(mode==3) {
                                Mesh hm=Mesh(Clip(snapshot.Hand,center,dir),_=>HandColor),wm=Mesh(Clip(snapshot.ObjectSurface,center,dir),_=>ObjectColor);sectionMeshes.Add(hm);sectionMeshes.Add(wm);
                                sectionObjects.Add(Add(snapshot,hm,solid));sectionObjects.Add(Add(snapshot,wm,solid));
                            }
                            if(r.ShowContactMarkers && report.ContactMarkers.Count>0 && mode!=4) {
                                // В рентгене показываем скрытые контакты; в обычном виде учитываем глубину.
                                markers.SetFloat("_ZTest",mode==1||mode==2?(int)CompareFunction.Always:(int)CompareFunction.LessEqual);
                                IEnumerable<FitContactCandidate> pairs=report.ContactMarkers;
                                if(mode==3) pairs=pairs.Where(p=>Vector3.Dot(p.Position-center,dir)<=0 && Vector3.Dot(p.TargetPosition-center,dir)<=0);
                                Mesh points=ContactMarkers(pairs,camera,r.ImageSize,r.ContactMarkerRadiusPixels);
                                sectionMeshes.Add(points);sectionObjects.Add(Add(snapshot,points,markers));
                            }
                            snapshot.Preview.BeginStaticPreview(new Rect(0,0,r.ImageSize,r.ImageSize));
                            if(mode==1) HandPoseXrayRenderer.DrawPreview(snapshot.Preview,xray,hand,weapon,Matrix4x4.identity,otherHand:other,intersection:intersection);
                            snapshot.Preview.Render(true);
                            Texture2D texture=snapshot.Preview.EndStaticPreview();
                            try {
                                FitRasterLabels.Label(texture,report,modeName,names[view],frame);
                                string name=names[view]+"-"+modeName+".png";File.WriteAllBytes(Path.Combine(report.OutputDirectory,name),texture.EncodeToPNG());
                                report.Cameras.Add(new FitCameraReport{View=names[view],Mode=modeName,Image=name,Position=camera.transform.position,Forward=camera.transform.forward,Up=camera.transform.up,OrthographicSizeMeters=camera.orthographicSize,SectionNormal=mode==3?dir:Vector3.zero,SectionOffsetMeters=mode==3?Vector3.Dot(center,dir):0});
                            } finally {Object.DestroyImmediate(texture);}
                        } finally {foreach(var o in sectionObjects) Object.DestroyImmediate(o);foreach(var m in sectionMeshes) Object.DestroyImmediate(m);}
                    }
                }
                var html=new StringBuilder("<!doctype html><meta charset='utf-8'><title>Посадка кисти</title><style>body{background:#151a21;color:#ddd;font:16px sans-serif}section{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}img{width:100%}figure{margin:0}h2{grid-column:1/-1}</style><h1>Посадка кисти</h1><p>Обычный вид · рентген · расстояние · сечение. Голубой → оранжевый: unsigned дистанция. Пурпурный: касание/пересечение. Красный: подтверждённое вложение. Серый: вне контактной маски. Допуск близости не является нормой качества.</p>");
                html.Append("<p>Точки в диапазоне "+r.ContactDistanceMinMm.ToString("0.##",CultureInfo.InvariantCulture)+"–"+r.ProximityMm.ToString("0.##",CultureInfo.InvariantCulture)+" мм включительно: "+report.ContactMarkers.Count+" показано из "+report.ContactCandidateCount+". В рентгене внешняя оболочка оружия скрывает его внутренние детали, кисть просвечивает, пересечение с мешом выделено красным, а точки контакта — жёлтым. "+(r.ShowContactMarkers?"":"Маркеры выключены.")+"</p>");
                string Escape(string value) => System.Security.SecurityElement.Escape(value??"");
                html.Append("<p>Источник: "+Escape(report.CaptureSource)+"; кадр "+Escape(report.CaptureFrame.ToString(CultureInfo.InvariantCulture))+"; состояние "+Escape(report.StateName)+". Фиолетовая поверхность — другая кисть; оранжевые рёбра — аналитический объём; зелёные рамки — контактные регионы.</p><h2>Достоверность</h2><ul>");
                foreach(var item in report.Reliability) html.Append("<li>"+Escape(item.Metric)+": "+Escape(item.Status)+" — "+Escape(item.Reason)+"</li>");html.Append("</ul><h2>Контакт по фалангам</h2><table><tr><th>Зона</th><th>P50, мм</th><th>Доля близости</th><th>Неизвестный знак</th></tr>");
                foreach(var item in report.Segments) html.Append("<tr><td>"+Escape(item.Zone)+"</td><td>"+item.DistanceP50Mm.ToString("F2",CultureInfo.InvariantCulture)+"</td><td>"+item.NearSurfaceFraction.ToString("P1",CultureInfo.InvariantCulture)+"</td><td>"+item.UnknownSignSamples+"</td></tr>");html.Append("</table>");
                if(metrics!=null) {
                    string F(double v) => v.ToString("F1",CultureInfo.InvariantCulture);string N(double? v) => v.HasValue?v.Value.ToString("F2",CultureInfo.InvariantCulture):"—";
                    html.Append("<h2>Подушечки со знаком</h2><p>Расстояние со знаком, мм: плюс — зазор снаружи, минус — точка кисти внутри предмета (число оборотов ≥0,5). Окно ±"+F(metrics.WindowMm)+" мм, полуугол подушечки "+F(metrics.PadHalfAngleDeg)+"°. Режим pads: красный — внутри, зелёный — в окне, голубой→синий — зазор до 20 мм, жёлтый — distal вне подушечки. Пороги качества не заданы.</p><table><tr><th>Палец</th><th>Подушечка, мм</th><th>Мин. по подушечке</th><th>Глубина проникновения</th><th>Площадь в окне, мм²</th><th>Opposition</th><th>Доля подушечки в distal</th><th>Мин. по пальцу</th></tr>");
                    foreach(var f in metrics.Fingers) html.Append("<tr><td>"+Escape(f.Finger)+"</td><td>"+F(f.PadSignedDistanceMm)+"</td><td>"+F(f.PadMinSignedMm)+"</td><td>"+F(f.PadPenetrationDepthMm)+"</td><td>"+F(f.PadWindowAreaMm2)+"</td><td>"+N(f.PadPointOpposition)+"</td><td>"+N(f.PadAreaFraction)+"</td><td>"+F(f.FingerMinSignedMm)+"</td></tr>");
                    html.Append("</table>");
                }
                var rules=report.GripRules;
                if(rules!=null) {
                    string F(double v) => (v>=0?"+":"")+v.ToString("F1",CultureInfo.InvariantCulture);
                    html.Append("<h2>Грубые нарушения по полосам SDK</h2><p>Тип хвата "+Escape(rules.GripType)+" ("+Escape(rules.GripTypeSource)+"), полосы "+Escape(rules.BandsVersion)+", эталонов "+rules.BandCases+(rules.LowConfidence?", <b>low_confidence</b>":"")+". Итог: <b>"+Escape(rules.Overall)+"</b>"+(rules.HandFlags.Count>0?" · кисть: "+Escape(string.Join(", ",rules.HandFlags)):"")+". notable — выход ≥"+rules.NotableExcessMm+" мм, gross — ≥"+rules.GrossExcessMm+" мм. Палец вне поверхности допустим: правило ищет только грубые выходы, финальная оценка — по листу ревью.</p>");
                    html.Append("<table><tr><th>Палец</th><th>Подушечка, мм</th><th>Полоса SDK</th><th>P50 SDK</th><th>Severity</th><th>Направление</th><th>Выход, мм</th><th>Подсказка curl, °</th><th>Ожидается, мм</th></tr>");
                    foreach(var f in rules.Fingers.Concat(rules.Palm!=null?new[]{rules.Palm}:new FitFingerVerdict[0])) {
                        var h=report.CorrectionHints?.Fingers.FirstOrDefault(x=>x.Finger==f.Finger&&x.Severity!=null);
                        html.Append("<tr><td>"+Escape(f.Finger)+"</td><td>"+(f.PadMm.HasValue?F(f.PadMm.Value):"—")+"</td><td>"+(f.PadBandLo.HasValue?"["+F(f.PadBandLo.Value)+"; "+F(f.PadBandHi.Value)+"]":"—")+"</td><td>"+(f.PadBandMedian.HasValue?F(f.PadBandMedian.Value):"—")+"</td><td>"+Escape(f.Severity)+"</td><td>"+Escape(f.Direction)+"</td><td>"+f.MaxExcessMm.ToString("F1",CultureInfo.InvariantCulture)+"</td><td>"+(h!=null?F(h.CurlDeg)+(h.Recommended?"":" (не нужна)")+(h.DepthLimited?" *глубина":""):"—")+"</td><td>"+(h!=null?F(h.ExpectedMm):"—")+"</td></tr>");
                    }
                    html.Append("</table>");
                    if(!string.IsNullOrEmpty(report.ReviewSheet)) html.Append("<h2>Лист ревью</h2><img src='"+Escape(Path.GetFileName(report.ReviewSheet))+"' style='max-width:100%'>");
                }
                html.Append("<ul>");foreach(string item in report.Findings) html.Append("<li>"+Escape(item)+"</li>");html.Append("</ul>");
                foreach(string name in names) {html.Append("<section><h2>"+name+"</h2>");foreach(var image in report.Cameras.Where(c=>c.View==name)) html.Append("<figure><figcaption>"+image.Mode+"</figcaption><img src='"+image.Image+"'></figure>");html.Append("</section>");}
                File.WriteAllText(Path.Combine(report.OutputDirectory,"index.html"),html.ToString(),new UTF8Encoding(false));
            } finally {foreach(var o in objects) Object.DestroyImmediate(o);foreach(var resource in resources) Object.DestroyImmediate(resource);}
        }

        /// <summary>Тот же frozen mesh с направлением текущей SceneView; original scene не читается.</summary>
        public static void RenderEditorView(HandPoseFitSnapshot snapshot,HandPoseFitReport report,Vector3 position,Quaternion rotation,bool orthographic,float size,float fov)
        {
            RenderEditorViewCore(snapshot,report,camera=>{
                camera.transform.SetPositionAndRotation(position,rotation);
                camera.orthographic=orthographic;camera.orthographicSize=size;camera.fieldOfView=fov;camera.aspect=1;
                camera.nearClipPlane=.0001f;camera.farClipPlane=1000;camera.ResetProjectionMatrix();
            },1);
        }

        /// <summary>Экспорт исходной проекции без превращения прямоугольного SceneView в квадратный кадр.</summary>
        public static void RenderEditorView(HandPoseFitSnapshot snapshot,HandPoseFitReport report,HandPoseXrayRenderer.CameraState camera)
        {
            if(camera==null)throw new ArgumentNullException(nameof(camera));
            RenderEditorViewCore(snapshot,report,camera.Apply,camera.Aspect);
        }

        static void RenderEditorViewCore(HandPoseFitSnapshot snapshot,HandPoseFitReport report,Action<Camera> configure,float aspect)
        {
            var resources=new List<Object>();
            using var xray=HandPoseXrayRenderer.CreateMaterials();
            try {
                var hand=Mesh(snapshot.Hand,_=>HandColor);var weapon=Mesh(snapshot.ObjectSurface,_=>xray.VisualStyle.ObjectColor);
                resources.Add(hand);resources.Add(weapon);Mesh other=null;
                if(snapshot.OtherHand.Count>0) {other=Mesh(snapshot.OtherHand,_=>Color.white);resources.Add(other);}
                Mesh intersection= null;
                if(snapshot.ObjectSurface.Count>0 && snapshot.Hand.Count>0) {
                    var surface=new FitSurface(snapshot.ObjectSurface.ToArray());
                    intersection=BuildIntersectionMesh(snapshot.Hand,surface);
                    if(intersection) resources.Add(intersection);
                }
                var camera=snapshot.Preview.camera;
                configure(camera);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=xray.VisualStyle.Background;camera.cullingMask=-1;
                int width=Mathf.Max(1,Mathf.RoundToInt(report.Settings.ImageSize*Mathf.Min(1,aspect)));
                int height=Mathf.Max(1,Mathf.RoundToInt(report.Settings.ImageSize*Mathf.Min(1,1/aspect)));
                Mesh points=null;
                if(report.Settings.ShowContactMarkers && report.ContactMarkers.Count>0) {
                    points=ContactMarkers(report.ContactMarkers,camera,height,report.Settings.ContactMarkerRadiusPixels);
                    resources.Add(points);
                }
                snapshot.Preview.BeginStaticPreview(new Rect(0,0,width,height));
                // BeginStaticPreview может подстроить camera aspect: возвращаем точную исходную проекцию.
                configure(camera);
                HandPoseXrayRenderer.DrawPreview(snapshot.Preview,xray,hand,weapon,Matrix4x4.identity,otherHand:other,intersection:intersection);
                if(points)snapshot.Preview.DrawMesh(points,Matrix4x4.identity,xray.Overlay,0);
                snapshot.Preview.Render(true);var texture=snapshot.Preview.EndStaticPreview();
                try {
                    FitRasterLabels.Label(texture,report,"xray","editor-view",report.Settings.FrameSizeMeters);
                    File.WriteAllBytes(Path.Combine(report.OutputDirectory,"editor-view-xray.png"),texture.EncodeToPNG());
                } finally {Object.DestroyImmediate(texture);}
                report.Cameras.Add(new FitCameraReport {View="editor-view",Mode="xray_shared_frozen",Image="editor-view-xray.png",
                    Position=camera.transform.position,Forward=camera.transform.forward,Up=camera.transform.up,OrthographicSizeMeters=camera.orthographic?camera.orthographicSize:0});
                File.WriteAllText(Path.Combine(report.OutputDirectory,"editor-view-camera.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    policy="handpose-xray-v4",width,height,camera.aspect,camera.orthographic,camera.orthographicSize,camera.fieldOfView,
                    camera.nearClipPlane,camera.farClipPlane,
                    projection=Enumerable.Range(0,16).Select(i=>camera.projectionMatrix[i]).ToArray(),
                    background=new[]{camera.backgroundColor.r,camera.backgroundColor.g,camera.backgroundColor.b,camera.backgroundColor.a},
                    position=camera.transform.position,rotation=camera.transform.rotation,
                    diagnosticLayer=report.Settings.ShowContactMarkers?"yellow_contact_markers_and_red_mesh_intersection":"red_mesh_intersection"
                },FitUnityJsonConverter.Settings),new UTF8Encoding(false));
                File.AppendAllText(Path.Combine(report.OutputDirectory,"index.html"),
                    "<section><h2>Текущий ракурс · frozen editor geometry</h2><img src='editor-view-xray.png'></section>",new UTF8Encoding(false));
            } finally {foreach(var resource in resources)Object.DestroyImmediate(resource);snapshot.Preview.camera.ResetProjectionMatrix();}
        }

        /// <summary>Выделяет общую / пересекающуюся часть меша кисти с поверхностью предмета для красной подсветки.</summary>
        public static Mesh BuildIntersectionMesh(IEnumerable<FitTriangle> hand,FitSurface weaponSurface)
        {
            if(hand==null||weaponSurface==null) return null;
            var hit=hand.Where(t=>weaponSurface.IntersectionCount(t)>0||weaponSurface.WindingNumber((t.A+t.B+t.C)/3f)>=.5).ToList();
            return hit.Count>0?Mesh(hit,_=>HandPoseXrayRenderer.Style.Default.IntersectionColor):null;
        }

        internal static Material Material(Shader shader,bool transparent,bool unlit)
        {var m=new Material(shader){hideFlags=HideFlags.HideAndDontSave};m.SetFloat("_Src",transparent?(int)BlendMode.SrcAlpha:(int)BlendMode.One);m.SetFloat("_Dst",transparent?(int)BlendMode.OneMinusSrcAlpha:(int)BlendMode.Zero);m.SetFloat("_ZWrite",transparent?0:1);m.SetFloat("_ZTest",transparent?(int)CompareFunction.Always:(int)CompareFunction.LessEqual);m.SetFloat("_Shade",unlit?0:1);m.SetFloat("_Alpha",transparent&&!unlit?.15f:1);m.renderQueue=transparent?3000:2000;return m;}
        internal static GameObject Add(HandPoseFitSnapshot snapshot,Mesh mesh,Material material)
        {var go=new GameObject("HandPoseFit diagnostic mesh"){hideFlags=HideFlags.HideAndDontSave};snapshot.Preview.AddSingleGO(go);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;return go;}
        internal static Mesh Mesh(IEnumerable<FitTriangle> triangles,Func<FitTriangle,Color> color)
        {
            var v=new List<Vector3>();var n=new List<Vector3>();var c=new List<Color>();
            foreach(var t in triangles) {v.Add(t.A);v.Add(t.B);v.Add(t.C);Color tint=color(t);for(int i=0;i<3;i++){n.Add(t.Normal);c.Add(tint);}}
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave};mesh.SetVertices(v);mesh.SetNormals(n);mesh.SetColors(c);mesh.SetTriangles(Enumerable.Range(0,v.Count).ToArray(),0);mesh.RecalculateBounds();return mesh;
        }
        internal static Mesh Wire(IEnumerable<FitTriangle> triangles,Color color)
        {
            var v=new List<Vector3>();foreach(var t in triangles) v.AddRange(new[]{t.A,t.B,t.B,t.C,t.C,t.A});
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave};mesh.SetVertices(v);mesh.SetColors(Enumerable.Repeat(color,v.Count).ToList());mesh.SetIndices(Enumerable.Range(0,v.Count).ToArray(),MeshTopology.Lines,0);mesh.RecalculateBounds();return mesh;
        }
        static Mesh ContactMarkers(IEnumerable<FitContactCandidate> pairs,Camera camera,int imageSize,float radiusPixels)
        {
            var v=new List<Vector3>();var c=new List<Color>();
            Vector3 right=camera.transform.right,up=camera.transform.up;
            Color contactYellow=HandPoseXrayRenderer.Style.Default.ContactColor,dark=new Color(.02f,.03f,.04f);
            void Triangle(Vector3 a,Vector3 b,Vector3 d,Color color) {v.Add(a);v.Add(b);v.Add(d);c.Add(color);c.Add(color);c.Add(color);}
            void Disk(Vector3 p,float size,Color color) {
                for(int i=0;i<12;i++) {float a=i*Mathf.PI/6,b=(i+1)*Mathf.PI/6;Triangle(p,p+(right*Mathf.Cos(a)+up*Mathf.Sin(a))*size,p+(right*Mathf.Cos(b)+up*Mathf.Sin(b))*size,color);}
            }
            foreach(var pair in pairs) {
                float depth=camera.orthographic?1:Mathf.Max(.001f,Vector3.Dot(pair.Position-camera.transform.position,camera.transform.forward));
                float pixel=2*depth/(Mathf.Max(1e-6f,Mathf.Abs(camera.projectionMatrix[1,1]))*imageSize),radius=radiusPixels*pixel;
                Vector3 towardCamera=-camera.transform.forward*pixel*.15f;
                Vector3 hand=pair.Position+towardCamera,target=pair.TargetPosition+towardCamera;
                Vector3 projected=target-hand;projected-=camera.transform.forward*Vector3.Dot(projected,camera.transform.forward);
                if(projected.sqrMagnitude>pixel*pixel*.01f) {
                    Vector3 width=Vector3.Cross(camera.transform.forward,projected).normalized*pixel*.5f;
                    Triangle(hand-width,hand+width,target+width,contactYellow);Triangle(hand-width,target+width,target-width,contactYellow);
                }
                // Кольцо на поверхности предмета и диск точки контакта окрашены в жёлтый с тёмным контуром.
                float outer=radius*1.55f,inner=radius*1.18f;
                for(int i=0;i<12;i++) {
                    float a=i*Mathf.PI/6,b=(i+1)*Mathf.PI/6;
                    Vector3 ra=right*Mathf.Cos(a)+up*Mathf.Sin(a),rb=right*Mathf.Cos(b)+up*Mathf.Sin(b);
                    Triangle(target+ra*outer,target+rb*outer,target+rb*inner,contactYellow);Triangle(target+ra*outer,target+rb*inner,target+ra*inner,contactYellow);
                }
                Disk(hand,radius*1.25f,dark);
                Disk(hand+towardCamera*.1f,radius,pair.Inside==true?HandPoseXrayRenderer.Style.Default.IntersectionColor:contactYellow);
            }
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave};mesh.SetVertices(v);mesh.SetColors(c);mesh.SetTriangles(Enumerable.Range(0,v.Count).ToArray(),0);mesh.RecalculateBounds();return mesh;
        }
        static Mesh RegionBounds(IEnumerable<FitContactRegion> regions)
        {
            var v=new List<Vector3>();
            foreach(var region in regions) {
                Vector3 min=region.Bounds.min,max=region.Bounds.max;var p=new Vector3[8];for(int i=0;i<8;i++) p[i]=new Vector3((i&1)==0?min.x:max.x,(i&2)==0?min.y:max.y,(i&4)==0?min.z:max.z);
                for(int i=0;i<8;i++) for(int bit=1;bit<=4;bit*=2) if((i&bit)==0) {v.Add(p[i]);v.Add(p[i|bit]);}
            }
            var mesh=new Mesh{indexFormat=IndexFormat.UInt32,hideFlags=HideFlags.HideAndDontSave};mesh.SetVertices(v);mesh.SetColors(Enumerable.Repeat(new Color(.2f,1,.4f,.8f),v.Count).ToList());mesh.SetIndices(Enumerable.Range(0,v.Count).ToArray(),MeshTopology.Lines,0);mesh.RecalculateBounds();return mesh;
        }
        static IEnumerable<FitTriangle> Clip(IEnumerable<FitTriangle> triangles,Vector3 center,Vector3 normal)
        {
            foreach(var t in triangles) {
                var input=new[]{t.A,t.B,t.C};var polygon=new List<Vector3>();
                for(int i=0;i<3;i++) {Vector3 a=input[i],b=input[(i+1)%3];float da=Vector3.Dot(a-center,normal),db=Vector3.Dot(b-center,normal);if(da<=0) polygon.Add(a);if((da<0&&db>0)||(da>0&&db<0)) polygon.Add(Vector3.Lerp(a,b,da/(da-db)));}
                for(int i=1;i+1<polygon.Count;i++) yield return new FitTriangle(polygon[0],polygon[i],polygon[i+1]);
            }
        }
    }
}
