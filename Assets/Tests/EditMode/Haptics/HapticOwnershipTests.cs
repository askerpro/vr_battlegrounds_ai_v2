using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Tests.Haptics
{
    /// <summary>
    /// Сторож класса «у вибромотора много писателей». На Quest каждый вызов мотора обрывает текущую вибрацию,
    /// поэтому писатель один — <c>HapticService</c> через <c>UnityXRHapticDevice</c>; остальной код просит сигнал
    /// клипом UxrHapticClip (<c>tasks/haptics-system/Details.md</c>, п. 6). Временные исключения — источники, которые ещё не
    /// переведены; каждое со ссылкой на пункт, который его уберёт, и само исчезает из списка вместе с вызовом.
    /// </summary>
    public class HapticOwnershipTests
    {
        private static readonly Regex DirectCall = new Regex(
            @"\b(SendHapticFeedback|SendGrabbableHapticFeedback|StopHapticFeedback|SendHapticImpulse|SendHapticBuffer|StopHaptics)\s*\(");

        /// <summary>Единственный файл игры, который пишет в мотор.</summary>
        private const string Owner = "Scripts/Haptics/UnityXRHapticDevice.cs";

        /// <summary>
        /// Ещё не переведённые источники: путь от Assets → чья задача уберёт вызов и когда. Список ведёт владелец контракта
        /// haptics-api (haptics-system); строки чужих задач добавляются заранее, до их вливания.
        /// </summary>
        private static readonly Dictionary<string, (string task, string note)> Pending = new Dictionary<string, (string, string)>
        {
            ["Scripts/Weapons/AutomaticWeaponSlideFeedback.cs"] = ("weapon-system", "удаляет этап D/H"),
            ["Scripts/Weapons/WeaponAttemptFeedback.cs"] = ("weapon-system", "удаляет этап D"),
            ["Scripts/Weapons/WeaponChamberingReminder.cs"] = ("weapon-system", "удаляет этап H"),
            ["Scripts/Weapons/BarrelObstruction.cs"] = ("weapon-system", "переводит этап D (остаётся датчиком)"),
            ["Scripts/Weapons/WeaponSystem/WeaponHapticOutput.cs"] = ("weapon-system", "временный адаптер этапа drive до HapticService"),
            ["Scripts/Player/WallPass/WallPassFeedback.cs"] = ("haptics-system", "переводит этап sources"),
            ["Scripts/UI/HUD/WristDisplay.cs"] = ("haptics-system", "переводит этап sources"),
            ["Scripts/Player/Ghost/GhostViewEffect.cs"] = ("haptics-system", "переводит этап sources"),
            ["Scripts/Debug/DebugMode/DebugGestureInput.cs"] = ("haptics-system", "переводит этап sources"),
        };

        [Test]
        public void Вибромотор_пишет_только_сервис()
        {
            string[] offenders = FindDirectCalls()
                .Where(x => x.path != Owner && !Pending.ContainsKey(x.path))
                .Select(x => $"{x.path}:{x.line}: {x.text}")
                .ToArray();

            Assert.IsEmpty(offenders,
                "Прямая вибрация в обход HapticService (нужно HapticService.Play/Begin с UxrHapticClip с формой):\n" +
                string.Join("\n", offenders));
        }

        /// <summary>
        /// Свои строки списка не устаревают: переведённый источник haptics-system убирает из списка сам. Строки чужих задач
        /// здесь не проверяются — их файл может ещё не быть влит или уже быть удалён, и чужое вливание не должно падать на
        /// нашем тесте; их чистит владелец списка после вливания той задачи.
        /// </summary>
        [Test]
        public void Временные_исключения_не_устарели()
        {
            HashSet<string> callers = new HashSet<string>(FindDirectCalls().Select(x => x.path));
            string[] stale = Pending.Where(p => p.Value.task == "haptics-system" && !callers.Contains(p.Key))
                .Select(p => $"{p.Key} ({p.Value.note})")
                .ToArray();

            Assert.IsEmpty(stale, "Файл переведён или удалён — убрать его из HapticOwnershipTests.Pending:\n" +
                                  string.Join("\n", stale));
        }

        private const string PatchMark = "VR Battlegrounds patch 65";
        private const string SdkDevices = "ThirdParty/UltimateXR/Runtime/Scripts/Devices/";

        /// <summary>
        /// Точки SDK, где запрос вибрации первым делом уходит <c>HapticService</c> через <c>UxrHapticRouting</c> (SDK-патч 65,
        /// <c>tasks/haptics-system/Details.md</c> §6 п. 3): путь от Assets, метод и фабрика запроса. Прямые вызовы мотора в этих
        /// файлах остаются (ветка SDK без маршрутизатора) и правилом владельца выше не прощаются.
        /// </summary>
        private static readonly (string path, string method, string request)[] RoutedSdkPoints =
        {
            (SdkDevices + "UxrControllerInput.cs", "SendHapticFeedback", "ForClipType"),
            (SdkDevices + "Integrations/UxrUnityXRControllerInput.cs", "SendHapticFeedback", "ForClip"),
            (SdkDevices + "Integrations/UxrUnityXRControllerInput.cs", "SendHapticFeedback", "ForRaw"),
            (SdkDevices + "Integrations/UxrUnityXRControllerInput.cs", "StopHapticFeedback", "ForStop"),
        };

        /// <summary>
        /// Вибрации UltimateXR перехватываются: в каждой точке первая инструкция метода — <c>UxrHapticRouting.TryRoute</c> с
        /// меткой патча. Без неё SDK снова пишет в мотор в обход сервиса и обрывает его вибрацию.
        /// </summary>
        [Test]
        public void Вибрация_SDK_маршрутизируется_патчем_65()
        {
            var errors = new List<string>();
            foreach ((string path, string method, string request) in RoutedSdkPoints)
            {
                string file = Path.Combine(Application.dataPath, path);
                if (!File.Exists(file))
                {
                    errors.Add($"{path}: файла нет");
                    continue;
                }

                string[] lines = File.ReadAllLines(file);
                var route = new Regex(@"^\s*if\s*\(\s*UxrHapticRouting\.TryRoute\(\s*UxrHapticRequest\." + request +
                                      @"\(\s*this\s*,\s*handSide\b.*\)\)\s*return\s*;");
                int at = Array.FindIndex(lines, l => route.IsMatch(l));
                if (at < 0)
                {
                    errors.Add($"{path}: нет перехвата UxrHapticRouting.TryRoute(UxrHapticRequest.{request}(this, handSide, …))");
                    continue;
                }

                string mark = at > 0 ? lines[at - 1].Trim() : "";
                if (!mark.StartsWith("//") || !mark.Contains(PatchMark))
                    errors.Add($"{path}:{at + 1}: перед перехватом нет комментария «{PatchMark}»");

                // Перехват — первая инструкция метода: между заголовком и ним только параметры и скобка.
                var header = new Regex(@"\bvoid\s+" + method + @"\s*\(");
                int start = at - 1;
                while (start >= 0 && at - start <= 12 && !header.IsMatch(lines[start])) start--;
                if (start < 0 || !header.IsMatch(lines[start]))
                    errors.Add($"{path}:{at + 1}: перехват {request} не в методе {method}");
                else if (lines.Skip(start).Take(at - start).Any(l => !l.TrimStart().StartsWith("//") && l.Contains(";")))
                    errors.Add($"{path}:{at + 1}: перехват {request} — не первая инструкция {method}");
            }

            string routing = Path.Combine(Application.dataPath, "ThirdParty/UltimateXR/Runtime/Scripts/Haptics/UxrHapticRouting.cs");
            if (!File.Exists(routing) || !File.ReadAllText(routing).Contains(PatchMark))
                errors.Add($"UxrHapticRouting.cs: нет файла или метки «{PatchMark}»");

            Assert.IsEmpty(errors, "SDK-патч 65 нарушен — вибрации UltimateXR пишут в мотор в обход HapticService:\n" +
                                   string.Join("\n", errors));
        }

        private static IEnumerable<(string path, int line, string text)> FindDirectCalls()
        {
            string assets = Application.dataPath;
            foreach (string folder in new[] { "Scripts", "Editor" })
            {
                foreach (string file in Directory.GetFiles(Path.Combine(assets, folder), "*.cs", SearchOption.AllDirectories))
                {
                    string path = file.Substring(assets.Length + 1).Replace('\\', '/');
                    string[] lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string trimmed = lines[i].TrimStart();
                        if (trimmed.StartsWith("//")) continue;
                        if (DirectCall.IsMatch(lines[i])) yield return (path, i + 1, trimmed);
                    }
                }
            }
        }
    }
}
