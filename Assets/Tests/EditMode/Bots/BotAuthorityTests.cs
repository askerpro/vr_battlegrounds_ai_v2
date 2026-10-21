using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Tests.Bots
{
    /// <summary>
    /// Бот на вводе устройства (bot-input, принято пользователем 2026-10-21): мозг бота решает только намерения, а хват,
    /// выстрел и подготовку оружия делает код человека по кнопкам ввода. Сторож по исходникам: в коде ботов нет прямых
    /// действий SDK — <c>TryToShootRound</c>, <c>GrabObject</c>/<c>ReleaseObject</c>, <c>RequestAutomationPreparation</c>,
    /// долива магазина сеттером. Issue 23 (один автор выстрела) обеспечивает штатный <c>UxrFirearmWeapon</c> автора
    /// предмета (у бота — сервер).
    /// </summary>
    public class BotAuthorityTests
    {
        private static readonly string[] ForbiddenCalls =
        {
            ".TryToShootRound(", ".RequestAutomationPreparation(", ".GrabObject(", ".ReleaseObject(", "SetAmmoLeft(",
            ".PlaceObject("
        };

        [Test]
        public void Код_ботов_не_вызывает_действий_SDK_только_ввод()
        {
            string folder = Path.Combine(Application.dataPath, "Scripts/Bots");
            foreach (string file in Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string code = File.ReadAllText(file);
                foreach (string call in ForbiddenCalls)
                    Assert.AreEqual(-1, code.IndexOf(call, StringComparison.Ordinal),
                        $"{Path.GetFileName(file)}: прямой вызов SDK {call} — бот действует только вводом устройства.");
            }
        }

        [Test]
        public void Решение_жать_курок_гасит_запрос_и_не_стреляет_само()
        {
            string code = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Bots/BotGunner.cs"));
            int decide = code.IndexOf("public BotShotOutcome DecideTriggerPress()", StringComparison.Ordinal);
            Assert.GreaterOrEqual(decide, 0, "У стрелка нет решения «жать курок».");
            int consume = code.IndexOf("_shotRequested = false;", decide, StringComparison.Ordinal);
            int aim = code.IndexOf("MaxShotAimErrorDegrees", decide, StringComparison.Ordinal);
            int path = code.IndexOf("IsPathBlocked(", decide, StringComparison.Ordinal);
            Assert.Greater(consume, decide, "Запрос Blaze гасится при любом исходе.");
            Assert.Less(consume, aim, "Запрос гасится до проверок наведения.");
            Assert.Greater(path, aim, "Путь пули проверяется после наведения.");
        }
    }
}
