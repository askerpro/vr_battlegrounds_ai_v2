using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UltimateXR.Extensions.System;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Arsenal
{
    /// <summary>
    ///     Роль компонента слота с UniqueId: ключ роли и UniqueId компонента в префабе слота, прочитанный с
    ///     неразбуженного клона. По нему компонент клона связывается с ролью, а не по имени или пути объекта.
    /// </summary>
    [Serializable]
    public struct ArsenalTemplateRole
    {
        public string RoleKey, SourceUniqueId, SourceComponentId, ComponentType;
        public ArsenalTemplateRole(string key, string uid, string id, string type)
        { RoleKey = key; SourceUniqueId = uid; SourceComponentId = id; ComponentType = type; }
    }

    /// <summary>
    ///     Детерминированные UniqueId ролей сгенерированной станции. Одинаковый вход даёт одинаковый ID на сервере,
    ///     клиентах и у позднего клиента — независимо от порядка создания объектов.
    ///     ID = <c>GuidExt.Combine(RoleBaseUid(роль), SemanticSeed(ключ запуска, станция, логический слот, роль))</c>.
    ///     Зона, индекс в пресете, шаблон и оформление в seed не входят: перестановка оружия на следующей загрузке
    ///     не меняет логические ID, а новый <see cref="MapRunKey" /> меняет их все.
    ///     Смена алгоритма или полей — только с новым <see cref="SchemaVersion" />.
    /// </summary>
    public static class ArsenalGeneratedIdentityManifest
    {
        public const string Namespace = "vr-battlegrounds/arsenal-generated";
        public const int SchemaVersion = 1;
        public const string ItemAnchorRole = "item-anchor";
        public const string MagazineAnchorRole = "magazine-anchor";

        /// <summary>Роли, которым схема выдаёт ID. Любая другая роль — отказ, а не молчаливый ID.</summary>
        public static IReadOnlyList<string> SchemaRoles { get; } = Array.AsReadOnly(new[] { ItemAnchorRole, MagazineAnchorRole });

        private static readonly string SchemaVersionText = SchemaVersion.ToString(CultureInfo.InvariantCulture);

        /// <summary>Базовый ID роли: не зависит от исходного UniqueId шаблона (тот — только provenance).</summary>
        public static Guid RoleBaseUid(string roleKey)
        {
            RequireSchemaRole(roleKey);
            return Derive("role-base", Namespace, SchemaVersionText, roleKey);
        }

        /// <summary>Семантический seed слота. Регистр не нормализуется: «MP5K» и «mp5k» — разные ключи.</summary>
        public static Guid SemanticSeed(MapRunKey run, string stationKey, string logicalSlotKey, string roleKey)
        {
            if (!run.IsValid) throw new ArgumentException("ArsenalIdentity.InvalidRunKey");
            if (string.IsNullOrWhiteSpace(stationKey)) throw new ArgumentException("ArsenalIdentity.EmptyStationKey");
            if (string.IsNullOrWhiteSpace(logicalSlotKey)) throw new ArgumentException("ArsenalIdentity.EmptyLogicalSlotKey");
            RequireSchemaRole(roleKey);
            return Derive("semantic-seed", Namespace, SchemaVersionText, run.SessionEpoch.ToString("N"),
                          run.LoadSequence.ToString(CultureInfo.InvariantCulture), stationKey, logicalSlotKey, roleKey);
        }

        /// <summary>Ожидаемый UniqueId роли сгенерированного слота.</summary>
        public static Guid ExpectedUniqueId(MapRunKey run, string stationKey, string logicalSlotKey, string roleKey) =>
            GuidExt.Combine(RoleBaseUid(roleKey), SemanticSeed(run, stationKey, logicalSlotKey, roleKey));

        /// <summary>
        ///     Таблица ролей шаблона пригодна для выдачи ID: каждая роль схемы ровно один раз, без неизвестных ролей,
        ///     с непустым уникальным исходным UniqueId. Полнота против фактических компонентов клона проверяется
        ///     отдельно, на экземпляре шаблона.
        /// </summary>
        public static bool TryValidateTemplateRoles(IReadOnlyList<ArsenalTemplateRole> roles, out string error)
        {
            error = null;
            if (roles == null) { error = "ArsenalIdentity.NoRoles"; return false; }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var sources = new HashSet<Guid>();
            foreach (ArsenalTemplateRole role in roles)
            {
                if (!IsSchemaRole(role.RoleKey)) { error = "ArsenalIdentity.UnknownRole:" + role.RoleKey; return false; }
                if (!keys.Add(role.RoleKey)) { error = "ArsenalIdentity.DuplicateRole:" + role.RoleKey; return false; }
                if (!Guid.TryParse(role.SourceUniqueId, out Guid source) || source == Guid.Empty)
                { error = "ArsenalIdentity.InvalidSourceUniqueId:" + role.RoleKey; return false; }
                if (!sources.Add(source)) { error = "ArsenalIdentity.DuplicateSourceUniqueId:" + role.RoleKey; return false; }
            }
            foreach (string required in SchemaRoles)
                if (!keys.Contains(required)) { error = "ArsenalIdentity.MissingRole:" + required; return false; }
            return true;
        }

        private static bool IsSchemaRole(string roleKey) => roleKey == ItemAnchorRole || roleKey == MagazineAnchorRole;

        private static void RequireSchemaRole(string roleKey)
        {
            if (!IsSchemaRole(roleKey)) throw new ArgumentException("ArsenalIdentity.UnknownRole:" + roleKey);
        }

        /// <summary>
        ///     Кодек полей: длина UTF-8 (4 байта, big-endian) + байты, SHA-256, первые 16 байт с маской версии/варианта.
        ///     Префикс длины исключает склейку полей («a|bc» ≠ «ab|c»); некорректный UTF-16 — исключение.
        /// </summary>
        private static Guid Derive(params string[] fields)
        {
            var utf8 = new UTF8Encoding(false, true);
            using (var bytes = new MemoryStream())
            {
                foreach (string field in fields)
                {
                    if (field == null) throw new ArgumentException("ArsenalIdentity.NullField");
                    byte[] data = utf8.GetBytes(field);
                    uint length = (uint)data.Length;
                    bytes.WriteByte((byte)(length >> 24));
                    bytes.WriteByte((byte)(length >> 16));
                    bytes.WriteByte((byte)(length >> 8));
                    bytes.WriteByte((byte)length);
                    bytes.Write(data, 0, data.Length);
                }

                using (SHA256 sha = SHA256.Create())
                {
                    byte[] hash = sha.ComputeHash(bytes.ToArray());
                    var guid = new byte[16];
                    Array.Copy(hash, guid, 16);
                    guid[7] = (byte)((guid[7] & 0x0F) | 0x80);
                    guid[8] = (byte)((guid[8] & 0x3F) | 0x80);
                    return new Guid(guid);
                }
            }
        }
    }
}
