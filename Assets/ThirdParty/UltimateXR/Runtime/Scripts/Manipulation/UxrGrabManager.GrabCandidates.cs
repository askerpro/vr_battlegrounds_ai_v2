// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 67: кандидат хвата по руке — результат, который UpdateAffordances уже вычисляет каждый кадр.
// --------------------------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace UltimateXR.Manipulation
{
    public partial class UxrGrabManager
    {
        /// <summary>
        ///     VR Battlegrounds patch 67: у руки сменился кандидат хвата — предмет, который она возьмёт, если сейчас нажать grip
        ///     (свободный, лежащий в якоре или прокси якоря). Аргументы: рука, предмет (null — кандидата больше нет), точка хвата.
        ///     Только для локальных рук (тот же отбор, что у подсказок хвата, патч 18). Новых расчётов нет: сохраняется результат
        ///     первого прохода <c>UpdateAffordances</c> (<see cref="GetClosestGrabbableObject(UxrGrabber, out UxrGrabbableObject, out int, IEnumerable{UxrGrabbableObject})" />).
        ///     Проход «рука рядом с предметом в якоре» (патч 26) не включается.
        /// </summary>
        public event Action<UxrGrabber, UxrGrabbableObject, int> GrabCandidateChanged;

        private readonly Dictionary<UxrGrabber, (UxrGrabbableObject item, int point)> _grabCandidates     = new Dictionary<UxrGrabber, (UxrGrabbableObject, int)>();
        private readonly Dictionary<UxrGrabber, (UxrGrabbableObject item, int point)> _grabCandidatesNext = new Dictionary<UxrGrabber, (UxrGrabbableObject, int)>();
        private readonly List<UxrGrabber>                                                _grabCandidatesGone = new List<UxrGrabber>();

        /// <summary>
        ///     VR Battlegrounds patch 67: текущий кандидат хвата руки. false — рука держит предмет, не локальная или рядом нечего
        ///     взять.
        /// </summary>
        public bool TryGetGrabCandidate(UxrGrabber grabber, out UxrGrabbableObject item, out int grabPoint)
        {
            if (grabber != null && _grabCandidates.TryGetValue(grabber, out (UxrGrabbableObject item, int point) candidate) && candidate.item != null)
            {
                item      = candidate.item;
                grabPoint = candidate.point;
                return true;
            }

            item      = null;
            grabPoint = -1;
            return false;
        }

        private void BeginGrabCandidatesFrame() => _grabCandidatesNext.Clear();

        private void SetGrabCandidate(UxrGrabber grabber, UxrGrabbableObject item, int grabPoint) => _grabCandidatesNext[grabber] = (item, grabPoint);

        /// <summary>Сравнить кандидатов кадра с прежними и поднять события смены; руки без кандидата в этом кадре — «нет кандидата».</summary>
        private void EndGrabCandidatesFrame()
        {
            _grabCandidatesGone.Clear();

            foreach (KeyValuePair<UxrGrabber, (UxrGrabbableObject item, int point)> previous in _grabCandidates)
            {
                if (!_grabCandidatesNext.ContainsKey(previous.Key))
                {
                    _grabCandidatesGone.Add(previous.Key);
                }
            }

            foreach (UxrGrabber grabber in _grabCandidatesGone)
            {
                _grabCandidates.Remove(grabber);
                GrabCandidateChanged?.Invoke(grabber, null, -1);
            }

            foreach (KeyValuePair<UxrGrabber, (UxrGrabbableObject item, int point)> next in _grabCandidatesNext)
            {
                if (_grabCandidates.TryGetValue(next.Key, out (UxrGrabbableObject item, int point) old) && old.item == next.Value.item && old.point == next.Value.point)
                {
                    continue;
                }

                _grabCandidates[next.Key] = next.Value;
                GrabCandidateChanged?.Invoke(next.Key, next.Value.item, next.Value.point);
            }
        }
    }
}
