using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandRigQuality
{
    /// <summary>Детерминированные ортографические каркасы в рамке отчёта; не трогает камеры редактора.</summary>
    internal static class HandRigQualityRenderer
    {
        const int ImageSize=640;
        static readonly Color32 Background=new Color32(16,20,27,255),Target=new Color32(255,155,60,255),Reference=new Color32(60,200,245,255);

        public static string[] Render(HandRigCapture target, HandRigCapture reference, string directory)
        {
            var files=new List<string>();
            var views=new[]{("front",Vector3.right,Vector3.up),("back",Vector3.left,Vector3.up),
                ("side",Vector3.forward,Vector3.up),("top",Vector3.right,Vector3.forward)};
            var points=Points(target).Concat(reference!=null?Points(reference):Enumerable.Empty<Vector3>()).ToArray();
            foreach(var view in views)
            {
                Vector2 Project(Vector3 p)=>new Vector2(Vector3.Dot(p,view.Item2),Vector3.Dot(p,view.Item3));
                var projected=points.Select(Project).ToArray();
                Vector2 min=new Vector2(projected.Min(v=>v.x),projected.Min(v=>v.y)),max=new Vector2(projected.Max(v=>v.x),projected.Max(v=>v.y));
                float extent=Mathf.Max(max.x-min.x,max.y-min.y)*1.15f;extent=Mathf.Max(extent,.05f);Vector2 center=(min+max)*.5f;
                Vector2 Pixel(Vector3 p)=>(Project(p)-center)/extent*(ImageSize-1)+Vector2.one*(ImageSize-1)*.5f;
                var pixels=Enumerable.Repeat(Background,ImageSize*ImageSize).ToArray();
                void Disc(Vector2 p, int radius, Color32 color)
                {
                    int cx=Mathf.RoundToInt(p.x),cy=Mathf.RoundToInt(p.y);
                    for(int dy=-radius;dy<=radius;dy++)for(int dx=-radius;dx<=radius;dx++)
                    {
                        int x=cx+dx,y=cy+dy;
                        if(dx*dx+dy*dy<=radius*radius && x>=0&&x<ImageSize&&y>=0&&y<ImageSize)pixels[y*ImageSize+x]=color;
                    }
                }
                void Line(Vector3 a, Vector3 b, Color32 color, int radius=0)
                {
                    Vector2 pa=Pixel(a),pb=Pixel(b);int count=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(pa,pb)));
                    for(int i=0;i<=count;i++)
                    {
                        Vector2 p=Vector2.Lerp(pa,pb,(float)i/count);int x=Mathf.RoundToInt(p.x),y=Mathf.RoundToInt(p.y);
                        if(radius>0)Disc(p,radius,color);
                        else if(x>=0&&x<ImageSize&&y>=0&&y<ImageSize)pixels[y*ImageSize+x]=color;
                    }
                }
                void Draw(HandRigCapture capture, Color32 color)
                {
                    foreach(var mesh in capture.Meshes)
                    {
                        var v=mesh.Sample.Vertices;var ix=mesh.HandTriangles;
                        for(int i=0;i<ix.Length;i+=3)for(int edge=0;edge<3;edge++)Line(v[ix[i+edge]],v[ix[i+(edge+1)%3]],color);
                    }
                }
                if(reference!=null)Draw(reference,Reference);Draw(target,Target);
                if(reference!=null)foreach(var joint in target.Data.Joints)
                {
                    var other=reference.Data.Joints.FirstOrDefault(j=>j.Segment==joint.Segment);
                    if(other!=null)Line(other.Position,joint.Position,new Color32(255,70,110,255));
                }
                // Диагностические кости рисуем последним слоем, поверх обеих сеток.
                var targetBone=new Color32(205,255,65,255);var referenceBone=new Color32(255,110,240,255);
                void Skeleton(HandRigCapture capture, Color32 color)
                {
                    foreach(var joint in capture.Data.Joints)
                    {
                        Vector3 parent=joint.ParentSegment=="wrist"?capture.Data.Wrist:capture.Data.Joints.First(j=>j.Segment==joint.ParentSegment).Position;
                        Line(parent,joint.Position,Background,3);Line(parent,joint.Position,color,1);
                    }
                }
                void Markers(HandRigCapture capture, Color32 color, int radius)
                {
                    foreach(var joint in capture.Data.Joints)
                    {Disc(Pixel(joint.Position),radius+2,Background);Disc(Pixel(joint.Position),radius,color);}
                }
                if(reference!=null)Skeleton(reference,referenceBone);Skeleton(target,targetBone);
                Vector3 origin=target.Data.Wrist;
                Line(origin,origin+Vector3.right*.025f,new Color32(255,65,65,255),2);
                Line(origin,origin+Vector3.up*.025f,new Color32(65,255,110,255),2);
                Line(origin,origin+Vector3.forward*.025f,new Color32(100,145,255,255),2);
                // Разные радиусы сохраняют оба маркера при совпадении суставов.
                if(reference!=null)Markers(reference,referenceBone,8);Markers(target,targetBone,4);
                Disc(Pixel(origin),12,Background);Disc(Pixel(origin),9,Color.white);Disc(Pixel(origin),5,Background);
                var texture=new Texture2D(ImageSize,ImageSize,TextureFormat.RGBA32,false);
                try {texture.SetPixels32(pixels);texture.Apply();string file=view.Item1+".png";File.WriteAllBytes(Path.Combine(directory,file),texture.EncodeToPNG());files.Add(file);}
                finally {UnityEngine.Object.DestroyImmediate(texture);}
            }
            return files.ToArray();
        }

        static IEnumerable<Vector3> Points(HandRigCapture capture)=>capture.Meshes.SelectMany(m=>m.HandTriangles.Select(i=>m.Sample.Vertices[i])).Concat(capture.Data.Joints.Select(j=>j.Position))
            .Concat(new[]{capture.Data.Wrist,capture.Data.Wrist+Vector3.right*.025f,capture.Data.Wrist+Vector3.up*.025f,capture.Data.Wrist+Vector3.forward*.025f});

        public static void WriteIndex(HandRigQualityReport report)
        {
            string Escape(string text)=>WebUtility.HtmlEncode(text??"");
            var html=new StringBuilder("<!doctype html><html lang=\"ru\"><meta charset=\"utf-8\"><title>Hand Rig Quality</title><style>body{background:#10141b;color:#ddd;font:15px system-ui;margin:24px}table{border-collapse:collapse;width:100%}th,td{border:1px solid #39404d;padding:7px;text-align:left}img{width:45%;max-width:640px}small{color:#aaa}</style><h1>Качество рига кисти</h1>");
            html.Append("<p>Статус: ").Append(Escape(report.Status)).Append(". Сетка: оранжевый — цель, голубой — эталон. Кости и точки: салатовый — цель, пурпурный — эталон; точки поверх обеих сеток. Белое кольцо — запястье. Оси длиной 25 мм: X красная, Y зелёная, Z синяя. Розовые линии — смещения суставов.</p>");
            html.Append("<p>Поза: ").Append(Escape(report.Target?.PoseName)).Append(" / ").Append(Escape(report.Reference?.PoseName)).Append(". Рамка: ").Append(Escape(report.Target?.FrameKind)).Append(". Кисти совмещены по запястью и осям SDK, без искусственного разнесения.</p>");
            html.Append("<p>Статическая оценка: ").Append(report.StaticScore.HasValue?report.StaticScore.Value.ToString("F1",System.Globalization.CultureInfo.InvariantCulture)+" / 100":"—").Append("; измерений в оценке: ").Append(report.ScoredMetrics).Append(". ").Append(Escape(report.CalibrationStatus)).Append("</p>");
            html.Append("<p><a href=\"report.json\">JSON отчёта</a> · <a href=\"geometry-target.json\">Полная геометрия и веса</a></p>");
            if(report.Error!=null)html.Append("<pre>").Append(Escape(report.Error)).Append("</pre>");
            foreach(string file in report.Images)html.Append("<a href=\"").Append(Escape(file)).Append("\"><img alt=\"").Append(Escape(file)).Append("\" src=\"").Append(Escape(file)).Append("\"></a>");
            html.Append("<table><tr><th>Метрика</th><th>Область</th><th>Число</th><th>Эталон</th><th>Статус и условие</th></tr>");
            foreach(var metric in report.Metrics)
            {
                string Number(double? value)=>value.HasValue?value.Value.ToString("G6",System.Globalization.CultureInfo.InvariantCulture):"—";
                html.Append("<tr><td>").Append(Escape(metric.Id)).Append("</td><td>").Append(Escape(metric.Subject)).Append("</td><td>").Append(Number(metric.Value)).Append(" ").Append(Escape(metric.Unit)).Append("</td><td>").Append(Number(metric.ReferenceValue)).Append("</td><td>").Append(Escape(metric.Status)).Append("<br><small>").Append(Escape(metric.Reason)).Append("</small></td></tr>");
            }
            html.Append("</table><h2>Пределы проверки</h2><ul>");
            foreach(string limitation in report.Limitations.Concat(report.Target?.Limitations??new List<string>()).Concat(report.Reference?.Limitations??new List<string>()))html.Append("<li>").Append(Escape(limitation)).Append("</li>");
            html.Append("</ul></html>");File.WriteAllText(Path.Combine(report.OutputDirectory,"index.html"),html.ToString(),new UTF8Encoding(false));
        }
    }
}
