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

        internal static StandManifest Read()
        {
            if (!File.Exists(FilePath)) return null;
            try
            {
                var value = JsonUtility.FromJson<StandManifest>(File.ReadAllText(FilePath));
                if (value == null || string.IsNullOrEmpty(value.RunId) || value.OwnerPid < 1) return null;
                using (var owner = Process.GetProcessById(value.OwnerPid))
                    if (owner.HasExited) return null;
                return value;
            }
            catch (Exception) { return null; }
        }

        internal void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".new";
            File.WriteAllText(temporary, JsonUtility.ToJson(this), new System.Text.UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
            else File.Move(temporary, FilePath);
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
