using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mirror;
using NUnit.Framework;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Tests.Network
{
    /// <summary>
    /// Mirror различает <c>[Command]</c>/<c>[ClientRpc]</c>/<c>[TargetRpc]</c> по 16-битному хэшу полного имени метода
    /// (<c>RemoteProcedureCalls</c>). При совпадении второй метод не регистрируется: вызов уходит чужому
    /// обработчику. Так переименование <c>ShotgunShellReceiver</c> → <c>CartridgeIntake</c> дало
    /// <c>CmdRequestAdmission</c> тот же хэш 14651, что у <c>NetworkStateRelay.CmdComponentStateChanged</c>,
    /// и запрос приёма патрона перестал доходить до сервера. Класс ошибки ловится до запуска игры.
    /// </summary>
    public class RemoteCallHashTests
    {
        // Сборки, чьи NetworkBehaviour живут в одной игре: игра, Mirror и его компоненты.
        private static readonly string[] Assemblies = { "VrBattlegrounds", "Mirror", "Mirror.Components" };

        /// <summary>Хэш, под которым Mirror регистрирует вызов (тот же расчёт, что в RemoteProcedureCalls).</summary>
        private static ushort Hash(string functionFullName) => (ushort)(functionFullName.GetStableHashCode() & 0xFFFF);

        /// <summary>Полное имя в формате Cecil, которое передаёт Weaver: «System.Void Ns.Type::Name(Arg1,Arg2)».</summary>
        private static string FullName(Type type, string name, IEnumerable<Type> parameters) =>
            $"System.Void {type.FullName.Replace('+', '/')}::{name}({string.Join(",", parameters.Select(p => p.FullName))})";

        private static IEnumerable<(string Name, string FullName)> RemoteCalls()
        {
            var seen = new HashSet<string>();
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => Assemblies.Contains(assembly.GetName().Name))
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => typeof(NetworkBehaviour).IsAssignableFrom(type));
            foreach (Type type in types)
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                                          BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!method.IsDefined(typeof(CommandAttribute), false) && !method.IsDefined(typeof(ClientRpcAttribute), false) &&
                    !method.IsDefined(typeof(TargetRpcAttribute), false)) continue;
                // Weaver оставляет атрибут либо на исходном методе, либо на «UserCode_<имя>__<аргументы>».
                string name = method.Name;
                if (name.StartsWith("UserCode_", StringComparison.Ordinal))
                {
                    name = name.Substring("UserCode_".Length);
                    int suffix = name.IndexOf("__", StringComparison.Ordinal);
                    if (suffix >= 0) name = name.Substring(0, suffix);
                }
                string fullName = FullName(type, name, method.GetParameters().Select(p => p.ParameterType));
                if (seen.Add(fullName)) yield return ($"{type.Name}.{name}", fullName);
            }
        }

        [Test]
        public void Расчёт_хэша_совпадает_с_регистрацией_Mirror()
        {
            // 14651 — ключ, под которым Mirror зарегистрировал команду в живой игре (RemoteProcedureCalls, 2026-10-06).
            string relay = FullName(typeof(NetworkStateRelay), "CmdComponentStateChanged",
                new[] { typeof(byte[]), typeof(NetworkConnectionToClient) });
            Assert.AreEqual(14651, Hash(relay));
            Assert.That(RemoteCalls().Select(call => call.FullName), Has.Member(relay),
                "Тест не нашёл известную команду — поменялся формат Weaver.");
        }

        [Test]
        public void Прежнее_имя_команды_приёма_патрона_давало_коллизию()
        {
            string relay = FullName(typeof(NetworkStateRelay), "CmdComponentStateChanged",
                new[] { typeof(byte[]), typeof(NetworkConnectionToClient) });
            string old = FullName(typeof(VrBattlegrounds.Weapons.CartridgeIntake), "CmdRequestAdmission",
                new[] { typeof(ulong), typeof(uint), typeof(uint), typeof(Guid), typeof(NetworkConnectionToClient) });
            Assert.AreEqual(Hash(relay), Hash(old), "Проверка обязана видеть исторический случай.");
        }

        [Test]
        public void У_каждого_сетевого_вызова_свой_хэш()
        {
            var collisions = RemoteCalls()
                .GroupBy(call => Hash(call.FullName))
                .Where(group => group.Count() > 1)
                .Select(group => $"{group.Key}: {string.Join(", ", group.Select(call => call.Name))}")
                .ToList();
            Assert.That(collisions, Is.Empty, "Переименуйте один из методов:\n" + string.Join("\n", collisions));
        }
    }
}
