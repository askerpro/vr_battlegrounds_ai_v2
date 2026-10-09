using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;
using VrBattlegrounds.Editor.HandGeometry;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Владелец временной preview-сцены; исходные prefab/mesh/pose не меняются.</summary>
    public sealed class HandPoseFitSnapshot : IDisposable
    {
        PreviewRenderUtility _preview;
        public PreviewRenderUtility Preview { get => _preview ?? (_preview=new PreviewRenderUtility()); private set => _preview=value; }
        public readonly List<FitTriangle> Hand = new List<FitTriangle>(), ContactHand = new List<FitTriangle>(), ObjectSurface = new List<FitTriangle>(), ContactObject = new List<FitTriangle>();
        public Bounds HandBounds;
        public Vector3 GripCenter;
        public readonly List<FitTriangle> OtherHand=new List<FitTriangle>(),AnalysisVolume=new List<FitTriangle>();
        Matrix4x4 _worldToFrame=Matrix4x4.identity;
        bool _runtime;
        HandPoseFitSnapshot() {}
        /// <summary>Математические fixtures без preview-сцены и SDK. Заполняет только геометрию.</summary>
        public static HandPoseFitSnapshot FromGeometry() => new HandPoseFitSnapshot();
        /// <summary>Чтение текущей формы кисти через SDK preview core с размещением на выбранном GrabPoint.</summary>
        public HandPoseFitSnapshot(UxrAvatar avatar,UxrHandPoseAsset pose,float blend,GameObject root,HandPoseFitRequest r,HandPoseFitReport report,HandPoseFitSnapshot objectCache=null,HandPoseFitReport objectMetadata=null)
        {
            try {
                if(avatar.transform.IsChildOf(root.transform)) throw new ArgumentException("Предмет не должен содержать аватар.");
                var grabbable=ResolveIndexPath(root.transform,r.GrabbableIndexPath).GetComponent<UxrGrabbableObject>();
                if(!grabbable || r.GrabPoint<0 || r.GrabPoint>=grabbable.GrabPointCount) throw new ArgumentException("Выберите Grabbable и существующую точку хвата.");
                var point=grabbable.GetGrabPoint(r.GrabPoint);
                if(point.SnapMode!=UxrSnapToHandMode.PositionAndRotation) throw new NotSupportedException("Для SDK preview требуется Snap Mode Position And Rotation.");
                if(!point.BothHandsCompatible && point.HandSide!=r.Side) throw new ArgumentException("Эта точка не поддерживает выбранную сторону.");
                var grabber=avatar.GetComponentsInChildren<UxrGrabber>(true).FirstOrDefault(g=>g.Side==r.Side);
                if(!grabber || !(grabber.HandRenderer is SkinnedMeshRenderer skin) || !skin.sharedMesh) throw new NotSupportedException("Кисть должна иметь SDK Skinned HandRenderer.");
                var align=grabbable.Editor_GetGrabPointGrabAlignTransform(avatar,r.GrabPoint,r.Side);
                if(!align) throw new ArgumentException("Нет snap transform выбранной стороны.");
                _worldToFrame=Matrix4x4.TRS(root.transform.position,root.transform.rotation,Vector3.one).inverse;
                var snap=Matrix4x4.TRS(align.position,align.rotation,Vector3.one);
                var sourceToSnap=_worldToFrame*snap*Matrix4x4.TRS(grabber.transform.position,grabber.transform.rotation,Vector3.one).inverse;
                GripCenter=_worldToFrame.MultiplyPoint3x4(align.position);
                report.CaptureSource="editor_sdk_grip_preview";report.CaptureFrame=Time.frameCount;report.CaptureGameTime=Time.time;
                report.Avatar=SceneIdentity(avatar.gameObject);report.Object=SceneIdentity(root);report.Pose=HandPoseFitAnalyzer.Identity(pose);
                r.AvatarPath=report.Avatar.Path;r.ObjectPath=report.Object.Path;r.ObjectPrefabLocalFileId=report.Object.LocalFileId;
                report.PoseType=pose.PoseType.ToString();report.AppliedBlend=blend;report.RuntimePoseName=null;
                report.AvatarGuidChain=avatar.GetPrefabGuidChain().ToArray();report.ObjectScale=root.transform.lossyScale.x;report.ObjectScaleAxes=root.transform.lossyScale;
                report.GrabbablePath=HumanPath(grabbable.transform,root.transform);report.AlignToControllerEnabled=point.AlignToController;report.ControllerAlignmentApplied=false;
                var hand=avatar.GetHand(r.Side);var zones=new Dictionary<Transform,string>();var segments=new Dictionary<Transform,string>();
                void Finger(UxrAvatarFinger f,string name) {
                    var nodes=new[]{f.Metacarpal,f.Proximal,f.Intermediate,f.Distal};var labels=new[]{"metacarpal","proximal","intermediate","distal"};
                    for(int i=0;i<nodes.Length;i++) if(nodes[i]) {
                        foreach(var child in nodes[i].GetComponentsInChildren<Transform>(true)){zones[child]=name;segments[child]=name+"/"+labels[i];}
                        report.Joints.Add(new FitJointReport{Zone=name,Segment=labels[i],Path=HumanPath(nodes[i],avatar.transform),PositionMeters=sourceToSnap.MultiplyPoint3x4(nodes[i].position),LocalRotation=nodes[i].localRotation});
                    }
                }
                Finger(hand.Thumb,"thumb");Finger(hand.Index,"index");Finger(hand.Middle,"middle");Finger(hand.Ring,"ring");Finger(hand.Little,"little");
                // Текущие bone rotations читаются в transient Fixed descriptor: AutoSave SDK может быть выключен.
                var visiblePose=ScriptableObject.CreateInstance<UxrHandPoseAsset>();visiblePose.hideFlags=HideFlags.HideAndDontSave;
                UltimateXR.Editor.Manipulation.HandPoses.UxrPreviewHandGripMesh preview=null;
                try {
                    visiblePose.PoseType=UxrHandPoseType.Fixed;visiblePose.HandDescriptorLeft=r.Side==UxrHandSide.Left?new UxrHandDescriptor(avatar,r.Side):new UxrHandDescriptor();visiblePose.HandDescriptorRight=r.Side==UxrHandSide.Right?new UxrHandDescriptor(avatar,r.Side):new UxrHandDescriptor();
                    report.EditorAppliedPoseStateJson=EditorJsonUtility.ToJson(visiblePose,false);
                    preview=UltimateXR.Editor.Manipulation.HandPoses.UxrPreviewHandGripMesh.BuildForAvatar(avatar,visiblePose,r.Side);
                    if(preview==null || !preview.IsValid) throw new ArgumentException("SDK не смог построить preview кисти.");
                    var mesh=preview.UnityMesh;var vertices=mesh.vertices;var weights=mesh.boneWeights;var bones=skin.bones;
                    var axes=avatar.AvatarRigInfo.GetArmInfo(r.Side);var matrix=_worldToFrame*snap;var path=HumanPath(skin.transform,avatar.transform);
                    for(int sub=0;sub<mesh.subMeshCount;sub++) {
                        var ix=mesh.GetTriangles(sub);
                        for(int i=0;i<ix.Length;i+=3) {
                            int a=ix[i],b=ix[i+1],c=ix[i+2];var votes=new Dictionary<int,float>();
                            foreach(int index in new[]{a,b,c}) {
                                var w=weights[index];int[] ids={w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};float[] values={w.weight0,w.weight1,w.weight2,w.weight3};
                                for(int k=0;k<4;k++)if(values[k]>0 && ids[k]>=0 && ids[k]<bones.Length)votes[ids[k]]=(votes.TryGetValue(ids[k],out float old)?old:0)+values[k];
                            }
                            int dominant=votes.OrderByDescending(v=>v.Value).Select(v=>v.Key).DefaultIfEmpty(-1).First();if(dominant<0) continue;
                            var bone=bones[dominant];string zone=zones.TryGetValue(bone,out string z)?z:"palm_wrist";
                            var t=new FitTriangle(matrix.MultiplyPoint3x4(vertices[a]),matrix.MultiplyPoint3x4(vertices[b]),matrix.MultiplyPoint3x4(vertices[c]),zone){Source=path,SourceTriangle=i/3,SourceSubMesh=sub,FingerSegment=segments.TryGetValue(bone,out string segment)?segment:"palm_wrist"};
                            if(t.Area<1e-12f)continue;Hand.Add(t);
                            var up=zone=="palm_wrist"?axes.HandUniversalLocalAxes.LocalUp:axes.FingerUniversalLocalAxes.LocalUp;
                            var normal=sourceToSnap.MultiplyVector(bone.TransformDirection(up)).normalized*r.PalmNormalSign;
                            if(!r.PalmarOnly || Vector3.Dot(t.Normal,normal)>=r.PalmarMinDot)ContactHand.Add(t);
                        }
                    }
                    report.Surfaces.Add(new FitSurfaceReport{Path=path,Kind="hand",Triangles=Hand.Count,Topology=new FitTopology{Reason="evaluated_on_analysis"}});
                } finally {if(preview?.UnityMesh)Object.DestroyImmediate(preview.UnityMesh);Object.DestroyImmediate(visiblePose);}
                if(objectCache!=null && objectMetadata!=null) {
                    ObjectSurface.AddRange(objectCache.ObjectSurface);ContactObject.AddRange(objectCache.ContactObject);
                    report.Surfaces.AddRange(objectMetadata.Surfaces.Where(s=>s.Kind=="object"));
                } else CaptureObject(root,r,report,false);
                CaptureVolume(r,report);EnsureGeometry();
                report.Limitations.Add("SDK grip preview: текущая форма кисти размещена на выбранном snap transform. Runtime IK/контроллер и фактическое удержание не проверены.");
                report.Limitations.Add("Источник кисти — единственный SDK HandRenderer. Вторая кисть не размещалась; основной хват и поддержка анализируются отдельными точками.");
                report.Limitations.Add("Зоны по весам костей и ладонная маска требуют визуальной проверки.");
            } catch {Dispose();throw;}
        }
        public HandPoseFitSnapshot(HandPoseFitRequest request, HandPoseFitReport report)
        {
            Preview = new PreviewRenderUtility();
            try { Build(request,report); }
            catch { Dispose();throw; }
        }

        /// <summary>Только чтение уже обновлённых живых объектов; никаких повторных pose/snap/IK.</summary>
        public HandPoseFitSnapshot(HandPoseFitRequest r,HandPoseFitReport report,UxrGrabber grabber,GameObject geometryRoot=null)
        {
            Preview=new PreviewRenderUtility();_runtime=true;
            try {
                if(!Application.isPlaying||!grabber||!grabber.GrabbedObject) throw new ArgumentException("Для игрового снимка нужна рука с реально схваченным предметом в Play Mode.");
                UxrAvatar avatar=grabber.Avatar;var held=grabber.GrabbedObject;var root=geometryRoot?geometryRoot:held.gameObject;
                if(!avatar||EditorUtility.IsPersistent(avatar)||!held.transform.IsChildOf(root.transform)) throw new ArgumentException("Неверный живой аватар/корень геометрии.");
                int point=UxrGrabManager.Instance.GetGrabbedPoint(grabber);if(point<0) throw new ArgumentException("SDK не подтвердил точку текущего хвата.");
                r.Side=grabber.Side;r.GrabPoint=point;r.GrabbableIndexPath=IndexPath(held.transform,root.transform);
                report.GrabbablePath=HumanPath(held.transform,root.transform);report.CaptureSource="runtime_after_sdk_update";
                report.CaptureFrame=Time.frameCount;report.CaptureGameTime=Time.time;
                var pose=avatar.GetCurrentRuntimeHandPose(r.Side);report.RuntimePoseName=pose?.PoseName;report.PoseType=pose?.PoseType.ToString();report.AppliedBlend=avatar.GetCurrentHandPoseBlendValue(r.Side);
                report.Pose=HandPoseFitAnalyzer.Identity(pose!=null?avatar.GetHandPose(pose.PoseName):null);
                report.Avatar=SceneIdentity(avatar.gameObject);report.Object=SceneIdentity(root);r.AvatarPath=report.Avatar.Path;r.ObjectPath=report.Object.Path;
                r.ObjectPrefabLocalFileId=report.Object.LocalFileId;
                report.AvatarGuidChain=avatar.GetPrefabGuidChain().ToArray();report.ObjectScale=root.transform.lossyScale.x;
                report.ObjectScaleAxes=root.transform.lossyScale;
                if(string.IsNullOrEmpty(report.Object.Guid)||string.IsNullOrEmpty(report.Avatar.Guid)) report.Limitations.Add("Unity не сохранила prefab-происхождение живого экземпляра: GUID неизвестен, сравнение с эталоном по идентичности входов недоступно.");
                report.AlignToControllerEnabled=held.GetGrabPoint(point).AlignToController;report.ControllerAlignmentApplied=true;
                var model=avatar.ControllerInput?.GetController3DModel(r.Side);report.ControllerModel=model?model.name:null;
                _worldToFrame=Matrix4x4.TRS(root.transform.position,root.transform.rotation,Vector3.one).inverse;
                GripCenter=_worldToFrame.MultiplyPoint3x4(grabber.transform.position);
                if(!grabber.HandRenderer||!grabber.HandRenderer.enabled||!grabber.HandRenderer.gameObject.activeInHierarchy) throw new NotSupportedException("Живая кисть скрыта; нельзя выдавать её за видимую игровую поверхность.");
                CaptureHand(avatar,grabber,r,report);CaptureObject(root,r,report);CaptureVolume(r,report);CaptureOtherHand(avatar,r);
                if(root.GetComponentsInChildren<LODGroup>(true).Length>0) report.Limitations.Add("Runtime LOD: захвачены enabled Renderer; выбор конкретного LOD камерой требует отдельной проверки/фильтра Renderer.");
                EnsureGeometry();
            } catch {Dispose();throw;}
        }

        static FitAssetIdentity SceneIdentity(GameObject go)
        {
            var source=PrefabUtility.GetCorrespondingObjectFromSource(go);return HandPoseFitAnalyzer.Identity(source?source:go);
        }

        void Build(HandPoseFitRequest r,HandPoseFitReport report)
        {
            GameObject avatarRoot=Clone(r.AvatarPrefab.gameObject),objectRoot=Clone(r.ObjectPrefab);
            // Трансформы предмета сохраняют авторский uniform scale. Общая система: метры, оси корня предмета.
            objectRoot.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
            UxrAvatar avatar=avatarRoot.GetComponent<UxrAvatar>();
            Transform gripTransform=ResolveIndexPath(objectRoot.transform,r.GrabbableIndexPath);
            UxrGrabbableObject grabbable=gripTransform.GetComponent<UxrGrabbableObject>();
            if(!grabbable) throw new ArgumentException("На выбранном пути нет UxrGrabbableObject.");
            if(r.GrabPoint<0 || r.GrabPoint>=grabbable.GrabPointCount) throw new ArgumentException("Неверный индекс точки хвата.");
            var point=grabbable.GetGrabPoint(r.GrabPoint);
            if(!point.BothHandsCompatible && point.HandSide!=r.Side) throw new ArgumentException("Сторона руки запрещена точкой хвата.");
            if(point.HideHandGrabberRenderer) throw new NotSupportedException("Точка намеренно скрывает руку.");
            if(point.SnapMode!=UxrSnapToHandMode.PositionAndRotation) throw new NotSupportedException("Нужен статический PositionAndRotation.");
            if(grabbable.GetComponents<UxrGrabPointShape>().Any()) throw new NotSupportedException("Dynamic snap shape не поддерживается.");
            if(r.Profile=="hand_support") throw new NotSupportedException("Профиль поддержки другой рукой требует второго snapshot.");
            var info=point.GetGripPoseInfo(avatar);
            if(info==null) throw new ArgumentException("Не разрешилась запись хвата.");
            report.DefaultGrip=ReferenceEquals(info,point.DefaultGripPoseInfo);
            report.GripAvatarGuid=info.AvatarPrefabGuid;
            report.AvatarGuidChain=avatar.GetPrefabGuidChain().ToArray();
            if(report.DefaultGrip && !r.AllowDefaultGrip) throw new ArgumentException("SDK выбрал default: разрешите его явно либо выберите аватар с записью хвата.");
            var sourcePose=r.PoseOverride ? r.PoseOverride : info.HandPose;
            if(!sourcePose) throw new ArgumentException("В записи отсутствует поза.");
            var pose=r.PoseOverride ? sourcePose : avatar.GetHandPose(sourcePose.name);
            if(!pose) throw new ArgumentException("Аватар не разрешил позу по имени: " + sourcePose.name);
            report.PoseInherited=pose!=sourcePose;
            report.Pose=HandPoseFitAnalyzer.Identity(pose);report.PoseType=pose.PoseType.ToString();
            float blend=r.OverrideBlend?r.Blend:info.PoseBlendValue;
            if(!Finite(blend)||blend<0||blend>1) throw new ArgumentException("Blend должен быть в диапазоне 0..1.");
            report.AppliedBlend=pose.PoseType==UxrHandPoseType.Blend?blend:0;
            UxrGrabber grabber=avatarRoot.GetComponentsInChildren<UxrGrabber>(true).SingleOrDefault(g=>g.Side==r.Side);
            if(!grabber || avatar.GetHand(r.Side).Wrist==null) throw new ArgumentException("Нет граббера или костей кисти.");
            Transform align=grabbable.Editor_GetGrabPointGrabAlignTransform(avatar,r.GrabPoint,r.Side);
            if(!align || !align.IsChildOf(objectRoot.transform)) throw new ArgumentException("Трансформ выравнивания отсутствует или вне предмета.");
            GripCenter=align.position;
            if(point.SnapReference==UxrSnapReference.UseOtherTransform && (r.Side==UxrHandSide.Left?info.GripAlignTransformHandLeft:info.GripAlignTransformHandRight)==null) throw new ArgumentException("Отсутствует трансформ выравнивания стороны.");
            ValidateScale(objectRoot.transform);ValidateScale(avatarRoot.transform);
            report.ObjectScale=objectRoot.transform.lossyScale.x;
            report.ObjectScaleAxes=objectRoot.transform.lossyScale;
            Vector3 targetPosition=align.position;Quaternion targetRotation=align.rotation;
            report.AlignToControllerEnabled=point.AlignToController;
            if(point.AlignToController) report.Limitations.Add("AlignToController включён: grip_reference оценивает позу относительно сохранённого трансформа, без дополнительного поворота контроллера.");
            Quaternion rotation=targetRotation*Quaternion.Inverse(grabber.transform.rotation);
            avatarRoot.transform.rotation=rotation*avatarRoot.transform.rotation;
            avatarRoot.transform.position+=targetPosition-grabber.transform.position+r.HandOffsetMm*.001f;
            if(pose.PoseType==UxrHandPoseType.Fixed) avatar.SetCurrentHandPoseImmediately(r.Side,pose);
            else if(pose.PoseType==UxrHandPoseType.Blend) UxrAvatarRig.UpdateHandUsingDescriptor(avatar,r.Side,pose.GetHandDescriptor(r.Side,UxrBlendPoseType.OpenGrip),pose.GetHandDescriptor(r.Side,UxrBlendPoseType.ClosedGrip),blend);
            else throw new NotSupportedException("Неподдерживаемый тип позы.");
            report.GrabbablePath=HumanPath(gripTransform,objectRoot.transform);
            CaptureHand(avatar,grabber,r,report);
            CaptureObject(objectRoot,r,report);CaptureVolume(r,report);CaptureOtherHand(avatar,r);EnsureGeometry();
            foreach(Renderer renderer in avatarRoot.GetComponentsInChildren<Renderer>(true)) renderer.enabled=false;
            foreach(Renderer renderer in objectRoot.GetComponentsInChildren<Renderer>(true)) renderer.enabled=false;
            report.Limitations.Add("Статический снимок: полный IK, движение контроллеров и игровые ограничения двух рук не проверяются.");
            report.Limitations.Add("Контактная AABB включает целый треугольник по его центру; это грубая маска, не обрезка поверхности.");
        }

        void CaptureObject(GameObject objectRoot,HandPoseFitRequest r,HandPoseFitReport report,bool measureTopology=true)
        {
            var excluded=new HashSet<Renderer>();
            foreach(var lod in objectRoot.GetComponentsInChildren<LODGroup>(true)) {
                var levels=lod.GetLODs();if(levels.Length==0)continue;var first=new HashSet<Renderer>(levels[0].renderers);
                foreach(var level in levels.Skip(1))foreach(var renderer in level.renderers)if(renderer&&!first.Contains(renderer))excluded.Add(renderer);
            }
            foreach(Renderer renderer in objectRoot.GetComponentsInChildren<Renderer>(false)) {
                if(!renderer.enabled || excluded.Contains(renderer) || renderer is ParticleSystemRenderer || renderer.GetComponent<UxrGrabbableObjectPreviewMeshProxy>() || renderer.name.IndexOf("GrabHighlight",StringComparison.OrdinalIgnoreCase)>=0) continue;
                Mesh mesh=null; bool baked=false;
                if(renderer is SkinnedMeshRenderer skin) {mesh=new Mesh();baked=true;skin.BakeMesh(mesh,true);}
                else if(renderer is MeshRenderer) {var filter=renderer.GetComponent<MeshFilter>();mesh=filter?filter.sharedMesh:null;}
                if(!mesh) {report.Limitations.Add("Исключён неподдерживаемый renderer: "+HumanPath(renderer.transform,objectRoot.transform));continue;}
                try {
                    string path=HumanPath(renderer.transform,objectRoot.transform);
                    var ts=ReadTriangles(mesh,_worldToFrame*renderer.localToWorldMatrix,path,null);
                    ObjectSurface.AddRange(ts);
                    bool selected=r.ContactRendererPaths.Length==0 || r.ContactRendererPaths.Contains(path);
                    if(selected) ContactObject.AddRange(ts.Where(t=>!r.ContactBoundsEnabled || r.ContactBounds.Contains((t.A+t.B+t.C)/3)));
                    report.Surfaces.Add(new FitSurfaceReport {Path=path,Kind="object",Triangles=ts.Count,Topology=measureTopology?HandPoseFitGeometry.Topology(ts.ToArray()):new FitTopology{Reason="not_evaluated_in_live_capture"}});
                } finally {if(baked) Object.DestroyImmediate(mesh);}
            }
        }

        void EnsureGeometry()
        {
            if(Hand.Count==0 || ObjectSurface.Count==0 || ContactHand.Count==0 || ContactObject.Count==0) throw new ArgumentException($"Нет геометрии: кисть={Hand.Count}, предмет={ObjectSurface.Count}, контакт кисти={ContactHand.Count}, контакт предмета={ContactObject.Count}.");
            HandBounds=Hand[0].Bounds;foreach(var t in Hand) HandBounds.Encapsulate(t.Bounds);
        }

        void CaptureVolume(HandPoseFitRequest r,HandPoseFitReport report)
        {
            if(string.IsNullOrEmpty(r.AnalysisVolumeMeshPath)) return;
            var mesh=ResolveVolumeMesh(r);
            if(!mesh) throw new ArgumentException("Не найден mesh аналитического объёма: "+r.AnalysisVolumeMeshPath);
            report.AnalysisVolume=HandPoseFitAnalyzer.Identity(mesh);
            AnalysisVolume.AddRange(ReadTriangles(mesh,Matrix4x4.TRS(r.AnalysisVolumePositionMeters,Quaternion.Euler(r.AnalysisVolumeEulerDegrees),r.AnalysisVolumeScale),"analysis_volume",null));
            report.AnalysisVolumeTopology=HandPoseFitGeometry.Topology(AnalysisVolume.ToArray());
        }

        public static Mesh ResolveVolumeMesh(HandPoseFitRequest r)
        {
            string path=string.IsNullOrEmpty(r.AnalysisVolumeMeshGuid)?r.AnalysisVolumeMeshPath:AssetDatabase.GUIDToAssetPath(r.AnalysisVolumeMeshGuid);
            if(string.IsNullOrEmpty(path)) return null;
            var meshes=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
            if(!string.IsNullOrEmpty(r.AnalysisVolumeMeshGuid)) return meshes.FirstOrDefault(m=>AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m,out string guid,out long id)&&guid==r.AnalysisVolumeMeshGuid&&id==r.AnalysisVolumeMeshLocalFileId);
            if(meshes.Length>1) throw new ArgumentException("В файле несколько mesh: нужен GUID + LocalFileId выбранного subasset.");
            return meshes.SingleOrDefault();
        }

        void CaptureOtherHand(UxrAvatar avatar,HandPoseFitRequest r)
        {
            if(!r.IncludeOtherHand) return;var side=r.Side==UxrHandSide.Left?UxrHandSide.Right:UxrHandSide.Left;
            var grabber=avatar.GetComponentsInChildren<UxrGrabber>(true).FirstOrDefault(g=>g.Side==side);
            if(!grabber || (_runtime && (!grabber.HandRenderer||!grabber.HandRenderer.enabled))) return;
            // Выделение второй кисти тем же способом, без изменения её позы.
            int handCount=Hand.Count,contactCount=ContactHand.Count;var savedBounds=HandBounds;var copy=r.Copy();copy.Side=side;copy.PalmarOnly=false;
            CaptureHand(avatar,grabber,copy,new HandPoseFitReport());OtherHand.AddRange(Hand.Skip(handCount));Hand.RemoveRange(handCount,Hand.Count-handCount);ContactHand.RemoveRange(contactCount,ContactHand.Count-contactCount);HandBounds=savedBounds;
        }

        GameObject Clone(GameObject prefab)
        {
            var clone=Preview.InstantiatePrefabInScene(prefab);
            foreach(Behaviour b in clone.GetComponentsInChildren<Behaviour>(true)) b.enabled=false;
            foreach(LODGroup lod in clone.GetComponentsInChildren<LODGroup>(true)) {
                var levels=lod.GetLODs();if(levels.Length==0) continue;var visible=new HashSet<Renderer>(levels[0].renderers);
                foreach(var level in levels.Skip(1)) foreach(var renderer in level.renderers) if(renderer&&!visible.Contains(renderer)) renderer.enabled=false;
                lod.ForceLOD(0);
            }
            return clone;
        }

        void CaptureHand(UxrAvatar avatar,UxrGrabber grabber,HandPoseFitRequest r,HandPoseFitReport report)
        {
            var hand=avatar.GetHand(r.Side);var allowed=new HashSet<Transform>(hand.Transforms);
            foreach(Transform bone in hand.Transforms) foreach(Transform child in bone.GetComponentsInChildren<Transform>(true)) allowed.Add(child);
            var zones=new Dictionary<Transform,string>();
            var segments=new Dictionary<Transform,string>();
            void Finger(UxrAvatarFinger f,string name) {
                var nodes=new[]{f.Metacarpal,f.Proximal,f.Intermediate,f.Distal};var labels=new[]{"metacarpal","proximal","intermediate","distal"};
                for(int i=0;i<nodes.Length;i++) if(nodes[i]) {foreach(Transform child in nodes[i].GetComponentsInChildren<Transform>(true)) {zones[child]=name;segments[child]=name+"/"+labels[i];}
                    report.Joints.Add(new FitJointReport {Zone=name,Segment=labels[i],Path=HumanPath(nodes[i],avatar.transform),PositionMeters=_worldToFrame.MultiplyPoint3x4(nodes[i].position),LocalRotation=nodes[i].localRotation});}
            }
            Finger(hand.Thumb,"thumb");Finger(hand.Index,"index");Finger(hand.Middle,"middle");Finger(hand.Ring,"ring");Finger(hand.Little,"little");
            var main=grabber.HandRenderer as SkinnedMeshRenderer;
            foreach(var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if((_runtime||skin!=main) && (!skin.enabled || !skin.gameObject.activeInHierarchy)) continue;
                Mesh source=skin.sharedMesh;if(!source || !skin.bones.Any(allowed.Contains)) continue;
                {
                    var sample=HandMeshCapture.Capture(skin,_worldToFrame,avatar.transform,false);
                    Vector3[] v=sample.Vertices;var bones=sample.Bones;
                    var weights=new float[v.Length];var dominant=new int[v.Length];
                    for(int i=0;i<v.Length;i++) {float best=0;dominant[i]=-1;
                        for(int j=sample.WeightOffsets[i];j<sample.WeightOffsets[i+1];j++) {var w=sample.Weights[j];if(!allowed.Contains(bones[w.BoneIndex])) continue;weights[i]+=w.Weight;if(w.Weight>best) {best=w.Weight;dominant[i]=w.BoneIndex;} }
                    }
                    string path=HumanPath(skin.transform,avatar.transform); var extracted=new List<FitTriangle>();
                    for(int sub=0;sub<sample.SubmeshTriangles.Length;sub++) {
                        int[] ix=sample.SubmeshTriangles[sub];
                        for(int i=0;i<ix.Length;i+=3) {
                            int a=ix[i],b=ix[i+1],c=ix[i+2];if((weights[a]+weights[b]+weights[c])/3<.5f) continue;
                            // Голосование трёх вершин устойчивее, чем выбор первой вершины на границе фаланги.
                            int di=new[]{a,b,c}.Where(x=>dominant[x]>=0).GroupBy(x=>dominant[x]).OrderByDescending(g=>g.Sum(x=>weights[x])).Select(g=>g.Key).DefaultIfEmpty(-1).First();if(di<0) continue;
                            Transform bone=bones[di];string zone=zones.TryGetValue(bone,out string z)?z:"palm_wrist";
                            var t=new FitTriangle(v[a],v[b],v[c],zone){Source=path,SourceTriangle=i/3,SourceSubMesh=sub,FingerSegment=segments.TryGetValue(bone,out string segment)?segment:"palm_wrist"};
                            if(t.Area<1e-12f) continue;Hand.Add(t);extracted.Add(t);
                            var axes=avatar.AvatarRigInfo.GetArmInfo(r.Side);
                            Vector3 localUp=zone=="palm_wrist"?axes.HandUniversalLocalAxes.LocalUp:axes.FingerUniversalLocalAxes.LocalUp;
                            Vector3 normal=_worldToFrame.MultiplyVector(bone.TransformDirection(localUp)).normalized*r.PalmNormalSign;
                            if(!r.PalmarOnly || Vector3.Dot(t.Normal,normal)>=r.PalmarMinDot) ContactHand.Add(t);
                        }
                    }
                    report.Surfaces.Add(new FitSurfaceReport {Path=path,Kind="hand",Triangles=extracted.Count,Topology=HandPoseFitGeometry.Topology(extracted.ToArray())});
                }
            }
            report.Limitations.Add("Зоны по весам костей, ладонная маска по нормалям — требуют визуальной проверки для нового рига/перчатки; palm_wrist включает границу запястья.");
        }

        static List<FitTriangle> ReadTriangles(Mesh mesh,Matrix4x4 matrix,string path,string zone)
        {
            var result=new List<FitTriangle>();
            // Editor API читает тот же mesh в Play Mode даже при выключенном Read/Write.
            // Импорт, persistent asset и флаг isReadable остаются без изменений.
            using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
            using(var vertices=new NativeArray<Vector3>(data[0].vertexCount,Allocator.Temp)) {
                data[0].GetVertices(vertices);
                for(int sub=0;sub<data[0].subMeshCount;sub++) {
                    var descriptor=data[0].GetSubMesh(sub);if(descriptor.topology!=MeshTopology.Triangles) continue;
                    var ix=new int[descriptor.indexCount];
                    if(mesh.indexFormat==UnityEngine.Rendering.IndexFormat.UInt16) {
                        var raw=data[0].GetIndexData<ushort>();for(int i=0;i<ix.Length;i++) ix[i]=raw[descriptor.indexStart+i]+descriptor.baseVertex;
                    } else {
                        var raw=data[0].GetIndexData<int>();for(int i=0;i<ix.Length;i++) ix[i]=raw[descriptor.indexStart+i]+descriptor.baseVertex;
                    }
                    for(int i=0;i<ix.Length;i+=3) result.Add(new FitTriangle(matrix.MultiplyPoint3x4(vertices[ix[i]]),matrix.MultiplyPoint3x4(vertices[ix[i+1]]),matrix.MultiplyPoint3x4(vertices[ix[i+2]]),zone){Source=path,SourceTriangle=i/3,SourceSubMesh=sub});
                }
            }
            return result;
        }
        public static Transform ResolveIndexPath(Transform root,string path)
        {if(string.IsNullOrEmpty(path)) return root;foreach(string part in path.Split('/')) {if(!int.TryParse(part,out int i)||i<0||i>=root.childCount) throw new ArgumentException("Неверный путь Transform.");root=root.GetChild(i);}return root;}
        public static string IndexPath(Transform t,Transform root)
        {var parts=new List<string>();while(t!=root) {parts.Add(t.GetSiblingIndex().ToString());t=t.parent;if(!t) throw new ArgumentException("Transform вне корня.");}parts.Reverse();return string.Join("/",parts);}
        public static string HumanPath(Transform t,Transform root)
        {var parts=new List<string>();while(t!=root) {parts.Add(t.name);t=t.parent;}parts.Reverse();return string.Join("/",parts);}
        static void ValidateScale(Transform root)
        {Vector3 s=root.lossyScale;if(!Finite(s.x)||s.x<=0||Mathf.Abs(s.x-s.y)>1e-4f||Mathf.Abs(s.x-s.z)>1e-4f) throw new NotSupportedException("Требуется положительный uniform scale корня: "+root.name);}
        static bool Finite(float x) => !float.IsNaN(x)&&!float.IsInfinity(x);
        public void Dispose() { _preview?.Cleanup();_preview=null; }
    }
}
