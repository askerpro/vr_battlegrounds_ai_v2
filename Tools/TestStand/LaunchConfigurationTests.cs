using System.Text.Json;
#if LAUNCH_CONFIGURATION_PRESENT
using VrBattlegrounds.DevTools;
#endif

internal static class LaunchConfigurationTests
{
    internal static void Run(Action<string, Action> test)
    {
#if !LAUNCH_CONFIGURATION_PRESENT
        test("единый конфиг запуска и checkout profile доступны", () => throw new Exception("PlayLaunch configuration/store отсутствуют"));
#else
        test("чтение отсутствующего профиля не пишет файлы", () =>
        {
            WithStore((store, directory) => { Check(store.Read().SchemaVersion == 1 && !Directory.Exists(Path.Combine(directory, "UserSettings"))); });
        });
        test("профиль одного checkout изолирован от другого", () =>
        {
            WithStore((first, directory) => {
                var config = first.Read(); config.BotCount = 3; first.Save(config);
                var second = Store(Path.Combine(directory, "other"));
                Check(first.Read().BotCount == 3 && second.Read().BotCount == 0);
            });
        });
        test("битый и чужой schema профиль не становится defaults", () =>
        {
            WithStore((store, directory) => {
                Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)); File.WriteAllText(store.FilePath, "{");
                Denied(() => store.Read(), "ProfileInvalid");
                File.WriteAllText(store.FilePath, "{\"SchemaVersion\":9}");
                Denied(() => store.Read(), "SchemaUnsupported");
            });
        });
        test("два request owner запрещены, освобождение идемпотентно", () =>
        {
            var owner = new PlayLaunchRequestOwner(); var lease = owner.Acquire("agent", new PlayLaunchConfiguration());
            Denied(() => owner.Acquire("window", new PlayLaunchConfiguration()), "RequestOwnerBusy");
            lease.Dispose(); lease.Dispose(); Check(!owner.IsActive);
        });
        test("конфигурация request заморожена и не меняет профиль", () =>
        {
            WithStore((store, directory) => {
                var owner = new PlayLaunchRequestOwner(); var config = store.Read(); config.BotCount = 2;
                using (owner.Acquire("agent", config)) {
                    config.BotCount = 9; var returned = owner.Configuration; returned.BotCount = 7;
                    Check(owner.Configuration.BotCount == 2 && store.Read().BotCount == 0);
                }
            });
        });
        test("активный request защищает постоянный профиль", () =>
        {
            var directory = Temp();
            try {
                var owner = new PlayLaunchRequestOwner();
                var store = Store(directory, () => owner.IsActive);
                using (owner.Acquire("agent", new PlayLaunchConfiguration())) Denied(() => store.Save(new PlayLaunchConfiguration()), "RequestOwnerBusy");
                Check(!Directory.Exists(Path.Combine(directory, "UserSettings")));
            } finally { Directory.Delete(directory, true); }
        });
        test("неверные role, counts и сценовые пути отклоняются", () =>
        {
            Denied(() => new PlayLaunchConfiguration { Role = "unknown" }.Validate(), "RoleInvalid");
            Denied(() => new PlayLaunchConfiguration { ClientCount = -1 }.Validate(), "CountInvalid");
            Denied(() => new PlayLaunchConfiguration { BotCount = -1 }.Validate(), "CountInvalid");
            Denied(() => new PlayLaunchConfiguration { SceneSource = "scene", ScenePath = "../map.unity" }.Validate(), "ScenePathInvalid");
        });
        test("Client+Host topology поддержана, роли не мутируют через копию", () =>
        {
            var config = new PlayLaunchConfiguration { Role = "client", ClientCount = 1, AdditionalPlayerRoles = new[] { "host" } };
            config.Validate(); var copy = config.Copy(); copy.AdditionalPlayerRoles[0] = "client";
            Check(config.HasOwnedServer && config.RoleForParticipant(1) == "host");
            Denied(() => new PlayLaunchConfiguration { Role = "server", ClientCount = 1, AdditionalPlayerRoles = new[] { "host" } }.Validate(), "TopologyInvalid");
        });
        test("пустой список после Unity JSON сохраняет роли клиентов по умолчанию", () =>
        {
            var config = new PlayLaunchConfiguration { Role = "server", ClientCount = 2, AdditionalPlayerRoles = Array.Empty<string>() };
            config.Validate();
            Check(config.RoleForParticipant(0) == "server" && config.RoleForParticipant(1) == "client" && config.RoleForParticipant(2) == "client");
            Denied(() => new PlayLaunchConfiguration { ClientCount = 2, AdditionalPlayerRoles = new[] { "client" } }.Validate(), "TopologyInvalid");
        });
        test("старая lease не отзывает новый запуск того же owner", () =>
        {
            var owner = new PlayLaunchRequestOwner(); var first = owner.Acquire("agent", new PlayLaunchConfiguration());
            first.Dispose(); using var second = owner.Acquire("agent", new PlayLaunchConfiguration());
            first.Dispose(); Check(owner.IsActive);
        });
#endif
    }
#if LAUNCH_CONFIGURATION_PRESENT
    private static PlayLaunchProfileStore Store(string directory, Func<bool> active = null) => new PlayLaunchProfileStore(directory,
        s => JsonSerializer.Deserialize<PlayLaunchConfiguration>(s, new JsonSerializerOptions { IncludeFields = true }),
        c => JsonSerializer.Serialize(c, new JsonSerializerOptions { IncludeFields = true }), active);
    private static string Temp() { string path = Path.Combine(Path.GetTempPath(), "vrb-launch-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void WithStore(Action<PlayLaunchProfileStore, string> action) { string path = Temp(); try { action(Store(path), path); } finally { Directory.Delete(path, true); } }
    private static void Denied(Action action, string code) { try { action(); } catch (PlayLaunchException error) { Check(error.Code == code); return; } throw new Exception("ожидался отказ " + code); }
    private static void Check(bool condition) { if (!condition) throw new Exception("нарушен launch contract"); }
#endif
}
