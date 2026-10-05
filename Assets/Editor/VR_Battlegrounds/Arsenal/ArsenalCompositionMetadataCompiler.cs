using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UltimateXR.Core.Components;
using UltimateXR.Manipulation;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Явный read-only native compiler. Возвращает own transient catalog; caller сохраняет только объявленный output под lease.</summary>
    public static class ArsenalCompositionMetadataCompiler
    {
        public static ArsenalCompositionCatalog Compile(string catalogId, GameObject pegSource, GameObject shelfSource,
            GameObject acceptedStationProfile, IReadOnlyList<WeaponInfo> weapons, IReadOnlyList<ArsenalPresentationStyle> styles,
            IEnumerable<ArsenalDecorationDescriptor> decorations=null)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
                throw new InvalidOperationException("Compiler требует idle EditMode.");
            var accepted=acceptedStationProfile.GetComponentsInChildren<ArsenalSlotController>(true);
            var templates=new List<ArsenalFunctionalSlotTemplate>();
            foreach(var zone in new[]{ArsenalPresentationZone.Pegboard,ArsenalPresentationZone.Shelf})
            {
                var projected=accepted.Where(s=>s.PresentationZone==zone).ToArray();
                if(projected.Length<2)throw new InvalidOperationException("Недостаточно accepted native cells для измерения profile "+zone);
                templates.Add(Template(zone==ArsenalPresentationZone.Pegboard?pegSource:shelfSource,zone,projected));
            }
            var geometry=weapons.Distinct().OrderBy(w=>w.WeaponId,StringComparer.Ordinal).Select(w=>new ArsenalWeaponGeometry
            { Weapon=w,WeaponId=w.WeaponId,SourceGuid=GuidFor(w),Item=Item(w.WeaponPrefab),Magazine=Item(w.MagazinePrefab) }).ToArray();
            var supports=styles.Where(s=>s!=null&&s.SupportModule!=null).Select(s=>s.SupportModule).Distinct()
                .OrderBy(GuidFor,StringComparer.Ordinal).Select(m=>new ArsenalSupportGeometry
                {Module=m,SourceGuid=GuidFor(m),SourceFingerprint=Fingerprint(m),LocalBounds=VisualBounds(m.transform,false)}).ToArray();
            var materials=styles.Where(s=>s!=null&&s.ReturnReadyMaterial!=null).Select(s=>s.ReturnReadyMaterial).Distinct()
                .OrderBy(GuidFor,StringComparer.Ordinal).Select(m=>new ArsenalMaterialResource
                {Material=m,SourceGuid=GuidFor(m),SourceFingerprint=Fingerprint(m)}).ToArray();
            return ArsenalCompositionCatalog.CreateCompiled(catalogId,templates,decorations??Array.Empty<ArsenalDecorationDescriptor>(),geometry,supports,materials);
        }
        private static ArsenalFunctionalSlotTemplate Template(GameObject source,ArsenalPresentationZone zone,ArsenalSlotController[] accepted)
        {
            if(source==null||source.GetComponent<FirearmSlotController>()==null)
                throw new InvalidOperationException("Functional source должен иметь один root FirearmSlotController.");
            Unit(source.transform.localScale,"source functional root");
            var sourcePanel=Panel(source.transform);var projected=Panel(accepted[0].transform);
            var mesh=sourcePanel.GetComponent<MeshFilter>().sharedMesh;var collider=sourcePanel.GetComponent<BoxCollider>();
            foreach(var slot in accepted)
            {
                Unit(slot.transform.localScale,"accepted functional root");var p=Panel(slot.transform);
                if(Vector3.Distance(p.localPosition,projected.localPosition)>.00001f||Quaternion.Angle(p.localRotation,projected.localRotation)>.0001f||Vector3.Distance(p.localScale,projected.localScale)>.00001f)
                    throw new InvalidOperationException("Accepted panel projections расходятся в зоне "+zone);
                if(p.GetComponent<MeshFilter>().sharedMesh!=mesh||p.GetComponent<BoxCollider>().center!=collider.center||p.GetComponent<BoxCollider>().size!=collider.size)
                    throw new InvalidOperationException("Source/projected mesh или collider shape различны; нужен explicit geometry mapping.");
            }
            var pose=new ArsenalPresentationPose(projected.localPosition,projected.localRotation);
            var sourcePose=new ArsenalPresentationPose(sourcePanel.localPosition,sourcePanel.localRotation);
            var projection=new ArsenalSurfaceProjection
            { RoleKey="surface/panel",SourceComponentId=LocalId(sourcePanel),SourcePath="PegboardSection",
                ProjectionProfileGuid=GuidFor(accepted[0].gameObject),ProjectionProfileFingerprint=Fingerprint(accepted[0].gameObject),ProjectionSurfaceComponentId=LocalId(projected),
                SourcePose=sourcePose,ProjectedPose=pose,SourceScale=sourcePanel.localScale,ProjectedScale=projected.localScale,
                SourceMeshBounds=mesh.bounds,SourceColliderBounds=new Bounds(collider.center,collider.size),
                ProjectedMeshBounds=ScaledBounds(mesh.bounds,projected.localScale,pose),
                ProjectedColliderBounds=ScaledBounds(new Bounds(collider.center,collider.size),projected.localScale,pose) };
            var sorted=accepted.Select(s=>s.transform.localPosition.x).OrderBy(x=>x).ToArray();
            float pitch=sorted[1]-sorted[0];for(int i=2;i<sorted.Length;i++)if(Mathf.Abs(sorted[i]-sorted[i-1]-pitch)>.0001f)
                throw new InvalidOperationException("Accepted row pitch неодинаков.");
            float width=projection.ProjectedMeshBounds.size.x;float gap=pitch-width;
            if(Mathf.Abs(width-.4f)>.0001f||Mathf.Abs(gap-(zone==ArsenalPresentationZone.Shelf?.03f:.025f))>.0001f)
                throw new InvalidOperationException("Accepted profile не соответствует root ruling.");
            var row=accepted[0].transform.parent;
            if(accepted.Any(s=>s.transform.parent!=row))throw new InvalidOperationException("Нет общего row frame.");
            for(var node=row;node!=null;node=node.parent)Unit(node.localScale,"accepted row parent");
            var poses=accepted[0].GetComponentInParent<ArsenalEquipmentPoses>(true);
            if(poses==null)throw new InvalidOperationException("Нет authored equipment poses для profile.");
            var pair=poses.Targets.Where(t=>t.Target!=null&&(t.Target==row||row.IsChildOf(t.Target))).ToArray();
            if(pair.Length!=1||pair[0].OpenPose==null||pair[0].ClosedPose==null)
                throw new InvalidOperationException("Row не имеет однозначных Open/Closed authored poses.");
            var root=poses.transform;
            var rowRelative=RootPose(pair[0].Target,row);
            var first=accepted[0].transform;
            if(accepted.Any(s=>Mathf.Abs(s.transform.localPosition.y-first.localPosition.y)>.00001f||
                Mathf.Abs(s.transform.localPosition.z-first.localPosition.z)>.00001f||Quaternion.Angle(s.transform.localRotation,first.localRotation)>.0001f))
                throw new InvalidOperationException("Accepted slots имеют разные non-row root poses.");
            var basePose=new ArsenalPresentationPose(new Vector3(0,first.localPosition.y,first.localPosition.z),first.localRotation);
            var relative=ArsenalStationResolver.Compose(rowRelative,basePose);
            var open=ArsenalStationResolver.Compose(RootPose(root,pair[0].OpenPose),relative);
            var closed=ArsenalStationResolver.Compose(RootPose(root,pair[0].ClosedPose),relative);
            var controller=source.GetComponent<FirearmSlotController>();
            if(controller.ItemAnchor==null||controller.MagAnchor==null||controller.ItemAnchor==controller.MagAnchor)
                throw new InvalidOperationException("Нет однозначных authored item/magazine anchor refs.");
            var roles=source.GetComponentsInChildren<UxrComponent>(true).Select(c=>
            {
                var so=new SerializedObject(c);var property=so.FindProperty("_uxrUniqueId");
                Guid serialized;
                if(property==null||!Guid.TryParse(property.stringValue,out serialized)||serialized!=c.UniqueId)throw new InvalidOperationException("SDK cache/serialized UID mismatch "+LocalId(c));
                string role;
                if(c==controller.ItemAnchor)role="item-anchor";
                else if(c==controller.MagAnchor)role="magazine-anchor";
                else throw new InvalidOperationException("SDK component не имеет explicit functional role: "+LocalId(c));
                return new ArsenalTemplateRole(role,c.UniqueId.ToString(),LocalId(c),c.GetType().FullName);
            }).OrderBy(r=>r.RoleKey,StringComparer.Ordinal).ToArray();
            if(roles.Length==0||roles.Select(r=>r.SourceUniqueId).Distinct(StringComparer.Ordinal).Count()!=roles.Length)
                throw new InvalidOperationException("Source SDK UID/role пусты или дублируются.");
            var structure=new List<ArsenalStructuralGeometry>();
            foreach(var renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                if(renderer.transform==sourcePanel||renderer.GetComponent<TMPro.TMP_Text>()!=null||Under(renderer.transform,"WeaponCard",source.transform)||Under(renderer.transform,"PriceTag",source.transform))continue;
                if(!Active(renderer.transform,source.transform)||!renderer.enabled)continue;
                structure.Add(new ArsenalStructuralGeometry {SourceComponentId=LocalId(renderer),ComponentType=renderer.GetType().FullName,SlotLocalBounds=RendererBounds(renderer,source.transform,false)});
            }
            foreach(var other in source.GetComponentsInChildren<Collider>(true))
                if(other.transform!=sourcePanel&&other.enabled&&!other.isTrigger&&Active(other.transform,source.transform)&&
                    !Under(other.transform,"WeaponCard",source.transform)&&!Under(other.transform,"PriceTag",source.transform))
                    structure.Add(new ArsenalStructuralGeometry {SourceComponentId=LocalId(other),ComponentType=other.GetType().FullName,
                        SlotLocalBounds=MatrixBounds(ColliderBounds(other),source.transform.worldToLocalMatrix*other.transform.localToWorldMatrix)});
            return new ArsenalFunctionalSlotTemplate("functional/"+zone.ToString().ToLowerInvariant(),GuidFor(source),Fingerprint(source),zone,
                source,width,gap,open,closed,projection,roles,structure.OrderBy(s=>s.SourceComponentId,StringComparer.Ordinal));
        }
        private static Transform Panel(Transform root)
        {
            var panel=root.Find("PegboardSection");
            if(panel==null||panel.parent!=root||panel.GetComponent<MeshFilter>()==null||panel.GetComponent<MeshFilter>().sharedMesh==null||panel.GetComponents<BoxCollider>().Length!=1||panel.GetComponent<MeshRenderer>()==null)
                throw new InvalidOperationException("Explicit direct surface/panel role не найден.");
            if(root.GetComponentsInChildren<Transform>(true).Count(t=>t.name=="PegboardSection")!=1)
                throw new InvalidOperationException("Surface role неоднозначна.");
            return panel;
        }
        private static ArsenalItemGeometry Item(GameObject source)
        {
            if(source==null)return default;
            ArsenalPresentationResolver.ValidatePrefabAlignment(source);var item=source.GetComponent<UxrGrabbableObject>();
            return new ArsenalItemGeometry { Present=true,Resource=source,SourceGuid=GuidFor(source),SourceFingerprint=Fingerprint(source),
                PhysicalScale=source.transform.localScale,
                DropOffsetPhysical=Vector3.Scale(source.transform.InverseTransformPoint(item.DropAlignTransform.position),source.transform.localScale),
                DropRotation=Quaternion.Inverse(source.transform.rotation)*item.DropAlignTransform.rotation,
                PhysicalRootBounds=VisualBounds(source.transform,true) };
        }
        private static Bounds VisualBounds(Transform root,bool physical)
        {
            Bounds result=default;bool has=false;
            foreach(var renderer in root.GetComponentsInChildren<Renderer>(true))
                if(renderer.enabled&&Active(renderer.transform,root)&&renderer.GetComponent<TMPro.TMP_Text>()==null)
                { var bounds=RendererBounds(renderer,root,physical);if(has)result.Encapsulate(bounds);else{result=bounds;has=true;} }
            if(physical)foreach(var collider in root.GetComponentsInChildren<Collider>(true))
                if(collider.enabled&&!collider.isTrigger&&Active(collider.transform,root))
                { var bounds=MatrixBounds(ColliderBounds(collider),Matrix4x4.Scale(root.localScale)*root.worldToLocalMatrix*collider.transform.localToWorldMatrix);
                    if(has)result.Encapsulate(bounds);else{result=bounds;has=true;} }
            if(!has)throw new InvalidOperationException("Нет активной render geometry "+root.name);
            return result;
        }
        private static Bounds RendererBounds(Renderer renderer,Transform root,bool physical)
        {
            Bounds source;var mesh=renderer.GetComponent<MeshFilter>();var skinned=renderer as SkinnedMeshRenderer;
            if(mesh!=null&&mesh.sharedMesh!=null)source=mesh.sharedMesh.bounds;
            else if(skinned!=null&&skinned.sharedMesh!=null)source=skinned.localBounds;
            else throw new InvalidOperationException("Не поддерживается renderer metadata "+renderer.GetType().FullName);
            var matrix=root.worldToLocalMatrix*renderer.transform.localToWorldMatrix;
            if(physical)matrix=Matrix4x4.Scale(root.localScale)*matrix;
            return MatrixBounds(source,matrix);
        }
        private static Bounds MatrixBounds(Bounds source,Matrix4x4 matrix)
        {
            Bounds result=default;bool has=false;
            for(int i=0;i<8;i++) {var corner=source.center+Vector3.Scale(source.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                var point=matrix.MultiplyPoint3x4(corner);if(has)result.Encapsulate(point);else{result=new Bounds(point,Vector3.zero);has=true;} }
            return result;
        }
        private static Bounds ColliderBounds(Collider collider)
        {
            var box=collider as BoxCollider;if(box!=null)return new Bounds(box.center,box.size);
            var sphere=collider as SphereCollider;if(sphere!=null)return new Bounds(sphere.center,Vector3.one*(sphere.radius*2));
            var capsule=collider as CapsuleCollider;if(capsule!=null){var size=Vector3.one*(capsule.radius*2);size[capsule.direction]=Mathf.Max(capsule.height,capsule.radius*2);return new Bounds(capsule.center,size);}
            var mesh=collider as MeshCollider;if(mesh!=null&&mesh.sharedMesh!=null)return mesh.sharedMesh.bounds;
            throw new InvalidOperationException("Collider geometry не поддерживается "+collider.GetType().FullName);
        }
        private static Bounds ScaledBounds(Bounds b,Vector3 scale,ArsenalPresentationPose pose) =>
            ArsenalStationResolver.TransformBounds(new Bounds(Vector3.Scale(b.center,scale),Vector3.Scale(b.size,scale)),pose);
        private static ArsenalPresentationPose RootPose(Transform root,Transform target) =>
            new ArsenalPresentationPose(root.InverseTransformPoint(target.position),Quaternion.Inverse(root.rotation)*target.rotation);
        private static bool Active(Transform item,Transform root){for(var t=item;t!=null;t=t.parent){if(!t.gameObject.activeSelf)return false;if(t==root)return true;}return false;}
        private static bool Under(Transform item,string name,Transform root){for(var t=item;t!=null&&t!=root;t=t.parent)if(t.name==name)return true;return false;}
        private static void Unit(Vector3 scale,string context){ArsenalPresentationResolver.ValidateScale(scale,context);if((scale-Vector3.one).sqrMagnitude>1e-10f)throw new InvalidOperationException("Не unit frame "+context);}
        private static string GuidFor(UnityEngine.Object asset){var path=AssetDatabase.GetAssetPath(asset);if(string.IsNullOrEmpty(path))throw new InvalidOperationException("Resource не native asset.");return AssetDatabase.AssetPathToGUID(path);}
        private static string Fingerprint(UnityEngine.Object asset)=>AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(asset)).ToString();
        private static string LocalId(UnityEngine.Object item){string guid;long id;if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item,out guid,out id)||id==0)throw new InvalidOperationException("Нет native source localFileID.");return id.ToString(CultureInfo.InvariantCulture);}
    }
}
