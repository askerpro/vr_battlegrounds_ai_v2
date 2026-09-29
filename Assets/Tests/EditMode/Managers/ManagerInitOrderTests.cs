using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Tests.Managers
{
    /// <summary>
    /// T-17: порядок инициализации менеджеров задан в одном месте и действительно соблюдается.
    ///
    /// <para>
    /// Что охраняют эти тесты. Порядок объявлен константами в <see cref="ManagerOrder" />,
    /// а работает он через <c>[DefaultExecutionOrder]</c> на самих менеджерах — то есть
    /// объявление и применение разнесены по разным файлам. Такая пара расходится молча:
    /// забытый атрибут не ломает компиляцию, менеджер просто просыпается в случайный
    /// момент, и это всплывает через полгода как «иногда не грузится карта».
    /// </para>
    ///
    /// <para>
    /// Тесты сверяют пару целиком: у каждого менеджера из объявленного состава есть
    /// атрибут, его значение взято из <see cref="ManagerOrder" />, и все значения различны.
    /// </para>
    /// </summary>
    public class ManagerInitOrderTests
    {
        /// <summary>
        /// Менеджеры, для которых порядок обязателен, и имя их константы в <see cref="ManagerOrder" />.
        /// Список ведётся руками намеренно: он и есть проверяемое утверждение
        /// «вот кто участвует в порядке инициализации».
        /// </summary>
        private static readonly Dictionary<Type, string> Expected = new Dictionary<Type, string>
        {
            { typeof(PersistentRoot),                                  nameof(ManagerOrder.PersistentRoot) },
            { typeof(PlayersManager),                                  nameof(ManagerOrder.PlayersManager) },
            { typeof(SessionRecoveryManager),                          nameof(ManagerOrder.SessionRecoveryManager) },
            { typeof(global::VrBattlegrounds.Player.Avatars.AvatarManager), nameof(ManagerOrder.AvatarManager) },
            { typeof(MapLoader),                                      nameof(ManagerOrder.MapLoader) },
            { typeof(global::VrBattlegrounds.PhysicalSpaceUtils.PhysicalSpaceSyncManager), nameof(ManagerOrder.PhysicalSpaceSyncManager) },
            { typeof(global::VrBattlegrounds.Network.GameNetworkManager), nameof(ManagerOrder.GameNetworkManager) },
            { typeof(global::VrBattlegrounds.DevTools.DebugOrchestrator), nameof(ManagerOrder.DebugOrchestrator) },
            { typeof(SessionManager),                                  nameof(ManagerOrder.SessionManager) },
            { typeof(global::VrBattlegrounds.Network.NetworkStateRelay), nameof(ManagerOrder.NetworkStateRelay) },
            { typeof(MapReferee),                                 nameof(ManagerOrder.MapReferee) }
        };

        private static int ConstantValue(string name)
        {
            FieldInfo field = typeof(ManagerOrder).GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, $"В ManagerOrder нет константы {name}.");
            return (int)field.GetValue(null);
        }

        [Test]
        public void У_каждого_менеджера_есть_явный_порядок_выполнения()
        {
            List<string> problems = new List<string>();

            foreach (KeyValuePair<Type, string> pair in Expected)
            {
                var attribute = (DefaultExecutionOrder)Attribute.GetCustomAttribute(
                    pair.Key, typeof(DefaultExecutionOrder));

                if (attribute == null)
                {
                    problems.Add($"{pair.Key.Name}: нет [DefaultExecutionOrder]");
                    continue;
                }

                int expected = ConstantValue(pair.Value);
                if (attribute.order != expected)
                {
                    problems.Add($"{pair.Key.Name}: атрибут {attribute.order}, " +
                                 $"а ManagerOrder.{pair.Value} = {expected}");
                }
            }

            Assert.IsEmpty(problems,
                "Объявленный порядок и реальный разошлись:\n  " + string.Join("\n  ", problems.ToArray()) +
                "\nПорядок задаётся в ManagerOrder, применяется атрибутом на классе менеджера. " +
                "Расходятся они молча — компиляция от этого не ломается.");
        }

        [Test]
        public void Порядок_менеджеров_не_содержит_совпадающих_значений()
        {
            Dictionary<int, string> seen = new Dictionary<int, string>();
            List<string> collisions = new List<string>();

            foreach (FieldInfo field in typeof(ManagerOrder).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(int)) continue;

                int value = (int)field.GetValue(null);

                if (seen.TryGetValue(value, out string other))
                    collisions.Add($"{field.Name} и {other} оба равны {value}");
                else
                    seen[value] = field.Name;
            }

            Assert.IsEmpty(collisions,
                "Одинаковые значения возвращают порядок в исходное неопределённое состояние — " +
                "Unity сама решит, кто первый:\n  " + string.Join("\n  ", collisions.ToArray()));
        }

        [Test]
        public void Корень_иерархии_инициализируется_раньше_всех()
        {
            int root = ConstantValue(nameof(ManagerOrder.PersistentRoot));

            foreach (KeyValuePair<Type, string> pair in Expected)
            {
                if (pair.Key == typeof(PersistentRoot)) continue;

                Assert.Less(root, ConstantValue(pair.Value),
                    $"PersistentRoot обязан просыпаться раньше {pair.Key.Name}: он делает " +
                    "DontDestroyOnLoad всей ветке, и до этого менеджеры трогать нельзя.");
            }
        }

        [Test]
        public void Сеть_инициализируется_после_тех_кого_зовёт_из_колбэков()
        {
            int network = ConstantValue(nameof(ManagerOrder.GameNetworkManager));

            Assert.Greater(network, ConstantValue(nameof(ManagerOrder.PlayersManager)),
                "GameNetworkManager.OnServerReady и OnGamePlayerConnect зовут PlayersManager — " +
                "он обязан быть готов раньше.");

            Assert.Greater(network, ConstantValue(nameof(ManagerOrder.AvatarManager)),
                "GameNetworkManager.OnServerReady зовёт AvatarManager.ChangeAvatar — " +
                "он обязан быть готов раньше.");
        }

        [Test]
        public void Состав_постоянных_менеджеров_описывает_только_синглтоны()
        {
            FieldInfo rosterField = typeof(ManagerBootstrap).GetField("PersistentRoster",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(rosterField,
                "ManagerBootstrap.PersistentRoster не найден — состав менеджеров переименовали.");

            Array roster = (Array)rosterField.GetValue(null);
            Assert.Greater(roster.Length, 0, "Состав постоянных менеджеров пуст.");

            FieldInfo typeField = roster.GetType().GetElementType()
                .GetField("Type", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(typeField, "У описания менеджера нет поля Type.");

            List<string> problems = new List<string>();

            foreach (object slot in roster)
            {
                Type managerType = (Type)typeField.GetValue(slot);

                PropertyInfo instance = managerType.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

                if (instance == null)
                    problems.Add($"{managerType.Name}: нет статического свойства Instance");
            }

            Assert.IsEmpty(problems,
                "ManagerBootstrap проверяет готовность по свойству Instance. Тип без него " +
                "он проверить не сможет и напишет Error на пустом месте:\n  " +
                string.Join("\n  ", problems.ToArray()));
        }
    }
}
