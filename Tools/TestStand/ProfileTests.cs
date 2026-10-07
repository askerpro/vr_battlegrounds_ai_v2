using VrBattlegrounds.Core;
using UnityEngine;

internal static class ProfileTests
{
    internal static void Run(Action<string, Action> test)
    {
#if !PROFILE_SCOPES_PRESENT
        test("временный профиль и идентичность доступны", () => throw new Exception("отсутствует владелец временной идентичности"));
#else
        test("профиль восстанавливает отсутствие override", () =>
        {
            string original = LocalClientProfile.OverrideStateKey;
            using (LocalClientProfile.BeginTemporaryOverride(ClientDeviceType.PC, true, GameRole.Spectator))
            {
                Check(LocalClientProfile.HasTemporaryOverride && LocalClientProfile.IsLocalAdmin && LocalClientProfile.LocalRole == GameRole.Spectator);
                LocalClientProfile.SetDebugOverride(ClientDeviceType.VR, false, GameRole.Player);
                Check(LocalClientProfile.LocalRole == GameRole.Spectator);
            }
            Check(!LocalClientProfile.HasTemporaryOverride && LocalClientProfile.OverrideStateKey == original);
        });
        test("два владельца профиля одновременно запрещены", () =>
        {
            using var first = LocalClientProfile.BeginTemporaryOverride(ClientDeviceType.PC, false, GameRole.Player);
            bool denied = false;
            try { LocalClientProfile.BeginTemporaryOverride(ClientDeviceType.VR, true, GameRole.Player); }
            catch (InvalidOperationException) { denied = true; }
            Check(denied);
        });
        test("сохранённая baseline восстанавливается после потери статики", () =>
        {
            string original = LocalClientProfile.OverrideStateKey;
            LocalClientProfile.SetDebugOverride(ClientDeviceType.PC, true, GameRole.Spectator);
            using (LocalClientProfile.BeginTemporaryOverride(ClientDeviceType.VR, false, GameRole.Player, original))
                Check(LocalClientProfile.LocalDeviceType == ClientDeviceType.VR);
            Check(LocalClientProfile.OverrideStateKey == original && !LocalClientProfile.HasTemporaryOverride);
        });
        test("тестовый токен не пишет PlayerPrefs и восстанавливает fallback", () =>
        {
            PlayerPrefs.SetString("DeviceToken", "persistent");
            int saves = PlayerPrefs.SaveCount;
            using (ClientDeviceIdentity.BeginTemporaryToken("test-token"))
                Check(ClientDeviceIdentity.BaseToken == "test-token" && PlayerPrefs.GetString("DeviceToken") == "persistent");
            Check(!ClientDeviceIdentity.HasTemporaryToken && ClientDeviceIdentity.BaseToken == "persistent" && PlayerPrefs.SaveCount == saves);
        });
        test("второй владелец токена запрещён и Dispose идемпотентен", () =>
        {
            var scope = ClientDeviceIdentity.BeginTemporaryToken("first");
            bool denied = false;
            try { ClientDeviceIdentity.BeginTemporaryToken("second"); }
            catch (InvalidOperationException) { denied = true; }
            scope.Dispose(); scope.Dispose();
            Check(denied && !ClientDeviceIdentity.HasTemporaryToken);
        });
#endif
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("нарушено владение временной конфигурацией"); }
}
