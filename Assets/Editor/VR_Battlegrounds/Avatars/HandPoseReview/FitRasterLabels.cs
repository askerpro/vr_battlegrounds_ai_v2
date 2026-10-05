using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Подписи прямо в PNG, без зависимости от OnGUI и установленного шрифта.</summary>
    internal static class FitRasterLabels
    {
        static readonly Dictionary<char,string> Glyph=new Dictionary<char,string>();
        static FitRasterLabels()
        {
            string chars="ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-/: ";
            string[] rows={"01110/10001/10001/11111/10001/10001/10001","11110/10001/10001/11110/10001/10001/11110","01111/10000/10000/10000/10000/10000/01111","11110/10001/10001/10001/10001/10001/11110","11111/10000/10000/11110/10000/10000/11111","11111/10000/10000/11110/10000/10000/10000","01111/10000/10000/10111/10001/10001/01110","10001/10001/10001/11111/10001/10001/10001","11111/00100/00100/00100/00100/00100/11111","00111/00010/00010/00010/10010/10010/01100","10001/10010/10100/11000/10100/10010/10001","10000/10000/10000/10000/10000/10000/11111","10001/11011/10101/10101/10001/10001/10001","10001/11001/10101/10011/10001/10001/10001","01110/10001/10001/10001/10001/10001/01110","11110/10001/10001/11110/10000/10000/10000","01110/10001/10001/10001/10101/10010/01101","11110/10001/10001/11110/10100/10010/10001","01111/10000/10000/01110/00001/00001/11110","11111/00100/00100/00100/00100/00100/00100","10001/10001/10001/10001/10001/10001/01110","10001/10001/10001/10001/10001/01010/00100","10001/10001/10001/10101/10101/11011/10001","10001/10001/01010/00100/01010/10001/10001","10001/10001/01010/00100/00100/00100/00100","11111/00001/00010/00100/01000/10000/11111","01110/10001/10011/10101/11001/10001/01110","00100/01100/00100/00100/00100/00100/01110","01110/10001/00001/00010/00100/01000/11111","11110/00001/00001/01110/00001/00001/11110","00010/00110/01010/10010/11111/00010/00010","11111/10000/10000/11110/00001/00001/11110","01110/10000/10000/11110/10001/10001/01110","11111/00001/00010/00100/01000/01000/01000","01110/10001/10001/01110/10001/10001/01110","01110/10001/10001/01111/00001/00001/01110","00000/00000/00000/00000/00000/00100/00100","00000/00000/00000/11111/00000/00000/00000","00001/00001/00010/00100/01000/10000/10000","00000/00100/00100/00000/00100/00100/00000","00000/00000/00000/00000/00000/00000/00000"};
            for(int i=0;i<chars.Length;i++) Glyph[chars[i]]=rows[i].Replace("/","");
        }
        public static void Label(Texture2D texture,HandPoseFitReport report,string mode,string view,float frame)
        {
            int scale=Math.Max(1,texture.width/400),line=9*scale;
            for(int y=texture.height-line*5-8;y<texture.height;y++) for(int x=0;x<texture.width;x++) texture.SetPixel(x,y,new Color(.04f,.05f,.07f));
            Text(texture,8,texture.height-line-4,$"{report.Settings.Side} POINT {report.Settings.GrabPoint} BLEND {report.AppliedBlend.ToString("F2",CultureInfo.InvariantCulture)} {view} {mode}".ToUpperInvariant(),scale);
            string range=report.Settings.ContactDistanceMinMm.ToString("0.##",CultureInfo.InvariantCulture)+"-"+report.Settings.ProximityMm.ToString("0.##",CultureInfo.InvariantCulture);
            Text(texture,8,texture.height-line*2-4,report.Settings.ShowContactMarkers?"PAIRS "+range+" MM INCLUSIVE / CYAN RING OBJECT":"CONTACT MARKERS OFF",scale);
            Text(texture,8,texture.height-line*3-4,"DOT GREEN OUTSIDE / YELLOW UNKNOWN / RED INSIDE",scale);
            Text(texture,8,texture.height-line*4-4,mode=="distance"?"HEAT CYAN-ORANGE MM / MAGENTA TOUCH / GRAY MASK":"XRAY SHOWS HIDDEN PAIRS / SECTION CLIPS PAIRS",scale);
            string frameLabel=report.CaptureSource!=null && report.CaptureSource.StartsWith("editor_",StringComparison.Ordinal)?"EDITOR FRAME "+report.CaptureFrame:report.CaptureFrame>=0?"RUNTIME FRAME "+report.CaptureFrame:"PREFAB STATIC";
            Text(texture,8,texture.height-line*5-4,(frameLabel+" STATE "+report.StateName).ToUpperInvariant(),scale);
            float mm=frame<.1f?frame*1000*.25f:50;int length=(int)(texture.width*mm*.001f/frame);int y0=18;
            for(int x=12;x<12+length && x<texture.width-12;x++) for(int y=y0;y<y0+3;y++) texture.SetPixel(x,y,Color.white);
            Text(texture,12,y0+8,mm.ToString("0.##",CultureInfo.InvariantCulture)+" MM",scale);texture.Apply();
        }
        static void Text(Texture2D t,int x,int y,string text,int scale)
        {
            foreach(char c in text) {
                if(Glyph.TryGetValue(c,out string bits)) for(int row=0;row<7;row++) for(int col=0;col<5;col++) if(bits[row*5+col]=='1')
                    for(int a=0;a<scale;a++) for(int b=0;b<scale;b++) {int px=x+col*scale+a,py=y-row*scale-b;if(px>=0&&px<t.width&&py>=0&&py<t.height)t.SetPixel(px,py,Color.white);}
                x+=6*scale;
            }
        }
    }
}
