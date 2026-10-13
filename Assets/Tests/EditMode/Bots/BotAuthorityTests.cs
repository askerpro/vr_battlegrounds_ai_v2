using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Tests.Bots
{
    /// <summary>
    /// Issue 23 для ботов (T-48): спуск бота жмётся в <c>Update</c> — синхронизируемый выстрел обязан пройти
    /// проверку автора предмета (<c>StateEventAuthority.IsAuthorOfItem</c>), иначе машина, исполнившая тот же код
    /// не как автор, выстрелит второй раз и удвоит урон. Сторож по исходнику (player-flow, clip-cutover cc-1):
    /// проверка стоит перед подготовкой оружия (<c>RequestAutomationPreparation</c>) и <c>TryToShootRound</c>;
    /// магазин бот не доливает сеттером — патроны ведёт штатный учёт оружия.
    /// </summary>
    public class BotAuthorityTests
    {
        [Test]
        public void Стрелок_бота_жмёт_спуск_только_как_автор_предмета()
        {
            string code = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Bots/BotGunner.cs"));

            int check = code.IndexOf("StateEventAuthority.IsAuthorOfItem(_firearm)", StringComparison.Ordinal);
            int prepare = code.IndexOf(".RequestAutomationPreparation(", StringComparison.Ordinal);
            int shot = code.IndexOf("_firearm.TryToShootRound(", StringComparison.Ordinal);

            Assert.GreaterOrEqual(check, 0, "BotGunner не проверяет автора предмета перед выстрелом.");
            Assert.GreaterOrEqual(prepare, 0, "BotGunner готовит оружие не через RequestAutomationPreparation.");
            Assert.GreaterOrEqual(shot, 0, "BotGunner не стреляет через TryToShootRound.");
            Assert.Less(check, prepare, "Проверка автора должна стоять до подготовки оружия.");
            Assert.Less(check, shot, "Проверка автора должна стоять до TryToShootRound.");
            Assert.AreEqual(-1, code.IndexOf("SetAmmoLeft(", StringComparison.Ordinal),
                "Бот не доливает магазин сеттером: патроны ведёт учёт оружия.");
        }
    }
}
