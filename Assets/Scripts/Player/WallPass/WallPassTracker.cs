using System;
using UnityEngine;

namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>Чистая машина состояний: не знает об аватаре, IK, сети или физической сцене.</summary>
    public sealed class WallPassTracker
    {
        private WallPassStatus _status;
        private double _suspectAt = double.NaN;
        private double _releaseAt = double.NaN;
        private double _lastDamageAt = double.NegativeInfinity;
        private double _lastNow = double.NegativeInfinity;
        private bool _killed;

        public bool HasSupport { get; private set; }
        public Vector3 Support { get; private set; }

        public void Reset()
        {
            HasSupport = false;
            Support = default;
            _status = default;
            _suspectAt = _releaseAt = double.NaN;
            _lastDamageAt = _lastNow = double.NegativeInfinity;
            _killed = false;
        }

        /// <summary>
        /// Пропавшая поза прерывает доказательство непрерывного касания/возврата.
        /// Сторона входа, действующий штраф, дедлайн смерти и кулдаун урона сохраняются.
        /// </summary>
        public void SuspendObservation()
        {
            _suspectAt = _releaseAt = double.NaN;
        }

        public WallPassDecision Evaluate(WallPassObservation observation, double now,
                                         bool punitive, WallPassSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (double.IsNaN(now) || double.IsInfinity(now))
                return new WallPassDecision { Status = _status };
            // Сетевое время должно быть монотонным; откат не продлевает ни один таймер.
            now = Math.Max(now, _lastNow);
            _lastNow = now;
            bool returned = observation.HasOriginalSupport && observation.HeadDepth <= WallPassRules.ReleaseDepth;
            bool invalid = observation.HeadDepth >= WallPassRules.HeadDepth || !observation.HasOriginalSupport;

            if (!HasSupport)
            {
                if (observation.HasAnySupport && observation.HeadDepth <= WallPassRules.ReleaseDepth)
                {
                    HasSupport = true;
                    Support = observation.Support;
                }
                else
                {
                    // Начальная поза внутри стены не доказывает проход. Ждём законной опоры.
                    _status = new WallPassStatus { Stage = WallPassStage.Hint, Punitive = punitive };
                    return new WallPassDecision { Status = _status };
                }
            }

            bool damage = false;
            bool kill = false;
            if (_status.Stage == WallPassStage.Violating)
            {
                if (_status.Punitive != punitive)
                    _status.DeathAt = punitive ? now + settings.SafeDeathDelay : 0;
                _status.Punitive = punitive;
                if (returned)
                {
                    if (double.IsNaN(_releaseAt)) _releaseAt = now;
                    if (now - _releaseAt >= WallPassRules.ReleaseSeconds)
                    {
                        Support = observation.Support;
                        _status = default;
                        _suspectAt = _releaseAt = double.NaN;
                        _killed = false;
                    }
                }
                else
                {
                    _releaseAt = double.NaN;
                    // Голова в стене ещё может вернуться: независимая опора сама по себе не смерть.
                    bool crossed = !observation.HasOriginalSupport && observation.HasAnySupport &&
                                   observation.CrossedBarrier && observation.HeadDepth <= 0.001f;
                    if (punitive && !_killed && (crossed || now >= _status.DeathAt))
                    {
                        _status.Cause = crossed ? WallPassCause.CrossedBarrier : WallPassCause.Timeout;
                        _killed = kill = true;
                    }
                }
            }
            else if (invalid)
            {
                // Замораживаем опору уже на подозрении, иначе кольцо перенесёт её через стену.
                if (double.IsNaN(_suspectAt)) _suspectAt = now;
                _status.Stage = WallPassStage.Hint;
                _status.Punitive = punitive;
                _status.ReturnPoint = Support;
                if (now - _suspectAt >= WallPassRules.ContactSeconds)
                {
                    _status = new WallPassStatus
                    {
                        Stage = WallPassStage.Violating,
                        EnteredAt = now,
                        DeathAt = punitive ? now + settings.SafeDeathDelay : 0,
                        ReturnPoint = Support,
                        Punitive = punitive,
                        Cause = observation.HeadDepth >= WallPassRules.HeadDepth
                            ? WallPassCause.HeadEmbedded : WallPassCause.NoSupport
                    };
                    if (punitive && settings.SafeContactDamage > 0 &&
                        now - _lastDamageAt >= settings.SafeDamageCooldown)
                    {
                        damage = true;
                        _lastDamageAt = now;
                    }
                }
            }
            else
            {
                _suspectAt = double.NaN;
                if (returned) Support = observation.Support;
                bool wasHint = _status.Stage == WallPassStage.Hint;
                bool hint = observation.HeadDepth > 0 ||
                            observation.HeadClearance < (wasHint ? WallPassRules.HintReleaseClearance : WallPassRules.HintClearance) ||
                            observation.LeanDistance > (wasHint ? WallPassRules.HintReleaseLean : WallPassRules.HintLean);
                _status = hint ? new WallPassStatus
                {
                    Stage = WallPassStage.Hint,
                    ReturnPoint = Support,
                    Punitive = punitive
                } : default;
            }
            return new WallPassDecision { Status = _status, DealContactDamage = damage, Kill = kill };
        }
    }
}
