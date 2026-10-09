using System;
using System.IO;
using System.Text;

namespace VrBattlegrounds.DevTools
{
    /// <summary>Checkout-local storage. Чтение defaults не пишет профиль; кодек не зависит от Unity.</summary>
    public sealed class PlayLaunchProfileStore
    {
        private readonly Func<string, PlayLaunchConfiguration> _decode;
        private readonly Func<PlayLaunchConfiguration, string> _encode;
        private readonly Func<bool> _requestActive;
        public string FilePath { get; }

        public PlayLaunchProfileStore(string checkoutRoot, Func<string, PlayLaunchConfiguration> decode,
            Func<PlayLaunchConfiguration, string> encode, Func<bool> requestActive = null)
        {
            if (string.IsNullOrWhiteSpace(checkoutRoot)) throw new ArgumentException("Нужен checkout root.");
            FilePath = Path.Combine(Path.GetFullPath(checkoutRoot), "UserSettings", "VrBattlegrounds", "play-launch.json");
            _decode = decode ?? throw new ArgumentNullException(nameof(decode));
            _encode = encode ?? throw new ArgumentNullException(nameof(encode));
            _requestActive = requestActive ?? (() => false);
        }

        public PlayLaunchConfiguration Read()
        {
            if (!File.Exists(FilePath)) return new PlayLaunchConfiguration();
            PlayLaunchConfiguration configuration;
            try { configuration = _decode(File.ReadAllText(FilePath, new UTF8Encoding(false, true))); }
            catch (Exception e) { throw new PlayLaunchException("ProfileInvalid", "Профиль не прочитан: " + e.Message); }
            if (configuration == null) throw new PlayLaunchException("ProfileInvalid", "Пустой профиль.");
            configuration.Validate();
            return configuration.Copy();
        }

        public void Save(PlayLaunchConfiguration configuration)
        {
            if (_requestActive()) throw new PlayLaunchException("RequestOwnerBusy", "Постоянный профиль защищён активным запросом.");
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            configuration.Validate();
            string json = _encode(configuration.Copy());
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".new";
            try
            {
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (_requestActive()) throw new PlayLaunchException("RequestOwnerBusy", "Запрос появился во время сохранения профиля.");
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
