using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Совместимость режимов с картой (<see cref="MapModeRules"/>) — чистые правила.
    ///
    /// <para>
    /// Что доказывает. Любая карта стартует в разминке; «Начать матч» берёт выбор
    /// администратора, если он совместим с картой, иначе первый совместимый режим матча;
    /// у лобби режимов матча нет — матч там не начинается. Заменяет прежний тест
    /// «режим сцены бьёт выбор администратора»: поле «режим сцены» удалено.
    /// </para>
    /// </summary>
    public class MapModeRulesTests
    {
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void Drop()
        {
            foreach (Object o in _assets) if (o != null) Object.DestroyImmediate(o);
            _assets.Clear();
        }

        private GameModeData Mode(string id, bool warmup = false)
        {
            var data = ScriptableObject.CreateInstance<GameModeData>();
            data.modeId = id;
            data.isWarmup = warmup;
            _assets.Add(data);
            return data;
        }

        private MapData Map(string scene, params GameModeData[] modes)
        {
            var map = ScriptableObject.CreateInstance<MapData>();
            map.sceneName = scene;
            map.supportedModes = modes;
            _assets.Add(map);
            return map;
        }

        private GameModeRegistry Registry(params GameModeData[] modes)
        {
            var registry = ScriptableObject.CreateInstance<GameModeRegistry>();
            registry.modes = modes;
            _assets.Add(registry);
            return registry;
        }

        [Test]
        public void Карта_стартует_с_разминки()
        {
            GameModeData warmup = Mode("warmup", warmup: true), elim = Mode("elimination");
            GameModeRegistry registry = Registry(elim, warmup);

            Assert.AreSame(warmup, MapModeRules.ResolveWarmup(Map("A", elim, warmup), registry), "Разминка из списка карты.");
            Assert.AreSame(warmup, MapModeRules.ResolveWarmup(null, registry), "Карта не из реестра — разминка реестра.");
            Assert.AreSame(warmup, MapModeRules.ResolveWarmup(Map("B", elim), registry),
                "Карта без разминки в списке всё равно стартует с разминки реестра.");
        }

        [Test]
        public void Совместимый_выбор_администратора_запускается()
        {
            GameModeData warmup = Mode("warmup", true), elim = Mode("elimination"), respawn = Mode("respawn");
            MapData map = Map("A", warmup, elim, respawn);

            Assert.AreSame(respawn, MapModeRules.ResolveMatchMode(map, respawn, Registry(warmup, elim, respawn)));
        }

        [Test]
        public void Несовместимый_выбор_заменяется_первым_совместимым()
        {
            GameModeData warmup = Mode("warmup", true), elim = Mode("elimination"), respawn = Mode("respawn");
            MapData map = Map("A", warmup, elim);

            Assert.AreSame(elim, MapModeRules.ResolveMatchMode(map, respawn, Registry(warmup, elim, respawn)),
                "Режим несовместим с картой — берётся первый совместимый режим матча.");
            Assert.AreSame(elim, MapModeRules.ResolveMatchMode(map, null, Registry(warmup, elim, respawn)),
                "Ничего не выбрано — первый совместимый.");
            Assert.AreSame(elim, MapModeRules.ResolveMatchMode(map, warmup, Registry(warmup, elim, respawn)),
                "Разминка — не режим матча: «Начать матч» с ней не остаётся в разминке.");
        }

        [Test]
        public void В_лобби_матч_не_запускается()
        {
            GameModeData warmup = Mode("warmup", true), elim = Mode("elimination");
            MapData lobby = Map("Lobby", warmup);

            Assert.IsFalse(MapModeRules.IsCompatible(lobby, elim), "Elimination совместим с лобби.");
            Assert.IsNull(MapModeRules.ResolveMatchMode(lobby, elim, Registry(warmup, elim)),
                "В лобби запустился режим матча: у лобби совместима только разминка.");
        }
    }
}
