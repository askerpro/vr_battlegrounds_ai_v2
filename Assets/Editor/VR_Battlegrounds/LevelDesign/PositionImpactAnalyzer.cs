using System.Collections.Generic;
using System;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    public static class PositionImpactAnalyzer
    {
        public static List<ImpactPairState> EvaluatePair(MapGrid grid, ImpactPosition from, ImpactPosition to)
        {
            var rows = new List<ImpactPairState>();
            ImpactState baseline = null;
            if (!string.IsNullOrEmpty(from.protectedState))
            {
                foreach (ImpactState s in from.states) if (s.id == from.protectedState) baseline = s;
                if (baseline == null) throw new ArgumentException("Неизвестная защищённая поза: " + from.protectedState);
            }
            foreach (ImpactState a in from.states)
            foreach (ImpactState b in to.states)
            {
                ImpactPairState row = Evaluate(grid, a, b);
                row.from = from.id; row.to = to.id;
                if (baseline != null)
                {
                    row.hasProtectedBaseline = true;
                    row.sourceBaselineExposure = Probe(grid, b, baseline).shotShare;
                    row.openingExposureDelta = row.sourceExposure - row.sourceBaselineExposure;
                }
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>Одна явная пара поз. Обратная открытость относится к тому же состоянию источника.</summary>
        public static ImpactPairState Evaluate(MapGrid grid, ImpactState from, ImpactState to)
        {
            var row = Probe(grid, from, to);
            row.fromState = from.id; row.toState = to.id;
            row.distance = (to.center - from.center).magnitude;
            row.sourceExposure = Probe(grid, to, from).shotShare;
            row.targetProtection = 1f - row.shotShare;
            return row;
        }

        private static ImpactPairState Probe(MapGrid grid, ImpactState from, ImpactState to)
        {
            if (grid == null || from == null || to == null || to.body == null || to.body.Length == 0)
                throw new ArgumentException("Нет сетки, состояния или образцов тела.");
            double weight = 0, visible = 0, shot = 0, visibleShot = 0, hiddenShot = 0;
            Vector3 eye = World(from, from.eyeOffset), muzzle = World(from, from.muzzleOffset);
            foreach (ImpactBodySample sample in to.body)
            {
                if (sample == null || !PositionImpactValidation.Finite(sample.weight) || sample.weight <= 0)
                    throw new ArgumentException("Вес образца должен быть положительным конечным числом.");
                Vector3 target = World(to, sample.offset);
                bool seen = grid.LineOfSight(eye, target), hit = grid.ShotLine(muzzle, target);
                // «Видимая траектория ствола» не доказывает отсутствие пробития: boolean API не сообщает материалы.
                bool trajectorySeen = hit && grid.LineOfSight(muzzle, target);
                weight += sample.weight;
                if (seen) visible += sample.weight;
                if (hit) shot += sample.weight;
                if (trajectorySeen) visibleShot += sample.weight;
                if (hit && !trajectorySeen) hiddenShot += sample.weight;
            }
            return new ImpactPairState
            {
                visibleShare = (float)(visible / weight), shotShare = (float)(shot / weight),
                visibleShotShare = (float)(visibleShot / weight), hiddenShotShare = (float)(hiddenShot / weight)
            };
        }

        /// <summary>Локальные образцы поворачиваются по yaw вокруг вертикали; Y отсчитывается над полом MapGrid.</summary>
        public static Vector3 World(ImpactState state, Vector3 offset)
        {
            double radians = state.yaw * Math.PI / 180.0;
            float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
            return new Vector3(state.center.x + offset.x * c + offset.z * s, offset.y,
                state.center.y - offset.x * s + offset.z * c);
        }
    }
}
