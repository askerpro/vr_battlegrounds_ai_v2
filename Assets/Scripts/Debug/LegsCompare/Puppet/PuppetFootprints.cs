using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Следы стоп на полу для человека (стенд <see cref="AvatarPuppetStand"/>, гизмо Scene view): точки опоры — зелёные,
    /// проскальзывание в опоре — красная линия. Хранит последние <see cref="Seconds"/> секунд; источник — <see cref="FootContactProbe"/>.
    /// </summary>
    public sealed class PuppetFootprints
    {
        public float Seconds = 10f;

        private readonly List<Trail> _trails = new List<Trail>();

        private sealed class Trail
        {
            public FootContactProbe Probe;
            public readonly List<(float t, Vector3 p, bool slip, bool start)> Points = new List<(float, Vector3, bool, bool)>();
            public bool WasContact;
        }

        public void Add(FootContactProbe probe) => _trails.Add(new Trail { Probe = probe });

        /// <summary>Кадр — после замера стоп.</summary>
        public void Record(float time)
        {
            foreach (Trail tr in _trails)
            {
                if (tr.Probe.InContact) tr.Points.Add((time, tr.Probe.Point, tr.Probe.Slipping, !tr.WasContact));
                tr.WasContact = tr.Probe.InContact;
                int old = 0;
                while (old < tr.Points.Count && tr.Points[old].t < time - Seconds) old++;
                if (old > 0) tr.Points.RemoveRange(0, old);
            }
        }

        public void Draw()
        {
            foreach (Trail tr in _trails)
            {
                for (int i = 0; i < tr.Points.Count; i++)
                {
                    var pt = tr.Points[i];
                    if (pt.start)
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawSphere(pt.p + Vector3.up * 0.005f, 0.025f);
                    }
                    if (i == 0 || pt.start || !pt.slip) continue;
                    Gizmos.color = Color.red;
                    Gizmos.DrawLine(tr.Points[i - 1].p + Vector3.up * 0.01f, pt.p + Vector3.up * 0.01f);
                }
            }
        }
    }
}
