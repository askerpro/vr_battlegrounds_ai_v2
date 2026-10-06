using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>Отладочные данные строк стенда перемотки — для окна «Отладка аватара» (выделенный аватар строки).</summary>
    public partial class ClipScrubStand
    {
        /// <summary>Нога живого аватара: подошва и колено.</summary>
        public struct LegDebug
        {
            public bool Valid;
            public float SoleCm;          // самая низкая точка подошвы над полом, см
            public float Knee;            // колено над полом, м
            public float KneeFromClipCm;  // колено от колена клипа, см (NaN — нет)
        }

        /// <summary>Клип, который сейчас даёт позу строки: роль (поза цепочки или кандидат), кадр, вес.</summary>
        public struct ActiveClip
        {
            public string Role, ClipName;
            public float Frame, Weight;
            public float HeadHeight;   // высота головы позы цепочки, м (у кандидата — NaN)
            public bool IsCandidate;
        }

        /// <summary>Строка сейчас: поза, цепочка, отличие; в Play — живой аватар.</summary>
        public struct RowDebug
        {
            public bool Valid;
            public string Label, ClipName, Source;
            public ActiveClip[] Playing;      // все клипы строки (позы цепочки и кандидат) с весами, включая 0
            public bool CandidatePlaying;     // кандидат ведёт позу (голова ниже цепочки или кадр без следования за камерой)
            public float CandidateFromCamera; // камера ниже этого — играет кандидат, м (NaN — нет цепочки)
            public float Frame, LastFrame, EntryFrame, RangeFrom, RangeTo;
            public float Camera, ClipHeadTarget, HeadNow, ClipHips;
            public string[] ChainLabels;
            public float[] ChainHeads;
            public bool HasDelta;
            public PoseDelta Delta;

            public bool HasLive;
            public float BodyIKHips, PelvisAngle, FloorOffset; // PelvisAngle — NaN, если кости таза ног нет
            public string PelvisName;
            public LegDebug Left, Right;
        }

        /// <summary>Строка, которой принадлежит объект (живой аватар, позовый манекен или их ребёнок); −1 — не строка стенда.</summary>
        public int RowOf(GameObject go)
        {
            if (go == null) return -1;
            foreach (LiveRow row in _live)
            {
                if (row.Root != null && go.transform.IsChildOf(row.Root)) return row.Index;
            }

            for (int i = 0; i < _puppets.Count; i++)
            {
                if (_puppets[i].Root != null && go.transform.IsChildOf(_puppets[i].Root.transform)) return i;
            }

            return -1;
        }

        /// <summary>
        /// Кадры кандидата-перехода строки для переноса в игру статичными позами: от кадра входа (самая высокая голова
        /// отрезка) к кадру с самой низкой головой, <paramref name="count"/> поз равномерно по высоте головы (вход не берётся —
        /// это стык с последней позой цепочки). Кандидат-поза — один кадр позы.
        /// </summary>
        public float[] CandidateFramesForGame(int row, int count)
        {
            ClipScrubList.Entry e = EntryOf(row);
            if (e.poseBlend) return new[] { e.frame };
            Puppet p = At(row);
            if (p == null || p.HeadHeights == null || p.HeadHeights.Length == 0) return new[] { e.frame };
            float[] h = p.HeadHeights;
            (int from, int to) = Range(e, h.Length - 1);
            int top = from, bottom = from;
            for (int f = from; f <= to; f++)
            {
                if (h[f] > h[top]) top = f;
                if (h[f] < h[bottom]) bottom = f;
            }

            var frames = new List<float>();
            int step = bottom >= top ? 1 : -1;
            for (int k = 1; k <= count; k++)
            {
                float target = Mathf.Lerp(h[top], h[bottom], k / (float)count);
                for (int f = top; f != bottom + step; f += step)
                {
                    if (h[f] > target + 1e-4f) continue;
                    if (!frames.Contains(f)) frames.Add(f);
                    break;
                }
            }

            return frames.ToArray();
        }

        /// <summary>Живой аватар строки (Play) или null.</summary>
        public GameObject LiveAvatarOf(int index)
        {
            LiveRow row = LiveRowOf(index);
            return row != null && row.Root != null ? row.Root.gameObject : null;
        }

        private LiveRow LiveRowOf(int index)
        {
            foreach (LiveRow row in _live)
            {
                if (row.Index == index) return row;
            }

            return null;
        }

        /// <summary>Отладочные данные строки сейчас.</summary>
        public RowDebug GetRowDebug(int index)
        {
            var r = new RowDebug();
            if (list == null || index < 0 || index >= RowCount) return r;
            ClipScrubList.Entry e = EntryOf(index);
            r.Valid = true;
            r.Label = RowLabel(index);
            r.ClipName = e.clip != null ? e.clip.name : "—";
            r.Frame = CurrentFrame(index);
            r.LastFrame = LastFrame(e.clip);
            r.EntryFrame = EntryFrame(index);
            r.RangeFrom = e.rangeFrom;
            r.RangeTo = e.rangeTo;
            r.Source = PoseSource(index);
            r.Camera = head != null ? transform.InverseTransformPoint(head.position).y : float.NaN;
            r.ClipHeadTarget = r.Camera - EyeAboveHead;
            r.HeadNow = HeadHeightNow(index);

            Puppet p = At(index);
            if (p != null)
            {
                r.ClipHips = p.HipsHeight;
                if (p.ChainHeads != null)
                {
                    r.ChainHeads = p.ChainHeads;
                    r.ChainLabels = new string[p.ChainHeads.Length];
                    for (int k = 0; k < r.ChainLabels.Length; k++) r.ChainLabels[k] = ChainLabel(index, k);
                }
            }

            r.HasDelta = TryGetDelta(index, out r.Delta);
            FillPlaying(ref r, index, p);
            FillLive(ref r, index);
            return r;
        }

        private void FillPlaying(ref RowDebug r, int index, Puppet p)
        {
            ClipScrubList.Entry e = EntryOf(index);
            List<ClipScrubList.KeyPose> chain = ChainOf(index);
            r.CandidateFromCamera = p != null && p.ChainHeads != null && p.ChainHeads.Length > 0
                ? p.ChainHeads[p.ChainHeads.Length - 1] + EyeAboveHead
                : float.NaN;
            bool inChain = p != null && followHead && head != null && p.HeadFrame < 0f && p.ChainHeads != null && p.ChainHeads.Length > 0;
            r.CandidatePlaying = !inChain;

            var all = new System.Collections.Generic.List<ActiveClip>();
            bool byHead = p != null && followHead && head != null && p.ChainHeads != null && p.ChainHeads.Length > 0;
            float candidateWeight = !byHead ? 1f : inChain ? 0f : p.CandidateWeight;
            for (int k = 0; k < chain.Count; k++)
            {
                float weight = inChain ? (k == p.ChainPose ? 1f - p.ChainBlend : k == p.ChainPose + 1 ? p.ChainBlend : 0f)
                             : byHead && k == chain.Count - 1 ? 1f - candidateWeight : 0f;
                ClipScrubList.KeyPose pose = chain[k];
                all.Add(new ActiveClip
                {
                    Role = ChainLabel(index, k),
                    ClipName = pose.clip != null ? pose.clip.name : "—",
                    Frame = pose.frame,
                    Weight = weight,
                    HeadHeight = p != null && p.ChainHeads != null && k < p.ChainHeads.Length ? p.ChainHeads[k] : float.NaN,
                });
            }

            all.Add(new ActiveClip
            {
                Role = e.poseBlend ? "кандидат (поза)" : "кандидат (переход)",
                ClipName = r.ClipName,
                Frame = CurrentFrame(index),
                Weight = candidateWeight,
                HeadHeight = float.NaN,
                IsCandidate = true,
            });
            r.Playing = all.ToArray();
        }

        private void FillLive(ref RowDebug r, int index)
        {
            LiveRow row = LiveRowOf(index);
            if (row == null || row.Avatar == null) return;
            r.HasLive = true;
            float floorY = transform.position.y;
            Transform hips = row.Avatar.AvatarRig.Hips;
            r.BodyIKHips = hips != null ? row.Avatar.transform.InverseTransformPoint(hips.position).y : float.NaN;
            r.PelvisAngle = float.NaN;
            (Vector3 framePos, Quaternion frameRot) = ClipFrame(row);
            if (row.Pelvis != null && row.PosePelvis != null)
            {
                r.PelvisName = row.Pelvis.name;
                r.PelvisAngle = Quaternion.Angle(row.Pelvis.rotation, frameRot * row.PosePelvis.rotation);
            }

            r.FloorOffset = floor != null ? floor.position.y - floorY : 0f;
            if (row.Legs.Count > 0) r.Left = Leg(row.Legs[0], framePos, frameRot, floorY);
            if (row.Legs.Count > 1) r.Right = Leg(row.Legs[1], framePos, frameRot, floorY);
        }

        private static LegDebug Leg(LiveLeg leg, Vector3 framePos, Quaternion frameRot, float floorY)
        {
            if (leg.Solver == null) return default;
            return new LegDebug
            {
                Valid = true,
                SoleCm = SoleHeight(leg, leg.Solver.Foot, leg.Solver.Toes, floorY) * 100f,
                Knee = leg.Solver.Calf.position.y - floorY,
                KneeFromClipCm = leg.PoseCalf != null ? Vector3.Distance(leg.Solver.Calf.position, framePos + frameRot * leg.PoseCalf.position) * 100f : float.NaN,
            };
        }
    }
}
