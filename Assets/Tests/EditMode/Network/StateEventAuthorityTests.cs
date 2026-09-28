using NUnit.Framework;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Правило авторства канала состояния (<see cref="StateEventAuthority"/>): событие предмета в руке
    /// шлёт только автор держащего аватара. Сквозная проверка — выстрел клиента в сетевой игре даёт одну
    /// пулю (Quest + выделенный сервер); здесь — таблица решений.
    /// </summary>
    public class StateEventAuthorityTests
    {
        [TestCase(true,  true,  false, ExpectedResult = true,  TestName = "Клиент_свой_аватар_автор")]
        [TestCase(false, true,  false, ExpectedResult = false, TestName = "Клиент_чужой_аватар_не_автор")]
        [TestCase(false, true,  true,  ExpectedResult = false, TestName = "Сервер_аватар_игрока_не_автор")]
        [TestCase(false, false, true,  ExpectedResult = true,  TestName = "Сервер_аватар_без_владельца_автор")]
        [TestCase(true,  false, true,  ExpectedResult = true,  TestName = "Хост_свой_аватар_автор")]
        [TestCase(false, false, false, ExpectedResult = false, TestName = "Клиент_аватар_без_владельца_не_автор")]
        public bool Автор_действий_аватара(bool ownedHere, bool hasOwnerConnection, bool isServer)
        {
            return StateEventAuthority.IsAuthor(ownedHere, hasOwnerConnection, isServer);
        }

        /// <summary>
        /// Предмет в руке: автор — машина держащего аватара. Так решают затвор (Reload) и канал:
        /// оружие в руке чужого игрока эта машина не перезаряжает — перезарядка придёт событием.
        /// </summary>
        [Test]
        public void Автор_предмета_в_руке_машина_держащего()
        {
            System.Collections.Generic.List<Player.TwoHandGrabCase> cases = Player.TwoHandGrabCases.Collect();
            Assume.That(cases.Count, Is.GreaterThan(0), "Нет пары оружие–аватар с позами хвата.");
            Player.TwoHandGrabCase c = cases[0];

            System.Func<UltimateXR.Avatar.UxrAvatar, bool> saved = StateEventAuthority.IsAuthoredHere;
            using (var harness = new Player.TwoHandGrabHarness(c.WeaponPath, c.AvatarPath, c.SupportPoint))
            {
                try
                {
                    StateEventAuthority.IsAuthoredHere = _ => false;
                    Assert.IsFalse(StateEventAuthority.IsAuthorOfItem(harness.Weapon.transform),
                        "Оружие в руке чужого игрока: эта машина не автор — иначе перезарядка затвором размножится.");

                    StateEventAuthority.IsAuthoredHere = _ => true;
                    Assert.IsTrue(StateEventAuthority.IsAuthorOfItem(harness.Weapon.transform),
                        "Оружие в руке своего аватара: автор — эта машина.");
                }
                finally
                {
                    StateEventAuthority.IsAuthoredHere = saved;
                }
            }
        }
    }
}
