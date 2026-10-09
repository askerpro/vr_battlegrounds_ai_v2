using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Явный read-only native compiler. Возвращает own transient catalog; caller сохраняет только объявленный output под lease.</summary>
    public static class ArsenalCompositionMetadataCompiler
    {
        /// <summary>
        /// Ресурсы оформления и метаданные оружия. Шаблонов слотов и поз рядов здесь нет: слот — префаб, который
        /// указывает ряд корпуса (<see cref="ArsenalSlotRow"/>), а геометрию ряда хранит сам ряд.
        /// </summary>
        public static ArsenalCompositionCatalog Compile(string catalogId, IReadOnlyList<WeaponInfo> weapons,
            IReadOnlyList<ArsenalPresentationStyle> styles, IEnumerable<ArsenalDecorationDescriptor> decorations=null)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
                throw new InvalidOperationException("Compiler требует idle EditMode.");
            var geometry=weapons.Distinct().OrderBy(w=>w.WeaponId,StringComparer.Ordinal).Select(w=>new ArsenalWeaponGeometry
            { Weapon=w,WeaponId=w.WeaponId,SourceGuid=GuidFor(w),Item=Item(w.WeaponPrefab),Magazine=Item(w.MagazinePrefab) }).ToArray();
            var supports=styles.Where(s=>s!=null&&s.SupportModule!=null).Select(s=>s.SupportModule).Distinct()
                .OrderBy(GuidFor,StringComparer.Ordinal).Select(m=>new ArsenalSupportGeometry
                {Module=m,SourceGuid=GuidFor(m),SourceFingerprint=Fingerprint(m),LocalBounds=VisualBounds(m.transform,false)}).ToArray();
            var materials=styles.Where(s=>s!=null&&s.ReturnReadyMaterial!=null).Select(s=>s.ReturnReadyMaterial).Distinct()
                .OrderBy(GuidFor,StringComparer.Ordinal).Select(m=>new ArsenalMaterialResource
                {Material=m,SourceGuid=GuidFor(m),SourceFingerprint=Fingerprint(m)}).ToArray();
            return ArsenalCompositionCatalog.CreateCompiled(catalogId,decorations??Array.Empty<ArsenalDecorationDescriptor>(),geometry,supports,materials);
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
        private static bool Active(Transform item,Transform root){for(var t=item;t!=null;t=t.parent){if(!t.gameObject.activeSelf)return false;if(t==root)return true;}return false;}
        private static string GuidFor(UnityEngine.Object asset){var path=AssetDatabase.GetAssetPath(asset);if(string.IsNullOrEmpty(path))throw new InvalidOperationException("Resource не native asset.");return AssetDatabase.AssetPathToGUID(path);}
        private static string Fingerprint(UnityEngine.Object asset)=>AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(asset)).ToString();
    }
}
