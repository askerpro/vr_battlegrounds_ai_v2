using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Зарегистрированные аватары — единственный источник для тестов, проверяющих аватары.
    ///
    /// <para>
    /// Проверяется то, что игра может выдать игроку: <c>AvatarRegistry</c> (все его ассеты вне
    /// <c>ThirdParty</c>). Префаб, который просто лежит в проекте и не зарегистрирован, тесты не
    /// валит: в игру он не попадёт. Поиск по папке, как было раньше, делал красными заброшенные скины.
    /// </para>
    /// </summary>
    public static class RegisteredAvatars
    {
        public const string RegistryPath = "Assets/Data/Player/Avatars/AvatarsRegistry.asset";

        public static IEnumerable<AvatarData> Data()
        {
            return AssetDatabase.FindAssets("t:AvatarRegistry")
                                .Select(AssetDatabase.GUIDToAssetPath)
                                .Where(p => !p.StartsWith("Assets/ThirdParty/"))
                                .Select(AssetDatabase.LoadAssetAtPath<AvatarRegistry>)
                                .Where(r => r != null && r.avatars != null)
                                .SelectMany(r => r.avatars)
                                .Where(a => a != null)
                                .Distinct();
        }

        public static IEnumerable<GameObject> Prefabs()
        {
            return Data().Select(a => a.prefab).Where(p => p != null).Distinct();
        }

        public static bool Contains(GameObject prefab) => prefab != null && Prefabs().Contains(prefab);
    }
}
