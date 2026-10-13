using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mirror;
using Mirror.RemoteCalls;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Разбор сообщений <c>NetworkTransformUnreliable.RpcServerToClientSync</c> для маркеров стенда (net-round1, attempt-6):
    /// опознание RPC по хешу, байт изменённых осей (<see cref="Changed"/>) и переданные значения позиции — ровно то, что
    /// ушло с сервера или пришло клиенту. Только чтение: сообщения не меняются, трансформы не пишутся.
    /// <para>
    /// Формат полезной нагрузки — <c>SyncDataReaderWriter.WriteSyncData</c> (Mirror): байт флагов, затем float x/y/z
    /// только для установленных PosX/PosY/PosZ, затем поворот и масштаб (здесь не разбираются).
    /// </para>
    /// </summary>
    public static class BotStandNtWire
    {
        private const string SyncRpcName = "RpcServerToClientSync";
        private static readonly Dictionary<ushort, bool> SyncHashes = new Dictionary<ushort, bool>();
        private static bool _registered;

        /// <summary>
        /// Регистрация RPC компонента происходит в статическом конструкторе (Weaver). У клиента RPC может прийти раньше
        /// первого экземпляра NT (до спавна объекта) — конструктор запускается заранее, иначе хеш не опознать.
        /// </summary>
        public static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            RuntimeHelpers.RunClassConstructor(typeof(NetworkTransformUnreliable).TypeHandle);
        }

        /// <summary>Хеш принадлежит <c>RpcServerToClientSync</c> (по имени сгенерированного обработчика).</summary>
        public static bool IsSyncRpc(ushort hash)
        {
            if (SyncHashes.TryGetValue(hash, out bool known)) return known;
            RemoteCallDelegate handler = RemoteProcedureCalls.GetDelegate(hash);
            bool result = handler != null && handler.Method != null &&
                          handler.Method.Name.IndexOf(SyncRpcName, StringComparison.Ordinal) >= 0;
            // Неопознанный хеш не кешируется: регистрация могла ещё не пройти.
            if (handler != null) SyncHashes[hash] = result;
            return result;
        }

        /// <summary>Флаги и переданные оси позиции; непереданная ось — NaN.</summary>
        public static bool TryRead(ArraySegment<byte> payload, out byte flags, out Vector3 position)
        {
            flags = 0;
            position = new Vector3(float.NaN, float.NaN, float.NaN);
            if (payload.Array == null || payload.Count < 1) return false;
            int at = payload.Offset;
            int end = payload.Offset + payload.Count;
            flags = payload.Array[at++];
            if (Has(flags, Changed.PosX)) { if (at + 4 > end) return false; position.x = BitConverter.ToSingle(payload.Array, at); at += 4; }
            if (Has(flags, Changed.PosY)) { if (at + 4 > end) return false; position.y = BitConverter.ToSingle(payload.Array, at); at += 4; }
            if (Has(flags, Changed.PosZ)) { if (at + 4 > end) return false; position.z = BitConverter.ToSingle(payload.Array, at); }
            return true;
        }

        public static bool Has(byte flags, Changed flag) => (flags & (byte)flag) != 0;

        /// <summary>Поворот передан: при сжатии — CompressRot вместе с Rot, без сжатия — любая из RotX/RotY/RotZ.</summary>
        public static bool RotationSent(byte flags) => (flags & (byte)Changed.Rot) != 0;

        /// <summary>Пакет «без изменений» (None или только CompressRot): все оси берутся из буфера или трансформа.</summary>
        public static bool Unchanged(byte flags) => flags == (byte)Changed.None || flags == (byte)Changed.CompressRot;

        public static string FlagText(byte flags)
        {
            if (Unchanged(flags)) return "none";
            var parts = new List<string>(6);
            if (Has(flags, Changed.PosX)) parts.Add("PosX");
            if (Has(flags, Changed.PosY)) parts.Add("PosY");
            if (Has(flags, Changed.PosZ)) parts.Add("PosZ");
            if (RotationSent(flags)) parts.Add("Rot");
            if (Has(flags, Changed.Scale)) parts.Add("Scale");
            return string.Join("|", parts);
        }

        /// <summary>Роль цели NT в аватаре: root, camera, wristR, wristL; иначе имя цели.</summary>
        public static string Role(UxrAvatar avatar, NetworkTransformBase nt)
        {
            Transform target = Target(nt);
            if (avatar == null || target == null) return target != null ? target.name : "?";
            if (target == avatar.transform) return "root";
            if (target == avatar.GetHandBone(UxrHandSide.Right)) return "wristR";
            if (target == avatar.GetHandBone(UxrHandSide.Left)) return "wristL";
            if (target == avatar.CameraTransform) return "camera";
            return target.name;
        }

        public static Transform Target(NetworkTransformBase nt) => nt == null ? null : nt.target != null ? nt.target : nt.transform;

        /// <summary>Позиция в пространстве NT — то, что читают <c>Construct</c>/<c>GetPosition</c>.</summary>
        public static Vector3 Position(NetworkTransformBase nt)
        {
            Transform target = Target(nt);
            return nt.coordinateSpace == CoordinateSpace.Local ? target.localPosition : target.position;
        }

        public static Quaternion Rotation(NetworkTransformBase nt)
        {
            Transform target = Target(nt);
            return nt.coordinateSpace == CoordinateSpace.Local ? target.localRotation : target.rotation;
        }

        /// <summary>Порог сброса буфера получателя — как в <c>OnServerToClientSync</c>, секунды.</summary>
        public static double ResetCheckSeconds(NetworkTransformUnreliable nt) =>
            nt.bufferResetMultiplier * nt.sendIntervalMultiplier * NetworkServer.sendInterval;

        /// <summary>
        /// Пауза между метками двух отправок, после которой получатель сбросит буфер: Mirror сравнивает
        /// <c>remoteTime</c> последнего снимка (метка + сдвиг ключа) + порог с меткой нового пакета, то есть пауза &gt;
        /// порог + сдвиг. Секунды.
        /// </summary>
        public static double ResetGapSeconds(NetworkTransformUnreliable nt) => ResetCheckSeconds(nt) + SnapshotShift(nt);

        /// <summary>Сдвиг ключа снимка относительно метки батча (<c>timeStampAdjustment + offset</c>).</summary>
        public static double SnapshotShift(NetworkTransformBase nt)
        {
            double interval = NetworkServer.sendInterval;
            return interval * (nt.sendIntervalMultiplier - 1) + (nt.timelineOffset ? interval * nt.sendIntervalMultiplier : 0.0);
        }

        // ── Патч Mirror 3 (tasks/mirror-nt-keyframe): последнее полученное значение и ключевые снимки ──
        // Поля читаются отражением: стенд компилируется и с непатченным Mirror (тогда модель — прежняя).

        private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo HasClientReceivedField = typeof(NetworkTransformUnreliable).GetField("hasClientReceived", AnyInstance);
        private static readonly FieldInfo ClientReceivedPositionField = typeof(NetworkTransformUnreliable).GetField("clientReceivedPosition", AnyInstance);
        private static readonly FieldInfo ClientReceivedRotationField = typeof(NetworkTransformUnreliable).GetField("clientReceivedRotation", AnyInstance);
        private static readonly FieldInfo KeyframeIntervalField = typeof(NetworkTransformUnreliable).GetField("keyframeInterval", AnyInstance);
        private static readonly FieldInfo KeyframeWarmupField = typeof(NetworkTransformUnreliable).GetField("keyframeWarmup", AnyInstance);

        /// <summary>
        /// Mirror с патчем 3: непришедшие оси приёмник берёт из последнего полученного значения (буфер, после
        /// <c>ResetState</c> — <c>clientReceived*</c>, поза спавна — <c>OnDeserialize</c>), трансформ — только пока ничего
        /// не получено; отправитель раз в <c>keyframeInterval</c> шлёт все оси.
        /// </summary>
        public static bool Patched => HasClientReceivedField != null && ClientReceivedPositionField != null && ClientReceivedRotationField != null;

        public static string ModelName => Patched ? "patched" : "legacy";

        /// <summary>Последнее полученное приёмником значение (патч 3); false — не получено или Mirror без патча.</summary>
        public static bool TryGetClientReceived(NetworkTransformUnreliable nt, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (!Patched || nt == null || !(HasClientReceivedField.GetValue(nt) is bool has) || !has) return false;
            position = (Vector3)ClientReceivedPositionField.GetValue(nt);
            rotation = (Quaternion)ClientReceivedRotationField.GetValue(nt);
            return true;
        }

        /// <summary>Интервал ключевого снимка, с; NaN — поля нет (Mirror без патча).</summary>
        public static float KeyframeInterval(NetworkTransformUnreliable nt) =>
            nt != null && KeyframeIntervalField != null && KeyframeIntervalField.GetValue(nt) is float value ? value : float.NaN;

        /// <summary>Прогрев ключевыми снимками после первой отправки, с (доработка патча); NaN — поля нет.</summary>
        public static float KeyframeWarmup(NetworkTransformUnreliable nt) =>
            nt != null && KeyframeWarmupField != null && KeyframeWarmupField.GetValue(nt) is float value ? value : float.NaN;

        /// <summary>Полный снимок: переданы все синхронизируемые оси позиции и поворот.</summary>
        public static bool Full(byte flags, NetworkTransformBase nt)
        {
            if (nt == null) return false;
            if (nt.syncPosition && (!Has(flags, Changed.PosX) || !Has(flags, Changed.PosY) || !Has(flags, Changed.PosZ))) return false;
            if (nt.syncRotation && !RotationSent(flags)) return false;
            return nt.syncPosition || nt.syncRotation;
        }

        /// <summary>Вектор для отчёта: 5 знаков, NaN → null.</summary>
        public static object[] Vec(Vector3 v) => new object[] { Num(v.x), Num(v.y), Num(v.z) };

        public static object Num(float value) => float.IsNaN(value) || float.IsInfinity(value) ? null : (object)Math.Round(value, 5);
    }
}
