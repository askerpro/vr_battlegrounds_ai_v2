using System.Collections.Generic;
using UltimateXR.Mechanics.Weapons;

namespace VrBattlegrounds.Player
{
    /// <summary>
    /// Кто ранил игрока с последнего возрождения — для зачёта убийства и ассистов.
    ///
    /// <para>
    /// <b>Откуда источник.</b> Попадание пули приходит в <c>UxrActor.ReceiveImpact</c> с актором
    /// стрелка (<c>UxrWeaponManager</c>: <c>ProjectileSource.TryGetWeaponOwner()</c>), и тот
    /// едет в <c>UxrDamageEventArgs.ActorSource</c>. Смертельный урон не поднимает
    /// <c>DamageReceived</c> (SDK сразу зовёт <c>DieInternal</c>), зато <c>DamageReceiving</c>
    /// приходит на любой урон — поэтому запись идёт оттуда, и последний записанный источник
    /// перед <c>Died</c> — источник смертельного урона. <c>ReceiveDamage(float)</c> (падение,
    /// отладка) источника не несёт — убийства за такую смерть нет.
    /// </para>
    ///
    /// <para>
    /// Убийца — сессия, а не аватар: аватар пересоздаётся при смене скина, статистика серии
    /// ведётся по сессии. Самоубийство (источник — сама жертва) убийства не даёт.
    /// </para>
    /// </summary>
    public sealed class DamageLedger
    {
        private UxrActor _lastSource;
        private readonly List<UxrActor> _damagers = new List<UxrActor>();

        /// <summary>Урон прошёл (не отменён правилом режима). Источник может быть null.</summary>
        public void Record(UxrActor source)
        {
            _lastSource = source;
            if (source != null && !_damagers.Contains(source)) _damagers.Add(source);
        }

        /// <summary>Игрок возродился или погиб — копить заново.</summary>
        public void Clear()
        {
            _lastSource = null;
            _damagers.Clear();
        }

        /// <summary>
        /// Убийца и ассистенты гибели <paramref name="victim"/>. Убийца null — урон без источника,
        /// от не-игрока или от самой жертвы. Ассистенты — прочие ранившие игроки, без жертвы и убийцы.
        /// </summary>
        public void Resolve(PlayerSession victim, out PlayerSession killer, out List<PlayerSession> assists)
        {
            killer = SessionOf(_lastSource);
            if (killer == victim) killer = null;

            assists = new List<PlayerSession>();
            foreach (UxrActor actor in _damagers)
            {
                PlayerSession helper = SessionOf(actor);
                if (helper == null || helper == victim || helper == killer || assists.Contains(helper)) continue;
                assists.Add(helper);
            }
        }

        private static PlayerSession SessionOf(UxrActor actor)
        {
            if (actor == null) return null;
            PlayerController player = actor.GetComponent<PlayerController>();
            return player != null ? player.Session : null;
        }
    }
}
