using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandRigQuality
{
    public sealed class HandSection
    {
        public bool Valid;
        public string Reason;
        public Vector3 Center;
        public float Area, Radius;
        public float MeanWeight, FingerSupportFraction;
        public Vector3[] Points=Array.Empty<Vector3>();
    }

    public sealed class HandCuffLeakage
    {
        public bool Valid;
        public float WeightMean, Area;
        public int Triangles;
    }

    public sealed class HandWeightCrossing
    {
        public bool Valid;
        public string Reason;
        public float PositionMeters;
    }

    /// <summary>Чистые расчёты в метрах; отказ измерения не превращается в нулевой дефект.</summary>
    public static class HandRigQualityMath
    {
        const float WeldTolerance=0.00001f;

        /// <summary>Площадная PCA: направления с неопределённой толщиной/сводом не принимаются.</summary>
        public static bool PalmAxes(Vector3[] vertices, int[] triangles, float[] palmWeights,
            Vector3 expectedForward, Vector3 expectedUp, out Vector3 forward, out Vector3 up)
        {
            forward=up=Vector3.zero;var samples=new List<(Vector3 a,Vector3 b,Vector3 c,double area)>();double total=0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                if((palmWeights[a]+palmWeights[b]+palmWeights[c])/3<.6f)continue;
                double area=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).magnitude*.5;
                if(area<1e-12)continue;samples.Add((vertices[a],vertices[b],vertices[c],area));total+=area;
            }
            if(total<1e-9 || samples.Count<6)return false;
            Vector3 mean=Vector3.zero;foreach(var s in samples)mean+=(s.a+s.b+s.c)*(float)(s.area/(3*total));
            var m=new double[3,3];var eigen=new double[3,3];for(int i=0;i<3;i++)eigen[i,i]=1;
            foreach(var s in samples)
            {
                Vector3 sum=s.a+s.b+s.c;
                // Точный E[x_i*x_j] равномерной площади треугольника, включая внутритреугольные моменты.
                for(int i=0;i<3;i++)for(int j=0;j<3;j++)
                    m[i,j]+=(s.a[i]*s.a[j]+s.b[i]*s.b[j]+s.c[i]*s.c[j]+sum[i]*sum[j])*s.area/(12*total);
            }
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)m[i,j]-=(double)mean[i]*mean[j];
            for(int step=0;step<24;step++)
            {
                int p=0,q=1;for(int i=0;i<3;i++)for(int j=i+1;j<3;j++)if(Math.Abs(m[i,j])>Math.Abs(m[p,q])){p=i;q=j;}
                if(Math.Abs(m[p,q])<1e-14)break;
                double angle=.5*Math.Atan2(2*m[p,q],m[q,q]-m[p,p]);double c=Math.Cos(angle),s=Math.Sin(angle);
                double pp=m[p,p],qq=m[q,q],pq=m[p,q];
                for(int k=0;k<3;k++)if(k!=p&&k!=q)
                {double kp=m[k,p],kq=m[k,q];m[k,p]=m[p,k]=c*kp-s*kq;m[k,q]=m[q,k]=s*kp+c*kq;}
                m[p,p]=c*c*pp-2*s*c*pq+s*s*qq;m[q,q]=s*s*pp+2*s*c*pq+c*c*qq;m[p,q]=m[q,p]=0;
                for(int k=0;k<3;k++){double kp=eigen[k,p],kq=eigen[k,q];eigen[k,p]=c*kp-s*kq;eigen[k,q]=s*kp+c*kq;}
            }
            var order=Enumerable.Range(0,3).OrderBy(i=>m[i,i]).ToArray();
            if(m[order[1],order[1]]<1e-10 || m[order[1],order[1]]<m[order[0],order[0]]*1.25 ||
                m[order[2],order[2]]<m[order[1],order[1]]*1.10)return false;
            Vector3 Axis(int i)=>new Vector3((float)eigen[0,i],(float)eigen[1,i],(float)eigen[2,i]).normalized;
            up=Axis(order[0]);if(Vector3.Dot(up,expectedUp)<0)up=-up;
            Vector3 a1=Axis(order[1]),a2=Axis(order[2]);
            forward=Mathf.Abs(Vector3.Dot(a1,expectedForward))>Mathf.Abs(Vector3.Dot(a2,expectedForward))?a1:a2;
            if(Vector3.Dot(forward,expectedForward)<0)forward=-forward;
            return true;
        }

        /// <summary>Средний вес на контуре по длине пересечённых рёбер треугольников.</summary>
        public static float SectionWeight(Vector3[] vertices, int[] triangles, float[] weights, Vector3 origin, Vector3 normal)
        {
            double weighted=0,length=0;normal.Normalize();
            for(int i=0;i<triangles.Length;i+=3)
            {
                var cuts=new List<(Vector3 p,float w)>();
                for(int edge=0;edge<3;edge++)
                {
                    int a=triangles[i+edge],b=triangles[i+(edge+1)%3];
                    float da=Vector3.Dot(vertices[a]-origin,normal),db=Vector3.Dot(vertices[b]-origin,normal);
                    if((da>0&&db>0)||(da<0&&db<0)||Mathf.Abs(da-db)<1e-12f)continue;
                    float t=da/(da-db);Vector3 p=Vector3.Lerp(vertices[a],vertices[b],t);
                    if(!cuts.Any(c=>(c.p-p).sqrMagnitude<1e-12f))cuts.Add((p,Mathf.Lerp(weights[a],weights[b],t)));
                }
                if(cuts.Count!=2)continue;double l=Vector3.Distance(cuts[0].p,cuts[1].p);
                length+=l;weighted+=l*(cuts[0].w+cuts[1].w)*.5;
            }
            return length<1e-9?float.NaN:(float)(weighted/length);
        }

        /// <summary>Сечение одного замкнутого контура. Ветвление, несколько контуров и открытый край — отказ.</summary>
        public static HandSection Section(Vector3[] vertices, int[] triangles, Vector3 origin, Vector3 normal)
        {
            var contours=Contours(vertices,triangles,null,null,null,origin,normal,out string failure);
            if(failure!=null)return new HandSection{Reason=failure};
            if(contours.Count!=1)return new HandSection{Reason="Нет одного замкнутого контура: сечение содержит несколько областей."};
            return contours[0];
        }

        /// <summary>Полные контуры без обрезания треугольников; принадлежность задают веса цепи пальца.</summary>
        public static HandSection FingerSection(Vector3[] vertices,int[] triangles,float[] childWeights,float[] fingerWeights,Vector3 origin,Vector3 normal)
            =>FingerSectionBySurface(vertices,triangles,childWeights,fingerWeights,null,origin,normal);

        public static HandSection FingerSectionBySurface(Vector3[] vertices,int[] triangles,float[] childWeights,float[] fingerWeights,int[] surfaceIds,Vector3 origin,Vector3 normal)
        {
            var contours=Contours(vertices,triangles,childWeights,fingerWeights,surfaceIds,origin,normal,out string failure);
            if(failure!=null)return new HandSection{Reason=failure};
            // ≥80% периметра должно иметь вес цепи пальца ≥0,1. Это условие зоны, а не порог качества.
            var supported=contours.Where(c=>c.FingerSupportFraction>=.8f).ToArray();
            if(supported.Length!=1)return new HandSection{FingerSupportFraction=contours.Count>0?contours.Max(c=>c.FingerSupportFraction):0,Reason=supported.Length==0?
                "Нет однозначного контура пальца: плоскость пересекает смешанную область ладони/пальцев.":"Несколько контуров принадлежат пальцу: выбор поверхности неоднозначен."};
            if(supported[0].Valid)supported[0].Reason="Полный контур; ≥80% периметра поддержано весами цепи пальца ≥0,1.";
            return supported[0];
        }

        static List<HandSection> Contours(Vector3[] vertices,int[] triangles,float[] childWeights,float[] fingerWeights,int[] surfaceIds,Vector3 origin,Vector3 normal,out string failure)
        {
            failure=null;var sections=new List<HandSection>();
            if(normal.sqrMagnitude<1e-12f){failure="Нулевая нормаль сечения.";return sections;}
            normal.Normalize();var points=new List<Vector3>();var childMin=new List<float>();var childMax=new List<float>();var fingers=new List<float>();var surfaces=new List<int>();
            var edges=new HashSet<(int,int)>();var duplicated=new HashSet<int>();
            int Node(Vector3 p,float child,float finger,int surface)
            {
                for(int i=0;i<points.Count;i++)if(surfaces[i]==surface&&(points[i]-p).sqrMagnitude<WeldTolerance*WeldTolerance)
                {childMin[i]=Mathf.Min(childMin[i],child);childMax[i]=Mathf.Max(childMax[i],child);fingers[i]=Mathf.Min(fingers[i],finger);return i;}
                points.Add(p);childMin.Add(child);childMax.Add(child);fingers.Add(finger);surfaces.Add(surface);return points.Count-1;
            }
            for(int t=0;t<triangles.Length;t+=3)
            {
                float d0=Vector3.Dot(vertices[triangles[t]]-origin,normal),d1=Vector3.Dot(vertices[triangles[t+1]]-origin,normal),d2=Vector3.Dot(vertices[triangles[t+2]]-origin,normal);
                if((d0>0&&d1>0&&d2>0)||(d0<0&&d1<0&&d2<0))continue;
                var cuts=new List<(Vector3 point,float child,float finger)>();
                for(int e=0;e<3;e++)
                {
                    int ia=triangles[t+e],ib=triangles[t+(e+1)%3];Vector3 a=vertices[ia],b=vertices[ib];
                    float da=Vector3.Dot(a-origin,normal),db=Vector3.Dot(b-origin,normal);
                    if(Mathf.Abs(da)<1e-8f&&Mathf.Abs(db)<1e-8f)
                    {
                        if(fingerWeights==null||(fingerWeights[ia]+fingerWeights[ib])*.5f>=.1f)
                        {failure="Плоскость совпала с ребром области пальца; сечение неустойчиво.";return sections;}
                        continue;
                    }
                    if((da>0&&db>0)||(da<0&&db<0)||Mathf.Abs(da-db)<1e-12f)continue;
                    float s=da/(da-db);Vector3 p=Vector3.Lerp(a,b,s);
                    if(!cuts.Any(q=>(q.point-p).sqrMagnitude<1e-12f))cuts.Add((p,
                        childWeights==null?0:Mathf.Lerp(childWeights[ia],childWeights[ib],s),
                        fingerWeights==null?1:Mathf.Lerp(fingerWeights[ia],fingerWeights[ib],s)));
                }
                if(cuts.Count!=2)continue;
                int surface=surfaceIds==null?0:surfaceIds[triangles[t]];
                if(surfaceIds!=null&&(surfaceIds[triangles[t+1]]!=surface||surfaceIds[triangles[t+2]]!=surface))
                {failure="Треугольник содержит несколько идентичностей поверхности.";return sections;}
                int i=Node(cuts[0].point,cuts[0].child,cuts[0].finger,surface),j=Node(cuts[1].point,cuts[1].child,cuts[1].finger,surface);
                if(i!=j&&!edges.Add(i<j?(i,j):(j,i))){duplicated.Add(i);duplicated.Add(j);}
            }
            var adjacency=Enumerable.Range(0,points.Count).Select(_=>new List<int>()).ToArray();
            foreach(var edge in edges){adjacency[edge.Item1].Add(edge.Item2);adjacency[edge.Item2].Add(edge.Item1);}
            var unseen=new HashSet<int>(Enumerable.Range(0,points.Count));
            while(unseen.Count>0)
            {
                var component=new List<int>();var stack=new Stack<int>();int start=unseen.First();unseen.Remove(start);stack.Push(start);
                while(stack.Count>0){int i=stack.Pop();component.Add(i);foreach(int j in adjacency[i])if(unseen.Remove(j))stack.Push(j);}
                float perimeter=0,support=0,weighted=0;
                foreach(int i in component)foreach(int j in adjacency[i])if(j>i)
                {float length=Vector3.Distance(points[i],points[j]);perimeter+=length;weighted+=length*(childMin[i]+childMax[i]+childMin[j]+childMax[j])*.25f;
                 support+=length*SupportFraction(fingers[i],fingers[j],.1f);}
                var section=new HandSection {Reason="Сечение открыто или имеет ветвление.",FingerSupportFraction=perimeter>0?support/perimeter:0,MeanWeight=perimeter>0?weighted/perimeter:0};
                if(component.Any(i=>childMax[i]-childMin[i]>1e-5f))section.MeanWeight=float.NaN;
                if(component.Any(i=>duplicated.Contains(i)))section.Reason="Совпадающие сегменты разных граней: поверхность неоднозначна.";
                else if(component.Count>=3&&component.All(i=>adjacency[i].Count==2))
                {
                    var ordered=new List<int>();int previous=-1,current=start;
                    do{ordered.Add(current);int next=adjacency[current].First(n=>n!=previous);previous=current;current=next;}while(current!=start&&ordered.Count<=component.Count);
                    section=Polygon(points,ordered,origin,normal,section);
                }
                sections.Add(section);
            }
            return sections;
        }

        static float SupportFraction(float a,float b,float threshold)
        {
            if(a>=threshold&&b>=threshold)return 1;
            if(a<threshold&&b<threshold)return 0;
            return a>=threshold?(a-threshold)/(a-b):(b-threshold)/(b-a);
        }

        /// <summary>Оценка смены знака на полностью известном дискретном профиле; не доказательство между сэмплами.</summary>
        public static HandWeightCrossing SampledWeightCrossing(float?[] weights,float startMeters,float stepMeters)
        {
            var result=new HandWeightCrossing{Reason="Профиль не даёт единственной смены знака на полученных сэмплах."};
            if(weights==null||weights.Length<3||stepMeters<=0||float.IsNaN(stepMeters)||float.IsInfinity(stepMeters))return result;
            if(weights.Any(w=>!w.HasValue||float.IsNaN(w.Value)||float.IsInfinity(w.Value)))
            {result.Reason="В профиле есть недоступные интервалы; единственность перехода не установлена.";return result;}
            var roots=new List<float>();
            for(int i=0;i<weights.Length;i++)
            {
                float value=weights[i].Value-.5f;
                if(Mathf.Abs(value)<=1e-6f)
                {
                    int end=i;while(end+1<weights.Length&&Mathf.Abs(weights[end+1].Value-.5f)<=1e-6f)end++;
                    if(end>i||i==0||end==weights.Length-1)
                    {result.Reason="Плато или неограниченное с двух сторон равенство веса0,5.";return result;}
                    if((weights[i-1].Value-.5f)*(weights[i+1].Value-.5f)<0)roots.Add(startMeters+i*stepMeters);
                    continue;
                }
                if(i+1<weights.Length)
                {
                    float next=weights[i+1].Value-.5f;
                    if(Mathf.Abs(next)>1e-6f&&value*next<0)roots.Add(startMeters+(i-value/(next-value))*stepMeters);
                }
            }
            if(roots.Count!=1)return result;
            result.Valid=true;result.PositionMeters=roots[0];result.Reason="Оценка единственной смены знака на сэмплах; узкие переходы/плато между сэмплами не исключены.";return result;
        }

        static HandSection Polygon(List<Vector3> points,List<int> ordered,Vector3 origin,Vector3 normal,HandSection result)
        {
            Vector3 u=Vector3.Cross(normal,Mathf.Abs(normal.y)<.9f?Vector3.up:Vector3.right).normalized;
            Vector3 v=Vector3.Cross(normal,u);float twiceArea=0;Vector2 weightedCenter=Vector2.zero;
            for(int i=0;i<ordered.Count;i++)
            {
                Vector3 pa=points[ordered[i]]-origin,pb=points[ordered[(i+1)%ordered.Count]]-origin;
                var a=new Vector2(Vector3.Dot(pa,u),Vector3.Dot(pa,v));var b=new Vector2(Vector3.Dot(pb,u),Vector3.Dot(pb,v));
                float cross=a.x*b.y-b.x*a.y;twiceArea+=cross;weightedCenter+=(a+b)*cross;
            }
            if(Mathf.Abs(twiceArea)<1e-10f) {result.Reason="Площадь сечения вырождена.";return result;}
            weightedCenter/=3*twiceArea;
            result.Center=origin+u*weightedCenter.x+v*weightedCenter.y;
            result.Area=Mathf.Abs(twiceArea)*.5f;result.Radius=Mathf.Sqrt(result.Area/Mathf.PI);
            result.Points=ordered.Select(i=>points[i]).ToArray();result.Valid=true;result.Reason="";
            return result;
        }

        /// <summary>Средняя энергия разности весов на уникальном индексированном ребре (без размерности).</summary>
        public static float WeightEnergy(Vector3[] vertices, int[] triangles, float[][] weights)
        {
            var edges=new HashSet<(int,int)>();
            for(int i=0;i<triangles.Length;i+=3) for(int j=0;j<3;j++)
            {int a=triangles[i+j],b=triangles[i+(j+1)%3];if(a!=b)edges.Add(a<b?(a,b):(b,a));}
            double energy=0;
            foreach(var e in edges)
            {
                var a=weights[e.Item1];var b=weights[e.Item2];
                if(a.Length!=b.Length) throw new ArgumentException("Веса должны иметь одну карту костей.");
                for(int k=0;k<a.Length;k++) {double d=a[k]-b[k];energy+=d*d;}
            }
            return edges.Count==0?float.NaN:(float)(energy/edges.Count);
        }

        /// <summary>Площадная средняя веса кисти в цилиндре рукава; треугольники отбираются по центру.</summary>
        public static HandCuffLeakage CuffLeakage(Vector3[] vertices, int[] triangles, float[] handWeights,
            Vector3 wrist, Vector3 towardForearm, float minDistance, float maxDistance, float radius)
        {
            var result=new HandCuffLeakage();if(towardForearm.sqrMagnitude<1e-12f)return result;
            towardForearm.Normalize();double area=0,weighted=0;
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                Vector3 delta=(vertices[a]+vertices[b]+vertices[c])/3-wrist;
                float distance=Vector3.Dot(delta,towardForearm);
                if(distance<=minDistance || distance>maxDistance || (delta-towardForearm*distance).sqrMagnitude>radius*radius)continue;
                float triangleArea=Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]).magnitude*.5f;
                area+=triangleArea;weighted+=triangleArea*(handWeights[a]+handWeights[b]+handWeights[c])/3;result.Triangles++;
            }
            result.Area=(float)area;result.Valid=area>1e-12;
            if(result.Valid)result.WeightMean=(float)(weighted/area);
            return result;
        }
    }
}
