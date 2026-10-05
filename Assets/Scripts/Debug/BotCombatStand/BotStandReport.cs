using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>Переносимый HTML: численные наблюдения, фактические траектории и ссылки на фотографии.</summary>
    public static class BotStandReport
    {
        public static void Write(string folder, BotStandRunStatus status, IEnumerable<BotStandCaseResult> results)
        {
            var html=new StringBuilder("<!doctype html><meta charset='utf-8'><title>BotCombatStand</title><style>body{background:#202733;color:#edf2fa;font:16px sans-serif;max-width:1100px;margin:30px auto}a{color:#93c5fd}table{border-collapse:collapse}td,th{padding:8px;border:1px solid #566}svg{width:320px;background:#121923}article{margin:24px 0;border-top:1px solid #566}small{color:#bdc}</style>");
            html.Append("<h1>Стенд ботов — ").Append(E(status.RunId)).Append("</h1><p>NeedsReview требует оценки. Unsupported не выполнен. Отсутствие обнаруженного нарушения не означает качественный бой.</p><p><a href='summary.json'>Полный JSON</a></p><table><tr><th>Сценарий</th><th>Профиль</th><th>Статус</th><th>Выстрелы</th><th>Снимки</th></tr>");
            foreach(var item in results)
                html.Append("<tr><td><a href='#").Append(item.Id).Append('-').Append(item.Seed).Append("'>").Append(item.Id).Append('/').Append(item.Seed).Append("</a></td><td>").Append(item.Profile).Append("</td><td>").Append(item.Status).Append("</td><td>").Append(item.Metrics.TryGetValue("shots",out var shots)?shots:"—").Append("</td><td>").Append(item.Images.Count).Append("</td></tr>");
            html.Append("</table>");
            foreach(var item in results)
            {
                html.Append("<article id='").Append(item.Id).Append('-').Append(item.Seed).Append("'><h2>").Append(item.Id).Append('/').Append(item.Seed).Append(" — ").Append(item.Status).Append("</h2><p>").Append(E(item.Error)).Append(E(string.Join("; ",item.Findings))).Append("</p>");
                if(item.Images.Count>0) html.Append("<p><a href='").Append(item.Id).Append('-').Append(item.Seed).Append("/report.html'>Открыть все ракурсы и кисти</a></p>");
                if(item.Frames.Count>0)
                {
                    html.Append("<svg viewBox='0 0 240 200'><rect x='1' y='1' width='238' height='198' fill='none' stroke='#667'/>");
                    if(item.Id=="T01"||item.Id=="T08") html.Append("<rect x='90' y='97' width='60' height='6' fill='#889'/>");
                    if(item.Id=="T10") html.Append("<rect x='1' y='85' width='238' height='30' fill='#433'/>");
                    if(item.Id=="T03"||item.Id=="T04"||item.Id=="T05") html.Append("<rect x='102.5' y='112' width='35' height='6' fill='#889'/>");
                    foreach(var body in item.Frames.GroupBy(f=>f.BodyId)) html.Append("<polyline fill='none' stroke='#49c6ff' stroke-width='2' points='").Append(Points(body.Select(f=>f.Feet))).Append("'/>");
                    html.Append("<polyline fill='none' stroke='#fb8' stroke-width='2' points='").Append(Points(item.Frames.Where(f=>f.TargetFeet!=null).Select(f=>f.TargetFeet))).Append("'/></svg><p><small>Вид сверху: синий — фактические ноги бота, оранжевый — контрольная цель; серый — стендовая преграда. Выборка 0,1 с.</small></p>");
                }
                html.Append("</article>");
            }
            File.WriteAllText(Path.Combine(folder,"report.html"),html.ToString(),new UTF8Encoding(false));
        }
        private static string E(string value) => WebUtility.HtmlEncode(value ?? "");
        private static string Points(IEnumerable<float[]> values) => string.Join(" ",values.Select(p=>(120+p[0]*10).ToString("F1",CultureInfo.InvariantCulture)+","+(100-p[2]*10).ToString("F1",CultureInfo.InvariantCulture)));
    }
}
