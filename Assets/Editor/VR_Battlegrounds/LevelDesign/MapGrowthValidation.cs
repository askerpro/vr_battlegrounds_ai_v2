using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    [Serializable] public sealed class MapGrowthInputIssue
    {
        public string code, ownerId, field, message;
    }

    [Serializable] public sealed class MapGrowthInputValidation
    {
        public List<MapGrowthInputIssue> errors = new List<MapGrowthInputIssue>();
        public List<MapGrowthInputIssue> warnings = new List<MapGrowthInputIssue>();
        public List<MapGrowthInputIssue> uncheckedRequirements = new List<MapGrowthInputIssue>();
        public bool CanStart => errors.Count == 0;
    }

    /// <summary>Проверка замысла без физики и изменения авторского артефакта; наличие защиты не выводится из схемы.</summary>
    public static class MapGrowthValidation
    {
        public static MapGrowthInputValidation Validate(BlockoutMarkup markup, MapGrowthSettings settings)
        {
            var result = ValidateInput(markup, ReferenceEquals(markup, null) || ReferenceEquals(markup.bodyProfile, null)
                ? null : markup.bodyProfile.data, ReferenceEquals(settings, null) ? null : settings.search,
                ReferenceEquals(settings, null) ? Array.Empty<string>() : settings.fixedBlockGlobalObjectIds);
            if (!ReferenceEquals(settings, null) && (!Enum.IsDefined(typeof(MapEvaluationProfile), settings.profile)
                || settings.profile == MapEvaluationProfile.Unspecified))
                Add(result.uncheckedRequirements, "MAP_PROFILE", "map", "profile", "Характер Open/Closed не задан или неизвестен.");
            return result;
        }

        /// <summary>Проверка ссылок и совместимости контактов перед объединением; не заменяет проверку всего входа поиска.</summary>
        public static MapGrowthInputValidation ValidateContactInput(BlockoutMarkup markup)
        {
            var result = new MapGrowthInputValidation();
            if (ReferenceEquals(markup, null)) { Error(result, "MISSING_MARKUP", "map", "markup", "Нет разметки."); return result; }
            var states = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (markup.positions == null) Error(result, "POSITIONS", "map", "positions", "Нет списка позиций.");
            foreach (var p in markup.positions ?? new List<BlockoutMarkup.Position>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.id) || states.ContainsKey(p.id))
                { Error(result, "POSITION_ID", p?.id, "id", "Пустой или повторяющийся ID позиции."); continue; }
                var ids = new HashSet<string>(StringComparer.Ordinal); states.Add(p.id, ids);
                foreach (var s in p.states ?? Array.Empty<BlockoutPositionState>())
                    if (s == null || string.IsNullOrWhiteSpace(s.id) || !ids.Add(s.id))
                        Error(result, "STATE_ID", p.id, "states.id", "Пустой или повторяющийся ID позы.");
            }
            ValidateContacts(markup.contacts, states, result);
            return result;
        }

        // Автономный вход для числовой проверки; Unity-объекты здесь используются только как контейнер сериализованных полей.
        public static MapGrowthInputValidation ValidateInput(BlockoutMarkup markup, MapBodyProfileData profile, MapGrowthSearchParameters search,
            IReadOnlyList<string> fixedBlockIds = null)
        {
            var r = new MapGrowthInputValidation();
            if (ReferenceEquals(markup, null)) { Error(r, "MISSING_MARKUP", "map", "markup", "Нет разметки."); return r; }
            if (markup.schemaVersion != BlockoutMarkup.CurrentSchemaVersion) Error(r, "SCHEMA", "map", "schemaVersion", "Нужна явная миграция разметки v2.");
            if (!Finite(markup.origin.x) || !Finite(markup.origin.y) || !Positive(markup.step) || !Positive(markup.module))
                Error(r, "GRID", "map", "origin/step/module", "Нужны конечное начало и положительные шаг/модуль.");
            ValidateProfile(profile, r);
            ValidateSearch(search, r);
            if (fixedBlockIds != null)
            {
                var fixedIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in fixedBlockIds)
                    if (string.IsNullOrWhiteSpace(id) || !fixedIds.Add(id))
                        Error(r, "FIXED_BLOCK_ID", "map", "fixedBlockGlobalObjectIds", "Пустой или повторяющийся ID фиксированного блока.");
                if (fixedIds.Count > 0) Add(r.uncheckedRequirements, "FIXED_BLOCK_RESOLUTION", "map", "fixedBlockGlobalObjectIds",
                    "Ссылки на фиксированные блоки нужно разрешить в актуальной сцене при захвате геометрии.");
            }
            var positions = new Dictionary<string, BlockoutMarkup.Position>(StringComparer.Ordinal);
            var states = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (markup.positions == null || markup.positions.Count < 1 || markup.positions.Count > 32)
                Error(r, "POSITION_LIMIT", "map", "positions", "Требуется от 1 до 32 позиций.");
            int totalStates = 0;
            foreach (var p in markup.positions ?? new List<BlockoutMarkup.Position>())
            {
                if (p == null || !NewId(positions, p.id, p)) { Error(r, "POSITION_ID", p?.id, "id", "Пустой или повторяющийся ID позиции."); continue; }
                if (p.sizeCells < 1) Error(r, "POSITION_SIZE", p.id, "sizeCells", "Размер области должен быть положительным.");
                // Те же клетки, что PaintPosition: чётный размер асимметричен относительно центра опорной клетки.
                float areaMin = (-(p.sizeCells / 2) - .5f) * markup.step;
                float areaMax = (p.sizeCells - p.sizeCells / 2 - .5f) * markup.step;
                if (!Finite(areaMin) || !Finite(areaMax)) Error(r, "POSITION_SIZE", p.id, "sizeCells", "Границы области переполнены.");
                if (!Finite(p.mainThreatYaw)) Error(r, "NONFINITE", p.id, "mainThreatYaw", "Угол угрозы не конечен.");
                var ids = new HashSet<string>(StringComparer.Ordinal); states.Add(p.id, ids);
                if (p.states == null || p.states.Length == 0) Error(r, "MISSING_STATES", p.id, "states", "Позиция не содержит явно заданных поз.");
                if (p.states != null && p.states.Length > 8) Error(r, "STATE_LIMIT", p.id, "states", "Допустимо до 8 поз на позицию.");
                foreach (var s in p.states ?? Array.Empty<BlockoutPositionState>())
                {
                    totalStates++;
                    if (s == null || string.IsNullOrWhiteSpace(s.id) || !ids.Add(s.id)) { Error(r, "STATE_ID", p.id, "states.id", "Пустой или повторяющийся ID позы."); continue; }
                    string field = "states/" + s.id;
                    if (!Finite(s.centerOffset.x) || !Finite(s.centerOffset.y) || !Finite(s.yaw))
                        Error(r, "NONFINITE", p.id, field, "Координаты/угол позы не конечны.");
                    else if (s.centerOffset.x < areaMin || s.centerOffset.x > areaMax || s.centerOffset.y < areaMin || s.centerOffset.y > areaMax)
                        Error(r, "STATE_OUTSIDE_POSITION", p.id, field, "Центр позы вне области позиции.");
                    if (!Enum.IsDefined(typeof(BlockoutStance), s.stance)) Error(r, "STANCE", p.id, field, "Неизвестная поза тела.");
                }
                if (!string.IsNullOrEmpty(p.protectedStateId) && !ids.Contains(p.protectedStateId))
                    Error(r, "UNKNOWN_STATE", p.id, "protectedStateId", "Неизвестная закрытая поза «" + p.protectedStateId + "».");
            }
            if (totalStates > 64) Error(r, "STATE_LIMIT", "map", "positions.states", "Суммарно допустимо до 64 поз.");
            var routeIds = new HashSet<string>(StringComparer.Ordinal);
            if (markup.routes == null || markup.routes.Count > 32) Error(r, "ROUTE_LIMIT", "map", "routes", "Нужен список не более 32 маршрутов.");
            foreach (var route in markup.routes ?? new List<BlockoutMarkup.Route>())
            {
                if (route == null || string.IsNullOrWhiteSpace(route.id) || !routeIds.Add(route.id)) { Error(r, "ROUTE_ID", route?.id, "id", "Пустой или повторяющийся ID маршрута."); continue; }
                StateReference(r, states, route.id, "from", route.fromPositionId, route.fromStateId);
                StateReference(r, states, route.id, "to", route.toPositionId, route.toStateId);
                if (route.viaCells == null || route.viaCells.Count > 16) Error(r, "PORTAL_LIMIT", route.id, "viaCells", "Нужен список не более 16 промежуточных точек прохода.");
                else foreach (var point in route.viaCells)
                    if (!PositionImpactValidation.Finite(markup.origin + new Vector2(point.x + .5f, point.y + .5f) * markup.step))
                        Error(r, "PORTAL_COORDINATE", route.id, "viaCells", "Промежуточная точка выходит за конечные мировые координаты.");
                if (route.widthCells < 1 || route.widthCells > 15) Error(r, "CORRIDOR_WIDTH", route.id, "widthCells", "Ширина кисти прохода: 1–15 клеток.");
            }
            ValidateMemberships(markup, positions, routeIds, r);
            ValidateContacts(markup.contacts, states, r);
            return r;
        }

        private static void ValidateMemberships(BlockoutMarkup markup, Dictionary<string, BlockoutMarkup.Position> positions,
            HashSet<string> routes, MapGrowthInputValidation r)
        {
            var covers = new HashSet<string>(StringComparer.Ordinal);
            if (markup.covers == null) Error(r, "COVERS", "map", "covers", "Нет списка укрытий.");
            foreach (var cover in markup.covers ?? new List<BlockoutMarkup.Cover>())
            {
                if (cover == null || string.IsNullOrWhiteSpace(cover.id) || !covers.Add(cover.id))
                { Error(r, "COVER_ID", cover?.id, "id", "Пустой или повторяющийся ID укрытия."); continue; }
                Membership(cover.blockGlobalObjectIds, null, cover.id, "blockGlobalObjectIds", r);
            }
            foreach (var position in positions.Values)
            {
                Membership(position.supportingCoverIds, null, position.id, "supportingCoverIds", r);
                foreach (string id in position.supportingCoverIds ?? new List<string>())
                    if (!string.IsNullOrWhiteSpace(id) && !covers.Contains(id))
                        Error(r, "UNKNOWN_SUPPORT", position.id, "supportingCoverIds", "Неизвестное укрытие «" + id + "».");
            }
            if (markup.cells == null) Error(r, "CELLS", "map", "cells", "Нет списка клеток разметки.");
            var coordinates = new HashSet<(int, int)>();
            var usedRoutes = new HashSet<string>(StringComparer.Ordinal);
            var positionIds = new HashSet<string>(positions.Keys, StringComparer.Ordinal);
            foreach (var cell in markup.cells ?? new List<BlockoutMarkup.Cell>())
            {
                if (cell == null) { Error(r, "CELL", "map", "cells", "Пустая запись клетки."); continue; }
                string owner = "cell:" + cell.x + "," + cell.z;
                if (!coordinates.Add((cell.x, cell.z))) Error(r, "DUPLICATE_CELL", owner, "cells", "Несколько записей одной клетки.");
                Membership(cell.positionIds, positionIds, owner, "positionIds", r);
                Membership(cell.routeIds, routes, owner, "routeIds", r);
                Membership(cell.coverIds, covers, owner, "coverIds", r);
                // Constraint IDs пока являются самостоятельными метками: отдельного реестра ограничений ещё нет.
                Membership(cell.constraintIds, null, owner, "constraintIds", r);
                foreach (string id in cell.routeIds ?? new List<string>()) if (id != null) usedRoutes.Add(id);
                if (cell.positionIds?.Count > 0 && cell.coverIds?.Count > 0)
                    Add(r.warnings, "POSITION_COVER_CONFLICT", owner, "positionIds/coverIds", "Область позиции пересекает требование укрытия; разметка сохранена.");
            }
            foreach (string id in routes)
                if (!usedRoutes.Contains(id)) Error(r, "MISSING_CORRIDOR", id, "cells.routeIds", "У маршрута нет нарисованных клеток допустимого коридора.");
        }

        private static void Membership(List<string> ids, HashSet<string> known, string owner, string field, MapGrowthInputValidation r)
        {
            if (ids == null) { Error(r, "MEMBERSHIP_LIST", owner, field, "Нет списка принадлежностей."); return; }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) Error(r, "MEMBERSHIP_ID", owner, field, "Пустая или повторяющаяся принадлежность.");
                else if (known != null && !known.Contains(id)) Error(r, "UNKNOWN_MEMBERSHIP", owner, field, "Неизвестная ссылка «" + id + "».");
        }

        private static void ValidateProfile(MapBodyProfileData p, MapGrowthInputValidation r)
        {
            if (p == null) { Error(r, "MISSING_PROFILE", "map", "bodyProfile", "Не выбран профиль тела."); return; }
            if (string.IsNullOrWhiteSpace(p.id) || p.version < 1) Error(r, "PROFILE_ID", p.id, "id/version", "Нужны ID и положительная версия профиля.");
            if (!Positive(p.radius) || !Positive(p.speed)) Error(r, "PROFILE_SIZE", p.id, "radius/speed", "Радиус и скорость должны быть конечными положительными числами.");
            if (!p.calibrated) Add(r.uncheckedRequirements, "PROFILE_UNCALIBRATED", p.id, "calibrated", "Профиль демонстрационный; полный габарит и позы требуют принятия человеком.");
            PoseTemplate(p, p.standing, "standing", r); PoseTemplate(p, p.crouching, "crouching", r);
        }

        private static void PoseTemplate(MapBodyProfileData p, MapBodyPoseTemplate pose, string field, MapGrowthInputValidation r)
        {
            if (pose == null) { Error(r, "MISSING_BODY_POSE", p.id, field, "Нет шаблона тела."); return; }
            if (!Finite(pose.eyeOffset) || !Finite(pose.muzzleOffset) || pose.eyeOffset.y < 0 || pose.muzzleOffset.y < 0)
                Error(r, "BODY_ANCHOR", p.id, field, "Глаз/ствол должны иметь конечные координаты и неотрицательную высоту.");
            if (pose.body == null || pose.body.Length < 1 || pose.body.Length > 16)
                Error(r, "BODY_SAMPLE", p.id, field, "Нужно от 1 до 16 образцов тела.");
            double totalWeight = 0;
            foreach (var sample in pose.body ?? Array.Empty<ImpactBodySample>())
            {
                if (sample == null || !Finite(sample.offset) || sample.offset.y < 0 || !Positive(sample.weight))
                    Error(r, "BODY_SAMPLE", p.id, field, "Неверные координаты или вес образца тела.");
                else
                {
                    totalWeight += sample.weight;
                    if ((double)sample.offset.x * sample.offset.x + (double)sample.offset.z * sample.offset.z > (double)p.radius * p.radius)
                        Error(r, "BODY_OUTSIDE_RADIUS", p.id, field, "Образец тела вне проверяемого горизонтального диска.");
                }
            }
            if (totalWeight > float.MaxValue) Error(r, "BODY_SAMPLE", p.id, field, "Сумма весов переполнена.");
        }

        private static void ValidateSearch(MapGrowthSearchParameters p, MapGrowthInputValidation r)
        {
            if (p == null) { Error(r, "MISSING_SETTINGS", "map", "settings", "Нет параметров поиска."); return; }
            if (p.targetCandidates < 1 || p.targetCandidates > 10 || p.branchCount < 1 || p.attemptsPerBranch < 1 || p.maximumGeneratedBlocks < 1 || p.maximumGeneratedBlocks > 512
                || (long)p.branchCount * p.attemptsPerBranch > int.MaxValue)
                Error(r, "SEARCH_BUDGET", "map", "search", "Нужны 1–10 результатов и положительный непереполняющийся бюджет ветвей/попыток.");
            if ((p.operations & ~MapGrowthOperation.All) != 0 || p.operations == MapGrowthOperation.None)
                Error(r, "OPERATIONS", "map", "operations", "Не задано допустимых операций либо есть неизвестные флаги.");
            Range(p.density, "map", "density", r); Range(p.closureStanding, "map", "closureStanding", r); Range(p.closureCrouching, "map", "closureCrouching", r);
        }

        private static void ValidateContacts(List<BlockoutContactSpec> contacts, Dictionary<string, HashSet<string>> states, MapGrowthInputValidation r)
        {
            if (contacts == null) { Error(r, "CONTACTS", "map", "contacts", "Нужен список контактов."); return; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var grouped = new Dictionary<(string, string, string, string), List<BlockoutContactCase>>();
            foreach (var original in contacts)
            {
                if (original == null || string.IsNullOrWhiteSpace(original.id) || !ids.Add(original.id)) { Error(r, "CONTACT_ID", original?.id, "id", "Пустой или повторяющийся ID контакта."); continue; }
                var contact = BlockoutContactSpec.CanonicalCopy(original);
                if (contact.positionAId == contact.positionBId) Error(r, "SELF_CONTACT", contact.id, "positions", "Контакт требует две разные позиции.");
                if (contact.cases == null || contact.cases.Length == 0) Error(r, "CONTACT_CASES", contact.id, "cases", "Не заданы условия контакта.");
                var caseIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var c in contact.cases ?? Array.Empty<BlockoutContactCase>())
                {
                    if (c == null || string.IsNullOrWhiteSpace(c.id) || !caseIds.Add(c.id)) { Error(r, "CONTACT_CASE_ID", contact.id, "cases.id", "Пустой или повторяющийся ID условий."); continue; }
                    StateReference(r, states, contact.id, c.id + "/A", contact.positionAId, c.stateAId);
                    StateReference(r, states, contact.id, c.id + "/B", contact.positionBId, c.stateBId);
                    Direction(c.aToB, contact.id, c.id + "/A→B", r); Direction(c.bToA, contact.id, c.id + "/B→A", r);
                    if (!Enum.IsDefined(typeof(ExpectedAdvantage), c.advantage)) Error(r, "ADVANTAGE", contact.id, c.id, "Неизвестное преимущество.");
                    else if (c.advantage != ExpectedAdvantage.Unspecified)
                        Add(r.uncheckedRequirements, "ADVANTAGE_UNCALIBRATED", contact.id, c.id, "Преимущество — пожелание до принятия критерия оценки.");
                    var key = (contact.positionAId, contact.positionBId, c.stateAId, c.stateBId);
                    if (!grouped.TryGetValue(key, out var group)) grouped.Add(key, group = new List<BlockoutContactCase>());
                    group.Add(c);
                    CheckCombined(group, contact.id, c.id, r);
                }
            }
        }

        private static void CheckCombined(List<BlockoutContactCase> cases, string owner, string field, MapGrowthInputValidation r)
        {
            var advantages = cases.Select(c => c.advantage).Where(a => a != ExpectedAdvantage.Unspecified).Distinct().ToArray();
            if (advantages.Length > 1) Error(r, "ADVANTAGE_CONTRADICTION", owner, field, "Для тех же поз предписаны преимущества обеих сторон.");
            var a = cases.Select(c => c.aToB).Where(d => d != null).ToArray();
            var b = cases.Select(c => c.bToA).Where(d => d != null).ToArray();
            CombinedDirection(a, b, owner, field + "/A→B", r);
            CombinedDirection(b, a, owner, field + "/B→A", r);
        }

        private static void CombinedDirection(BlockoutContactDirection[] directions, BlockoutContactDirection[] opposite, string owner, string field, MapGrowthInputValidation r)
        {
            bool required = directions.Any(d => d.vision == ContactRequirement.Required), forbidden = directions.Any(d => d.vision == ContactRequirement.Forbidden);
            float anyMin = 0, anyMax = 1, visibleMin = 0, visibleMax = 1;
            foreach (var d in directions)
            {
                float min = d.shot == ContactRequirement.Required ? float.Epsilon : 0, max = d.shot == ContactRequirement.Forbidden ? 0 : 1;
                if (d.shotShare != null && d.shotShare.enabled) { min = Math.Max(min, d.shotShare.min); max = Math.Min(max, d.shotShare.max); }
                if (d.visibleShotOnly) { visibleMin = Math.Max(visibleMin, min); visibleMax = Math.Min(visibleMax, max); }
                else { anyMin = Math.Max(anyMin, min); anyMax = Math.Min(anyMax, max); }
            }
            // Открытость противоположного источника — та же доля любого ответного прострела при этих позах.
            foreach (var d in opposite)
                if (d.sourceExposure != null && d.sourceExposure.enabled)
                { anyMin = Math.Max(anyMin, d.sourceExposure.min); anyMax = Math.Min(anyMax, d.sourceExposure.max); }
            if (required && forbidden || anyMin > anyMax || visibleMin > visibleMax || visibleMin > anyMax)
                Error(r, "CONTACT_CONTRADICTION", owner, field, "Несовместимые требования обзора/прострела при одинаковых условиях.");
        }

        private static void Direction(BlockoutContactDirection d, string owner, string field, MapGrowthInputValidation r)
        {
            if (d == null) { Error(r, "CONTACT_DIRECTION", owner, field, "Нет описания направления."); return; }
            if (!Enum.IsDefined(typeof(ContactRequirement), d.vision) || !Enum.IsDefined(typeof(ContactRequirement), d.shot))
                Error(r, "CONTACT_REQUIREMENT", owner, field, "Неизвестный режим требования.");
            Range(d.shotShare, owner, field + "/shotShare", r); Range(d.sourceExposure, owner, field + "/sourceExposure", r);
        }

        private static void Range(MapGrowthMetricRange value, string owner, string field, MapGrowthInputValidation r)
        {
            if (value == null) Error(r, "METRIC_RANGE", owner, field, "Нет диапазона метрики.");
            else if (value.enabled && (!Finite(value.min) || !Finite(value.max) || value.min < 0 || value.max > 1 || value.min > value.max))
                Error(r, "METRIC_RANGE", owner, field, "Нужен конечный диапазон min≤max внутри [0,1].");
        }

        private static void StateReference(MapGrowthInputValidation r, Dictionary<string, HashSet<string>> states, string owner, string field, string position, string state)
        {
            if (string.IsNullOrWhiteSpace(position) || !states.TryGetValue(position, out var ids)) Error(r, "UNKNOWN_POSITION", owner, field, "Неизвестная позиция «" + position + "».");
            else if (string.IsNullOrWhiteSpace(state) || !ids.Contains(state)) Error(r, "UNKNOWN_STATE", owner, field, "Неизвестная поза «" + position + "/" + state + "».");
        }
        private static bool NewId<T>(Dictionary<string, T> values, string id, T value)
        { if (string.IsNullOrWhiteSpace(id) || values.ContainsKey(id)) return false; values.Add(id, value); return true; }
        private static bool Positive(float v) => Finite(v) && v > 0;
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static void Error(MapGrowthInputValidation r, string code, string owner, string field, string message) => Add(r.errors, code, owner, field, message);
        private static void Add(List<MapGrowthInputIssue> list, string code, string owner, string field, string message) => list.Add(new MapGrowthInputIssue {code = code, ownerId = owner, field = field, message = message});
    }
}
