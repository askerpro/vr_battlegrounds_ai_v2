using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Отпечаток числового запроса. Живые Unity-коллайдеры не заменяет снимком физики.</summary>
    public static class PositionImpactFingerprint
    {
        public static string Compute(MapGrid grid, PositionImpactLayout layout)
        {
            if (grid == null || layout == null) return null;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1); writer.Write(layout.version); Text(writer, layout.map);
                Text(writer, layout.sceneGuid); Text(writer, layout.bodyProfileId);
                writer.Write(layout.bodyProfileVersion); writer.Write(layout.bodyProfileCalibrated);
                writer.Write((int)layout.profile); writer.Write(layout.radius); writer.Write(layout.speed);
                Array(writer, layout.positions, p => {
                    Text(writer, p.id); V(writer, p.min); V(writer, p.max); Text(writer, p.protectedState);
                    Array(writer, p.states, s => {
                        Text(writer, s.id); V(writer, s.center); writer.Write(s.yaw);
                        V(writer, s.eyeOffset); V(writer, s.muzzleOffset);
                        Array(writer, s.body, b => { V(writer, b.offset); writer.Write(b.weight); });
                    });
                });
                Array(writer, layout.routes, r => {
                    Text(writer, r.id); Text(writer, r.from); Text(writer, r.to);
                    Text(writer, r.fromState); Text(writer, r.toState); writer.Write(r.requireDirect);
                    Array(writer, r.via, v => V(writer, v));
                    Array(writer, r.allowedCellIndices, i => writer.Write(i));
                });
                writer.Write(grid.Width); writer.Write(grid.Depth); writer.Write(grid.Cell); V(writer, grid.Origin);
                Array(writer, grid.Blocked, v => writer.Write(v)); Array(writer, grid.Height, v => writer.Write(v));
                Array(writer, grid.Owner, v => writer.Write(v)); Array(writer, grid.Zone, v => writer.Write(v));
                writer.Write(grid.Obstacles.Count);
                foreach (string name in grid.Obstacles) Text(writer, name);
                foreach (var cover in grid.ObstacleCover) writer.Write((int)cover);
                foreach (bool mixed in grid.ObstacleMixedCover) writer.Write(mixed);
                // Проверяется вместе с ReferenceEquals(grid): делегаты относятся к этому экземпляру сетки.
                writer.Write(grid.LineOfSight?.GetHashCode() ?? 0); writer.Write(grid.ShotLine?.GetHashCode() ?? 0);
                writer.Flush();
                using (var hash = SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "");
            }
        }

        private static void Text(BinaryWriter writer, string value)
        { writer.Write(value != null); if (value != null) writer.Write(value); }
        private static void Array<T>(BinaryWriter writer, T[] values, Action<T> write)
        {
            writer.Write(values == null ? -1 : values.Length);
            if (values == null) return;
            foreach (T value in values)
            { writer.Write(value != null); if (value != null) write(value); }
        }
        private static void V(BinaryWriter w, Vector2 v) { w.Write(v.x); w.Write(v.y); }
        private static void V(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
    }
}
