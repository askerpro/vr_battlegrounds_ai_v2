using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    public struct FitTriangle
    {
        public Vector3 A, B, C;
        public string Zone, Source, FingerSegment;
        public int SourceTriangle, SourceSubMesh;
        public FitTriangle(Vector3 a, Vector3 b, Vector3 c, string zone = "surface")
        { A = a; B = b; C = c; Zone = zone; Source = ""; FingerSegment = ""; SourceTriangle = 0; SourceSubMesh = 0; }
        public float Area => Vector3.Cross(B - A, C - A).magnitude * 0.5f;
        public Vector3 Normal => Vector3.Cross(B - A, C - A).normalized;
        public Bounds Bounds { get { var b = new Bounds(A, Vector3.zero); b.Encapsulate(B); b.Encapsulate(C); return b; } }
    }

    public sealed class FitTopology
    {
        public int BoundaryEdges, NonManifoldEdges, OrientationErrors, DegenerateTriangles;
        public bool CanDetermineInside;
        public string Reason;
        internal List<FitTriangle[]> Components;
    }

    public struct FitSample { public Vector3 Position; public float Area; public int Triangle; }
    public struct FitNearestHit { public Vector3 Position, Normal; public int Triangle; }
    public sealed class FitContactPatch { public string Zone, FingerSegment; public float AreaMm2; public int SampleCount, TriangleCount; }
    public sealed class FitContactCandidate
    {
        public Vector3 Position, TargetPosition;
        public float DistanceMm, AreaMm2;
        public int Triangle;
        public string Zone, FingerSegment, TargetRegion;
        public Vector3 HandNormal, TargetNormal;
        public int TargetTriangle;
        public float NormalOppositionDot;
        public bool? Inside;
    }

    /// <summary>BVH по треугольникам; расстояния и координаты — метры.</summary>
    public sealed class FitSurface
    {
        public readonly FitTriangle[] Triangles;
        readonly int[] _order;
        readonly Node _root;
        sealed class Node { public Bounds Bounds; public int Start, Count; public Node Left, Right; }

        public FitSurface(FitTriangle[] triangles)
        {
            if (triangles == null || triangles.Length == 0) throw new ArgumentException("Нет треугольников поверхности.");
            Triangles = triangles; _order = Enumerable.Range(0, triangles.Length).ToArray();
            _root = Build(0, _order.Length);
        }

        Node Build(int start, int count)
        {
            var b = Triangles[_order[start]].Bounds;
            for (int i=start+1;i<start+count;i++) b.Encapsulate(Triangles[_order[i]].Bounds);
            var n = new Node { Bounds=b, Start=start, Count=count };
            if (count <= 8) return n;
            int axis = b.size.x > b.size.y ? 0 : 1; if (b.size.z > b.size[axis]) axis=2;
            Array.Sort(_order,start,count,Comparer<int>.Create((a,c)=>Triangles[a].Bounds.center[axis].CompareTo(Triangles[c].Bounds.center[axis])));
            n.Left=Build(start,count/2); n.Right=Build(start+count/2,count-count/2); return n;
        }

        public float Distance(Vector3 p) => (NearestPoint(p)-p).magnitude;
        public Vector3 NearestPoint(Vector3 p) => NearestHit(p).Position;
        public FitNearestHit NearestHit(Vector3 p) {float best=float.PositiveInfinity;Vector3 closest=Vector3.zero;int index=0;Nearest(_root,p,ref best,ref closest,ref index);return new FitNearestHit {Position=closest,Normal=Triangles[index].Normal,Triangle=index};}
        void Nearest(Node n, Vector3 p, ref float best, ref Vector3 closest,ref int index)
        {
            if (BoundsDistanceSquared(n.Bounds,p)>best) return;
            if (n.Left == null) {
                for(int i=n.Start;i<n.Start+n.Count;i++) {Vector3 point=HandPoseFitGeometry.ClosestPoint(p,Triangles[_order[i]]);float d=(p-point).sqrMagnitude;if(d<best){best=d;closest=point;index=_order[i];}}
                return;
            }
            Node a=n.Left,b=n.Right;
            if (BoundsDistanceSquared(a.Bounds,p)>BoundsDistanceSquared(b.Bounds,p)) { a=n.Right;b=n.Left; }
            Nearest(a,p,ref best,ref closest,ref index); Nearest(b,p,ref best,ref closest,ref index);
        }
        static float BoundsDistanceSquared(Bounds b,Vector3 p)
        {
            Vector3 min=b.min,max=b.max;
            float x=Mathf.Max(min.x-p.x,Mathf.Max(0,p.x-max.x)),y=Mathf.Max(min.y-p.y,Mathf.Max(0,p.y-max.y)),z=Mathf.Max(min.z-p.z,Mathf.Max(0,p.z-max.z));
            return x*x+y*y+z*z;
        }

        public IEnumerable<int> Overlaps(Bounds bounds)
        {
            var stack=new Stack<Node>(); stack.Push(_root); bounds.Expand(2e-6f);
            while(stack.Count>0) {
                var n=stack.Pop(); if(!n.Bounds.Intersects(bounds)) continue;
                if(n.Left==null) { for(int i=n.Start;i<n.Start+n.Count;i++) yield return _order[i]; }
                else { stack.Push(n.Left); stack.Push(n.Right); }
            }
        }
        public int IntersectionCount(FitTriangle t) => Overlaps(t.Bounds).Count(i=>HandPoseFitGeometry.Intersects(t,Triangles[i]));
        /// <summary>Копирует уже построенный BVH для GPU; порядок/разбиение имеет одного владельца.</summary>
        public void ExportGpuBvh(out Vector4[] minimum, out Vector4[] maximum, out Vector4[] ranges, out FitTriangle[] ordered)
        {
            var nodes = new List<Node>();
            void Visit(Node n) { nodes.Add(n); if(n.Left!=null) {Visit(n.Left); Visit(n.Right);} }
            Visit(_root);
            var indexes = nodes.Select((n,i)=>(n,i)).ToDictionary(x=>x.n,x=>x.i);
            minimum=new Vector4[nodes.Count]; maximum=new Vector4[nodes.Count]; ranges=new Vector4[nodes.Count];
            for(int i=0;i<nodes.Count;i++) {
                var n=nodes[i]; var lo=n.Bounds.min; var hi=n.Bounds.max;
                minimum[i]=new Vector4(lo.x,lo.y,lo.z,n.Left!=null?indexes[n.Left]:-1);
                maximum[i]=new Vector4(hi.x,hi.y,hi.z,n.Right!=null?indexes[n.Right]:-1);
                ranges[i]=new Vector4(n.Start,n.Left==null?n.Count:0,0,0);
            }
            ordered=_order.Select(i=>Triangles[i]).ToArray();
        }
    }

    /// <summary>Чистые вычисления без коллайдеров, сцен и Unity callbacks.</summary>
    public static class HandPoseFitGeometry
    {
        // Допуск вычисления касания, не порог качества позы.
        public const float Epsilon = 1e-6f;

        public static Vector3 ClosestPoint(Vector3 p, FitTriangle t)
        {
            if(t.Area<1e-12f) {
                Vector3 a=Segment(p,t.A,t.B),b=Segment(p,t.B,t.C),c=Segment(p,t.C,t.A);
                return (p-a).sqrMagnitude<(p-b).sqrMagnitude ? ((p-a).sqrMagnitude<(p-c).sqrMagnitude?a:c) : ((p-b).sqrMagnitude<(p-c).sqrMagnitude?b:c);
            }
            Vector3 ab=t.B-t.A,ac=t.C-t.A,ap=p-t.A;
            float d1=Vector3.Dot(ab,ap),d2=Vector3.Dot(ac,ap);
            if(d1<=0 && d2<=0) return t.A;
            Vector3 bp=p-t.B; float d3=Vector3.Dot(ab,bp),d4=Vector3.Dot(ac,bp);
            if(d3>=0 && d4<=d3) return t.B;
            float vc=d1*d4-d3*d2;
            if(vc<=0 && d1>=0 && d3<=0) return t.A+ab*(d1/(d1-d3));
            Vector3 cp=p-t.C; float d5=Vector3.Dot(ab,cp),d6=Vector3.Dot(ac,cp);
            if(d6>=0 && d5<=d6) return t.C;
            float vb=d5*d2-d1*d6;
            if(vb<=0 && d2>=0 && d6<=0) return t.A+ac*(d2/(d2-d6));
            float va=d3*d6-d5*d4;
            if(va<=0 && d4-d3>=0 && d5-d6>=0) return t.B+(t.C-t.B)*((d4-d3)/(d4-d3+d5-d6));
            float den=va+vb+vc;
            if(Math.Abs(den)<1e-25f) { // Вырожденная грань: ближайшее ребро.
                Vector3 a=Segment(p,t.A,t.B),b=Segment(p,t.B,t.C),c=Segment(p,t.C,t.A);
                return (p-a).sqrMagnitude<(p-b).sqrMagnitude ? ((p-a).sqrMagnitude<(p-c).sqrMagnitude?a:c) : ((p-b).sqrMagnitude<(p-c).sqrMagnitude?b:c);
            }
            return t.A+ab*(vb/den)+ac*(vc/den);
        }
        static Vector3 Segment(Vector3 p,Vector3 a,Vector3 b) => a+(b-a)*Mathf.Clamp01(Vector3.Dot(p-a,b-a)/Mathf.Max((b-a).sqrMagnitude,1e-25f));

        public static bool Intersects(FitTriangle a, FitTriangle b)
        {
            if(a.Area<1e-12f || b.Area<1e-12f) return false;
            Vector3[] ae={a.B-a.A,a.C-a.B,a.A-a.C},be={b.B-b.A,b.C-b.B,b.A-b.C};
            Vector3 an=Vector3.Cross(ae[0],ae[1]),bn=Vector3.Cross(be[0],be[1]);
            if(Separates(an,a,b)||Separates(bn,a,b)) return false;
            for(int i=0;i<3;i++) {
                // Дополнительные оси в плоскости нужны при копланарности.
                if(Separates(Vector3.Cross(an,ae[i]),a,b)||Separates(Vector3.Cross(bn,be[i]),a,b)) return false;
                for(int j=0;j<3;j++) if(Separates(Vector3.Cross(ae[i],be[j]),a,b)) return false;
            }
            return true;
        }
        static bool Separates(Vector3 axis,FitTriangle a,FitTriangle b)
        {
            if(axis.sqrMagnitude<1e-20f) return false; axis.Normalize();
            float aa=Vector3.Dot(a.A,axis),ab=Vector3.Dot(a.B,axis),ac=Vector3.Dot(a.C,axis);
            float ba=Vector3.Dot(b.A,axis),bb=Vector3.Dot(b.B,axis),bc=Vector3.Dot(b.C,axis);
            return Mathf.Max(aa,Mathf.Max(ab,ac))<Mathf.Min(ba,Mathf.Min(bb,bc))-Epsilon || Mathf.Max(ba,Mathf.Max(bb,bc))<Mathf.Min(aa,Mathf.Min(ab,ac))-Epsilon;
        }

        public static FitTopology Topology(FitTriangle[] triangles)
        {
            var result=new FitTopology(); var vertices=new Dictionary<Vector3,int>();
            var edges=new Dictionary<(int,int),List<(int face,int direction)>>();
            var links=Enumerable.Range(0,triangles.Length).Select(_=>new List<int>()).ToArray();
            int Vertex(Vector3 p) { if(!vertices.TryGetValue(p,out int i)) {i=vertices.Count;vertices.Add(p,i);} return i; }
            for(int f=0;f<triangles.Length;f++) {
                FitTriangle t=triangles[f]; if(t.Area<1e-12f) result.DegenerateTriangles++;
                int[] v={Vertex(t.A),Vertex(t.B),Vertex(t.C)};
                for(int j=0;j<3;j++) {
                    int a=v[j],b=v[(j+1)%3]; var key=(Math.Min(a,b),Math.Max(a,b));
                    if(!edges.TryGetValue(key,out var list)) edges.Add(key,list=new List<(int,int)>());
                    list.Add((f,a<b?1:-1));
                }
            }
            foreach(var e in edges.Values) {
                if(e.Count==1) result.BoundaryEdges++;
                if(e.Count>2) result.NonManifoldEdges++;
                if(e.Count==2 && e[0].direction==e[1].direction) result.OrientationErrors++;
                for(int i=1;i<e.Count;i++) { links[e[0].face].Add(e[i].face);links[e[i].face].Add(e[0].face); }
            }
            result.CanDetermineInside=triangles.Length>0 && result.BoundaryEdges==0 && result.NonManifoldEdges==0 && result.OrientationErrors==0 && result.DegenerateTriangles==0;
            result.Reason=result.CanDetermineInside ? "closed_oriented_exact_weld" : "open_nonmanifold_or_unoriented_exact_weld";
            result.Components=new List<FitTriangle[]>(); var visited=new bool[triangles.Length];
            for(int i=0;i<triangles.Length;i++) if(!visited[i]) {
                var list=new List<FitTriangle>(); var queue=new Stack<int>();queue.Push(i);visited[i]=true;
                while(queue.Count>0) { int f=queue.Pop();list.Add(triangles[f]);foreach(int n in links[f]) if(!visited[n]) {visited[n]=true;queue.Push(n);} }
                result.Components.Add(list.ToArray());
            }
            // Замкнутость по рёбрам недостаточна: отвергаем обнаруженные самопересечения.
            if(result.CanDetermineInside) {
                var surface=new FitSurface(triangles);
                for(int i=0;i<triangles.Length && result.CanDetermineInside;i++) foreach(int j in surface.Overlaps(triangles[i].Bounds)) {
                    if(j<=i || ShareVertex(triangles[i],triangles[j])) continue;
                    if(Intersects(triangles[i],triangles[j])) {result.CanDetermineInside=false;result.Reason="self_intersection";break;}
                }
            }
            return result;
        }
        static bool ShareVertex(FitTriangle a,FitTriangle b) => a.A==b.A||a.A==b.B||a.A==b.C||a.B==b.A||a.B==b.B||a.B==b.C||a.C==b.A||a.C==b.B||a.C==b.C;

        public static bool? IsInside(Vector3 p, FitSurface surface, FitTopology topology)
        {
            if(!topology.CanDetermineInside) return null;
            if(surface.Distance(p)<=Epsilon) return null; // На границе знак не определяем.
            double totalAngle=0;
            foreach(var component in topology.Components) {
                double angle=0;
                foreach(FitTriangle t in component) {
                    Vector3 a=t.A-p,b=t.B-p,c=t.C-p;
                    double al=a.magnitude,bl=b.magnitude,cl=c.magnitude;
                    double determinant=(double)a.x*((double)b.y*c.z-(double)b.z*c.y)-(double)a.y*((double)b.x*c.z-(double)b.z*c.x)+(double)a.z*((double)b.x*c.y-(double)b.y*c.x);
                    double denominator=al*bl*cl+Vector3.Dot(a,b)*cl+Vector3.Dot(b,c)*al+Vector3.Dot(c,a)*bl;
                    angle+=2*Math.Atan2(determinant,denominator);
                }
                totalAngle+=angle;
            }
            return Math.Abs(totalAngle)>2*Math.PI;
        }

        /// <summary>Площадная выборка: одинаковый seed даёт одинаковые точки, каждая несёт площадь.</summary>
        public static List<FitSample> Sample(FitTriangle[] triangles, int count, int seed)
        {
            if(count<1 || triangles.Length==0) throw new ArgumentException("Пустая выборка.");
            double total=0; var cumulative=new double[triangles.Length];
            for(int i=0;i<triangles.Length;i++) {total+=triangles[i].Area;cumulative[i]=total;}
            if(total<=0) throw new ArgumentException("Нулевая площадь.");
            var random=new System.Random(seed); var result=new List<FitSample>(count);
            for(int i=0;i<count;i++) {
                double chosen=(i+random.NextDouble())/count*total;
                int index=Array.BinarySearch(cumulative,chosen); if(index<0) index=~index;index=Math.Min(index,triangles.Length-1);
                float u=Mathf.Sqrt((float)random.NextDouble()),v=(float)random.NextDouble();FitTriangle t=triangles[index];
                result.Add(new FitSample {Position=(1-u)*t.A+u*(1-v)*t.B+u*v*t.C,Area=(float)(total/count),Triangle=index});
            }
            return result;
        }

        /// <summary>Пары близости: обе точки лежат на вычисленных поверхностях. Диапазон — мм, включительно.</summary>
        public static List<FitContactCandidate> ContactCandidates(FitTriangle[] hand,FitSurface contact,FitSurface full,FitTopology topology,int count,int seed,float minMm,float maxMm)
        {
            if(float.IsNaN(minMm)||float.IsNaN(maxMm)||float.IsInfinity(minMm)||float.IsInfinity(maxMm)||minMm<0||maxMm<minMm) throw new ArgumentException("Неверный диапазон контакта.");
            var result=new List<FitContactCandidate>();
            foreach(FitSample sample in Sample(hand,count,seed)) {
                FitNearestHit hit=contact.NearestHit(sample.Position);Vector3 target=hit.Position;float mm=(sample.Position-target).magnitude*1000;
                // 0.1 мкм компенсирует округление float на включительной границе.
                if(mm<minMm-1e-4f||mm>maxMm+1e-4f) continue;
                var t=hand[sample.Triangle];
                result.Add(new FitContactCandidate {Position=sample.Position,TargetPosition=target,DistanceMm=mm,AreaMm2=sample.Area*1e6f,Triangle=sample.Triangle,Zone=t.Zone,FingerSegment=t.FingerSegment,HandNormal=t.Normal,TargetNormal=hit.Normal,TargetTriangle=hit.Triangle,NormalOppositionDot=-Vector3.Dot(t.Normal,hit.Normal),Inside=IsInside(sample.Position,full,topology)});
            }
            return result;
        }

        /// <summary>Связность граней с выбранными контактами; площадь оценивается весами выборки.</summary>
        public static List<FitContactPatch> ContactPatches(FitTriangle[] hand,List<FitContactCandidate> pairs)
        {
            var result=new List<FitContactPatch>();
            foreach(var group in pairs.GroupBy(p=>(p.Zone,p.FingerSegment))) {
                var samples=group.GroupBy(p=>p.Triangle).ToDictionary(g=>g.Key,g=>g.ToList());
                var vertices=new Dictionary<Vector3,List<int>>();
                foreach(int index in samples.Keys) foreach(Vector3 p in new[]{hand[index].A,hand[index].B,hand[index].C}) {if(!vertices.TryGetValue(p,out var list)) vertices.Add(p,list=new List<int>());list.Add(index);}
                var remaining=new HashSet<int>(samples.Keys);
                while(remaining.Count>0) {
                    var patch=new FitContactPatch {Zone=group.Key.Zone,FingerSegment=group.Key.FingerSegment};var stack=new Stack<int>();int first=remaining.First();remaining.Remove(first);stack.Push(first);
                    while(stack.Count>0) {int index=stack.Pop();patch.TriangleCount++;patch.SampleCount+=samples[index].Count;patch.AreaMm2+=samples[index].Sum(p=>p.AreaMm2);
                        foreach(Vector3 p in new[]{hand[index].A,hand[index].B,hand[index].C}) foreach(int neighbor in vertices[p]) if(remaining.Remove(neighbor)) stack.Push(neighbor);
                    }
                    result.Add(patch);
                }
            }
            return result;
        }

        /// <summary>Несоседние грани: касание тоже считается; общие вершины исключены.</summary>
        public static int SelfIntersectionPairs(FitTriangle[] hand)
        {
            if(hand.Length==0) return 0;var surface=new FitSurface(hand);int count=0;
            for(int i=0;i<hand.Length;i++) foreach(int j in surface.Overlaps(hand[i].Bounds)) if(j>i&&!ShareVertex(hand[i],hand[j])&&Intersects(hand[i],hand[j])) count++;
            return count;
        }
    }
}
