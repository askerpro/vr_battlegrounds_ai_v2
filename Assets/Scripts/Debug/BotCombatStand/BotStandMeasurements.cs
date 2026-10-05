using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Bots;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>Приблизительные лучи на трёх фиксированных высотах; состояние Blaze не доказывает защиту видимого меша.</summary>
    public static class BotStandMeasurements
    {
        private static int Mask => LayerMask.GetMask("Default", "Ground");

        public static void ValidateFixture(BotStandScenarioId id, BotStandSession session)
        {
            if (session.Subject?.ActiveAvatar == null) throw new InvalidOperationException("Нет проверяемого тела.");
            if (session.Target?.ActiveAvatar == null) return;
            var body = session.Subject.ActiveAvatar.GetComponent<BotBody>();
            var target = session.Target.ActiveAvatar.GetComponent<BotBody>();
            bool complete = BotNavMesh.TryPath(body.Feet, target.Feet, new List<Vector3>());
            if (id == BotStandScenarioId.T10 ? complete : !complete)
                throw new InvalidOperationException(id == BotStandScenarioId.T10 ? "Разрыв сетки не создан: путь полный." : "Контрольная цель недостижима.");
            if (id != BotStandScenarioId.T08 && id != BotStandScenarioId.T10 && id != BotStandScenarioId.T09 &&
                Blocked(target.Feet + Vector3.up*1.55f, body.Feet + Vector3.up*1.55f, false))
                throw new InvalidOperationException("Начальный обзор контрольной цели перекрыт геометрией.");
        }

        public static void Sample(BotStandFrame frame, PlayerController player, PlayerController target)
        {
            var body = player.GetComponent<BotBody>(); var driver = player.GetComponent<BotCombatDriver>();
            var selected = driver.Target;
            frame.LegalTarget = selected == null || selected.Session != null && selected.IsAlive && selected.Session.Role == GameRole.Player &&
                selected.Session.TeamIndex != 0 && selected.Session.TeamIndex != player.Session.TeamIndex;
            var colliders = Physics.OverlapSphere(body.Feet + Vector3.up*.9f, .2f, Mask, QueryTriggerInteraction.Ignore);
            frame.InsideSolid = colliders.Any(c => IsEnvironment(c) && (c.ClosestPoint(body.Feet + Vector3.up*.9f) - (body.Feet + Vector3.up*.9f)).sqrMagnitude < .000001f);
            frame.NearestBotDistance = float.PositiveInfinity;
            foreach (var bot in BotDirector.Instance.Bots)
                if (bot.ActiveAvatar != null && bot.ActiveAvatar != player)
                    frame.NearestBotDistance = Mathf.Min(frame.NearestBotDistance, Vector3.Distance(body.Feet, bot.ActiveAvatar.GetComponent<BotBody>().Feet));
            if (float.IsPositiveInfinity(frame.NearestBotDistance)) frame.NearestBotDistance = -1;
            if (target == null) return;
            var other = target.GetComponent<BotBody>();
            frame.TargetFeet = new[] { other.Feet.x,other.Feet.y,other.Feet.z };
            var origin = other.Feet + Vector3.up*1.55f;
            frame.TargetDistance = Vector3.Distance(body.Feet, other.Feet);
            frame.Visible = !Blocked(origin, body.Feet + Vector3.up*1.55f, false);
            frame.ProtectedHead = Blocked(origin, body.Feet + Vector3.up*1.55f, true);
            frame.ProtectedChest = Blocked(origin, body.Feet + Vector3.up*1.15f, true);
            frame.ProtectedPelvis = Blocked(origin, body.Feet + Vector3.up*.6f, true);
        }

        private static bool IsEnvironment(Collider c) => c != null && c.attachedRigidbody == null && c.GetComponentInParent<UxrAvatar>() == null;
        private static bool Blocked(Vector3 from, Vector3 to, bool requireHard)
        {
            var hits = Physics.RaycastAll(from, (to-from).normalized, Vector3.Distance(from,to), Mask, QueryTriggerInteraction.Ignore)
                .Where(h => IsEnvironment(h.collider)).OrderBy(h=>h.distance);
            foreach (var hit in hits)
            {
                var cover = CoverSurface.Of(hit.collider);
                return !requireHard || cover == null || cover.Class == CoverClass.Hard;
            }
            return false;
        }

        public static void Evaluate(BotStandCaseResult result)
        {
            if (result.Status == "Unsupported" || result.Status == "InvalidFixture" || result.Frames.Count == 0) return;
            var frames = result.Frames;
            result.Metrics["frameCount"] = frames.Count;
            result.Metrics["shots"] = frames.GroupBy(f=>f.BodyId).Sum(g=>g.Max(f=>f.Shots));
            result.Metrics["states"] = frames.Select(f=>f.State).Distinct().ToArray();
            result.Metrics["approximateHeadAndChestProtectedFraction"] = frames.Count(f=>f.ProtectedHead && f.ProtectedChest)/(float)frames.Count;
            result.Metrics["visibleFraction"] = frames.Count(f=>f.Visible)/(float)frames.Count;
            result.Metrics["distanceRangeMeters"] = new[] { frames.Min(f=>f.TargetDistance),frames.Max(f=>f.TargetDistance) };
            result.Metrics["minimumBotDistanceMeters"] = frames.Where(f=>f.NearestBotDistance>=0).Select(f=>f.NearestBotDistance).DefaultIfEmpty(-1).Min();
            result.Metrics["environmentRayMask"] = "Approximate: Default/Ground static colliders, fixed heights 1.55/1.15/0.6 m. Not actual mesh protection or weapon collision mask.";
            float travel=0;
            foreach (var group in frames.GroupBy(f=>f.BodyId))
            {
                var ordered=group.ToArray();
                for(int i=1;i<ordered.Length;i++) travel += Vector3.Distance(Point(ordered[i-1].Feet), Point(ordered[i].Feet));
            }
            result.Metrics["travelMeters"] = travel;
            if (frames.Any(f=>!f.LegalTarget)) result.Findings.Add("Незаконная выбранная цель.");
            if (frames.Any(f=>f.InsideSolid)) result.Findings.Add("Центр корпуса оказался внутри статического коллайдера.");
            if (frames.Any(f=>f.Shots>0 && !f.Author)) result.Findings.Add("Оружие стрелявшего тела без author authority.");
            if (result.Id=="T09" && frames.Any(f=>f.Shots>0)) result.Findings.Add("Выстрел в сценарии без законного противника.");
            if (result.Findings.Count>0) result.Status="Failed";
        }
        private static Vector3 Point(float[] p) => new Vector3(p[0],p[1],p[2]);
    }
}
