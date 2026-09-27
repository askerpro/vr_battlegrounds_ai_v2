using NUnit.Framework;
using UltimateXR.Networking.Integrations.Net.Mirror;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Захват шлёт серверу <c>UxrMirrorAvatar.CmdRequestAuthority(предмет)</c>. Если предмет
    /// уничтожен раньше, чем команда дошла (снятие снаряжения при смене режима — <c>EquipmentStrip</c>),
    /// Mirror десериализует ссылку как null. Без проверки SDK падал с NRE, и Mirror разрывал
    /// соединение отправителя — на хосте это его собственный клиент (найдено в Play mode).
    /// Патч 13 в <c>Docs/UltimateXR/sdk-patches.md</c>.
    /// </summary>
    public class AuthorityRequestForDestroyedTests : MirrorTestHarness
    {
        [Test]
        public void Запрос_власти_над_уничтоженным_предметом_не_бросает()
        {
            SilenceMirrorNoise();

            UxrMirrorAvatar avatar = CreateNetworkComponent<UxrMirrorAvatar>("Avatar");
            SpawnOnServer(avatar);

            Assert.DoesNotThrow(() => InvokePrivateMethod(avatar, "UserCode_CmdRequestAuthority__NetworkIdentity", new object[] { null }),
                "Команда о уже уничтоженном предмете бросила исключение — Mirror разорвал бы соединение игрока.");
        }
    }
}
