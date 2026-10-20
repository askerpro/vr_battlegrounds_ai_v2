using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Рекламу Discovery запускает один владелец — <c>GameNetworkDiscovery</c>, по <c>ServerPublished</c>.
    ///
    /// <para>
    /// Что было. Апстримный <c>NetworkDiscoveryBase.Start()</c> в headless сам вызывал <c>AdvertiseServer()</c>
    /// на порту сцены 47777 — до шва <c>PlayerBeforeNetworkStart</c>, который ставит e2e-плееру его пару портов.
    /// При занятом 47777 (открытый Editor) сервер e2e-плеера писал <c>SocketException</c>, heavy <c>--profile windows</c>
    /// краснел плавающе; выделенный сервер объявлял себя клиентам до готовности карты (против S1).
    /// </para>
    ///
    /// <para>
    /// Что стало. SDK-патч Mirror 8 убрал автостарт (<c>tasks/e2e-discovery-autostart/changelog/2026-10-20-sdk-8.md</c>).
    /// Поведение headless в редакторе не воспроизвести (<c>Utils.IsHeadless</c> читает графическое устройство),
    /// поэтому тесты сторожат исходники: патч не потерян при обновлении Mirror и второй вызывающий не появился.
    /// </para>
    /// </summary>
    public class DiscoveryAdvertiseOwnerTests
    {
        private const string PatchMark = "VR Battlegrounds patch 8";
        private const string BasePath = "ThirdParty/Mirror/Components/Discovery/NetworkDiscoveryBase.cs";
        private const string OwnerFile = "GameNetworkDiscovery.cs";

        [Test]
        public void Start_Mirror_Discovery_не_запускает_рекламу()
        {
            string path = Path.Combine(Application.dataPath, BasePath);
            Assert.IsTrue(File.Exists(path), "Нет файла " + BasePath);

            string start = ExtractMethodBody(File.ReadAllText(path), "public virtual void Start()");
            Assert.IsNotNull(start, "В NetworkDiscoveryBase не найден Start() — сверить патч 8 с новой версией Mirror.");
            StringAssert.Contains(PatchMark, start, "Метка патча 8 пропала из Start() — патч потерян при обновлении Mirror?");
            Assert.IsFalse(Regex.IsMatch(StripComments(start), @"\bAdvertiseServer\s*\("),
                "Start() снова вызывает AdvertiseServer(): headless-сервер забиндит порт сцены до применения портов прогона.");
        }

        [Test]
        public void AdvertiseServer_вызывает_только_GameNetworkDiscovery()
        {
            var callers = new List<string>();
            foreach (string folder in new[] { "Scripts", "Editor" })
            {
                string root = Path.Combine(Application.dataPath, folder);
                if (!Directory.Exists(root))
                    continue;
                foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file) == OwnerFile)
                        continue;
                    if (Regex.IsMatch(StripComments(File.ReadAllText(file)), @"\.AdvertiseServer\s*\("))
                        callers.Add(file.Substring(Application.dataPath.Length - "Assets".Length).Replace('\\', '/'));
                }
            }

            Assert.IsEmpty(callers, "Рекламой Discovery владеет GameNetworkDiscovery; второй вызывающий AdvertiseServer():\n" +
                                    string.Join("\n", callers));
        }

        /// <summary>Тело метода от сигнатуры до парной закрывающей скобки; null — сигнатура не найдена.</summary>
        private static string ExtractMethodBody(string source, string signature)
        {
            int at = source.IndexOf(signature, System.StringComparison.Ordinal);
            if (at < 0)
                return null;
            int open = source.IndexOf('{', at);
            if (open < 0)
                return null;
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(open, i - open + 1);
            }
            return null;
        }

        private static string StripComments(string source) =>
            Regex.Replace(source, @"//[^\n]*|/\*.*?\*/", string.Empty, RegexOptions.Singleline);
    }
}
