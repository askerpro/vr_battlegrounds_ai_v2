using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>Один pure layout/selection. Не создаёт objects, runtime identities или игровой допуск.</summary>
    public static class ArsenalStationResolver
    {
        public static ArsenalStationDescription ResolveDescription(ArsenalStationBuildInput input)
        {
            if(input==null)throw new ArgumentNullException(nameof(input));
            var failures=new List<ArsenalCompositionFailure>(input.Failures); var slots=new List<ArsenalSlotManifest>();
            var diagnostics=new List<string>(); float peg=0,shelf=0;
            Action<ArsenalCompositionFailureKind,string,string> fail=(kind,key,detail)=>failures.Add(new ArsenalCompositionFailure(kind,input.StationKey,key,detail));
            var templates=input.Templates.GroupBy(t=>t.Zone).ToArray();
            if(string.IsNullOrWhiteSpace(input.CatalogId)||input.CompilerVersion!=ArsenalCompositionCatalog.CurrentCompilerVersion)
                fail(ArsenalCompositionFailureKind.InvalidCatalog,null,"CatalogId/compiler version.");
            try { UnitScale(input.Placement.FrameScale); }
            catch(Exception ex){fail(ArsenalCompositionFailureKind.InvalidScale,null,ex.Message);}
            try { ArsenalPresentationResolver.ValidatePose(input.Placement.StationPose); ValidateBounds(input.Placement.AvailableWorldBounds); }
            catch(Exception ex){fail(ArsenalCompositionFailureKind.InvalidFrame,null,ex.Message);}
            foreach(var zone in new[]{ArsenalPresentationZone.Pegboard,ArsenalPresentationZone.Shelf})
            {
                var entries=input.Entries.Where(e=>e.Zone==zone).ToArray(); if(entries.Length==0)continue;
                var group=templates.SingleOrDefault(g=>g.Key==zone);
                if(group==null||group.Count()!=1){fail(ArsenalCompositionFailureKind.InvalidTemplate,null,"Нет однозначного template зоны "+zone);continue;}
                var template=group.Single();
                try { ValidateTemplate(template); }
                catch(Exception ex){fail(ArsenalCompositionFailureKind.InvalidTemplate,null,ex.Message);continue;}
                float width=entries.Length*template.PanelWidth+(entries.Length-1)*template.Gap;
                if(zone==ArsenalPresentationZone.Pegboard)peg=width;else shelf=width;
                for(int i=0;i<entries.Length;i++)
                {
                    var entry=entries[i]; float x=(i-(entries.Length-1)*.5f)*(template.PanelWidth+template.Gap);
                    var open=Compose(template.OpenRow,new ArsenalPresentationPose(new Vector3(x,0,0),Quaternion.identity));
                    var closed=Compose(template.ClosedRow,new ArsenalPresentationPose(new Vector3(x,0,0),Quaternion.identity));
                    try { slots.Add(new ArsenalSlotManifest(entry,template,i,open,closed,Occupied(entry,template,input))); }
                    catch(Exception ex){fail(ArsenalCompositionFailureKind.MissingMetadata,entry.LogicalSlotKey,ex.Message);}
                }
            }
            slots.Sort((a,b)=>a.Entry.NetworkIndex.CompareTo(b.Entry.NetworkIndex));
            Bounds structural=default,openBounds=default,closedBounds=default,sweptBounds=default;
            bool hasStructure=false,hasOpen=false,hasClosed=false,hasSwept=false;
            foreach(var slot in slots)
            {
                Include(ref structural,ref hasStructure,TransformBounds(slot.Template.Surface.ProjectedMeshBounds,slot.OpenPose));
                foreach(var structure in slot.Template.Structure)Include(ref structural,ref hasStructure,TransformBounds(structure.SlotLocalBounds,slot.OpenPose));
                Include(ref openBounds,ref hasOpen,TransformBounds(slot.SlotOccupiedBounds,slot.OpenPose));
                Include(ref closedBounds,ref hasClosed,TransformBounds(slot.SlotOccupiedBounds,slot.ClosedPose));
                Include(ref sweptBounds,ref hasSwept,SweptBounds(slot.SlotOccupiedBounds,slot.OpenPose,slot.ClosedPose));
            }
            var selected=Select(input,slots,openBounds,closedBounds,sweptBounds,structural,diagnostics,fail);
            Bounds world=default; bool hasWorld=false;
            if(slots.Count!=0)
            {
                var frame=Compose(input.Placement.StationPose,selected.SlotFrame);
                Include(ref world,ref hasWorld,TransformBounds(sweptBounds,frame));
                Include(ref world,ref hasWorld,TransformBounds(structural,frame));
            }
            if(selected.Decoration!=null)
                Include(ref world,ref hasWorld,TransformBounds(selected.Decoration.ArtworkBounds,input.Placement.StationPose));
            if(hasWorld&&!Contains(input.Placement.AvailableWorldBounds,world))
                fail(ArsenalCompositionFailureKind.PlacementOverflow,null,"Итоговый swept/artwork envelope вне available placement; fallback не исправляет это.");
            return new ArsenalStationDescription(input,failures,slots,selected,peg,shelf,structural,openBounds,closedBounds,
                sweptBounds,world,LayoutHash(input,slots,selected,world),RoleHash(slots));
        }
        private static void ValidateTemplate(ArsenalFunctionalSlotTemplate t)
        {
            if(t.Prefab==null||string.IsNullOrWhiteSpace(t.TemplateId)||string.IsNullOrWhiteSpace(t.SourceGuid)||string.IsNullOrWhiteSpace(t.SourceFingerprint))
                throw new InvalidOperationException("Template provenance отсутствует.");
            if(!Finite(t.PanelWidth)||!Finite(t.Gap)||t.PanelWidth<=0||t.Gap<0)throw new InvalidOperationException("Template width/gap.");
            ArsenalPresentationResolver.ValidatePose(t.OpenRow); ArsenalPresentationResolver.ValidatePose(t.ClosedRow);
            var surface=t.Surface;
            if(surface.RoleKey!="surface/panel"||string.IsNullOrWhiteSpace(surface.SourceComponentId)||string.IsNullOrWhiteSpace(surface.SourcePath))
                throw new InvalidOperationException("Нет explicit source surface role.");
            if(string.IsNullOrWhiteSpace(surface.ProjectionProfileGuid)||string.IsNullOrWhiteSpace(surface.ProjectionProfileFingerprint)||string.IsNullOrWhiteSpace(surface.ProjectionSurfaceComponentId))
                throw new InvalidOperationException("Нет native measured projection provenance.");
            ArsenalPresentationResolver.ValidatePose(surface.SourcePose); ArsenalPresentationResolver.ValidatePose(surface.ProjectedPose);
            Positive(surface.SourceScale); Positive(surface.ProjectedScale); ValidateBounds(surface.SourceMeshBounds);
            ValidateBounds(surface.ProjectedMeshBounds); ValidateBounds(surface.ProjectedColliderBounds);
            if(Mathf.Abs(surface.ProjectedMeshBounds.size.x-t.PanelWidth)>.0001f)throw new InvalidOperationException("Panel projected width не совпадает с profile.");
            var roles=new HashSet<string>(StringComparer.Ordinal);var ids=new HashSet<Guid>();
            foreach(var role in t.Roles)
            { Guid uid;if(string.IsNullOrWhiteSpace(role.RoleKey)||!roles.Add(role.RoleKey)||!Guid.TryParse(role.SourceUniqueId,out uid)||uid==Guid.Empty||!ids.Add(uid)||string.IsNullOrWhiteSpace(role.SourceComponentId))
                    throw new InvalidOperationException("Повтор/пустота SDK role/source UID."); }
            if(roles.Count==0)throw new InvalidOperationException("Нет SDK role metadata.");
            foreach(var item in t.Structure)ValidateBounds(item.SlotLocalBounds);
        }
        private static Bounds Occupied(ArsenalFrozenEntry entry, ArsenalFunctionalSlotTemplate template, ArsenalStationBuildInput input)
        {
            Bounds bounds=template.Surface.ProjectedColliderBounds; bool has=true;
            foreach(var structure in template.Structure)Include(ref bounds,ref has,structure.SlotLocalBounds);
            var geometry=entry.Geometry; var p=entry.Presentation;
            Include(ref bounds,ref has,ItemBounds(geometry.Item,p.ItemTarget,true));
            if(geometry.Magazine.Present)Include(ref bounds,ref has,ItemBounds(geometry.Magazine,p.MagazineTarget,false));
            Include(ref bounds,ref has,TransformBounds(new Bounds(Vector3.zero,new Vector3(p.CardSize.x,p.CardSize.y,.003f)),p.CardTarget));
            if(p.Supports.Count>0)
            {
                var resource=input.Supports.Where(s=>s.Module==p.SupportModule).ToArray();
                if(resource.Length!=1)throw new InvalidOperationException("Support geometry отсутствует.");
                ValidateBounds(resource[0].LocalBounds);
                foreach(var support in p.Supports)Include(ref bounds,ref has,TransformBounds(resource[0].LocalBounds,support.SlotPose));
            }
            return bounds;
        }
        private static Bounds ItemBounds(ArsenalItemGeometry item, ArsenalPresentationPose target, bool required)
        {
            if(!item.Present)throw new InvalidOperationException(required?"Item geometry отсутствует.":"Magazine geometry отсутствует.");
            if(string.IsNullOrWhiteSpace(item.SourceGuid)||string.IsNullOrWhiteSpace(item.SourceFingerprint))throw new InvalidOperationException("Item provenance отсутствует.");
            Positive(item.PhysicalScale); ArsenalPresentationResolver.ValidateScale(item.PhysicalScale,"source physical item");
            ValidateBounds(item.PhysicalRootBounds); ArsenalPresentationResolver.ValidateVector(item.DropOffsetPhysical);
            if(!Finite(item.DropRotation.x)||!Finite(item.DropRotation.y)||!Finite(item.DropRotation.z)||!Finite(item.DropRotation.w)||Mathf.Abs(1-Quaternion.Dot(item.DropRotation,item.DropRotation))>.0001f)
                throw new InvalidOperationException("Drop rotation metadata invalid.");
            var rotation=target.Rotation*Quaternion.Inverse(item.DropRotation);
            return TransformBounds(item.PhysicalRootBounds,new ArsenalPresentationPose(target.Position-rotation*item.DropOffsetPhysical,rotation));
        }
        private static ArsenalDecorationSelection Select(ArsenalStationBuildInput input,List<ArsenalSlotManifest> slots,
            Bounds open,Bounds closed,Bounds swept,Bounds structure,List<string> diagnostics,
            Action<ArsenalCompositionFailureKind,string,string> fail)
        {
            var valid=new List<ArsenalDecorationDescriptor>();var ids=new HashSet<string>(StringComparer.Ordinal);int universal=0;
            foreach(var d in input.Decorations)
            {
                if(string.IsNullOrWhiteSpace(d.DecorationId)||!ids.Add(d.DecorationId))
                { fail(ArsenalCompositionFailureKind.InvalidCatalog,null,"Повтор/пустота DecorationId.");continue; }
                try { UnitScale(d.FrameScale); ArsenalPresentationResolver.ValidatePose(d.SlotFrame); ValidateBounds(d.ArtworkBounds);
                    if(d.Prefab==null||string.IsNullOrWhiteSpace(d.SourceFingerprint)||d.PegCapacity<0||d.ShelfCapacity<0)throw new InvalidOperationException("Artwork metadata.");
                    if(!d.DefaultUniversal)ValidateBounds(d.UsableBounds);
                    if(d.DefaultUniversal)universal++; valid.Add(d); }
                catch(Exception ex){diagnostics.Add("DecorationInvalid:"+d.DecorationId+":"+ex.Message);}
            }
            if(universal>1)fail(ArsenalCompositionFailureKind.InvalidCatalog,null,"DefaultUniversal должен быть единственным.");
            int peg=slots.Count(s=>s.Entry.Zone==ArsenalPresentationZone.Pegboard),shelf=slots.Count-peg;
            Func<ArsenalDecorationDescriptor,bool> fits=d=>!d.DefaultUniversal&&d.PegCapacity>=peg&&d.ShelfCapacity>=shelf&&
                (slots.Count==0||Contains(d.UsableBounds,TransformBounds(open,d.SlotFrame))&&Contains(d.UsableBounds,TransformBounds(closed,d.SlotFrame))&&
                Contains(d.UsableBounds,TransformBounds(swept,d.SlotFrame))&&Contains(d.UsableBounds,TransformBounds(structure,d.SlotFrame)));
            var set=valid.Where(d=>d.VisualSetId==input.Visual.VisualSetId).ToArray();
            if(!string.IsNullOrEmpty(input.Visual.DecorationId))
            {
                var requested=set.SingleOrDefault(d=>d.DecorationId==input.Visual.DecorationId);
                if(requested!=null&&fits(requested))return new ArsenalDecorationSelection(ArsenalDecorationFallback.Sized,requested,diagnostics);
                if(requested!=null&&requested.DefaultUniversal)return new ArsenalDecorationSelection(ArsenalDecorationFallback.Universal,requested,diagnostics);
                diagnostics.Add(requested==null?"ExplicitDecorationMissing":"ExplicitDecorationTooSmall");
            }
            var winner=set.Where(fits).OrderBy(d=>Volume(d.UsableBounds)).ThenBy(d=>d.PegCapacity+d.ShelfCapacity)
                .ThenBy(d=>d.PegCapacity).ThenBy(d=>d.ShelfCapacity).ThenBy(d=>d.DecorationId,StringComparer.Ordinal).FirstOrDefault();
            if(winner!=null)return new ArsenalDecorationSelection(ArsenalDecorationFallback.Sized,winner,diagnostics);
            winner=valid.FirstOrDefault(d=>d.DefaultUniversal);
            return new ArsenalDecorationSelection(winner!=null?ArsenalDecorationFallback.Universal:ArsenalDecorationFallback.Bare,winner,diagnostics);
        }
        public static ArsenalPresentationPose Compose(ArsenalPresentationPose parent,ArsenalPresentationPose child) =>
            new ArsenalPresentationPose(parent.Position+parent.Rotation*child.Position,parent.Rotation*child.Rotation);
        public static Bounds TransformBounds(Bounds bounds,ArsenalPresentationPose pose)
        {
            var e=bounds.extents;var x=pose.Rotation*new Vector3(e.x,0,0);var y=pose.Rotation*new Vector3(0,e.y,0);var z=pose.Rotation*new Vector3(0,0,e.z);
            return new Bounds(pose.Position+pose.Rotation*bounds.center,(Abs(x)+Abs(y)+Abs(z))*2);
        }
        private static Bounds SweptBounds(Bounds bounds,ArsenalPresentationPose open,ArsenalPresentationPose closed)
        {
            var a=TransformBounds(bounds,open);var b=TransformBounds(bounds,closed);
            if(Quaternion.Angle(open.Rotation,closed.Rotation)<.0001f){a.Encapsulate(b);return a;}
            // Любой промежуточный rotation внутри сферы относительно moving root; центр идёт по linear interpolation.
            float radius=(Abs(bounds.center)+bounds.extents).magnitude;
            return new Bounds((open.Position+closed.Position)*.5f,Abs(open.Position-closed.Position)+Vector3.one*(radius*2));
        }
        private static void Include(ref Bounds bounds,ref bool has,Bounds add){if(!has){bounds=add;has=true;}else bounds.Encapsulate(add);}
        private static bool Contains(Bounds outer,Bounds inner)=>outer.min.x<=inner.min.x+.00001f&&outer.min.y<=inner.min.y+.00001f&&outer.min.z<=inner.min.z+.00001f&&outer.max.x>=inner.max.x-.00001f&&outer.max.y>=inner.max.y-.00001f&&outer.max.z>=inner.max.z-.00001f;
        private static Vector3 Abs(Vector3 value)=>new Vector3(Mathf.Abs(value.x),Mathf.Abs(value.y),Mathf.Abs(value.z));
        private static float Volume(Bounds b)=>b.size.x*b.size.y*b.size.z;
        private static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        private static void Positive(Vector3 v){ArsenalPresentationResolver.ValidateVector(v);if(v.x<=0||v.y<=0||v.z<=0)throw new InvalidOperationException("Scale должен быть положительным.");}
        private static void UnitScale(Vector3 v){Positive(v);if((v-Vector3.one).sqrMagnitude>1e-10f)throw new InvalidOperationException("Functional frame должен быть unit scale.");}
        private static void ValidateBounds(Bounds b){ArsenalPresentationResolver.ValidateVector(b.center);ArsenalPresentationResolver.ValidateVector(b.size);if(b.size.x<0||b.size.y<0||b.size.z<0)throw new InvalidOperationException("Bounds отрицательны.");}
        private static string RoleHash(IEnumerable<ArsenalSlotManifest> slots)
        {
            using(var hash=new CanonicalHash())
            { hash.Add(ArsenalCompositionCatalog.CurrentIdentitySchemaVersion);foreach(var slot in slots.OrderBy(s=>s.Entry.LogicalSlotKey,StringComparer.Ordinal))
                {hash.Add(slot.Entry.LogicalSlotKey);foreach(var role in slot.Template.Roles.OrderBy(r=>r.RoleKey,StringComparer.Ordinal)){hash.Add(role.RoleKey);hash.Add(role.SourceUniqueId);}}
                return hash.Finish(); }
        }
        private static string LayoutHash(ArsenalStationBuildInput input,IEnumerable<ArsenalSlotManifest> slots,ArsenalDecorationSelection selection,Bounds world)
        {
            using(var hash=new CanonicalHash())
            {
                hash.Add(input.StationKey);hash.Add(input.PresetId);hash.Add(input.CatalogId);hash.Add(input.CompilerVersion);hash.Add(ArsenalCompositionCatalog.CurrentLayoutVersion);
                hash.Add((int)selection.Kind);hash.Add(selection.DecorationId);hash.Add(selection.SlotFrame);hash.Add(input.Placement.StationPose);hash.Add(world);
                if(selection.Decoration!=null)hash.Add(selection.Decoration.SourceFingerprint);
                foreach(var slot in slots)
                {
                    var e=slot.Entry;var p=e.Presentation;hash.Add(e.NetworkIndex);hash.Add(e.LogicalSlotKey);hash.Add((int)e.Zone);hash.Add(slot.OpenPose);hash.Add(slot.ClosedPose);
                    hash.Add(slot.Template.SourceFingerprint);hash.Add(slot.Template.PanelWidth);hash.Add(slot.Template.Gap);
                    var surface=slot.Template.Surface;hash.Add(surface.SourceComponentId);hash.Add(surface.SourcePose);hash.Add(surface.SourceScale);hash.Add(surface.SourceMeshBounds);hash.Add(surface.SourceColliderBounds);
                    hash.Add(surface.ProjectionProfileGuid);hash.Add(surface.ProjectionProfileFingerprint);hash.Add(surface.ProjectionSurfaceComponentId);
                    hash.Add(surface.ProjectedPose);hash.Add(surface.ProjectedScale);hash.Add(surface.ProjectedMeshBounds);hash.Add(surface.ProjectedColliderBounds);
                    foreach(var role in slot.Template.Roles){hash.Add(role.RoleKey);hash.Add(role.SourceUniqueId);hash.Add(role.SourceComponentId);hash.Add(role.ComponentType);}
                    foreach(var g in slot.Template.Structure){hash.Add(g.SourceComponentId);hash.Add(g.ComponentType);hash.Add(g.SlotLocalBounds);}
                    hash.Add(p.ItemTarget);hash.Add(p.MagazineTarget);hash.Add(p.CardTarget);hash.Add(p.CardSize.x);hash.Add(p.CardSize.y);hash.Add(p.CardFontSize);
                    hash.Add(p.ModuleSourceGuid);hash.Add(p.ReadyMaterialSourceGuid);hash.Add(p.ModuleFingerprint);hash.Add(p.ReadyMaterialFingerprint);hash.Add(p.SupportLocalBounds);foreach(var s in p.Supports){hash.Add(s.Role);hash.Add((int)s.AnchorKind);hash.Add(s.SlotPose);}
                    hash.Add(e.Geometry.SourceGuid);hash.Add(e.Geometry.Item.SourceGuid);hash.Add(e.Geometry.Item.SourceFingerprint);hash.Add(e.Geometry.Item.PhysicalScale);hash.Add(e.Geometry.Item.DropOffsetPhysical);hash.Add(e.Geometry.Item.DropRotation);hash.Add(e.Geometry.Item.PhysicalRootBounds);
                    hash.Add(e.Geometry.Magazine.Present?1:0);hash.Add(e.Geometry.Magazine.SourceGuid);hash.Add(e.Geometry.Magazine.SourceFingerprint);hash.Add(e.Geometry.Magazine.PhysicalScale);hash.Add(e.Geometry.Magazine.DropOffsetPhysical);hash.Add(e.Geometry.Magazine.DropRotation);hash.Add(e.Geometry.Magazine.PhysicalRootBounds);
                    hash.Add(e.Card.DisplayName);hash.Add(e.Card.Price);hash.Add(e.Card.HasBalance?1:0);hash.Add(e.Card.Damage);hash.Add(e.Card.Pellets);hash.Add(e.Card.MagazineSize);hash.Add(e.Card.Rpm);hash.Add(e.Card.FullAuto?1:0);hash.Add((int)e.Card.Category);
                }
                return hash.Finish();
            }
        }
        /// <summary>Length-prefixed UTF8 и binary floats; locale/InstanceID/enumeration order не являются hash input.</summary>
        private sealed class CanonicalHash:IDisposable
        {
            private readonly MemoryStream _stream=new MemoryStream();private readonly BinaryWriter _writer;
            public CanonicalHash(){_writer=new BinaryWriter(_stream,Encoding.UTF8,true);}
            public void Add(string value){var data=Encoding.UTF8.GetBytes(value??"");_writer.Write(data.Length);_writer.Write(data);}
            public void Add(int value)=>_writer.Write(value);
            public void Add(float value)=>_writer.Write(value==0?0:value);
            public void Add(Vector3 value){Add(value.x);Add(value.y);Add(value.z);}
            public void Add(Quaternion value){Add(value.x);Add(value.y);Add(value.z);Add(value.w);}
            public void Add(ArsenalPresentationPose pose){Add(pose.Position);Add(pose.EulerAngles);}
            public void Add(Bounds bounds){Add(bounds.center);Add(bounds.size);}
            public string Finish(){_writer.Flush();using(var algorithm=SHA256.Create())return BitConverter.ToString(algorithm.ComputeHash(_stream.ToArray())).Replace("-","").ToLowerInvariant();}
            public void Dispose(){_writer.Dispose();_stream.Dispose();}
        }
    }
}
