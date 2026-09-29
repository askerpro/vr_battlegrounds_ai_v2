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
    /// Что доказывает. Разминка — не режим каталога: её нет ни в списке режимов реестра, ни
    /// в списках карт, она отдельное поле реестра (<see cref="GameModeRegistry.warmup"/>),
    /// и «Начать матч» её не выбирает. «Начать матч» берёт выбор администратора, если он
    /// совместим с картой, иначе первый совместимый режим матча. Лобби — карта реестра
    /// с пустым списком режимов: матч там не начинается. Пустой список «подходит любой режим»
    /// значит только для сцены вне реестра (тестовая сцена).
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

        private GameModeData Mode(string id)
        {
            var data = ScriptableObject.CreateInstance<GameModeData>();
            data.modeId = id;
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

        private GameModeRegistry Registry(GameModeData warmup, params GameModeData[] modes)
        {
            var registry = ScriptableObject.CreateInstance<GameModeRegistry>();
            registry.warmup = warmup;
            registry.modes = modes;
            _assets.Add(registry);
            return registry;
        }

        [Test]
        public void Разминка_не_режим_матча()
        {
            GameModeData warmup = Mode("warmup"), elim = Mode("elimination");
            GameModeRegistry registry = Registry(warmup, elim);

            Assert.AreSame(warmup, registry.Warmup);
            CollectionAssert.AreEqual(new[] { elim }, registry.MatchModes, "Разминка попала в режимы матча.");
            Assert.AreSame(warmup, registry.GetById("warmup"), "Клиент не найдёт разминку по modeId.");
        }

        [Test]
        public void Совместимый_выбор_администратора_запускается()
        {
            GameModeData warmup = Mode("warmup"), elim = Mode("elimination"), respawn = Mode("respawn");
            MapData map = Map("A", elim, respawn);

            Assert.AreSame(respawn, MapModeRules.ResolveMatchMode(map, respawn, Registry(warmup, elim, respawn)));
        }

        [Test]
        public void Несовместимый_выбор_заменяется_первым_совместимым()
        {
            GameModeData warmup = Mode("warmup"), elim = Mode("elimination"), respawn = Mode("respawn");
            GameModeRegistry registry = Registry(warmup, elim, respawn);
            MapData map = Map("A", elim);

            Assert.AreSame(elim, MapModeRules.ResolveMatchMode(map, respawn, registry),
                "Режим несовместим с картой — берётся первый совместимый режим матча.");
            Assert.AreSame(elim, MapModeRules.ResolveMatchMode(map, null, registry),
                "Ничего не выбрано — первый совместимый.");
            Assert.AreSame(elim, MapModeRules.ResolveMatchMode(map, warmup, registry),
                "Разминка — не режим матча: «Начать матч» с ней не остаётся в разминке.");
        }

        [Test]
        public void В_лобби_матч_не_запускается()
        {
            GameModeData warmup = Mode("warmup"), elim = Mode("elimination");
            MapData lobby = Map("Lobby");

            Assert.IsFalse(MapModeRules.IsCompatible(lobby, elim), "Elimination совместим с лобби.");
            Assert.IsNull(MapModeRules.ResolveMatchMode(lobby, elim, Registry(warmup, elim)),
                "В лобби запустился режим матча: у лобби режимов матча нет.");
        }

        [Test]
        public void Сцена_вне_реестра_допускает_любой_режим_матча()
        {
            GameModeData warmup = Mode("warmup"), elim = Mode("elimination");

            Assert.IsTrue(MapModeRules.IsCompatible(null, elim));
            Assert.AreSame(elim, MapModeRules.ResolveMatchMode(null, null, Registry(warmup, elim)));
            Assert.AreSame(elim, MapModeRules.ResolveMatchMode(null, warmup, Registry(warmup, elim)),
                "Разминка — не режим матча и на сцене вне реестра.");
        }
    }
}
