using System;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Серверные маркеры отправки NetworkTransform тел без владельца (бот, контрольная цель) — net-round1, attempt-6.
    /// Только чтение: слушает исходящие сообщения Mirror (<c>NetworkDiagnostics.OutMessageEvent</c>) и стадию UXR.
    /// <list type="bullet">
    /// <item>каждая отправка <c>RpcServerToClientSync</c> NT корня, камеры и кистей: байт изменённых осей из полезной нагрузки
    /// (что фактически ушло), текущая позиция цели NT (что прочитал <c>Construct</c>), счётчики по осям за case, паузы
    /// отправки дольше порога сброса буфера получателя (bufferResetMultiplier × sendInterval);</item>
    /// <item>спавн тела каждому наблюдателю (<c>SpawnMessage</c>): локальный Y камеры и Y запястий, ушедшие в спавн, и кадр
    /// первой записи драйвера (<c>BotAvatarDriver.AppliedFrame</c>) — ушла ли камера в спавн до записи головы.</item>
    /// </list>
    /// Создаётся до <c>BotStandSession.Begin</c> (спавн первого тела case попадает в замер), пишет
    /// <c>result.Metrics["netSendMarkers"]</c>.
    /// </summary>
    public sealed class BotStandNetSendMarkers : IDisposable
    {
        private const int MaxSeries = 4000;
        private const int MaxSpawns = 32;
        private const int MaxAvatars = 16;
        /// <summary>Окно прогрева ключевыми снимками после первой отправки (доработка патча Mirror 3), секунды.</summary>
        private const double WarmupSeconds = 0.5;

        private sealed class NtSend
        {
            public string Role;
            public NetworkTransformUnreliable Nt;
            public int Sends, Deliveries, PosX, PosY, PosZ, Rot, Scale, UnchangedOnly, Silences;
            /// <summary>Полные снимки (все оси) за case и в первые <see cref="WarmupSeconds"/> после первой отправки этого NT.</summary>
            public int FullSends, FullSendsWarmup, SendsWarmup;
            public double LastTime = double.NaN, ResetCheck, FirstTime = double.NaN;
            /// <summary>Настройки NT, снятые при первой отправке: к отчёту тело (призрак, прежнее тело) может быть уничтожено.</summary>
            public Dictionary<string, object> Settings;
            public float MaxSilenceMs;
            public int LastFrame = -1;
            /// <summary>[t (NetworkTime.localTime = метка батча), кадр, флаги, x, y, z текущей позиции цели].</summary>
            public readonly List<object> Series = new List<object>();
        }

        private sealed class AvatarState
        {
            public uint NetId;
            public PlayerController Player;
            public UxrAvatar Avatar;
            public BotAvatarDriver Driver;
            public string Token, Name;
            public int FirstDriverFrame = -1;
            public double FirstDriverTime;
            public bool DriverWroteBeforeTracking;
            public object CameraLocalAfterFirstWrite;
            public int LastSpawnFrame = -1;
            public readonly List<Dictionary<string, object>> Spawns = new List<Dictionary<string, object>>();
            public readonly Dictionary<byte, NtSend> Nts = new Dictionary<byte, NtSend>();
        }

        private readonly Dictionary<uint, AvatarState> _avatars = new Dictionary<uint, AvatarState>();
        private readonly HashSet<uint> _ignored = new HashSet<uint>();
        private readonly List<AvatarState> _order = new List<AvatarState>();
        private string _error;
        private bool _disposed;

        public BotStandNetSendMarkers()
        {
            BotStandNtWire.EnsureRegistered();
            NetworkDiagnostics.OutMessageEvent += OnOut;
            UxrManager.StageUpdating += OnStage;
        }

        private void OnOut(NetworkDiagnostics.MessageInfo info)
        {
            if (_disposed || !NetworkServer.active) return;
            try
            {
                if (info.message is SpawnMessage spawn) { RecordSpawn(spawn.netId, info.count); return; }
                if (!(info.message is RpcMessage rpc) || !BotStandNtWire.IsSyncRpc(rpc.functionHash)) return;
                AvatarState avatar = Avatar(rpc.netId);
                if (avatar == null) return;
                NtSend send = Nt(avatar, rpc.componentIndex);
                if (send == null) return;
                // Одна отправка RPC = по сообщению на каждого готового наблюдателя в том же вызове.
                if (send.LastFrame == Time.frameCount) { send.Deliveries += info.count; return; }
                send.LastFrame = Time.frameCount;
                send.Deliveries += info.count;
                send.Sends++;
                BotStandNtWire.TryRead(rpc.payload, out byte flags, out _);
                if (BotStandNtWire.Unchanged(flags)) send.UnchangedOnly++;
                if (BotStandNtWire.Has(flags, Changed.PosX)) send.PosX++;
                if (BotStandNtWire.Has(flags, Changed.PosY)) send.PosY++;
                if (BotStandNtWire.Has(flags, Changed.PosZ)) send.PosZ++;
                if (BotStandNtWire.RotationSent(flags)) send.Rot++;
                if (BotStandNtWire.Has(flags, Changed.Scale)) send.Scale++;
                double now = NetworkTime.localTime;
                if (double.IsNaN(send.FirstTime)) send.FirstTime = now;
                bool full = BotStandNtWire.Full(flags, send.Nt);
                bool warmup = now - send.FirstTime <= WarmupSeconds;
                if (full) send.FullSends++;
                if (warmup) { send.SendsWarmup++; if (full) send.FullSendsWarmup++; }
                if (!double.IsNaN(send.LastTime))
                {
                    double gap = now - send.LastTime;
                    if (gap > send.ResetCheck) send.Silences++;
                    send.MaxSilenceMs = Mathf.Max(send.MaxSilenceMs, (float)(gap * 1000.0));
                }
                send.LastTime = now;
                if (send.Series.Count < MaxSeries)
                {
                    Vector3 current = BotStandNtWire.Position(send.Nt);
                    send.Series.Add(new object[] { Math.Round(now, 4), Time.frameCount, flags,
                        BotStandNtWire.Num(current.x), BotStandNtWire.Num(current.y), BotStandNtWire.Num(current.z) });
                }
            }
            catch (Exception e) { _error = e.Message; }
        }

        /// <summary>Тело без владельца с PlayerController (бот или контрольная цель); прочие netId — мимо.</summary>
        private AvatarState Avatar(uint netId)
        {
            if (_avatars.TryGetValue(netId, out AvatarState known)) return known;
            if (_ignored.Contains(netId)) return null;
            if (!NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity) || identity == null) return null;
            PlayerController player = identity.GetComponent<PlayerController>();
            if (player == null || identity.connectionToClient != null || _avatars.Count >= MaxAvatars)
            {
                _ignored.Add(netId);
                return null;
            }
            var state = new AvatarState { NetId = netId, Player = player, Avatar = player.GetComponent<UxrAvatar>(), Name = player.name };
            _avatars[netId] = state;
            _order.Add(state);
            return state;
        }

        private static NtSend Nt(AvatarState avatar, byte componentIndex)
        {
            if (avatar.Nts.TryGetValue(componentIndex, out NtSend known)) return known;
            NetworkIdentity identity = avatar.Player != null ? avatar.Player.netIdentity : null;
            if (identity == null || identity.NetworkBehaviours == null || componentIndex >= identity.NetworkBehaviours.Length) return null;
            if (!(identity.NetworkBehaviours[componentIndex] is NetworkTransformUnreliable nt)) return null;
            var send = new NtSend
            {
                Nt = nt, Role = BotStandNtWire.Role(avatar.Avatar, nt), ResetCheck = BotStandNtWire.ResetGapSeconds(nt),
                Settings = new Dictionary<string, object>
                {
                    ["type"] = nt.GetType().Name, ["target"] = BotStandNtWire.Target(nt) != null ? BotStandNtWire.Target(nt).name : null,
                    ["coordinateSpace"] = nt.coordinateSpace.ToString(), ["onlySyncOnChange"] = nt.onlySyncOnChange,
                    ["positionSensitivity"] = nt.positionSensitivity, ["rotationSensitivity"] = nt.rotationSensitivity,
                    ["compressRotation"] = nt.compressRotation, ["bufferResetMultiplier"] = nt.bufferResetMultiplier,
                    ["keyframeInterval"] = BotStandNtWire.Num(BotStandNtWire.KeyframeInterval(nt)),
                    ["keyframeWarmup"] = BotStandNtWire.Num(BotStandNtWire.KeyframeWarmup(nt))
                }
            };
            avatar.Nts[componentIndex] = send;
            return send;
        }

        /// <summary>Спавн каждому наблюдателю: что ушло в полезную нагрузку (сериализация — в этом же вызове, до отправки).</summary>
        private void RecordSpawn(uint netId, int deliveries)
        {
            AvatarState avatar = Avatar(netId);
            if (avatar == null || avatar.Avatar == null) return;
            if (avatar.LastSpawnFrame == Time.frameCount && avatar.Spawns.Count > 0)
            {
                Dictionary<string, object> last = avatar.Spawns[avatar.Spawns.Count - 1];
                last["deliveries"] = (int)last["deliveries"] + deliveries;
                return;
            }
            avatar.LastSpawnFrame = Time.frameCount;
            if (avatar.Spawns.Count >= MaxSpawns) return;
            if (avatar.Driver == null) avatar.Driver = avatar.Player.GetComponent<BotAvatarDriver>();
            Transform camera = avatar.Avatar.CameraTransform;
            Transform right = avatar.Avatar.GetHandBone(UxrHandSide.Right), left = avatar.Avatar.GetHandBone(UxrHandSide.Left);
            avatar.Spawns.Add(new Dictionary<string, object>
            {
                ["t"] = Math.Round(NetworkTime.localTime, 4), ["frame"] = Time.frameCount, ["deliveries"] = deliveries,
                ["cameraLocal"] = camera != null ? BotStandNtWire.Vec(camera.localPosition) : null,
                ["cameraAboveRootM"] = camera != null ? BotStandNtWire.Num(camera.position.y - avatar.Avatar.transform.position.y) : null,
                ["wristRY"] = right != null ? BotStandNtWire.Num(right.position.y) : null,
                ["wristLY"] = left != null ? BotStandNtWire.Num(left.position.y) : null,
                ["driverPresent"] = avatar.Driver != null,
                ["driverAppliedFrame"] = avatar.Driver != null ? avatar.Driver.AppliedFrame : -1
            });
        }

        /// <summary>Кадр первой записи драйвера: Manipulation начинается сразу после записи (AvatarUsingTracking).</summary>
        private void OnStage(UxrUpdateStage stage)
        {
            if (_disposed || stage != UxrUpdateStage.Manipulation) return;
            try
            {
                foreach (AvatarState avatar in _order)
                {
                    if (avatar.Player == null) continue;
                    if (avatar.Token == null && avatar.Player.Session != null) avatar.Token = avatar.Player.Session.DeviceToken;
                    if (avatar.FirstDriverFrame >= 0) continue;
                    if (avatar.Driver == null) avatar.Driver = avatar.Player.GetComponent<BotAvatarDriver>();
                    if (avatar.Driver == null || avatar.Driver.AppliedFrame < 0) continue;
                    avatar.FirstDriverFrame = avatar.Driver.AppliedFrame;
                    avatar.DriverWroteBeforeTracking = avatar.Driver.AppliedFrame != Time.frameCount;
                    avatar.FirstDriverTime = NetworkTime.localTime;
                    Transform camera = avatar.Avatar != null ? avatar.Avatar.CameraTransform : null;
                    avatar.CameraLocalAfterFirstWrite = camera != null ? BotStandNtWire.Vec(camera.localPosition) : null;
                }
            }
            catch (Exception e) { _error = e.Message; }
        }

        public void WriteMetrics(BotStandCaseResult result)
        {
            var avatars = new List<object>();
            foreach (AvatarState avatar in _order)
            {
                var nts = new Dictionary<string, object>();
                foreach (NtSend send in avatar.Nts.Values)
                {
                    nts[send.Role] = new Dictionary<string, object>
                    {
                        ["sends"] = send.Sends, ["deliveries"] = send.Deliveries, ["posX"] = send.PosX, ["posY"] = send.PosY,
                        ["posZ"] = send.PosZ, ["rot"] = send.Rot, ["scale"] = send.Scale, ["unchangedOnly"] = send.UnchangedOnly,
                        ["silencesOverResetCheck"] = send.Silences, ["maxSilenceMs"] = send.MaxSilenceMs,
                        ["resetGapMs"] = send.ResetCheck * 1000.0,
                        ["fullSends"] = send.FullSends, ["sendsFirst500ms"] = send.SendsWarmup, ["fullSendsFirst500ms"] = send.FullSendsWarmup,
                        // Настройки сняты при первой отправке; без них — явно «не найден», а не значения по умолчанию.
                        ["ntSettings"] = send.Settings != null ? (object)send.Settings : "не найден",
                        ["ntAliveAtReport"] = send.Nt != null,
                        ["series"] = send.Series
                    };
                }
                string token = avatar.Token ?? string.Empty;
                avatars.Add(new Dictionary<string, object>
                {
                    ["netId"] = avatar.NetId, ["name"] = avatar.Name,
                    ["kind"] = token.StartsWith(BotDirector.TokenPrefix, StringComparison.Ordinal) ? "bot" :
                        token.StartsWith("bot-stand-target-", StringComparison.Ordinal) ? "target" : "ownerless",
                    ["spawns"] = avatar.Spawns,
                    ["firstDriverWrite"] = new Dictionary<string, object>
                    {
                        ["frame"] = avatar.FirstDriverFrame, ["t"] = avatar.FirstDriverFrame >= 0 ? (object)Math.Round(avatar.FirstDriverTime, 4) : null,
                        ["wroteBeforeTracking"] = avatar.DriverWroteBeforeTracking, ["cameraLocalAfter"] = avatar.CameraLocalAfterFirstWrite
                    },
                    ["nts"] = nts
                });
            }
            result.Metrics["netSendMarkers"] = new Dictionary<string, object>
            {
                ["avatars"] = avatars,
                ["mirrorModel"] = BotStandNtWire.ModelName,
                ["error"] = _error,
                ["note"] = "Сервер, тела без владельца. Каждая отправка RpcServerToClientSync: флаги из полезной нагрузки (что ушло), " +
                           "series = [t = NetworkTime.localTime (= метка батча у клиента), кадр, флаги (биты Mirror.Changed: 1 PosX, 2 PosY, " +
                           "4 PosZ, 8 CompressRot, 112 Rot), x, y, z текущей позиции цели NT в её пространстве]. silencesOverResetCheck — " +
                           "паузы между отправками дольше resetGapMs (bufferResetMultiplier × sendInterval + сдвиг ключа снимка) — " +
                           "порога сброса буфера клиента: следующий пакет у клиента достраивает " +
                           "непереданные оси из трансформа. spawns — что ушло в SpawnMessage (камера локально, запястья мир) и был ли к " +
                           "этому кадру записан драйвер; firstDriverWrite — первая запись драйвера (камера после неё). ntSettings — настройки NT, " +
                           "снятые при первой отправке (\"не найден\" — NTU не прочитан); ntAliveAtReport=false — тело уничтожено к отчёту " +
                           "(призрак, прежнее тело). fullSends — снимки со всеми осями (ключевые и первый), fullSendsFirst500ms — в окне " +
                           "прогрева 0,5 с после первой отправки этого NT."
            };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            NetworkDiagnostics.OutMessageEvent -= OnOut;
            UxrManager.StageUpdating -= OnStage;
            if (_error != null) GameLog.Debug.Warning("[BotStandNetSendMarkers] " + _error);
        }
    }
}
