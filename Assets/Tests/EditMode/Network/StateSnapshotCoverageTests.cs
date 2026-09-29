using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UltimateXR.Core;
using UltimateXR.Core.Serialization;
using UltimateXR.Core.StateSave;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    ///     Снимок состояния UltimateXR покрывает всё, что канал состояния синхронизирует событиями (T-34).
    ///
    ///     <para>
    ///     <b>Класс ошибки.</b> В UltimateXR «синхронизируется» и «попадает в снимок» — два независимых
    ///     механизма: сеттер свойства шлёт событие <c>EndSyncProperty</c>, а начальный снимок для позднего
    ///     клиента (<c>NetworkStateRelay.CmdRequestInitialState</c> → <c>SaveStateChanges</c>) пишет только то,
    ///     что компонент перечислил в <c>SerializeState</c>. Поле, забытое во втором, у клиента, вошедшего
    ///     после изменения, остаётся значением из префаба. Так было с <c>UxrActor._life</c>: поздний клиент
    ///     видел раненых со 100 хп, а мёртвых — живыми.
    ///     </para>
    ///
    ///     <para>
    ///     Сканирование исходников, а не список известных свойств: новое синхронизируемое свойство
    ///     (в SDK после обновления или в коде игры) без записи в снимок — красный тест.
    ///     </para>
    /// </summary>
    public class StateSnapshotCoverageTests
    {
        /// <summary>Где искать синхронизируемые свойства: SDK и код игры.</summary>
        private static readonly string[] Roots =
        {
            "ThirdParty/UltimateXR/Runtime/Scripts",
            "Scripts",
        };

        /// <summary>
        ///     Свойства, которые синхронизируются, но в снимок сознательно не входят. Ключ — «Класс.Свойство».
        ///     Каждое исключение — с причиной; без неё не добавлять.
        /// </summary>
        private static readonly Dictionary<string, string> Exceptions = new Dictionary<string, string>
        {
            ["UxrGrabbableResizable.IsGrabbable"] =
                "своего поля нет: значение пишется в три дочерних UxrGrabbableObject, а у них _isGrabbable в снимке есть",
            ["UxrGrabbableResizable.IsKinematic"] =
                "своего поля нет: значение пишется в три дочерних UxrGrabbableObject, а у них isKinematic в снимке есть",
            ["UxrAvatar.ShowControllerHands"] =
                "в игре не переключается (0 вызовов в Assets/Scripts), а чтение одного поля из снимка не обновило бы " +
                "модели контроллеров — понадобился бы вызов сеттера при загрузке. Появится переключение — вносить в снимок",
        };

        private static readonly Regex PropertyHeader =
            new Regex(@"public\s+(?:override\s+|virtual\s+|new\s+)*(?!class\b|struct\b|interface\b|enum\b|partial\b|static\b|sealed\b|abstract\b)[\w<>\[\],\.\?]+\s+(\w+)\s*\{",
                      RegexOptions.Compiled);

        private static readonly Regex OwnFieldAssignment =
            new Regex(@"(?<![\.\w])(_\w+)\s*=\s*value\s*;", RegexOptions.Compiled);

        /// <summary>Синхронизируемое свойство, найденное в исходниках.</summary>
        private readonly struct SyncedProperty
        {
            public readonly string ClassName;
            public readonly string Property;
            public readonly string Field;
            public readonly string File;

            public SyncedProperty(string className, string property, string field, string file)
            {
                ClassName = className;
                Property = property;
                Field = field;
                File = file;
            }

            public string Key => $"{ClassName}.{Property}";
        }

        [Test]
        public void Каждое_синхронизируемое_свойство_попадает_в_снимок()
        {
            List<SyncedProperty> synced = FindSyncedProperties();

            // Контроль: сканер видит хотя бы эталонные свойства SDK. Иначе зелёный ничего не значит.
            Assert.That(synced.Select(p => p.Key), Does.Contain("UxrGrabbableObject.IsGrabbable"),
                        "Сканер не нашёл UxrGrabbableObject.IsGrabbable — сломан разбор исходников, а не снимок.");
            Assert.That(synced.Select(p => p.Key), Does.Contain("UxrActor.Life"),
                        "Сканер не нашёл UxrActor.Life — сломан разбор исходников, а не снимок.");

            var missing = new List<string>();

            foreach (SyncedProperty p in synced)
            {
                if (Exceptions.ContainsKey(p.Key)) continue;

                if (p.Field == null)
                {
                    missing.Add($"{p.Key} ({p.File}): сеттер не пишет своё поле — либо в снимок то, куда он пишет, " +
                                "либо в исключения теста с причиной");
                    continue;
                }

                if (!IsSerializedInState(p))
                {
                    missing.Add($"{p.Key} ({p.File}): поле {p.Field} синхронизируется событием, но не входит в " +
                                "SerializeState — поздний клиент увидит значение из префаба");
                }
            }

            // Исключение, которого больше нет в коде, — мёртвая запись, скрывающая будущий дефект.
            foreach (string key in Exceptions.Keys)
            {
                if (synced.All(p => p.Key != key))
                    missing.Add($"{key}: исключение есть, а синхронизируемого свойства нет — убрать исключение");
            }

            Assert.That(missing, Is.Empty,
                        "Состояние, которое едет только событиями, не попадает в снимок для позднего клиента (T-34):\n" +
                        string.Join("\n", missing));
        }

        /// <summary>
        ///     Поведенческая половина: жизнь актора переживает тот же снимок, что отдаёт
        ///     <c>NetworkStateRelay</c> позднему клиенту (<c>ChangesSinceBeginning</c>).
        /// </summary>
        [Test]
        public void Жизнь_актора_доезжает_позднему_клиенту_снимком()
        {
            UxrActor server = CreateActor("Server", 100f);
            UxrActor lateClient = CreateActor("LateClient", 100f);

            try
            {
                StoreInitialState(server);
                StoreInitialState(lateClient);

                SetLife(server, 37f);

                byte[] snapshot;
                using (var stream = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(stream))
                    {
                        ((IUxrStateSave)server).SerializeState(new UxrBinarySerializer(writer),
                                                               ((IUxrStateSave)server).StateSerializationVersion,
                                                               UxrStateSaveLevel.ChangesSinceBeginning,
                                                               UxrStateSaveOptions.None);
                    }

                    snapshot = stream.ToArray();
                }

                using (var reader = new BinaryReader(new MemoryStream(snapshot)))
                {
                    ((IUxrStateSave)lateClient).SerializeState(new UxrBinarySerializer(reader, UxrConstants.Serialization.CurrentBinaryVersion),
                                                               ((IUxrStateSave)lateClient).StateSerializationVersion,
                                                               UxrStateSaveLevel.ChangesSinceBeginning,
                                                               UxrStateSaveOptions.None);
                }

                Assert.That(lateClient.Life, Is.EqualTo(37f).Within(0.001f),
                            "Поздний клиент после снимка видит жизнь из префаба, а не серверную.");
            }
            finally
            {
                Object.DestroyImmediate(server.gameObject);
                Object.DestroyImmediate(lateClient.gameObject);
            }
        }

        /// <summary>
        ///     Компонент, чей <c>SerializeState</c> ничего не пишет, UltimateXR не регистрирует вовсе
        ///     (<c>UxrStateSaveImplementer.RegisterComponent</c>, пробная сериализация) — и в снимок он
        ///     не попадёт, что бы в нём ни менялось. Та же пробная сериализация здесь.
        /// </summary>
        [Test]
        public void Актор_регистрируется_для_снимка()
        {
            UxrActor actor = CreateActor("Probe", 100f);
            try
            {
                bool hasState = ((IUxrStateSave)actor).SerializeState(UxrDummySerializer.WriteModeSerializer,
                                                                     ((IUxrStateSave)actor).StateSerializationVersion,
                                                                     UxrStateSaveLevel.Complete,
                                                                     UxrStateSaveOptions.DontSerialize | UxrStateSaveOptions.DontCacheChanges);

                Assert.That(hasState, Is.True,
                            "UxrActor не пишет в снимок ничего — UltimateXR исключит его из SaveStateChanges целиком.");
            }
            finally
            {
                Object.DestroyImmediate(actor.gameObject);
            }
        }

        // ── Сканер исходников ─────────────────────────────────────────────

        private static List<SyncedProperty> FindSyncedProperties()
        {
            var result = new List<SyncedProperty>();

            foreach (string root in Roots)
            {
                string dir = Path.Combine(Application.dataPath, root);
                if (!Directory.Exists(dir)) continue;

                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(file);
                    if (!text.Contains("EndSyncProperty(")) continue;

                    string className = ClassNameOf(file);

                    foreach (Match header in PropertyHeader.Matches(text))
                    {
                        int open = header.Index + header.Length - 1;
                        int close = MatchingBrace(text, open);
                        if (close < 0) continue;

                        string body = text.Substring(open, close - open + 1);
                        if (!body.Contains("EndSyncProperty(")) continue;

                        Match field = OwnFieldAssignment.Match(body);
                        result.Add(new SyncedProperty(className, header.Groups[1].Value,
                                                      field.Success ? field.Groups[1].Value : null,
                                                      Relative(file)));
                    }
                }
            }

            return result;
        }

        /// <summary>Поле встречается в <c>SerializeStateValue</c> любого partial-файла класса.</summary>
        private static bool IsSerializedInState(SyncedProperty p)
        {
            string dir = Path.GetDirectoryName(Path.Combine(Application.dataPath, "..", p.File));
            var files = Directory.GetFiles(dir, p.ClassName + ".cs")
                                 .Concat(Directory.GetFiles(dir, p.ClassName + ".*.cs"));

            var usage = new Regex(@"SerializeStateValue\s*\([^;]*\b" + Regex.Escape(p.Field) + @"\b");
            return files.Any(f => usage.IsMatch(File.ReadAllText(f)));
        }

        /// <summary>Имя класса — по имени файла до первой точки (<c>UxrActor.StateSave.cs</c> → <c>UxrActor</c>).</summary>
        private static string ClassNameOf(string file)
        {
            string name = Path.GetFileName(file);
            return name.Substring(0, name.IndexOf('.'));
        }

        private static int MatchingBrace(string text, int open)
        {
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return i;
            }
            return -1;
        }

        private static string Relative(string file)
        {
            string full = Path.GetFullPath(file).Replace('\\', '/');
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(project) ? full.Substring(project.Length) : full;
        }

        // ── Актор без сцены ───────────────────────────────────────────────

        private static UxrActor CreateActor(string name, float life)
        {
            var go = new GameObject("T34_" + name);
            UxrActor actor = go.AddComponent<UxrActor>();
            SetLife(actor, life);
            return actor;
        }

        /// <summary>
        ///     Мимо сеттера <c>Life</c>: он синхронизируемый и в EditMode без <c>UxrManager</c> ни к чему.
        ///     Здесь проверяется снимок, а не событие.
        /// </summary>
        private static void SetLife(UxrActor actor, float life)
        {
            FieldInfo field = typeof(UxrActor).GetField("_life", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "В UxrActor нет поля _life — тест устарел вместе с SDK.");
            field.SetValue(actor, life);
        }

        /// <summary>То же, что <c>StoreInitialState</c> SDK в конце первого кадра: запомнить «начало».</summary>
        private static void StoreInitialState(UxrActor actor)
        {
            ((IUxrStateSave)actor).SerializeState(UxrDummySerializer.WriteModeSerializer,
                                                  ((IUxrStateSave)actor).StateSerializationVersion,
                                                  UxrStateSaveLevel.ChangesSinceBeginning,
                                                  UxrStateSaveOptions.DontSerialize | UxrStateSaveOptions.ResetChangesCache |
                                                  UxrStateSaveOptions.FirstFrame);
        }
    }
}
