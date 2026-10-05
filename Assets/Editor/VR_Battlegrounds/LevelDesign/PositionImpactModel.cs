using System;
using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    [Serializable] public sealed class ImpactBodySample { public Vector3 offset; public float weight = 1; }
    [Serializable] public sealed class ImpactState
    {
        public string id; public Vector2 center; public float yaw;
        public Vector3 eyeOffset; public Vector3 muzzleOffset; public ImpactBodySample[] body;
    }
    [Serializable] public sealed class ImpactPosition
    {
        public string id; public Vector2 min; public Vector2 max; public ImpactState[] states;
        // Необязательная явная закрытая поза для сравнения цены выглядывания.
        public string protectedState;
    }
    [Serializable] public sealed class ImpactRouteSpec
    {
        public string id; public string from; public string to;
        public string fromState; public string toState; public Vector2[] via;
        // null — прежний путь по всей сетке; пустой массив — явно пустой допустимый коридор.
        public int[] allowedCellIndices; public bool requireDirect;
    }
    [Serializable] public sealed class PositionImpactLayout
    {
        // Паспорт явно выбирает Open/Closed; отсутствие значения не подтверждает тип карты.
        public MapEvaluationProfile profile;
        public int version = 1; public string map; public float radius = 0.5f; public float speed = 1;
        public string sceneGuid, bodyProfileId;
        public int bodyProfileVersion; public bool bodyProfileCalibrated;
        public ImpactPosition[] positions; public ImpactRouteSpec[] routes = Array.Empty<ImpactRouteSpec>();
    }
    [Serializable] public sealed class ImpactPairState
    {
        public string from; public string to; public string fromState; public string toState;
        public float distance; public float visibleShare; public float shotShare;
        public float visibleShotShare; public float hiddenShotShare;
        public float sourceExposure; public float targetProtection;
        public bool hasProtectedBaseline; public float sourceBaselineExposure = -1; public float openingExposureDelta;
    }
    [Serializable] public sealed class ImpactRoute
    {
        public string id; public string from; public string to;
        public bool reachable; public string problem;
        public Vector2[] points = Array.Empty<Vector2>();
        public float length; public float duration; public float minimumClearance;
    }
    [Serializable] public sealed class ImpactRouteInterval
    {
        public Vector2 from; public Vector2 to; public float startDistance; public float length;
        public float visibleShare; public float shotShare; public float visibleShotShare; public float hiddenShotShare;
    }
    [Serializable] public sealed class ImpactRouteInfluence
    {
        public string position; public string state; public string route; public string targetTemplate;
        public float visibleLength; public float shotLength; public float hiddenShotLength;
        public float shotDuration; public float longestShotLength;
        public float firstVisibleDistance = -1; public float firstShotDistance = -1;
        public ImpactRouteInterval[] intervals;
    }

    public static class PositionImpactValidation
    {
        /// <summary>Проверка явной разметки; диск на полу не доказывает допустимость полного 3D тела.</summary>
        public static List<string> Validate(MapGrid grid, PositionImpactLayout layout, float[] clearance = null)
        {
            var errors = new List<string>();
            if (grid == null || layout == null) { errors.Add("Нет сетки или разметки."); return errors; }
            if (layout.version != 1) errors.Add("Неизвестная версия разметки.");
            if (!Finite(layout.radius) || layout.radius <= 0 || !Finite(layout.speed) || layout.speed <= 0)
                errors.Add("Радиус и скорость должны быть конечными положительными числами.");
            if (layout.positions == null || layout.positions.Length == 0 || layout.positions.Length > 32)
            { errors.Add("Требуется от 1 до 32 позиционных областей."); return errors; }
            float[] clear = clearance ?? MapAnalyzer.Clearance(grid);
            if (clear.Length != grid.Count) { errors.Add("Clearance принадлежит другой сетке."); return errors; }
            var positions = new Dictionary<string, ImpactPosition>(StringComparer.Ordinal);
            int stateCount = 0;
            foreach (ImpactPosition p in layout.positions)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.id) || positions.ContainsKey(p.id))
                { errors.Add("Пустой или повторяющийся ID позиции."); continue; }
                positions.Add(p.id, p);
                if (!Finite(p.min) || !Finite(p.max) || p.min.x > p.max.x || p.min.y > p.max.y)
                    errors.Add(p.id + ": неверные границы области.");
                if (p.states == null || p.states.Length == 0 || p.states.Length > 8)
                { errors.Add(p.id + ": требуется от 1 до 8 состояний."); continue; }
                stateCount += p.states.Length;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (ImpactState s in p.states)
                {
                    if (s == null || string.IsNullOrWhiteSpace(s.id) || !ids.Add(s.id))
                    { errors.Add(p.id + ": пустой или повторяющийся ID состояния."); continue; }
                    string key = p.id + "/" + s.id;
                    if (!Finite(s.center) || !Finite(s.yaw) || !Finite(s.eyeOffset) || !Finite(s.muzzleOffset))
                    { errors.Add(key + ": нечисловые координаты."); continue; }
                    if (s.center.x < p.min.x || s.center.x > p.max.x || s.center.y < p.min.y || s.center.y > p.max.y)
                        errors.Add(key + ": центр вне области позиции.");
                    int i = grid.IndexAt(s.center);
                    if (i < 0 || grid.Blocked[i] || clear[i] < layout.radius)
                        errors.Add(key + ": горизонтальный габарит не помещается в сетке.");
                    if (s.body == null || s.body.Length == 0 || s.body.Length > 16)
                    { errors.Add(key + ": требуется от 1 до 16 образцов тела."); continue; }
                    double total = 0;
                    foreach (ImpactBodySample b in s.body)
                    {
                        if (b == null || !Finite(b.offset) || b.offset.y < 0 || !Finite(b.weight) || b.weight <= 0)
                        { errors.Add(key + ": неверный образец тела/вес."); continue; }
                        total += b.weight;
                        if (new Vector2(b.offset.x, b.offset.z).magnitude > layout.radius)
                            errors.Add(key + ": образец тела выходит за проверяемый горизонтальный диск.");
                    }
                    if (total > float.MaxValue) errors.Add(key + ": сумма весов переполнена.");
                }
                if (!string.IsNullOrEmpty(p.protectedState) && !ids.Contains(p.protectedState))
                    errors.Add(p.id + ": неизвестная защищённая поза.");
            }
            if (stateCount > 64) errors.Add("Лимит MVP: 64 состояния на карту; сократите выборку.");
            if (layout.routes == null || layout.routes.Length > 32)
            { errors.Add("Маршруты должны быть массивом не более 32 элементов."); return errors; }
            var routeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ImpactRouteSpec r in layout.routes)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.id) || !routeIds.Add(r.id))
                { errors.Add("Пустой или повторяющийся ID маршрута."); continue; }
                if (!HasState(positions, r.from, r.fromState) || !HasState(positions, r.to, r.toState))
                    errors.Add(r.id + ": неизвестная позиция/состояние конца маршрута.");
                if (r.allowedCellIndices != null)
                {
                    var cells = new HashSet<int>();
                    foreach (int cell in r.allowedCellIndices)
                        if (cell < 0 || cell >= grid.Count || !cells.Add(cell))
                            errors.Add(r.id + ": индекс коридора вне сетки или повторяется.");
                }
                if (r.via != null)
                {
                    if (r.via.Length > 16) errors.Add(r.id + ": не более 16 промежуточных порталов.");
                    foreach (Vector2 v in r.via)
                        if (!Finite(v) || grid.IndexAt(v) < 0) errors.Add(r.id + ": портал вне сетки или нечисловой.");
                }
            }
            return errors;
        }

        private static bool HasState(Dictionary<string, ImpactPosition> positions, string position, string state)
        {
            if (position == null || state == null || !positions.TryGetValue(position, out ImpactPosition p) || p.states == null) return false;
            foreach (ImpactState s in p.states) if (s != null && s.id == state) return true;
            return false;
        }
        public static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        public static bool Finite(Vector2 v) => Finite(v.x) && Finite(v.y);
        public static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    }
}
