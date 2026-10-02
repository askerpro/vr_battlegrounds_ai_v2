using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VrBattlegrounds.Tests.Bots
{
    /// <summary>
    /// Issue 23 для ботов (T-48): спуск бота жмётся в <c>Update</c> — синхронизируемый выстрел обязан пройти
    /// проверку автора предмета (<c>StateEventAuthority.IsAuthorOfItem</c>), иначе машина, исполнившая тот же код
    /// не как автор, выстрелит второй раз и удвоит урон. Сторож по исходнику: проверка стоит перед
    /// <c>TryToShootRound</c> и пополнением магазина.
    /// </summary>
    public class BotAuthorityTests
    {
        [Test]
        public void Стрелок_бота_жмёт_спуск_только_как_автор_предмета()
        {
            string code = File.ReadAllText(Path.Combine(Application.dataPath, "Scripts/Bots/BotGunner.cs"));

            int check = code.IndexOf("StateEventAuthority.IsAuthorOfItem(_firearm)", StringComparison.Ordinal);
            int shot = code.IndexOf("_firearm.TryToShootRound(", StringComparison.Ordinal);
            int refill = code.IndexOf("_firearm.SetAmmoLeft(", StringComparison.Ordinal);

            Assert.GreaterOrEqual(check, 0, "BotGunner не проверяет автора предмета перед выстрелом.");
            Assert.Less(check, shot, "Проверка автора должна стоять до TryToShootRound.");
            Assert.Less(check, refill, "Проверка автора должна стоять до пополнения магазина.");
        }
    }
}
