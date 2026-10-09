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
    /// Чистая раскладка станции по пресету: порядок слотов, ряд и место в ряду. Геометрии нет — где ряды и какой
    /// у них шаг, знают ряды корпуса (<see cref="ArsenalSlotRow" />). Не создаёт объекты, ID и игровой допуск.
    /// Позы оружия, магазина и карточки задаёт стиль, генератор применяет их как есть.
    /// </summary>
    public static class ArsenalStationResolver
    {
        public static ArsenalStationDescription ResolveDescription(ArsenalStationBuildInput input)
        {
            if(input==null)throw new ArgumentNullException(nameof(input));
            var failures=new List<ArsenalCompositionFailure>(input.Failures);
            // Место в ряду — порядок записей пресета внутри одного ряда.
            var rowCounters=new Dictionary<string,int>(StringComparer.Ordinal);
            var slots=new List<ArsenalSlotManifest>(input.Entries.Count);
            foreach(var entry in input.Entries.OrderBy(e=>e.NetworkIndex))
            {
                string row=entry.RowKey;
                rowCounters.TryGetValue(row,out int index);
                rowCounters[row]=index+1;
                slots.Add(new ArsenalSlotManifest(entry,index));
            }
            var selected=Select(input,slots);
            return new ArsenalStationDescription(input,failures,slots,selected,LayoutHash(input,slots,selected,rowCounters),RoleHash(slots));
        }

        /// <summary>
        /// Корпус: заказанный явно; иначе универсальный; иначе без корпуса. Корпус станции с рядами — префаб на сцене,
        /// каталожный корпус только оформляет его.
        /// </summary>
        private static ArsenalDecorationSelection Select(ArsenalStationBuildInput input,List<ArsenalSlotManifest> slots)
        {
            var diagnostics=new List<string>();
            var available=input.Decorations.Where(d=>d!=null&&d.Prefab!=null&&!string.IsNullOrWhiteSpace(d.DecorationId)).ToArray();
            var set=available.Where(d=>d.VisualSetId==input.Visual.VisualSetId).ToArray();
            if(!string.IsNullOrEmpty(input.Visual.DecorationId))
            {
                var requested=set.FirstOrDefault(d=>d.DecorationId==input.Visual.DecorationId);
                if(requested!=null)
                    return new ArsenalDecorationSelection(requested.DefaultUniversal?ArsenalDecorationFallback.Universal:ArsenalDecorationFallback.Sized,requested,diagnostics);
                diagnostics.Add("ExplicitDecorationMissing:"+input.Visual.DecorationId);
            }
            var winner=available.Where(d=>d.DefaultUniversal).OrderBy(d=>d.DecorationId,StringComparer.Ordinal).FirstOrDefault();
            return new ArsenalDecorationSelection(winner!=null?ArsenalDecorationFallback.Universal:ArsenalDecorationFallback.Bare,winner,diagnostics);
        }

        /// <summary>
        /// Логические роли слотов: схема ID, логические ключи и ключи ролей. Исходные UniqueId префаба в ID не входят
        /// (ID = база роли + seed), поэтому и в отпечаток тоже.
        /// </summary>
        private static string RoleHash(IEnumerable<ArsenalSlotManifest> slots)
        {
            using(var hash=new CanonicalHash())
            {
                hash.Add(ArsenalCompositionCatalog.CurrentIdentitySchemaVersion);
                foreach(var slot in slots.OrderBy(s=>s.Entry.LogicalSlotKey,StringComparer.Ordinal))
                {
                    hash.Add(slot.Entry.LogicalSlotKey);
                    hash.Add(ArsenalGeneratedIdentityManifest.ItemAnchorRole);
                    hash.Add(ArsenalGeneratedIdentityManifest.MagazineAnchorRole);
                }
                return hash.Finish();
            }
        }

        /// <summary>
        /// Отпечаток применённых настроек: что, в какой ряд и каким по счёту, раскладки оружия и стиль. Хешируются
        /// только входы, а не вычисленные позы: вычисления с плавающей точкой на ПК и Quest (ARM64, FMA) могут дать
        /// разные биты. Геометрия рядов в отпечаток не входит — она часть сцены, одинаковой на всех машинах.
        /// </summary>
        private static string LayoutHash(ArsenalStationBuildInput input,IEnumerable<ArsenalSlotManifest> slots,
            ArsenalDecorationSelection selection,IReadOnlyDictionary<string,int> rowCounts)
        {
            using(var hash=new CanonicalHash())
            {
                hash.Add(input.StationKey);hash.Add(input.PresetId);hash.Add(input.CatalogId);hash.Add(ArsenalCompositionCatalog.CurrentLayoutVersion);
                hash.Add((int)selection.Kind);hash.Add(selection.DecorationId);hash.Add(input.Style!=null?input.Style.name:"");
                foreach(var slot in slots)
                {
                    var e=slot.Entry;
                    hash.Add(e.NetworkIndex);hash.Add(e.LogicalSlotKey);hash.Add(e.RowKey);
                    hash.Add(slot.RowIndex);hash.Add(rowCounts[e.RowKey]);
                    // Свои раскладки оружия; умолчания слотов — часть префабов, одинаковых на всех машинах.
                    foreach(var p in e.WeaponResource.SlotLayouts.Where(l=>l!=null).OrderBy(l=>(int)l.SlotKind))
                    {
                        hash.Add(p.name);hash.Add((int)p.SlotKind);hash.Add(p.ItemTarget);hash.Add(p.MagazineTarget);hash.Add(p.CardTarget);
                        hash.Add(p.CardSize.x);hash.Add(p.CardSize.y);hash.Add(p.CardFontSize);
                        foreach(var s in p.Supports){hash.Add(s.Role);hash.Add((int)s.AnchorKind);hash.Add(s.SlotPose);}
                        hash.Add(p.PlaceZone.Center);hash.Add(p.PlaceZone.Size);hash.Add(p.PlaceZone.EulerAngles);
                    }
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
