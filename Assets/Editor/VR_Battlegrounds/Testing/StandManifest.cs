using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.EditorTools.TestStand
{
    [Serializable]
    internal sealed class StandSetting
    {
        public string Key;
        public bool HadKey;
        public bool Value;
    }

    /// <summary>Descriptor принадлежит координатору; участники только читают его.</summary>
    [Serializable]
    internal sealed class StandManifest
    {
        public string RunId;
        public int OwnerPid;
        public string Phase;
        public string[] Participants;
        public string PreviousScenarioName;
        public string PreviousScenarioPath;
        public int NetworkPort;
        public int DiscoveryPort;
        public StandSetting[] Settings;
        public bool NativeStopRequested;
        public StandParticipantState[] ReleasedParticipants;
        public string Owner;
        public PlayLaunchConfiguration Configuration;
        public string StartScenePath;
        public string TargetScenePath;
        public string SceneKind;
        public bool MarkerProbe;
        public bool ExpectTargetScene;
        public string PreviousStartScenePath;

        internal static string ProjectRoot
        {
            get
            {
                string path = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
                int clone = path.IndexOf("/Library/VP/", StringComparison.OrdinalIgnoreCase);
                if (clone >= 0) return path.Substring(0, clone);
                return Path.GetDirectoryName(path);
            }
        }

        internal static string FilePath => Path.Combine(ProjectRoot, "Temp", "VRBattlegroundsTestStand", "active.json");
        internal static int CurrentPid => Process.GetCurrentProcess().Id;

        // Попыток доступа к файлу при IOException (координатор и клоны MPPM читают/пишут его каждый кадр).
        private const int IoAttempts = 5;
        private const int IoRetryDelayMs = 2;

        // Последний успешно прочитанный текст descriptor этого процесса: ответ при кратком сбое чтения.
        private static string _lastText;

        /// <summary>
        /// null — прогона нет: файла нет, он пуст/битый или координатор завершился. Кратковременный сбой
        /// доступа (координатор в этот момент заменяет файл) не означает «прогона нет»: возвращается последнее
        /// прочитанное значение, иначе участник покинул бы идущий прогон.
        /// </summary>
        internal static StandManifest Read()
        {
            string text = ReadText();
            if (text == null) return null;
            try
            {
                var value = JsonUtility.FromJson<StandManifest>(text);
                if (value == null || string.IsNullOrEmpty(value.RunId) || value.OwnerPid < 1) return null;
                using (var owner = Process.GetProcessById(value.OwnerPid))
                    if (owner.HasExited) return null;
                return value;
            }
            catch (Exception) { return null; }
        }

        private static string ReadText()
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    // FileShare.Delete: открытый читателем файл не мешает координатору заменить или удалить его.
                    using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false)))
                        return _lastText = reader.ReadToEnd();
                }
                catch (FileNotFoundException) { return _lastText = null; }
                catch (DirectoryNotFoundException) { return _lastText = null; }
                catch (IOException)
                {
                    if (attempt >= IoAttempts) return _lastText;
                    System.Threading.Thread.Sleep(IoRetryDelayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt >= IoAttempts) return _lastText;
                    System.Threading.Thread.Sleep(IoRetryDelayMs);
                }
            }
        }

        /// <summary>Только координатор. Атомарная замена; при занятости файла — повтор, затем исключение вызывающему.</summary>
        internal void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".new";
            File.WriteAllText(temporary, JsonUtility.ToJson(this), new System.Text.UTF8Encoding(false));
            Retry(() =>
            {
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            });
        }

        /// <summary>Только координатор: конец прогона.</summary>
        internal static void Delete() => Retry(() => { if (File.Exists(FilePath)) File.Delete(FilePath); });

        private static void Retry(Action action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { action(); return; }
                catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < IoAttempts)
                {
                    System.Threading.Thread.Sleep(IoRetryDelayMs);
                }
            }
        }

        internal static string ParticipantName()
        {
            if (Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor) return "main";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-name") return args[i + 1];
            return "unknown";
        }

        internal bool Admits(string participant) => Participants != null && Array.IndexOf(Participants, participant) >= 0;
        internal static string CommandEvent(string run, string session) => "vrb-stand-command-" + run + "-" + session;
        internal const string HelloEvent = "vrb-test-stand-v1-hello";
    }

    [Serializable]
    public sealed class StandParticipantState
    {
        public string RunId;
        public string ParticipantId;
        public string ProcessSessionId;
        public int ProcessId;
        public string Role;
        public string NetworkRole;
        public string RequestedRole;
        public bool Playing;
        public bool Server;
        public bool Client;
        public bool Connected;
        public int NetworkPort;
        public int DiscoveryPort;
        public bool Active;
        public int MarkerCount;
        public bool ProfileTemporary;
        public string DeviceType;
        public string GameRole;
        public bool Admin;
        public bool ProfileRestored;
        public bool PersistentTokenUnchanged;
        public string Scene;
        public bool MapPlayable;
        public double LastSeenAgeSeconds;
        public string MapRunKey;
        public bool MapRunValid;
        public string StartupError;
        public int ConnectionEpoch;
        public uint AvatarNetId;
        public uint SessionNetId;
        public int ServerSessions;
        public bool ClientManagerReady;
        public string DeviceToken;
        public string E2EStatus;
        public bool E2EFinished;
        public bool E2EPassed;
    }

    [Serializable]
    public sealed class StandOperation
    {
        public string RequestId;
        public bool Completed;
        public bool EffectUnknown;
        public StandReply Reply;
    }

    [Serializable]
    public sealed class StandStatus
    {
        public string RunId;
        public string Phase;
        public bool Playing;
        public bool CleanupPassed;
        public string LastRunId;
        public StandParticipantState[] Participants;
    }
}
