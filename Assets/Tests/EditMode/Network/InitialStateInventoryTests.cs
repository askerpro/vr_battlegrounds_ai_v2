using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UltimateXR.Core.Serialization;
using UltimateXR.Extensions.System.IO;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Инвентарь начального снимка: читает UniqueId адресатов из формата <c>UxrManager.SaveStateChanges</c>
    /// (заголовок 4 байта, затем записи «размер, метка, Guid, состояние»), в том числе сжатого Gzip.
    /// Незарегистрированный адресат считается пропущенным — барьер Relay сообщит об этом ошибкой.
    /// </summary>
    public class InitialStateInventoryTests
    {
        private static byte[] Snapshot(UxrSerializationFormat format, params Guid[] targets)
        {
            byte[] body;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                foreach (Guid target in targets)
                {
                    byte[] record;
                    using (var recordStream = new MemoryStream())
                    using (var recordWriter = new BinaryWriter(recordStream))
                    {
                        recordWriter.Write(BinaryWriterExt.UniqueComponentId);
                        recordWriter.Write(target.ToByteArray());
                        recordWriter.Write(new byte[] { 1, 2, 3 }); // состояние компонента — инвентарь его не читает
                        recordWriter.Flush();
                        record = recordStream.ToArray();
                    }
                    writer.WriteCompressedInt32(record.Length);
                    writer.Write(record);
                }
                writer.Flush();
                body = stream.ToArray();
            }

            if (format == UxrSerializationFormat.BinaryGzip)
            {
                using var compressed = new MemoryStream();
                using (var gzip = new GZipStream(compressed, CompressionMode.Compress, true)) gzip.Write(body, 0, body.Length);
                body = compressed.ToArray();
            }

            var result = new byte[4 + body.Length];
            result[0] = (byte)format;
            result[1] = 0;
            BitConverter.GetBytes((ushort)UltimateXR.Core.UxrConstants.Serialization.CurrentBinaryVersion).CopyTo(result, 2);
            body.CopyTo(result, 4);
            return result;
        }

        [Test]
        public void Инвентарь_читает_адресатов_несжатого_снимка()
        {
            Guid a = Guid.NewGuid(), b = Guid.NewGuid();
            CollectionAssert.AreEqual(new[] { a, b }, InitialStateInventory.Targets(Snapshot(UxrSerializationFormat.BinaryUncompressed, a, b)));
        }

        [Test]
        public void Инвентарь_читает_адресатов_сжатого_снимка()
        {
            Guid a = Guid.NewGuid();
            CollectionAssert.AreEqual(new[] { a }, InitialStateInventory.Targets(Snapshot(UxrSerializationFormat.BinaryGzip, a)));
        }

        [Test]
        public void Незарегистрированный_адресат_считается_пропущенным()
        {
            int missing = InitialStateInventory.CountMissingTargets(
                Snapshot(UxrSerializationFormat.BinaryUncompressed, Guid.NewGuid(), Guid.NewGuid()), out int total);

            Assert.AreEqual(2, total);
            Assert.AreEqual(2, missing, "Адресат без регистрации не замечен — барьер пропустил бы его молча.");
        }

        [Test]
        public void Пустой_снимок_без_адресатов()
        {
            Assert.IsEmpty(InitialStateInventory.Targets(Snapshot(UxrSerializationFormat.BinaryUncompressed)));
            Assert.IsEmpty(InitialStateInventory.Targets(null));
        }
    }
}
