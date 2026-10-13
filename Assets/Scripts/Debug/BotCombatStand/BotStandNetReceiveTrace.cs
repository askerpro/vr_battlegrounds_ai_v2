using System;
using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Клиентские маркеры приёма NetworkTransform чужих аватаров (net-round1, attempt-6) для
    /// <see cref="BotStandNetObserver"/>. Только чтение.
    /// <para>
    /// Каждый <c>RpcServerToClientSync</c> разбирается в <c>NetworkDiagnostics.InMessageEvent</c> — до обработчика NT. В этот
    /// момент воспроизводится решение <c>OnServerToClientSync</c>: будет ли <c>ResetState</c> (буфер пуст или пауза больше
    /// порога) и откуда <c>UpdateSyncData</c> возьмёт непереданные оси — из последнего снимка или из текущего трансформа
    /// (<c>GetPosition</c>), то есть из итога прошлого кадра после стадий UXR. Такая подстановка, отличающаяся от прежнего
    /// снимка, — «запечённая» поза клиента (bakedCandidate); строгий bakedFromTransform (|ось − сервер| &gt; 1 см) считает
    /// analyze.py по серверной серии той же метки.
    /// </para>
    /// Камера: Y локально в кадре спавна тела у клиента и число принятых пакетов с PosY; RPC до спавна объекта (Mirror
    /// молча отбрасывает) — отдельным счётчиком по netId.
    /// <para>
    /// Модель (<see cref="BotStandNtWire.ModelName"/>): legacy — Mirror без патча, после сброса оси из трансформа; patched
    /// — патч Mirror 3: из последнего полученного значения (<c>clientReceived*</c>, читается отражением до обработчика),
    /// трансформ — только пока ничего не получено. Строгий маркер patched считает analyze.py по серии <c>series</c>
    /// (последнее полученное против серверной позиции той же метки дольше 1 с без смены тела).
    /// </para>
    /// Оседание тела, появившегося при подключённом клиенте (attempt-8): первые 3 с после кадра спавна у клиента — высота
    /// камеры и запястья после NT каждый кадр (<c>settle.samples</c>) в системе координат их NT — той же, что серверная серия
    /// netSendMarkers. Время оседания считает analyze.py против серверной серии (рост калибровки бота клиенту неизвестен).
    /// </summary>
    public sealed class BotStandNetReceiveTrace
    {
        private const int MaxResetEvents = 240;
        private const int MaxPosYEvents = 80;
        private const int MaxDroppedEvents = 20;
        private const float BakedPositionEpsilon = 1e-4f;
        private const float FinalEqualityEpsilon = 1e-5f;
        private const float BakedRotationDegrees = 0.01f;
        private const int MaxSeries = 4000;
        private const float SettleWindowSeconds = 3f;

        private sealed class NtTrace
        {
            public string Role;
            public NetworkTransformUnreliable Nt;
            public int Messages, PosX, PosY, PosZ, Rot, UnchangedOnly, GapResets, EmptyFills;
            public int BakedX, BakedY, BakedZ, BakedRot, FillEqualsPrevFinal, FillDiffersPrevFinal, PrevFinalMissing;
            public double BakedYSumM, BakedYAbsSumM, BakedRotSumDeg;
            public int SnapshotConfirmed, SnapshotMismatch;
            public Vector3 PrevFinal;
            public int PrevFinalFrame = -1;
            public Vector3 LastReconstructed;
            public Quaternion LastRotation = Quaternion.identity;
            public bool HasReconstructed;
            public double ExpectedKey = double.NaN;
            public readonly List<object> ResetEvents = new List<object>();
            public readonly List<object> PosYEvents = new List<object>();
            /// <summary>Источник непереданных осей: последний снимок буфера / последнее полученное (патч) / трансформ.</summary>
            public int FillBuffer, FillReceived, FillTransform;
            /// <summary>Каждый пакет: [метка, кадр, флаги, источник (0 буфер, 1 полученное, 2 трансформ), x, y, z восстановленной позиции].</summary>
            public readonly List<object> Series = new List<object>();
        }

        private sealed class AvatarTrace
        {
            public NetworkIdentity Identity;
            public UxrAvatar Avatar;
            public readonly Dictionary<byte, NtTrace> Nts = new Dictionary<byte, NtTrace>();
            public readonly List<object> Spawns = new List<object>();
            // Оседание после спавна у подключённого клиента.
            public float SettleStart = -1f, SpawnSinceConnect = float.NaN;
            public NetworkTransformBase ShiftSource, WristRNt, WristLNt;
            public readonly List<object> SettleSamples = new List<object>();
        }

        private sealed class Dropped
        {
            public int Count, WithPosY;
            public readonly List<object> Events = new List<object>();
        }

        private struct PendingSpawn
        {
            public uint NetId;
            public int Epoch, Frame;
            public double Stamp;
        }

        private readonly Dictionary<string, AvatarTrace> _avatars = new Dictionary<string, AvatarTrace>();
        private readonly Dictionary<string, Dropped> _dropped = new Dictionary<string, Dropped>();
        private readonly List<PendingSpawn> _pendingSpawns = new List<PendingSpawn>();
        private readonly HashSet<uint> _notAvatars = new HashSet<uint>();
        private readonly Dictionary<int, float> _epochStart = new Dictionary<int, float>();
        private string _error;

        public BotStandNetReceiveTrace() => BotStandNtWire.EnsureRegistered();

        public string Error => _error;

        private static string Key(int epoch, uint netId) => epoch + ":" + netId;

        /// <summary>Входящее сообщение (до обработчика Mirror). <paramref name="epoch"/> — эпоха подключения наблюдателя.</summary>
        public void OnMessage(NetworkDiagnostics.MessageInfo info, int epoch)
        {
            try
            {
                if (!_epochStart.ContainsKey(epoch)) _epochStart[epoch] = Time.unscaledTime;
                if (info.message is SpawnMessage spawn)
                {
                    _notAvatars.Remove(spawn.netId);
                    _pendingSpawns.Add(new PendingSpawn { NetId = spawn.netId, Epoch = epoch, Frame = Time.frameCount, Stamp = Stamp() });
                    return;
                }
                if (!(info.message is RpcMessage rpc) || !BotStandNtWire.IsSyncRpc(rpc.functionHash)) return;
                BotStandNtWire.TryRead(rpc.payload, out byte flags, out Vector3 sent);
                if (!NetworkClient.spawned.TryGetValue(rpc.netId, out NetworkIdentity identity) || identity == null)
                {
                    RecordDropped(Key(epoch, rpc.netId), rpc.componentIndex, flags, sent);
                    return;
                }
                AvatarTrace avatar = Avatar(epoch, identity);
                if (avatar == null) return;
                NtTrace trace = Nt(avatar, rpc.componentIndex);
                if (trace == null || trace.Nt.isOwned) return;
                Receive(trace, flags, sent);
            }
            catch (Exception e) { _error = e.Message; }
        }

        private static double Stamp() => NetworkClient.connection != null ? NetworkClient.connection.remoteTimeStamp : double.NaN;

        private void RecordDropped(string key, byte componentIndex, byte flags, Vector3 sent)
        {
            if (!_dropped.TryGetValue(key, out Dropped dropped)) _dropped[key] = dropped = new Dropped();
            dropped.Count++;
            if (BotStandNtWire.Has(flags, Changed.PosY)) dropped.WithPosY++;
            if (dropped.Events.Count < MaxDroppedEvents)
                dropped.Events.Add(new object[] { Math.Round(Stamp(), 4), Time.frameCount, componentIndex, flags, BotStandNtWire.Num(sent.y) });
        }

        private AvatarTrace Avatar(int epoch, NetworkIdentity identity)
        {
            string key = Key(epoch, identity.netId);
            if (_avatars.TryGetValue(key, out AvatarTrace known) && known.Identity == identity) return known;
            if (_notAvatars.Contains(identity.netId)) return null;
            PlayerController player = identity.GetComponent<PlayerController>();
            if (player == null || identity.isLocalPlayer)
            {
                _notAvatars.Add(identity.netId);
                return null;
            }
            // Повторный спавн того же netId в той же эпохе — новые ссылки, счётчики продолжаются.
            if (known != null)
            {
                known.Identity = identity;
                known.Avatar = player.GetComponent<UxrAvatar>();
                known.Nts.Clear();
                return known;
            }
            var avatar = new AvatarTrace { Identity = identity, Avatar = player.GetComponent<UxrAvatar>() };
            _avatars[key] = avatar;
            return avatar;
        }

        private static NtTrace Nt(AvatarTrace avatar, byte componentIndex)
        {
            if (avatar.Nts.TryGetValue(componentIndex, out NtTrace known)) return known;
            NetworkBehaviour[] behaviours = avatar.Identity.NetworkBehaviours;
            if (behaviours == null || componentIndex >= behaviours.Length || !(behaviours[componentIndex] is NetworkTransformUnreliable nt)) return null;
            var trace = new NtTrace { Nt = nt, Role = BotStandNtWire.Role(avatar.Avatar, nt) };
            avatar.Nts[componentIndex] = trace;
            return trace;
        }

        /// <summary>Повтор решения <c>OnServerToClientSync</c>/<c>UpdateSyncData</c> до его выполнения.</summary>
        private static void Receive(NtTrace trace, byte flags, Vector3 sent)
        {
            NetworkTransformUnreliable nt = trace.Nt;
            SortedList<double, TransformSnapshot> snapshots = nt.clientSnapshots;
            double stamp = Stamp();
            bool empty = snapshots.Count == 0;
            bool gap = !empty && nt.onlySyncOnChange &&
                       snapshots.Values[snapshots.Count - 1].remoteTime + BotStandNtWire.ResetCheckSeconds(nt) < stamp;
            bool reset = empty || gap;
            Vector3 transformPosition = BotStandNtWire.Position(nt);
            Quaternion transformRotation = BotStandNtWire.Rotation(nt);
            // Последнее полученное (патч 3): до обработчика поле хранит значение прошлого пакета/спавна/телепорта.
            bool received = BotStandNtWire.TryGetClientReceived(nt, out Vector3 receivedPosition, out Quaternion receivedRotation);
            Vector3 previous = !empty ? snapshots.Values[snapshots.Count - 1].position
                : received ? receivedPosition
                : trace.HasReconstructed ? trace.LastReconstructed : new Vector3(float.NaN, float.NaN, float.NaN);
            Quaternion previousRotation = !empty ? snapshots.Values[snapshots.Count - 1].rotation : received ? receivedRotation : trace.LastRotation;
            // Источник непереданных осей: буфер (сброса нет), иначе — патч: полученное, без патча/без полученного: трансформ.
            int source = !reset ? 0 : received ? 1 : 2;
            bool fromTransform = source == 2;
            Vector3 fill = source == 0 ? previous : source == 1 ? receivedPosition : transformPosition;
            Quaternion fillRotation = source == 0 ? previousRotation : source == 1 ? receivedRotation : transformRotation;
            if (source == 0) trace.FillBuffer++;
            else if (source == 1) trace.FillReceived++;
            else trace.FillTransform++;

            bool unchanged = BotStandNtWire.Unchanged(flags);
            bool sentX = !unchanged && BotStandNtWire.Has(flags, Changed.PosX);
            bool sentY = !unchanged && BotStandNtWire.Has(flags, Changed.PosY);
            bool sentZ = !unchanged && BotStandNtWire.Has(flags, Changed.PosZ);
            bool sentRotation = !unchanged && BotStandNtWire.RotationSent(flags);
            var reconstructed = new Vector3(sentX ? sent.x : fill.x, sentY ? sent.y : fill.y, sentZ ? sent.z : fill.z);

            trace.Messages++;
            if (unchanged) trace.UnchangedOnly++;
            if (sentX) trace.PosX++;
            if (sentY) trace.PosY++;
            if (sentZ) trace.PosZ++;
            if (sentRotation) trace.Rot++;

            if (empty) trace.EmptyFills++;
            else if (gap) trace.GapResets++;
            if (reset)
            {
                bool prevFinalKnown = trace.PrevFinalFrame == Time.frameCount - 1;
                bool equalsFinal = prevFinalKnown && Mathf.Abs(transformPosition.x - trace.PrevFinal.x) < FinalEqualityEpsilon &&
                                   Mathf.Abs(transformPosition.y - trace.PrevFinal.y) < FinalEqualityEpsilon &&
                                   Mathf.Abs(transformPosition.z - trace.PrevFinal.z) < FinalEqualityEpsilon;
                if (!prevFinalKnown) trace.PrevFinalMissing++;
                else if (equalsFinal) trace.FillEqualsPrevFinal++;
                else trace.FillDiffersPrevFinal++;
                float dy = fill.y - previous.y;
                float rotationDelta = Quaternion.Angle(fillRotation, previousRotation);
                // Кандидаты запекания — только когда оси действительно взяты из трансформа.
                if (fromTransform)
                {
                    if (!sentX && Mathf.Abs(fill.x - previous.x) > BakedPositionEpsilon) trace.BakedX++;
                    if (!sentY && Mathf.Abs(dy) > BakedPositionEpsilon)
                    {
                        trace.BakedY++;
                        trace.BakedYSumM += dy;
                        trace.BakedYAbsSumM += Mathf.Abs(dy);
                    }
                    if (!sentZ && Mathf.Abs(fill.z - previous.z) > BakedPositionEpsilon) trace.BakedZ++;
                    if (!sentRotation && rotationDelta > BakedRotationDegrees)
                    {
                        trace.BakedRot++;
                        trace.BakedRotSumDeg += rotationDelta;
                    }
                }
                if (trace.ResetEvents.Count < MaxResetEvents)
                {
                    trace.ResetEvents.Add(new Dictionary<string, object>
                    {
                        ["stamp"] = Math.Round(stamp, 4), ["frame"] = Time.frameCount, ["flags"] = flags,
                        ["reset"] = empty ? "empty" : "gap",
                        ["source"] = source == 0 ? "buffer" : source == 1 ? "received" : "transform",
                        ["gapMs"] = empty ? null : (object)Math.Round((stamp - snapshots.Values[snapshots.Count - 1].remoteTime) * 1000.0, 1),
                        ["fill"] = BotStandNtWire.Vec(fill), ["previousSnapshot"] = BotStandNtWire.Vec(previous),
                        ["prevFinal"] = prevFinalKnown ? BotStandNtWire.Vec(trace.PrevFinal) : null,
                        ["fillEqualsPrevFinal"] = prevFinalKnown ? (object)equalsFinal : null,
                        ["reconstructed"] = BotStandNtWire.Vec(reconstructed),
                        ["sent"] = BotStandNtWire.Vec(sent),
                        ["rotationFillDeltaDeg"] = sentRotation ? null : BotStandNtWire.Num(rotationDelta)
                    });
                }
            }
            if (trace.Series.Count < MaxSeries)
                trace.Series.Add(new object[] { Math.Round(stamp, 4), Time.frameCount, flags, source,
                    BotStandNtWire.Num(reconstructed.x), BotStandNtWire.Num(reconstructed.y), BotStandNtWire.Num(reconstructed.z) });
            if (sentY && trace.PosYEvents.Count < MaxPosYEvents)
                trace.PosYEvents.Add(new object[] { Math.Round(stamp, 4), Time.frameCount, BotStandNtWire.Num(sent.y) });

            trace.LastReconstructed = reconstructed;
            // Переданный поворот (сжатый кватернион) не разбирается: берётся из вставленного снимка в OnUpdate.
            trace.LastRotation = fillRotation;
            trace.HasReconstructed = true;
            trace.ExpectedKey = stamp + BotStandNtWire.SnapshotShift(nt);
        }

        /// <summary>
        /// Update наблюдателя (после NT): спавны этого кадра — камера, какой её поставил спавн; сверка вставленного снимка
        /// с восстановленной позицией (проверка повтора решения Mirror).
        /// </summary>
        public void OnUpdate()
        {
            try
            {
                for (int i = _pendingSpawns.Count - 1; i >= 0; i--)
                {
                    PendingSpawn pending = _pendingSpawns[i];
                    _pendingSpawns.RemoveAt(i);
                    if (!NetworkClient.spawned.TryGetValue(pending.NetId, out NetworkIdentity identity) || identity == null) continue;
                    AvatarTrace avatar = Avatar(pending.Epoch, identity);
                    if (avatar == null || avatar.Avatar == null) continue;
                    NetworkTransformUnreliable camera = null, wristR = null, wristL = null;
                    foreach (NetworkBehaviour behaviour in identity.NetworkBehaviours)
                    {
                        if (!(behaviour is NetworkTransformUnreliable nt)) continue;
                        string role = BotStandNtWire.Role(avatar.Avatar, nt);
                        if (role == "camera") camera = nt;
                        else if (role == "wristR") wristR = nt;
                        else if (role == "wristL") wristL = nt;
                    }
                    Transform cameraTarget = camera != null ? BotStandNtWire.Target(camera) : avatar.Avatar.CameraTransform;
                    int snapshots = camera != null ? camera.clientSnapshots.Count : -1;
                    avatar.SettleStart = Time.unscaledTime;
                    avatar.SettleSamples.Clear();
                    avatar.SpawnSinceConnect = _epochStart.TryGetValue(pending.Epoch, out float connectedAt) ? Time.unscaledTime - connectedAt : float.NaN;
                    avatar.ShiftSource = camera;
                    avatar.WristRNt = wristR;
                    avatar.WristLNt = wristL;
                    avatar.Spawns.Add(new Dictionary<string, object>
                    {
                        ["stamp"] = Math.Round(pending.Stamp, 4), ["receivedFrame"] = pending.Frame, ["frame"] = Time.frameCount,
                        ["cameraLocal"] = cameraTarget != null ? BotStandNtWire.Vec(cameraTarget.localPosition) : null,
                        ["cameraAboveRootM"] = cameraTarget != null ? BotStandNtWire.Num(cameraTarget.position.y - avatar.Avatar.transform.position.y) : null,
                        ["cameraSnapshots"] = snapshots,
                        ["cameraNewestSnapshotY"] = snapshots > 0 ? BotStandNtWire.Num(camera.clientSnapshots.Values[snapshots - 1].position.y) : null,
                        ["note"] = "cameraSnapshots=0 — значение из полезной нагрузки спавна (OnDeserialize initialState)"
                    });
                }
                foreach (AvatarTrace avatar in _avatars.Values)
                {
                    if (avatar.Identity == null) continue;
                    SampleSettle(avatar);
                    foreach (NtTrace trace in avatar.Nts.Values)
                    {
                        if (double.IsNaN(trace.ExpectedKey) || trace.Nt == null) continue;
                        if (trace.Nt.clientSnapshots.TryGetValue(trace.ExpectedKey, out TransformSnapshot inserted))
                        {
                            trace.LastRotation = inserted.rotation;
                            if ((inserted.position - trace.LastReconstructed).sqrMagnitude < 1e-10f) trace.SnapshotConfirmed++;
                            else trace.SnapshotMismatch++;
                        }
                        trace.ExpectedKey = double.NaN;
                    }
                }
            }
            catch (Exception e) { _error = e.Message; }
        }

        /// <summary>
        /// Кадр окна оседания (Update наблюдателя — после применения снимков NT): [секунды от спавна, серверное время
        /// показанной позы (NetworkTime.time − сдвиг ключа), камера Y, запястья R/L xyz] — в системе координат своего NT
        /// (как серверная серия); без NT камеры — высота камеры над корнем, без NT запястья — NaN.
        /// </summary>
        private static void SampleSettle(AvatarTrace avatar)
        {
            if (avatar.SettleStart < 0f || avatar.Avatar == null) return;
            float since = Time.unscaledTime - avatar.SettleStart;
            if (since > SettleWindowSeconds) return;
            Transform root = avatar.Avatar.transform, camera = avatar.Avatar.CameraTransform;
            double shown = NetworkTime.time - (avatar.ShiftSource != null ? BotStandNtWire.SnapshotShift(avatar.ShiftSource) : 0.0);
            var none = new Vector3(float.NaN, float.NaN, float.NaN);
            Vector3 r = avatar.WristRNt != null ? BotStandNtWire.Position(avatar.WristRNt) : none;
            Vector3 l = avatar.WristLNt != null ? BotStandNtWire.Position(avatar.WristLNt) : none;
            object cameraY = avatar.ShiftSource != null ? BotStandNtWire.Num(BotStandNtWire.Position(avatar.ShiftSource).y)
                : camera != null ? BotStandNtWire.Num(camera.position.y - root.position.y) : null;
            avatar.SettleSamples.Add(new object[]
            {
                Math.Round(since, 4), Math.Round(shown, 4),
                cameraY,
                BotStandNtWire.Num(r.x), BotStandNtWire.Num(r.y), BotStandNtWire.Num(r.z), BotStandNtWire.Num(l.x), BotStandNtWire.Num(l.y), BotStandNtWire.Num(l.z)
            });
        }

        /// <summary>Итог кадра после всех стадий UXR (<c>UxrManager.AvatarsUpdated</c>) — «итог прошлого кадра» для следующего пакета.</summary>
        public void OnFrameEnd()
        {
            foreach (AvatarTrace avatar in _avatars.Values)
            {
                if (avatar.Identity == null) continue;
                foreach (NtTrace trace in avatar.Nts.Values)
                {
                    if (trace.Nt == null) continue;
                    trace.PrevFinal = BotStandNtWire.Position(trace.Nt);
                    trace.PrevFinalFrame = Time.frameCount;
                }
            }
        }

        /// <summary>Отчёт тела (эпоха, netId аватара): по NT и спавны; отброшенные до спавна RPC.</summary>
        public Dictionary<string, object> Describe(int epoch, uint netId)
        {
            string key = Key(epoch, netId);
            var nts = new Dictionary<string, object>();
            List<object> spawns = null;
            if (_avatars.TryGetValue(key, out AvatarTrace avatar))
            {
                spawns = avatar.Spawns;
                foreach (NtTrace trace in avatar.Nts.Values)
                {
                    nts[trace.Role] = new Dictionary<string, object>
                    {
                        ["messages"] = trace.Messages, ["posX"] = trace.PosX, ["posY"] = trace.PosY, ["posZ"] = trace.PosZ,
                        ["rot"] = trace.Rot, ["unchangedOnly"] = trace.UnchangedOnly,
                        ["gapResets"] = trace.GapResets, ["emptyFills"] = trace.EmptyFills,
                        ["fillEqualsPrevFinal"] = trace.FillEqualsPrevFinal, ["fillDiffersPrevFinal"] = trace.FillDiffersPrevFinal,
                        ["prevFinalMissing"] = trace.PrevFinalMissing,
                        ["bakedCandidates"] = new Dictionary<string, object>
                        {
                            ["x"] = trace.BakedX, ["y"] = trace.BakedY, ["z"] = trace.BakedZ, ["rot"] = trace.BakedRot,
                            ["sumDeltaYM"] = Math.Round(trace.BakedYSumM, 5), ["sumAbsDeltaYM"] = Math.Round(trace.BakedYAbsSumM, 5),
                            ["sumRotDeg"] = Math.Round(trace.BakedRotSumDeg, 3)
                        },
                        ["snapshotCheck"] = new Dictionary<string, object> { ["confirmed"] = trace.SnapshotConfirmed, ["mismatch"] = trace.SnapshotMismatch },
                        ["fillSource"] = new Dictionary<string, object> { ["buffer"] = trace.FillBuffer, ["received"] = trace.FillReceived, ["transform"] = trace.FillTransform },
                        ["resetEvents"] = trace.ResetEvents, ["posYEvents"] = trace.PosYEvents, ["series"] = trace.Series
                    };
                }
            }
            object settle = null;
            if (avatar != null && avatar.SettleSamples.Count > 0)
                settle = new Dictionary<string, object>
                {
                    ["spawnSinceConnectS"] = BotStandNtWire.Num(avatar.SpawnSinceConnect),
                    ["windowS"] = SettleWindowSeconds,
                    ["samples"] = avatar.SettleSamples
                };
            object dropped = null;
            if (_dropped.TryGetValue(key, out Dropped d))
                dropped = new Dictionary<string, object> { ["count"] = d.Count, ["withPosY"] = d.WithPosY, ["events"] = d.Events };
            return new Dictionary<string, object>
            {
                ["model"] = BotStandNtWire.ModelName,
                ["nts"] = nts, ["spawns"] = spawns, ["droppedNotSpawned"] = dropped, ["settle"] = settle,
                ["note"] = "Приём RpcServerToClientSync до обработчика Mirror. gapResets/emptyFills — пакеты, при которых непереданные " +
                           "оси берутся из текущего трансформа (ResetState после паузы / пустой буфер). bakedCandidates — такие оси, " +
                           "отличающиеся от прежнего снимка (> 0,1 мм / 0,01°): поза клиента ушла в снимок; sumDeltaYM — внесённый " +
                           "сдвиг Y. fillEqualsPrevFinal — подстановка совпала с итогом прошлого кадра (после всех стадий UXR) с " +
                           "точностью 1e-5. resetEvents: stamp — метка батча (= t серверной серии netSendMarkers), flags — биты " +
                           "Mirror.Changed. droppedNotSpawned — RPC на ещё не заспавненный (или уже удалённый) объект: Mirror их " +
                           "отбрасывает. snapshotCheck — вставленный снимок совпал с восстановлением (проверка модели). model: legacy " +
                           "(Mirror без патча) или patched (патч 3: после сброса — последнее полученное, fillSource.received). series — каждый " +
                           "пакет [метка, кадр, флаги, источник 0/1/2, x, y, z восстановленной позиции]. settle — оседание тела, " +
                           "появившегося при подключённом клиенте: samples [с от спавна, серверное время показанной позы, камера Y, " +
                           "запястье R xyz, L xyz] в системе координат своего NT (после NT); время оседания — analyze.py против серверной серии."
            };
        }
    }
}
