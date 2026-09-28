using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UltimateXR.Core.Components;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.EditorTools.Release
{
    /// <summary>
    /// Отпечаток <c>_uxrUniqueId</c> всего, что уходит в сборку, и проверка, что сборка унесёт
    /// именно id с диска.
    ///
    /// <para>
    /// <b>Зачем.</b> Канал состояния UltimateXR находит компонент по id. Объект сцены берёт id
    /// из сцены, объект из префаба — <c>Combine(id префаба, netId)</c>. Значит, сервер, Quest и
    /// планшет обязаны нести одинаковые id во всех сценах и префабах сборки, иначе события
    /// отвергаются с <c>UxrComponentNotFoundException</c>. Сами сборки id не выдают:
    /// <c>NotifyOnValidate</c> заглушён на <c>BuildPipeline.isBuildingPlayer</c>, а префабу-ассету
    /// id не меняет вовсе (патч 10). Но сборка сериализует загруженные префабы <b>из памяти</b>,
    /// и id, выданный в памяти и не сохранённый, уедет в одну сборку и не уедет в следующую,
    /// сделанную после перезапуска редактора.
    /// </para>
    ///
    /// <para>
    /// Поэтому проверок две: <see cref="FindMemoryMismatches" /> — в памяти то же, что на диске;
    /// <see cref="Compute" /> — хэш id с диска, одинаковый до и после каждой сборки и у всех
    /// трёх сборок. Хэш пишется рядом со сборкой (<see cref="FileName" />).
    /// </para>
    /// </summary>
    public sealed class UxrIdFingerprint
    {
        /// <summary>Файл с отпечатком рядом со сборкой.</summary>
        public const string FileName = "uxr-ids.txt";

        private const string ZeroGuid = "00000000000000000000000000000000";

        // Своя строка компонента и переопределение экземпляра префаба / варианта. Id бывает в двух
        // записях: с дефисами и 32 hex подряд (старые ассеты SDK, например UxrCompass).
        private static readonly Regex IdPattern = new Regex(
            @"_uxrUniqueId(?:: |\r?\n\s+value: )([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|[0-9a-f]{32})\b");

        /// <summary>Хэш (16 hex) отсортированных пар «файл — id».</summary>
        public string Hash;

        /// <summary>Сколько id учтено.</summary>
        public int IdCount;

        /// <summary>Сцены и префабы сборки, отсортированные.</summary>
        public List<string> Files = new List<string>();

        /// <summary>Файлы с нулевым id на диске: в плеере такому компоненту некому выдать общий id.</summary>
        public List<string> ZeroIds = new List<string>();

        /// <summary>Id по файлам — для сверки памяти с диском.</summary>
        private readonly Dictionary<string, HashSet<string>> _idsByFile = new Dictionary<string, HashSet<string>>();

        public string Summary => $"{Hash} (id: {IdCount}, файлов: {Files.Count})";

        /// <summary>
        /// Считает отпечаток по файлам на диске: сцены сборки, всё, от чего они зависят, и
        /// содержимое папок <c>Resources</c>.
        /// </summary>
        public static UxrIdFingerprint Compute(string[] scenes)
        {
            var result = new UxrIdFingerprint { Files = CollectFiles(scenes) };
            var lines = new List<string>();

            foreach (string path in result.Files)
            {
                var ids = new HashSet<string>();
                result._idsByFile[path] = ids;

                if (!File.Exists(path)) continue;

                foreach (Match m in IdPattern.Matches(File.ReadAllText(path)))
                {
                    string id = m.Groups[1].Value;
                    ids.Add(id);
                    lines.Add(path + "\t" + id);

                    if (id.Replace("-", "") == ZeroGuid && !result.ZeroIds.Contains(path)) result.ZeroIds.Add(path);
                }
            }

            lines.Sort(StringComparer.Ordinal);

            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
                result.Hash = BitConverter.ToString(digest, 0, 8).Replace("-", "").ToLowerInvariant();
            }

            result.IdCount = lines.Count;
            return result;
        }

        /// <summary>
        /// Префабы сборки, у которых id в памяти редактора нет ни в их файле, ни в файлах их баз
        /// и вложенных префабов. Такой id сборка унесёт, а следующая сборка — уже нет.
        /// </summary>
        public List<string> FindMemoryMismatches()
        {
            var problems = new List<string>();

            foreach (string path in Files)
            {
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                HashSet<string> onDisk = null;

                foreach (UxrComponent component in prefab.GetComponentsInChildren<UxrComponent>(true))
                {
                    SerializedProperty id = new SerializedObject(component).FindProperty("_uxrUniqueId");
                    if (id == null) continue;

                    onDisk ??= IdsOnDiskWithDependencies(path);
                    if (onDisk.Contains(id.stringValue)) continue;

                    problems.Add($"{path}: {component.GetType().Name} на '{component.name}' — id {id.stringValue} есть только в памяти");
                    break;
                }
            }

            return problems;
        }

        /// <summary>Записывает отпечаток в папку сборки.</summary>
        public void WriteTo(string directory, BuildProfile profile)
        {
            File.WriteAllText(Path.Combine(directory, FileName),
                              $"{Hash}\nprofile: {profile}\nids: {IdCount}\nfiles: {Files.Count}\nbuilt: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        }

        /// <summary>Хэш из файла отпечатка рядом со сборкой или null.</summary>
        public static string ReadHash(string directory)
        {
            string file = Path.Combine(directory, FileName);
            if (!File.Exists(file)) return null;

            string[] lines = File.ReadAllLines(file);
            return lines.Length > 0 ? lines[0].Trim() : null;
        }

        private HashSet<string> IdsOnDiskWithDependencies(string path)
        {
            var ids = new HashSet<string>();

            foreach (string dependency in AssetDatabase.GetDependencies(path, true))
            {
                if (_idsByFile.TryGetValue(dependency, out HashSet<string> own)) ids.UnionWith(own);
            }

            return ids;
        }

        private static List<string> CollectFiles(string[] scenes)
        {
            var roots = new List<string>(scenes);

            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (path.StartsWith("Assets/", StringComparison.Ordinal) && path.Contains("/Resources/"))
                    roots.Add(path);
            }

            return AssetDatabase.GetDependencies(roots.ToArray(), true)
                                .Where(p => p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                                            p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                                .Distinct()
                                .OrderBy(p => p, StringComparer.Ordinal)
                                .ToList();
        }
    }
}
