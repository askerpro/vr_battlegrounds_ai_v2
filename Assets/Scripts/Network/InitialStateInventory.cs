using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UltimateXR.Core.Serialization;
using UltimateXR.Core.Unique;
using UltimateXR.Extensions.System.IO;

namespace VrBattlegrounds.Network
{
    /// <summary>
    /// Инвентарь начального снимка UltimateXR: какие компоненты он описывает. Читает только заголовки записей
    /// формата <c>UxrManager.SaveStateChanges</c> (размер записи и UniqueId адресата), само состояние не трогает.
    ///
    /// <para>
    /// Нужен барьеру начального состояния (<see cref="NetworkStateRelay"/>): снимок применяется после локальной
    /// готовности запуска карты, и каждый упомянутый в нём адресат обязан быть зарегистрирован. SDK пропускает
    /// запись с незарегистрированным адресатом предупреждением; здесь пропуск становится именованной ошибкой.
    /// Ссылки внутри состояния компонента (например, текущий якорь предмета) инвентарь не разбирает —
    /// это верхний уровень замыкания ссылок.
    /// </para>
    /// </summary>
    public static class InitialStateInventory
    {
        /// <summary>UniqueId адресатов всех записей снимка в порядке сериализации.</summary>
        public static List<Guid> Targets(byte[] serializedState)
        {
            var targets = new List<Guid>();
            if (serializedState == null || serializedState.Length < 4) return targets;

            var format = (UxrSerializationFormat)serializedState[0];
            int version = BitConverter.ToUInt16(serializedState, 2);

            switch (format)
            {
                case UxrSerializationFormat.BinaryUncompressed:
                    using (var stream = new MemoryStream(serializedState, 4, serializedState.Length - 4))
                        Read(stream, version, targets);
                    break;

                case UxrSerializationFormat.BinaryGzip:
                    using (var compressed = new MemoryStream(serializedState, 4, serializedState.Length - 4))
                    using (var gzip = new GZipStream(compressed, CompressionMode.Decompress))
                    using (var plain = new MemoryStream())
                    {
                        gzip.CopyTo(plain);
                        plain.Position = 0;
                        Read(plain, version, targets);
                    }
                    break;

                default:
                    throw new InvalidDataException("InitialState.Format.Unknown:" + format);
            }

            return targets;
        }

        /// <summary>Сколько адресатов снимка не зарегистрировано на этой машине.</summary>
        public static int CountMissingTargets(byte[] serializedState, out int total)
        {
            List<Guid> targets;
            try
            {
                targets = Targets(serializedState);
            }
            catch (Exception)
            {
                // Битый заголовок разберёт и залогирует сам SDK при загрузке; инвентарь его не подменяет.
                total = 0;
                return 0;
            }

            total = targets.Count;
            int missing = 0;
            foreach (Guid id in targets)
                if (!UxrUniqueIdImplementer.TryGetComponentById(id, out _)) missing++;
            return missing;
        }

        private static void Read(Stream stream, int version, List<Guid> targets)
        {
            using var reader = new BinaryReader(stream);
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                int size = reader.ReadCompressedInt32(version);
                long start = reader.BaseStream.Position;
                if (size < 0 || start + size > reader.BaseStream.Length)
                    throw new InvalidDataException("InitialState.Record.Size");

                byte tag = reader.ReadByte();
                if (tag != BinaryWriterExt.UniqueComponentNull)
                {
                    Guid id = reader.ReadGuid(version);
                    if (id != Guid.Empty) targets.Add(id);
                }

                reader.BaseStream.Seek(start + size, SeekOrigin.Begin);
            }
        }
    }
}
