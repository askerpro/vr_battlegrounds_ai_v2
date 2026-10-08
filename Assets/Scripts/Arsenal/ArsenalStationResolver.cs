using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    /// Чистая раскладка станции по пресету. Не создаёт объекты, ID и игровой допуск.
    /// Применяет настройки как есть: позы оружия/магазина/карточки задаёт стиль, шаг слота — шаблон зоны.
    /// Размеры оружия и «влезает ли» генератор не проверяет — это зона того, кто настраивает визуал.
    /// </summary>
    public static class ArsenalStationResolver
    {
        public static ArsenalStationDescription ResolveDescription(ArsenalStationBuildInput input)
        {
            if(input==null)throw new ArgumentNullException(nameof(input));
            var failures=new List<ArsenalCompositionFailure>(input.Failures); var slots=new List<ArsenalSlotManifest>();
            float peg=0,shelf=0;
            foreach(var zone in new[]{ArsenalPresentationZone.Pegboard,ArsenalPresentationZone.Shelf})
            {
                var entries=input.Entries.Where(e=>e.Zone==zone).ToArray(); if(entries.Length==0)continue;
                var template=input.Templates.FirstOrDefault(t=>t.Zone==zone&&t.Prefab!=null);
                if(template==null)
                {
                    failures.Add(new ArsenalCompositionFailure(ArsenalCompositionFailureKind.MissingTemplate,input.StationKey,null,"Нет шаблона слота зоны "+zone));
                    continue;
                }
                // Ряд центрируется: шаг — ширина панели шаблона плюс зазор.
                float width=entries.Length*template.PanelWidth+(entries.Length-1)*template.Gap;
                if(zone==ArsenalPresentationZone.Pegboard)peg=width;else shelf=width;
                for(int i=0;i<entries.Length;i++)
                {
                    float x=(i-(entries.Length-1)*.5f)*(template.PanelWidth+template.Gap);
                    var offset=new ArsenalPresentationPose(new Vector3(x,0,0),Quaternion.identity);
                    slots.Add(new ArsenalSlotManifest(entries[i],template,i,Compose(template.OpenRow,offset),Compose(template.ClosedRow,offset)));
                }
            }
            slots.Sort((a,b)=>a.Entry.NetworkIndex.CompareTo(b.Entry.NetworkIndex));
            var selected=Select(input,slots);
            return new ArsenalStationDescription(input,failures,slots,selected,peg,shelf,LayoutHash(input,slots,selected),RoleHash(slots));
        }

        /// <summary>
        /// Корпус: заказанный явно; иначе наименьший по числу мест, в который помещаются ряды по количеству слотов;
        /// иначе универсальный; иначе без корпуса. Выбор по количеству, не по размерам оружия.
        /// </summary>
        private static ArsenalDecorationSelection Select(ArsenalStationBuildInput input,List<ArsenalSlotManifest> slots)
        {
            var diagnostics=new List<string>();
            var available=input.Decorations.Where(d=>d!=null&&d.Prefab!=null&&!string.IsNullOrWhiteSpace(d.DecorationId)).ToArray();
            var set=available.Where(d=>d.VisualSetId==input.Visual.VisualSetId).ToArray();
            int peg=slots.Count(s=>s.Entry.Zone==ArsenalPresentationZone.Pegboard),shelf=slots.Count-peg;
            if(!string.IsNullOrEmpty(input.Visual.DecorationId))
            {
                var requested=set.FirstOrDefault(d=>d.DecorationId==input.Visual.DecorationId);
                if(requested!=null)
                    return new ArsenalDecorationSelection(requested.DefaultUniversal?ArsenalDecorationFallback.Universal:ArsenalDecorationFallback.Sized,requested,diagnostics);
                diagnostics.Add("ExplicitDecorationMissing:"+input.Visual.DecorationId);
            }
            var winner=set.Where(d=>!d.DefaultUniversal&&d.PegCapacity>=peg&&d.ShelfCapacity>=shelf)
                .OrderBy(d=>d.PegCapacity+d.ShelfCapacity).ThenBy(d=>d.PegCapacity).ThenBy(d=>d.ShelfCapacity)
                .ThenBy(d=>d.DecorationId,StringComparer.Ordinal).FirstOrDefault();
            if(winner!=null)return new ArsenalDecorationSelection(ArsenalDecorationFallback.Sized,winner,diagnostics);
            winner=available.Where(d=>d.DefaultUniversal).OrderBy(d=>d.DecorationId,StringComparer.Ordinal).FirstOrDefault();
            return new ArsenalDecorationSelection(winner!=null?ArsenalDecorationFallback.Universal:ArsenalDecorationFallback.Bare,winner,diagnostics);
        }

        public static ArsenalPresentationPose Compose(ArsenalPresentationPose parent,ArsenalPresentationPose child) =>
            new ArsenalPresentationPose(parent.Position+parent.Rotation*child.Position,parent.Rotation*child.Rotation);

        public static Bounds TransformBounds(Bounds bounds,ArsenalPresentationPose pose)
        {
            var e=bounds.extents;var x=pose.Rotation*new Vector3(e.x,0,0);var y=pose.Rotation*new Vector3(0,e.y,0);var z=pose.Rotation*new Vector3(0,0,e.z);
            return new Bounds(pose.Position+pose.Rotation*bounds.center,(Abs(x)+Abs(y)+Abs(z))*2);
        }

        private static Vector3 Abs(Vector3 value)=>new Vector3(Mathf.Abs(value.x),Mathf.Abs(value.y),Mathf.Abs(value.z));

        private static string RoleHash(IEnumerable<ArsenalSlotManifest> slots)
        {
            using(var hash=new CanonicalHash())
            { hash.Add(ArsenalCompositionCatalog.CurrentIdentitySchemaVersion);foreach(var slot in slots.OrderBy(s=>s.Entry.LogicalSlotKey,StringComparer.Ordinal))
                {hash.Add(slot.Entry.LogicalSlotKey);foreach(var role in slot.Template.Roles.OrderBy(r=>r.RoleKey,StringComparer.Ordinal)){hash.Add(role.RoleKey);hash.Add(role.SourceUniqueId);}}
                return hash.Finish(); }
        }

        /// <summary>
        /// Отпечаток применённых настроек: что и куда поставлено. Хешируются только входы — сохранённые значения
        /// шаблона и стиля, индекс в ряду и число слотов ряда, — а не вычисленные позы: вычисления с плавающей
        /// точкой на ПК и Quest (ARM64, FMA) могут дать разные биты, и сервер с клиентом ложно разошлись бы.
        /// </summary>
        private static string LayoutHash(ArsenalStationBuildInput input,IEnumerable<ArsenalSlotManifest> slots,ArsenalDecorationSelection selection)
        {
            using(var hash=new CanonicalHash())
            {
                hash.Add(input.StationKey);hash.Add(input.PresetId);hash.Add(input.CatalogId);hash.Add(ArsenalCompositionCatalog.CurrentLayoutVersion);
                hash.Add((int)selection.Kind);hash.Add(selection.DecorationId);
                var rowCounts=slots.GroupBy(s=>s.Entry.Zone).ToDictionary(g=>g.Key,g=>g.Count());
                foreach(var slot in slots)
                {
                    var e=slot.Entry;var p=e.Presentation;hash.Add(e.NetworkIndex);hash.Add(e.LogicalSlotKey);hash.Add((int)e.Zone);
                    hash.Add(slot.RowIndex);hash.Add(rowCounts[e.Zone]);
                    hash.Add(slot.Template.TemplateId);hash.Add(slot.Template.PanelWidth);hash.Add(slot.Template.Gap);hash.Add(slot.Template.OpenRow);hash.Add(slot.Template.ClosedRow);
                    foreach(var role in slot.Template.Roles){hash.Add(role.RoleKey);hash.Add(role.SourceUniqueId);}
                    hash.Add(p.ItemTarget);hash.Add(p.MagazineTarget);hash.Add(p.CardTarget);hash.Add(p.CardSize.x);hash.Add(p.CardSize.y);hash.Add(p.CardFontSize);
                    foreach(var s in p.Supports){hash.Add(s.Role);hash.Add((int)s.AnchorKind);hash.Add(s.SlotPose);}
                    hash.Add(e.Card.DisplayName);hash.Add(e.Card.Price);hash.Add((int)e.Card.Category);
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
            public void Add(ArsenalPresentationPose pose){Add(pose.Position);Add(pose.EulerAngles);}
            public string Finish(){_writer.Flush();using(var algorithm=SHA256.Create())return BitConverter.ToString(algorithm.ComputeHash(_stream.ToArray())).Replace("-","").ToLowerInvariant();}
            public void Dispose(){_writer.Dispose();_stream.Dispose();}
        }
    }
}
