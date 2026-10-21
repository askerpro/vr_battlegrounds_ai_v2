using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>Какой комплект бот-компонентов получает тело (<see cref="BotKit"/>).</summary>
    public enum BotKitKind
    {
        /// <summary>
        /// Живое тело бота: ноги/курс/взгляд (<see cref="BotBody"/>, <see cref="BotNavigator"/>), выбор оружия и решение
        /// «жать курок» (<see cref="BotGunner"/>), Blaze в бою (<see cref="BotCombatDriver"/>) и ввод устройства
        /// (<see cref="BotInputKit"/>).
        /// </summary>
        Live,

        /// <summary>
        /// Пассивный комплект: тот же мозг ног и ввод, без Blaze — призрак выбывшего бота: идёт к своей зоне, не воюет.
        /// </summary>
        Passive
    }

    /// <summary>
    /// Ссылки на бот-компоненты одного тела — снимаются один раз при сборке комплекта (<see cref="BotKit"/>), чтобы тик
    /// директора не искал компоненты. Неизменяемы: повышение комплекта даёт новые ссылки (<see cref="BotKit.Upgrade"/>).
    /// Хранит их только <see cref="BotMind.Kit"/> (пишет директор).
    /// </summary>
    public sealed class BotKitRefs
    {
        public PlayerController Avatar { get; }
        public BotBody Body { get; }
        public BotNavigator Legs { get; }
        public BotGunner Gunner { get; }
        /// <summary>Blaze в бою; null — пассивный комплект.</summary>
        public BotCombatDriver Combat { get; }
        /// <summary>Устройство ввода тела (bot-input): единственный писатель позы — ввод, ниже него код человека.</summary>
        public BotInputKit Input { get; }

        /// <summary>Комплект пассивный (<see cref="BotKitKind.Passive"/>).</summary>
        public bool Passive { get; }

        public BotKitKind Kind => Passive ? BotKitKind.Passive : BotKitKind.Live;

        internal BotKitRefs(PlayerController avatar, BotBody body, BotNavigator legs, BotGunner gunner, BotCombatDriver combat,
                            BotInputKit input, bool passive)
        {
            Avatar = avatar;
            Body = body;
            Legs = legs;
            Gunner = gunner;
            Combat = combat;
            Input = input;
            Passive = passive;
        }
    }

    /// <summary>
    /// Единственная точка композиции бот-компонентов тела (bot-input). Бот — обычный аватар игрока без владельца, которым
    /// управляет ввод устройства: мозг (директор, <see cref="BotNavigator"/>, Blaze, <see cref="BotGunner"/>) решает
    /// намерения, <see cref="BlazeInputSource"/> превращает их в кадр шлема и контроллеров, дальше — код человека
    /// (хват по Grip, курок, IK, ноги от головы). Корень не пишет никто. Только сервер/хост, только аватар без владельца.
    ///
    /// <para>
    /// Комплект собирается <b>один раз на тело</b> — директором по <c>AvatarManager.AvatarSpawned</c> (повторное событие
    /// для того же тела — без изменений). Порядок: <see cref="BotBody"/>, <see cref="BotNavigator"/>, <see cref="BotGunner"/>,
    /// [живой: <see cref="BotCombatDriver"/>], <see cref="BotInputKit"/> последним — источник при привязке берёт мозг тела.
    /// Пассивный → живой (призрак ожил на месте) — <see cref="Upgrade"/>.
    /// </para>
    /// </summary>
    public static class BotKit
    {
        /// <summary>
        /// Собрать комплект на теле с источником ввода <paramref name="source"/> (общий для всех тел бота);
        /// <paramref name="keepHead"/> — голова продолжает с прежней позы (новое тело на той же карте). null — тела нет.
        /// </summary>
        public static BotKitRefs Attach(PlayerController avatar, BotKitKind kind, BlazeInputSource source, bool keepHead)
        {
            if (avatar == null || source == null) return null;
            GameObject go = avatar.gameObject;
            bool passive = kind == BotKitKind.Passive;
            bool reused = false;

            BotBody body = GetOrAdd<BotBody>(go, ref reused);
            BotNavigator legs = GetOrAdd<BotNavigator>(go, ref reused);
            BotGunner gunner = GetOrAdd<BotGunner>(go, ref reused);
            BotCombatDriver combat = passive ? null : GetOrAdd<BotCombatDriver>(go, ref reused);
            BotInputKit input = BotInputKit.Attach(avatar, source, keepHead);

            if (reused)
                GameLog.Player.Warning($"[BotKit] {avatar.name}: на теле уже были бот-компоненты — комплект собран повторно.", avatar);

            return new BotKitRefs(avatar, body, legs, gunner, combat, input, passive);
        }

        /// <summary>
        /// Пассивный комплект → живой на том же теле (призрак ожил на месте): добавляется Blaze (источник ввода найдёт
        /// его сам). Живой комплект возвращается как есть.
        /// </summary>
        public static BotKitRefs Upgrade(BotKitRefs kit)
        {
            if (kit == null || !kit.Passive || kit.Avatar == null) return kit;
            bool reused = false;
            BotCombatDriver combat = GetOrAdd<BotCombatDriver>(kit.Avatar.gameObject, ref reused);
            return new BotKitRefs(kit.Avatar, kit.Body, kit.Legs, kit.Gunner, combat, kit.Input, false);
        }

        private static T GetOrAdd<T>(GameObject go, ref bool reused) where T : Component
        {
            if (go.TryGetComponent(out T existing))
            {
                reused = true;
                return existing;
            }
            return go.AddComponent<T>();
        }
    }
}
