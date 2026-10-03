using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.LevelDesign
{
    public enum BlockoutGeometryMode { LegacyCellUnion = 0, ThinStraight = 1 }

    /// <summary>Фактическая занятая призма; AABB пригоден только для предварительного отбора пересечений.</summary>
    public struct BlockoutSolidPart
    {
        public Vector3 center, size;
        public Quaternion rotation;
        public Bounds BroadphaseBounds
        {
            get
            {
                Vector3 x=rotation*new Vector3(size.x,0,0),y=rotation*new Vector3(0,size.y,0),z=rotation*new Vector3(0,0,size.z);
                return new Bounds(center,new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),
                    Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z)));
            }
        }
    }

    /// <summary>Единая клеточная стена: сериализованная форма, общий меш и один коллайдер.</summary>
    [ExecuteAlways, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class BlockoutCellWall : MonoBehaviour
    {
        public float cellSize = .3f, length = 2.1f, thickness = .3f, wallHeight = 1.6f, yaw;
        public List<Vector2Int> cells = new List<Vector2Int>();
        public BlockoutOpeningSettings openings = BlockoutOpeningSettings.Default;
        // Нулевой режим сохраняет геометрию старых сериализованных стен без миграции.
        public BlockoutGeometryMode geometryMode;
        private Mesh generated;
        private string builtRecipe;
        private void OnEnable() => Rebuild();
        private void OnDestroy() { if (generated != null) { if (Application.isPlaying) Destroy(generated); else DestroyImmediate(generated); } }

        public static bool Connected(IEnumerable<Vector2Int> cells)
        {
            var set=new HashSet<Vector2Int>(cells); if(set.Count==0) return false;
            var queue=new Queue<Vector2Int>(); var seen=new HashSet<Vector2Int>();
            foreach(var first in set) {queue.Enqueue(first);seen.Add(first);break;}
            while(queue.Count>0)
            {
                var p=queue.Dequeue();
                foreach(var d in new[]{Vector2Int.up,Vector2Int.down,Vector2Int.left,Vector2Int.right})
                    if(set.Contains(p+d)&&seen.Add(p+d)) queue.Enqueue(p+d);
            }
            return seen.Count==set.Count;
        }

        public static List<Vector2Int> Raster(float length, float thickness, float yaw, float step)
        {
            var result = new List<Vector2Int>();
            Vector2 u = new Vector2(Mathf.Cos(yaw * Mathf.Deg2Rad), -Mathf.Sin(yaw * Mathf.Deg2Rad));
            Vector2 v = new Vector2(-u.y, u.x);
            Vector2 center = u * length / 2 + v * thickness / 2;
            float extent = length + thickness;
            int n = Mathf.CeilToInt(extent / step) + 1;
            for (int x = -n; x <= n; x++) for (int z = -n; z <= n; z++)
            {
                Vector2 delta = new Vector2((x + .5f) * step, (z + .5f) * step) - center;
                if (Mathf.Abs(delta.x) >= Mathf.Abs(u.x) * length / 2 + Mathf.Abs(v.x) * thickness / 2 + step / 2 - .00001f
                    || Mathf.Abs(delta.y) >= Mathf.Abs(u.y) * length / 2 + Mathf.Abs(v.y) * thickness / 2 + step / 2 - .00001f
                    || Mathf.Abs(Vector2.Dot(delta, u)) >= length / 2 + step / 2 * (Mathf.Abs(u.x) + Mathf.Abs(u.y)) - .00001f
                    || Mathf.Abs(Vector2.Dot(delta, v)) >= thickness / 2 + step / 2 * (Mathf.Abs(v.x) + Mathf.Abs(v.y)) - .00001f) continue;
                result.Add(new Vector2Int(x, z));
            }
            return result;
        }

        public void Rebuild()
        {
            if (BlockoutSectionGeometry.Owns(gameObject)) { GetComponent<BlockoutSectionGeometry>().Rebuild(); return; }
            if (cellSize <= 0 || wallHeight <= 0) return;
            string recipe=JsonUtility.ToJson(this);
            if(generated!=null&&builtRecipe==recipe&&GetComponent<MeshFilter>().sharedMesh==generated&&GetComponent<MeshCollider>().sharedMesh==generated)return;
            var occupied = new HashSet<Vector2Int>(cells);
            cells = new List<Vector2Int>(occupied);
            var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
            var grid = Partition(cells, cellSize, wallHeight, openings, geometryMode, length, thickness, yaw);
            Quaternion geometryRotation=geometryMode==BlockoutGeometryMode.ThinStraight?Quaternion.Euler(0,yaw,0):Quaternion.identity;
            for (int ix=0; ix<grid.x.Length-1; ix++) for(int iz=0; iz<grid.z.Length-1; iz++) for(int iy=0; iy<grid.y.Length-1; iy++)
            {
                if(!grid.Has(ix,iy,iz)) continue;
                float x=grid.x[ix], X=grid.x[ix+1], z=grid.z[iz], Z=grid.z[iz+1], y=grid.y[iy], Y=grid.y[iy+1];
                if(!grid.Has(ix,iy-1,iz)) Face(new Vector3(x,y,z),new Vector3(X,y,z),new Vector3(X,y,Z),new Vector3(x,y,Z));
                if(!grid.Has(ix,iy+1,iz)) Face(new Vector3(x,Y,Z),new Vector3(X,Y,Z),new Vector3(X,Y,z),new Vector3(x,Y,z));
                if(!grid.Has(ix,iy,iz-1)) Face(new Vector3(x,Y,z),new Vector3(X,Y,z),new Vector3(X,y,z),new Vector3(x,y,z));
                if(!grid.Has(ix,iy,iz+1)) Face(new Vector3(x,y,Z),new Vector3(X,y,Z),new Vector3(X,Y,Z),new Vector3(x,Y,Z));
                if(!grid.Has(ix-1,iy,iz)) Face(new Vector3(x,y,z),new Vector3(x,y,Z),new Vector3(x,Y,Z),new Vector3(x,Y,z));
                if(!grid.Has(ix+1,iy,iz)) Face(new Vector3(X,Y,z),new Vector3(X,Y,Z),new Vector3(X,y,Z),new Vector3(X,y,z));
            }
            var mesh = new Mesh { name = geometryMode==BlockoutGeometryMode.ThinStraight?"Тонкая прямая стена":"Клеточная стена", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.SetUVs(0,uv); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            GetComponent<MeshCollider>().sharedMesh = null;
            GetComponent<MeshCollider>().convex = false;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshCollider>().sharedMesh = mesh;
            if (generated != null) { if (Application.isPlaying) Destroy(generated); else DestroyImmediate(generated); }
            generated = mesh;
            builtRecipe=JsonUtility.ToJson(this);
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = vertices.Count; vertices.AddRange(new[]{geometryRotation*a,geometryRotation*b,geometryRotation*c,geometryRotation*d});
                triangles.AddRange(new[]{i,i+1,i+2,i,i+2,i+3});
                bool horizontal = Mathf.Abs(a.y-b.y)+Mathf.Abs(a.y-c.y) < .00001f;
                foreach (var p in new[]{a,b,c,d}) uv.Add(horizontal ? new Vector2(p.x,p.z)/1.8f : new Vector2(p.x+p.z,p.y)/1.8f);
            }
        }

        /// <summary>Угол формы включает сохранённый локальный угол старых стен и поворот целого корня.</summary>
        public float RotationYaw => Mathf.Repeat(yaw + transform.eulerAngles.y, 360f);
        /// <summary>Точные локальные объёмы меша; поворот корня не меняет authored cells или топологию.</summary>
        public IEnumerable<BlockoutSolidPart> LocalSolidParts() => BlockoutSectionGeometry.Owns(gameObject)
            ? GetComponent<BlockoutSectionGeometry>().LocalSolidParts()
            : SolidParts(cells,Vector3.zero,cellSize,wallHeight,openings,geometryMode,length,thickness,yaw);
        public IEnumerable<BlockoutSolidPart> SolidParts() => PartsAt(transform.position, transform.rotation);
        public IEnumerable<BlockoutSolidPart> PartsAt(Vector3 position, Quaternion rotation)
            => TransformParts(LocalSolidParts(), position, rotation);
        public static IEnumerable<BlockoutSolidPart> TransformParts(IEnumerable<BlockoutSolidPart> parts, Vector3 position, Quaternion rotation)
            => parts.Select(part => new BlockoutSolidPart { center=position+rotation*part.center, size=part.size, rotation=rotation*part.rotation });
        /// <summary>В режиме ThinStraight это только broadphase AABB, не фактический занятый объём.</summary>
        public IEnumerable<Bounds> LocalSolidBounds() => LocalSolidParts().Select(part=>part.BroadphaseBounds);
        public IEnumerable<Bounds> SolidVolumes() => SolidParts().Select(part=>part.BroadphaseBounds);
        public static IEnumerable<Bounds> SolidVolumes(IEnumerable<Vector2Int> cells, Vector3 origin, float step, float height, BlockoutOpeningSettings openings)
            => SolidVolumes(cells,origin,step,height,openings,BlockoutGeometryMode.LegacyCellUnion,0,0,0);
        public static IEnumerable<Bounds> SolidVolumes(IEnumerable<Vector2Int> cells, Vector3 origin, float step, float height, BlockoutOpeningSettings openings,
            BlockoutGeometryMode mode,float length,float thickness,float yaw)
            => SolidParts(cells,origin,step,height,openings,mode,length,thickness,yaw).Select(part=>part.BroadphaseBounds);
        public static IEnumerable<BlockoutSolidPart> SolidParts(IEnumerable<Vector2Int> cells,Vector3 origin,float step,float height,BlockoutOpeningSettings openings,
            BlockoutGeometryMode mode,float length,float thickness,float yaw)
        {
            var grid=Partition(cells,step,height,openings,mode,length,thickness,yaw);
            Quaternion rotation=mode==BlockoutGeometryMode.ThinStraight?Quaternion.Euler(0,yaw,0):Quaternion.identity;
            for(int x=0;x<grid.x.Length-1;x++) for(int z=0;z<grid.z.Length-1;z++) for(int y=0;y<grid.y.Length-1;y++)
                if(grid.Has(x,y,z))
                {
                    Vector3 lo=new Vector3(grid.x[x],grid.y[y],grid.z[z]), hi=new Vector3(grid.x[x+1],grid.y[y+1],grid.z[z+1]);
                    yield return new BlockoutSolidPart {center=origin+rotation*((lo+hi)/2),size=hi-lo,rotation=rotation};
            }
        }
        public static bool HasEffectiveOpenings(IEnumerable<Vector2Int> cells,float step,float height,BlockoutOpeningSettings openings)
            => HasEffectiveOpenings(cells,step,height,openings,BlockoutGeometryMode.LegacyCellUnion,0,0,0);
        public static bool HasEffectiveOpenings(IEnumerable<Vector2Int> cells,float step,float height,BlockoutOpeningSettings openings,
            BlockoutGeometryMode mode,float length,float thickness,float yaw)
        {
            if(!openings.enabled) return false;
            var grid=Partition(cells,step,height,openings,mode,length,thickness,yaw);
            if(grid.y.Length!=4) return false;
            for(int x=0;x<grid.x.Length-1;x++) for(int z=0;z<grid.z.Length-1;z++)
                if(grid.Has(x,0,z)&&!grid.Has(x,1,z)) return true;
            return false;
        }
        private sealed class SolidGrid
        {
            public float[] x,z,y;
            public bool[,,] occupied;
            public bool Has(int ix,int iy,int iz) => ix>=0&&iy>=0&&iz>=0&&ix<x.Length-1&&iy<y.Length-1&&iz<z.Length-1&&occupied[ix,iy,iz];
        }
        private static SolidGrid Partition(IEnumerable<Vector2Int> cells,float step,float height,BlockoutOpeningSettings slots,
            BlockoutGeometryMode mode,float length,float thickness,float yaw)
        {
            if(mode==BlockoutGeometryMode.LegacyCellUnion) return Partition(cells,step,height,slots);
            if(mode!=BlockoutGeometryMode.ThinStraight||float.IsNaN(yaw)||float.IsInfinity(yaw)) return EmptyGrid();
            return StraightPartition(length,thickness,height,slots);
        }
        private static SolidGrid EmptyGrid() => new SolidGrid {x=new float[0],z=new float[0],y=new float[0],occupied=new bool[0,0,0]};
        private static bool PositiveFinite(float value) => value>0&&!float.IsNaN(value)&&!float.IsInfinity(value);
        private static bool ValidSlots(BlockoutOpeningSettings slots,float height) => slots.enabled&&PositiveFinite(slots.spacing)&&slots.spacing>.01f
            &&PositiveFinite(slots.width)&&slots.width>.001f&&slots.width<slots.spacing&&PositiveFinite(slots.sillHeight)&&PositiveFinite(slots.lintelHeight)
            &&slots.sillHeight+slots.lintelHeight<height;
        private static SolidGrid StraightPartition(float length,float thickness,float height,BlockoutOpeningSettings slots)
        {
            if(!PositiveFinite(length)||!PositiveFinite(thickness)||!PositiveFinite(height)) return EmptyGrid();
            var xs=new SortedSet<float> {0,length};var gaps=new List<Vector2>();bool valid=ValidSlots(slots,height);
            if(valid)
            {
                // Щели направлены вдоль оси стены, а поперечная толщина не зависит от yaw или клеточной маски.
                for(float center=slots.spacing*.5f;center+slots.width*.5f<length-.001f;center+=slots.spacing)
                {
                    float a=center-slots.width*.5f,b=center+slots.width*.5f;
                    if(a<=.001f) continue;
                    xs.Add(a);xs.Add(b);gaps.Add(new Vector2(a,b));
                }
            }
            var grid=new SolidGrid {x=xs.ToArray(),z=new[]{0f,thickness},y=valid?new[]{0f,slots.sillHeight,height-slots.lintelHeight,height}:new[]{0f,height}};
            grid.occupied=new bool[grid.x.Length-1,grid.y.Length-1,1];
            for(int x=0;x<grid.x.Length-1;x++)
            {
                float center=(grid.x[x]+grid.x[x+1])/2;bool gap=gaps.Any(g=>center>g.x&&center<g.y);
                for(int y=0;y<grid.y.Length-1;y++) grid.occupied[x,y,0]=!valid||!gap||y!=1;
            }
            return grid;
        }
        private static SolidGrid Partition(IEnumerable<Vector2Int> source,float step,float height,BlockoutOpeningSettings slots)
        {
            var cells=new HashSet<Vector2Int>(source);
            var grid=EmptyGrid();
            if(cells.Count==0||step<=0||height<=0) return grid;
            var xs=new SortedSet<float>();var zs=new SortedSet<float>();
            foreach(var c in cells) {xs.Add(c.x*step);xs.Add((c.x+1)*step);zs.Add(c.y*step);zs.Add((c.y+1)*step);}
            bool alongX=xs.Max-xs.Min>=zs.Max-zs.Min;
            bool valid=ValidSlots(slots,height);
            var gaps=new List<Vector2>();
            if(valid)
            {
                var axis=alongX?xs:zs;float lo=axis.Min,hi=axis.Max;
                // Концевые стойки остаются целыми, щели проходят сквозь фактическую толщину стены.
                for(float center=lo+slots.spacing*.5f;center+slots.width*.5f<hi-.001f;center+=slots.spacing)
                {
                    float a=center-slots.width*.5f,b=center+slots.width*.5f;
                    if(a<=lo+.001f) continue;
                    axis.Add(a);axis.Add(b);gaps.Add(new Vector2(a,b));
                }
            }
            grid.x=xs.ToArray();grid.z=zs.ToArray();grid.y=valid?new[]{0f,slots.sillHeight,height-slots.lintelHeight,height}:new[]{0f,height};
            grid.occupied=new bool[grid.x.Length-1,grid.y.Length-1,grid.z.Length-1];
            for(int x=0;x<grid.x.Length-1;x++) for(int z=0;z<grid.z.Length-1;z++)
            {
                float cx=(grid.x[x]+grid.x[x+1])/2,cz=(grid.z[z]+grid.z[z+1])/2;
                if(!cells.Contains(new Vector2Int(Mathf.FloorToInt(cx/step),Mathf.FloorToInt(cz/step)))) continue;
                float coord=alongX?cx:cz;bool gap=gaps.Any(g=>coord>g.x&&coord<g.y);
                for(int y=0;y<grid.y.Length-1;y++) grid.occupied[x,y,z]=!gap||y!=1||!valid;
            }
            return grid;
        }
    }
}
