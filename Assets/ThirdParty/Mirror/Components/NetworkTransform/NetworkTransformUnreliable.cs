// NetworkTransform V2 by mischa (2021-07)
using System.Collections.Generic;
using UnityEngine;

namespace Mirror
{
    [AddComponentMenu("Network/Network Transform (Unreliable)")]
    public class NetworkTransformUnreliable : NetworkTransformBase
    {
        uint sendIntervalCounter = 0;
        double lastSendIntervalTime = double.MinValue;
        TransformSnapshot? pendingSnapshot;

        [Header("Additional Settings")]
        // Testing under really bad network conditions, 2%-5% packet loss and 250-1200ms ping, 5 proved to eliminate any twitching, however this should not be the default as it is a rare case Developers may want to cover.
        [Tooltip("How much time, as a multiple of send interval, has passed before clearing buffers.\nA larger buffer means more delay, but results in smoother movement.\nExample: 1 for faster responses minimal smoothing, 5 covers bad pings but has noticable delay, 3 is recommended for balanced results.")]
        public float bufferResetMultiplier = 3;
        public bool useFixedUpdate;

        [Header("Sensitivity"), Tooltip("Sensitivity of changes needed before an updated state is sent over the network")]
        public float positionSensitivity = 0.01f;
        public float rotationSensitivity = 0.01f;
        public float scaleSensitivity = 0.01f;

        // Used to store last sent snapshots
        protected TransformSnapshot lastSnapshot;
        protected Changed cachedChangedComparison;
        protected bool hasSentUnchangedPosition;

        // VR Battlegrounds patch 3 (tasks/mirror-nt-keyframe): ключевой снимок и последнее полученное значение.
        // Отправитель шлёт только изменившиеся оси; потерянная ось, которая потом не меняется, не восстанавливалась
        // никогда. Раз в keyframeInterval уходят все синхронизируемые оси.
        [Tooltip("VR Battlegrounds patch 3: interval in seconds of a full snapshot with all synced axes, even if unchanged. Recovers lost unreliable deltas. 0 disables.")]
        public float keyframeInterval = 1f;
        // Сразу после спавна приёмник может ещё не обработать спавн и молча отбросить первые снимки,
        // а цель (камера аватара) получает позу кадром позже спавна. Первые keyframeWarmup секунд с первой
        // отправки каждая отправка — полный снимок, чтобы не ждать следующего ключевого снимка с позой спавна.
        [Tooltip("VR Battlegrounds patch 3: seconds after the first send during which every send is a full snapshot. 0 disables.")]
        public float keyframeWarmup = 0.5f;
        double lastKeyframeTime = double.MinValue;
        double firstSendTime = double.MinValue;

        // Приёмник: значение осей, последнее полученное от отправителя, отдельно от буфера интерполяции.
        // ResetState его не сбрасывает. Непришедшие оси берутся отсюда, а не из трансформа: трансформ цели
        // на приёмнике правят другие писатели (хват, IK, отдача), и чтение назад запекало их правки в буфер.
        bool hasClientReceived;
        Vector3 clientReceivedPosition;
        Quaternion clientReceivedRotation;
        Vector3 clientReceivedScale;
        bool hasServerReceived;
        Vector3 serverReceivedPosition;
        Quaternion serverReceivedRotation;
        Vector3 serverReceivedScale;

        // update //////////////////////////////////////////////////////////////
        // Update applies interpolation
        void Update()
        {
            if (isServer) UpdateServerInterpolation();
            // for all other clients (and for local player if !authority),
            // we need to apply snapshots from the buffer.
            // 'else if' because host mode shouldn't interpolate client
            else if (isClient && !IsClientWithAuthority) UpdateClientInterpolation();
        }

        void FixedUpdate()
        {
            if (!useFixedUpdate) return;

            if (pendingSnapshot.HasValue)
            {
                Apply(pendingSnapshot.Value, pendingSnapshot.Value);
                pendingSnapshot = null;
            }
        }

        // LateUpdate broadcasts.
        // movement scripts may change positions in Update.
        // use LateUpdate to ensure changes are detected in the same frame.
        // otherwise this may run before user update, delaying detection until next frame.
        // this could cause visible jitter.
        void LateUpdate()
        {
            // if server then always sync to others.
            if (isServer) UpdateServerBroadcast();
            // client authority, and local player (= allowed to move myself)?
            // 'else if' because host mode shouldn't send anything to server.
            // it is the server. don't overwrite anything there.
            else if (isClient && IsClientWithAuthority) UpdateClientBroadcast();
        }

        protected virtual void CheckLastSendTime()
        {
            // We check interval every frame, and then send if interval is reached.
            // So by the time sendIntervalCounter == sendIntervalMultiplier, data is sent,
            // thus we reset the counter here.
            // This fixes previous issue of, if sendIntervalMultiplier = 1, we send every frame,
            // because intervalCounter is always = 1 in the previous version.

            // Changing == to >= https://github.com/MirrorNetworking/Mirror/issues/3571

            if (sendIntervalCounter >= sendIntervalMultiplier)
                sendIntervalCounter = 0;

            // timeAsDouble not available in older Unity versions.
            if (AccurateInterval.Elapsed(NetworkTime.localTime, NetworkServer.sendInterval, ref lastSendIntervalTime))
                sendIntervalCounter++;
        }

        void UpdateServerBroadcast()
        {
            // broadcast to all clients each 'sendInterval'
            // (client with authority will drop the rpc)
            // NetworkTime.localTime for double precision until Unity has it too
            //
            // IMPORTANT:
            // snapshot interpolation requires constant sending.
            // DO NOT only send if position changed. for example:
            // ---
            // * client sends first position at t=0
            // * ... 10s later ...
            // * client moves again, sends second position at t=10
            // ---
            // * server gets first position at t=0
            // * server gets second position at t=10
            // * server moves from first to second within a time of 10s
            //   => would be a super slow move, instead of a wait & move.
            //
            // IMPORTANT:
            // DO NOT send nulls if not changed 'since last send' either. we
            // send unreliable and don't know which 'last send' the other end
            // received successfully.
            //
            // Checks to ensure server only sends snapshots if object is
            // on server authority(!clientAuthority) mode because on client
            // authority mode snapshots are broadcasted right after the authoritative
            // client updates server in the command function(see above), OR,
            // since host does not send anything to update the server, any client
            // authoritative movement done by the host will have to be broadcasted
            // here by checking IsClientWithAuthority.
            // TODO send same time that NetworkServer sends time snapshot?
            CheckLastSendTime();

            if (sendIntervalCounter == sendIntervalMultiplier && // same interval as time interpolation!
                (syncDirection == SyncDirection.ServerToClient || IsClientWithAuthority))
            {
                // send snapshot without timestamp.
                // receiver gets it from batch timestamp to save bandwidth.
                TransformSnapshot snapshot = Construct();

                // VR Battlegrounds patch 3: ключевой снимок — все оси, даже без изменений.
                bool keyframe = TryTakeKeyframe(out Changed fullChange);
                cachedChangedComparison = keyframe ? fullChange : CompareChangedSnapshots(snapshot);

                if (!keyframe && (cachedChangedComparison == Changed.None || cachedChangedComparison == Changed.CompressRot) && hasSentUnchangedPosition && onlySyncOnChange) { return; }

                SyncData syncData = new SyncData(cachedChangedComparison, snapshot);

                RpcServerToClientSync(syncData);

                if (cachedChangedComparison == Changed.None || cachedChangedComparison == Changed.CompressRot)
                {
                    hasSentUnchangedPosition = true;
                }
                else
                {
                    hasSentUnchangedPosition = false;
                    UpdateLastSentSnapshot(cachedChangedComparison, snapshot);
                }
            }
        }

        void UpdateServerInterpolation()
        {
            // apply buffered snapshots IF client authority
            // -> in server authority, server moves the object
            //    so no need to apply any snapshots there.
            // -> don't apply for host mode player objects either, even if in
            //    client authority mode. if it doesn't go over the network,
            //    then we don't need to do anything.
            // -> connectionToClient is briefly null after scene changes:
            //    https://github.com/MirrorNetworking/Mirror/issues/3329
            if (syncDirection == SyncDirection.ClientToServer &&
                connectionToClient != null &&
                !isOwned)
            {
                if (serverSnapshots.Count == 0) return;

                // step the transform interpolation without touching time.
                // NetworkClient is responsible for time globally.
                SnapshotInterpolation.StepInterpolation(
                    serverSnapshots,
                    connectionToClient.remoteTimeline,
                    out TransformSnapshot from,
                    out TransformSnapshot to,
                    out double t);

                // interpolate & apply
                TransformSnapshot computed = TransformSnapshot.Interpolate(from, to, t);
                if (useFixedUpdate)
                    pendingSnapshot = computed;
                else
                    Apply(computed, to);
            }
        }

        void UpdateClientBroadcast()
        {
            // https://github.com/vis2k/Mirror/pull/2992/
            if (!NetworkClient.ready) return;

            // send to server each 'sendInterval'
            // NetworkTime.localTime for double precision until Unity has it too
            //
            // IMPORTANT:
            // snapshot interpolation requires constant sending.
            // DO NOT only send if position changed. for example:
            // ---
            // * client sends first position at t=0
            // * ... 10s later ...
            // * client moves again, sends second position at t=10
            // ---
            // * server gets first position at t=0
            // * server gets second position at t=10
            // * server moves from first to second within a time of 10s
            //   => would be a super slow move, instead of a wait & move.
            //
            // IMPORTANT:
            // DO NOT send nulls if not changed 'since last send' either. we
            // send unreliable and don't know which 'last send' the other end
            // received successfully.
            CheckLastSendTime();
            if (sendIntervalCounter == sendIntervalMultiplier) // same interval as time interpolation!
            {
                // send snapshot without timestamp.
                // receiver gets it from batch timestamp to save bandwidth.
                TransformSnapshot snapshot = Construct();

                // VR Battlegrounds patch 3: ключевой снимок и от владельца — сервер пересылает его наблюдателям.
                bool keyframe = TryTakeKeyframe(out Changed fullChange);
                cachedChangedComparison = keyframe ? fullChange : CompareChangedSnapshots(snapshot);

                if (!keyframe && (cachedChangedComparison == Changed.None || cachedChangedComparison == Changed.CompressRot) && hasSentUnchangedPosition && onlySyncOnChange) { return; }

                SyncData syncData = new SyncData(cachedChangedComparison, snapshot);

                CmdClientToServerSync(syncData);

                if (cachedChangedComparison == Changed.None || cachedChangedComparison == Changed.CompressRot)
                {
                    hasSentUnchangedPosition = true;
                }
                else
                {
                    hasSentUnchangedPosition = false;
                    UpdateLastSentSnapshot(cachedChangedComparison, snapshot);
                }
            }
        }

        void UpdateClientInterpolation()
        {
            // only while we have snapshots
            if (clientSnapshots.Count == 0) return;

            // step the interpolation without touching time.
            // NetworkClient is responsible for time globally.
            SnapshotInterpolation.StepInterpolation(
                clientSnapshots,
                NetworkTime.time, // == NetworkClient.localTimeline from snapshot interpolation
                out TransformSnapshot from,
                out TransformSnapshot to,
                out double t);

            // interpolate & apply
            TransformSnapshot computed = TransformSnapshot.Interpolate(from, to, t);
            if (useFixedUpdate)
                pendingSnapshot = computed;
            else
                Apply(computed, to);
        }

        public override void OnSerialize(NetworkWriter writer, bool initialState)
        {
            // sync target component's position on spawn.
            // fixes https://github.com/vis2k/Mirror/pull/3051/
            // (Spawn message wouldn't sync NTChild positions either)
            if (initialState)
            {
                if (syncPosition) writer.WriteVector3(GetPosition());
                if (syncRotation) writer.WriteQuaternion(GetRotation());
                if (syncScale) writer.WriteVector3(GetScale());
            }
        }

        public override void OnDeserialize(NetworkReader reader, bool initialState)
        {
            // sync target component's position on spawn.
            // fixes https://github.com/vis2k/Mirror/pull/3051/
            // (Spawn message wouldn't sync NTChild positions either)
            if (initialState)
            {
                if (syncPosition) SetPosition(reader.ReadVector3());
                if (syncRotation) SetRotation(reader.ReadQuaternion());
                if (syncScale) SetScale(reader.ReadVector3());

                // VR Battlegrounds patch 3: поза спавна — первое полученное значение осей приёмника.
                SetClientReceived(GetPosition(), GetRotation(), GetScale());
            }
        }

        protected virtual void UpdateLastSentSnapshot(Changed change, TransformSnapshot currentSnapshot)
        {
            if (change == Changed.None || change == Changed.CompressRot) return;

            if ((change & Changed.PosX) > 0) lastSnapshot.position.x = currentSnapshot.position.x;
            if ((change & Changed.PosY) > 0) lastSnapshot.position.y = currentSnapshot.position.y;
            if ((change & Changed.PosZ) > 0) lastSnapshot.position.z = currentSnapshot.position.z;

            if (compressRotation)
            {
                if ((change & Changed.Rot) > 0) lastSnapshot.rotation = currentSnapshot.rotation;
            }
            else
            {
                Vector3 newRotation;
                newRotation.x = (change & Changed.RotX) > 0 ? currentSnapshot.rotation.eulerAngles.x : lastSnapshot.rotation.eulerAngles.x;
                newRotation.y = (change & Changed.RotY) > 0 ? currentSnapshot.rotation.eulerAngles.y : lastSnapshot.rotation.eulerAngles.y;
                newRotation.z = (change & Changed.RotZ) > 0 ? currentSnapshot.rotation.eulerAngles.z : lastSnapshot.rotation.eulerAngles.z;

                lastSnapshot.rotation = Quaternion.Euler(newRotation);
            }

            if ((change & Changed.Scale) > 0) lastSnapshot.scale = currentSnapshot.scale;
        }

        // Returns true if position, rotation AND scale are unchanged, within given sensitivity range.
        // Note the sensitivity comparison are different for pos, rot and scale.
        protected virtual Changed CompareChangedSnapshots(TransformSnapshot currentSnapshot)
        {
            Changed change = Changed.None;

            if (syncPosition)
            {
                bool positionChanged = Vector3.SqrMagnitude(lastSnapshot.position - currentSnapshot.position) > positionSensitivity * positionSensitivity;
                if (positionChanged)
                {
                    if (Mathf.Abs(lastSnapshot.position.x - currentSnapshot.position.x) > positionSensitivity) change |= Changed.PosX;
                    if (Mathf.Abs(lastSnapshot.position.y - currentSnapshot.position.y) > positionSensitivity) change |= Changed.PosY;
                    if (Mathf.Abs(lastSnapshot.position.z - currentSnapshot.position.z) > positionSensitivity) change |= Changed.PosZ;
                }
            }

            if (syncRotation)
            {
                if (compressRotation)
                {
                    bool rotationChanged = Quaternion.Angle(lastSnapshot.rotation, currentSnapshot.rotation) > rotationSensitivity;
                    if (rotationChanged)
                    {
                        // Here we set all Rot enum flags, to tell us if there was a change in rotation
                        // when using compression. If no change, we don't write the compressed Quat.
                        change |= Changed.CompressRot;
                        change |= Changed.Rot;
                    }
                    else
                    {
                        change |= Changed.CompressRot;
                    }
                }
                else
                {
                    if (Mathf.Abs(lastSnapshot.rotation.eulerAngles.x - currentSnapshot.rotation.eulerAngles.x) > rotationSensitivity) change |= Changed.RotX;
                    if (Mathf.Abs(lastSnapshot.rotation.eulerAngles.y - currentSnapshot.rotation.eulerAngles.y) > rotationSensitivity) change |= Changed.RotY;
                    if (Mathf.Abs(lastSnapshot.rotation.eulerAngles.z - currentSnapshot.rotation.eulerAngles.z) > rotationSensitivity) change |= Changed.RotZ;
                }
            }

            if (syncScale)
            {
                if (Vector3.SqrMagnitude(lastSnapshot.scale - currentSnapshot.scale) > scaleSensitivity * scaleSensitivity) change |= Changed.Scale;
            }

            return change;
        }

        [Command(channel = Channels.Unreliable)]
        void CmdClientToServerSync(SyncData syncData)
        {
            OnClientToServerSync(syncData);
            //For client authority, immediately pass on the client snapshot to all other
            //clients instead of waiting for server to send its snapshots.
            if (syncDirection == SyncDirection.ClientToServer)
                RpcServerToClientSync(syncData);
        }

        protected virtual void OnClientToServerSync(SyncData syncData)
        {
            // only apply if in client authority mode
            if (syncDirection != SyncDirection.ClientToServer) return;

            // protect against ever growing buffer size attacks
            if (serverSnapshots.Count >= connectionToClient.snapshotBufferSizeLimit) return;

            // only player owned objects (with a connection) can send to
            // server. we can get the timestamp from the connection.
            double timestamp = connectionToClient.remoteTimeStamp;

            if (onlySyncOnChange)
            {
                double timeIntervalCheck = bufferResetMultiplier * sendIntervalMultiplier * NetworkClient.sendInterval;

                if (serverSnapshots.Count > 0 && serverSnapshots.Values[serverSnapshots.Count - 1].remoteTime + timeIntervalCheck < timestamp)
                    ResetState();
            }

            UpdateSyncData(ref syncData, serverSnapshots);

            // VR Battlegrounds patch 3: запомнить полученное (полные оси после достройки).
            hasServerReceived = true;
            serverReceivedPosition = syncData.position;
            serverReceivedRotation = syncData.quatRotation;
            serverReceivedScale = syncData.scale;

            AddSnapshot(serverSnapshots, connectionToClient.remoteTimeStamp + timeStampAdjustment + offset, syncData.position, syncData.quatRotation, syncData.scale);
        }

        [ClientRpc(channel = Channels.Unreliable)]
        void RpcServerToClientSync(SyncData syncData) =>
            OnServerToClientSync(syncData);

        protected virtual void OnServerToClientSync(SyncData syncData)
        {
            // in host mode, the server sends rpcs to all clients.
            // the host client itself will receive them too.
            // -> host server is always the source of truth
            // -> we can ignore any rpc on the host client
            // => otherwise host objects would have ever growing clientBuffers
            // (rpc goes to clients. if isServer is true too then we are host)
            if (isServer) return;

            // don't apply for local player with authority
            if (IsClientWithAuthority) return;

            // on the client, we receive rpcs for all entities.
            // not all of them have a connectionToServer.
            // but all of them go through NetworkClient.connection.
            // we can get the timestamp from there.
            double timestamp = NetworkClient.connection.remoteTimeStamp;

            if (onlySyncOnChange)
            {
                double timeIntervalCheck = bufferResetMultiplier * sendIntervalMultiplier * NetworkServer.sendInterval;

                if (clientSnapshots.Count > 0 && clientSnapshots.Values[clientSnapshots.Count - 1].remoteTime + timeIntervalCheck < timestamp)
                    ResetState();
            }

            UpdateSyncData(ref syncData, clientSnapshots);

            // VR Battlegrounds patch 3: запомнить полученное (полные оси после достройки).
            SetClientReceived(syncData.position, syncData.quatRotation, syncData.scale);

            AddSnapshot(clientSnapshots, NetworkClient.connection.remoteTimeStamp + timeStampAdjustment + offset, syncData.position, syncData.quatRotation, syncData.scale);
        }

        protected virtual void UpdateSyncData(ref SyncData syncData, SortedList<double, TransformSnapshot> snapshots)
        {
            // VR Battlegrounds patch 3: непришедшие оси — из последнего ПОЛУЧЕННОГО значения (буфер, а после
            // ResetState — сохранённое значение приёмника), а не из трансформа цели. Трансформ — только если
            // от отправителя ещё ничего не получено.
            GetReceivedBaseline(snapshots, out Vector3 basePosition, out Quaternion baseRotation, out Vector3 baseScale);

            if (syncData.changedDataByte == Changed.None || syncData.changedDataByte == Changed.CompressRot)
            {
                syncData.position = basePosition;
                syncData.quatRotation = baseRotation;
                syncData.scale = baseScale;
            }
            else
            {
                // Just going to update these without checking if syncposition or not,
                // because if not syncing position, NT will not apply any position data
                // to the target during Apply().

                syncData.position.x = (syncData.changedDataByte & Changed.PosX) > 0 ? syncData.position.x : basePosition.x;
                syncData.position.y = (syncData.changedDataByte & Changed.PosY) > 0 ? syncData.position.y : basePosition.y;
                syncData.position.z = (syncData.changedDataByte & Changed.PosZ) > 0 ? syncData.position.z : basePosition.z;

                // If compressRot is true, we already have the Quat in syncdata.
                if ((syncData.changedDataByte & Changed.CompressRot) == 0)
                {
                    Vector3 baseEuler = baseRotation.eulerAngles;
                    syncData.vecRotation.x = (syncData.changedDataByte & Changed.RotX) > 0 ? syncData.vecRotation.x : baseEuler.x;
                    syncData.vecRotation.y = (syncData.changedDataByte & Changed.RotY) > 0 ? syncData.vecRotation.y : baseEuler.y;
                    syncData.vecRotation.z = (syncData.changedDataByte & Changed.RotZ) > 0 ? syncData.vecRotation.z : baseEuler.z;

                    syncData.quatRotation = Quaternion.Euler(syncData.vecRotation);
                }
                else
                {
                    syncData.quatRotation = (syncData.changedDataByte & Changed.Rot) > 0 ? syncData.quatRotation : baseRotation;
                }

                syncData.scale = (syncData.changedDataByte & Changed.Scale) > 0 ? syncData.scale : baseScale;
            }
        }

        // VR Battlegrounds patch 3 ////////////////////////////////////////////

        // Опорное значение для непришедших осей: последний снимок буфера, иначе сохранённое полученное значение
        // этого направления, иначе (ничего ещё не получено) трансформ цели.
        void GetReceivedBaseline(SortedList<double, TransformSnapshot> snapshots, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            if (snapshots.Count > 0)
            {
                TransformSnapshot last = snapshots.Values[snapshots.Count - 1];
                position = last.position;
                rotation = last.rotation;
                scale = last.scale;
                return;
            }

            if (ReferenceEquals(snapshots, clientSnapshots) && hasClientReceived)
            {
                position = clientReceivedPosition;
                rotation = clientReceivedRotation;
                scale = clientReceivedScale;
                return;
            }

            if (ReferenceEquals(snapshots, serverSnapshots) && hasServerReceived)
            {
                position = serverReceivedPosition;
                rotation = serverReceivedRotation;
                scale = serverReceivedScale;
                return;
            }

            position = GetPosition();
            rotation = GetRotation();
            scale = GetScale();
        }

        void SetClientReceived(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            hasClientReceived = true;
            clientReceivedPosition = position;
            clientReceivedRotation = rotation;
            clientReceivedScale = scale;
        }

        // Пора ли ключевого снимка; полный набор флагов синхронизируемых осей. Первая отправка — всегда полная.
        bool TryTakeKeyframe(out Changed fullChange)
        {
            fullChange = Changed.None;
            if (keyframeInterval <= 0f) return false;

            double now = NetworkTime.localTime;
            if (firstSendTime == double.MinValue) firstSendTime = now;
            bool warmup = keyframeWarmup > 0f && now - firstSendTime < keyframeWarmup;
            if (!warmup && lastKeyframeTime != double.MinValue && now - lastKeyframeTime < keyframeInterval) return false;

            if (syncPosition) fullChange |= Changed.Pos;
            if (syncRotation) fullChange |= compressRotation ? (Changed.CompressRot | Changed.Rot) : Changed.Rot;
            if (syncScale) fullChange |= Changed.Scale;
            if (fullChange == Changed.None) return false;

            lastKeyframeTime = now;
            return true;
        }

        // Телепорт задаёт новую позу напрямую: она же — новое полученное значение приёмника.
        protected override void OnTeleport(Vector3 destination)
        {
            base.OnTeleport(destination);
            RememberTeleport();
        }

        protected override void OnTeleport(Vector3 destination, Quaternion rotation)
        {
            base.OnTeleport(destination, rotation);
            RememberTeleport();
        }

        void RememberTeleport()
        {
            if (hasClientReceived) SetClientReceived(GetPosition(), GetRotation(), GetScale());
            if (hasServerReceived)
            {
                serverReceivedPosition = GetPosition();
                serverReceivedRotation = GetRotation();
                serverReceivedScale = GetScale();
            }
        }
    }
}
