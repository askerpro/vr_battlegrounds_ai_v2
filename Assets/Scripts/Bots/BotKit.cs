using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>Какой комплект бот-компонентов получает тело (<see cref="BotKit"/>).</summary>
    public enum BotKitKind
    {
        /// <summary>
        /// Живое тело бота: ноги/курс/взгляд (<see cref="BotBody"/>, <see cref="BotNavigator"/>), выбор оружия и выстрел
        /// (<see cref="BotGunner"/>), Blaze в бою (<see cref="BotCombatDriver"/>), хват (<see cref="BotHands"/>), поза
        /// оружия (<see cref="BotWeaponAim"/>), писатель позы (<see cref="BotAvatarDriver"/>) и порядок кадра
        /// (<see cref="BotRuntimeController"/>).
        /// </summary>
        Live,

        /// <summary>
        /// Пассивный комплект: тот же писатель позы и порядок кадра, кисти в позе покоя — без хвата, прицела, Blaze и
        /// выстрелов. Призрак выбывшего бота и контрольная цель стенда.
        /// </summary>
        Passive
    }

    /// <summary>
    /// Ссылки на бот-компоненты одного тела — снимаются один раз при сборке комплекта (<see cref="BotKit"/>), чтобы тик
    /// директора не искал компоненты. Неизменяемы: повышение комплекта даёт новые ссылки (<see cref="BotKit.Upgrade"/>).
    /// Хранит их только <see cref="BotMind.Kit"/> (пишет директор) или сам вызывающий (стенд).
    /// </summary>
    public sealed class BotKitRefs
    {
        public PlayerController Avatar { get; }
        public BotBody Body { get; }
        public BotNavigator Legs { get; }
        public BotGunner Gunner { get; }
        /// <summary>Blaze в бою; null — пассивный комплект.</summary>
        public BotCombatDriver Combat { get; }
        public BotAvatarDriver Driver { get; }
        /// <summary>Хват через SDK; null — пассивный комплект.</summary>
        public BotHands Hands { get; }
        /// <summary>Поза оружия; null — пассивный комплект.</summary>
        public BotWeaponAim Aim { get; }
        public BotRuntimeController Runtime { get; }

        /// <summary>Комплект пассивный (<see cref="BotKitKind.Passive"/>).</summary>
        public bool Passive { get; }

        /// <summary>
        /// Тело на вводе устройства (bot-input): вместо писателей прежнего пути (<see cref="Driver"/>, <see cref="Hands"/>,
        /// <see cref="Aim"/>, <see cref="Runtime"/> — null) — <see cref="BotInputKit"/> с источником Blaze.
        /// </summary>
        public BotInputKit Input { get; }

        public BotKitKind Kind => Passive ? BotKitKind.Passive : BotKitKind.Live;

        internal BotKitRefs(PlayerController avatar, BotBody body, BotNavigator legs, BotGunner gunner, BotCombatDriver combat,
                            BotAvatarDriver driver, BotHands hands, BotWeaponAim aim, BotRuntimeController runtime, bool passive,
                            BotInputKit input = null)
        {
            Input = input;
            Avatar = avatar;
            Body = body;
            Legs = legs;
            Gunner = gunner;
            Combat = combat;
            Driver = driver;
            Hands = hands;
            Aim = aim;
            Runtime = runtime;
            Passive = passive;
        }
    }

    /// <summary>
    /// Единственная точка композиции бот-компонентов тела (clip-cutover cc-1). Бот — обычный аватар игрока без владельца
    /// (player-flow): корень, камеру и кисти пишет один <see cref="BotAvatarDriver"/>, порядок кадра —
    /// <see cref="BotRuntimeController"/>. Только сервер/хост, только аватар без владельца.
    ///
    /// <para>
    /// Комплект собирается <b>один раз на тело</b>: директор — по <c>AvatarManager.AvatarSpawned</c> (повторное событие
    /// для того же тела — без изменений), стенд — для своей контрольной цели. Порядок добавления: <see cref="BotBody"/>,
    /// <see cref="BotNavigator"/>, <see cref="BotGunner"/>, <see cref="BotAvatarDriver"/>, [живой:
    /// <see cref="BotCombatDriver"/>, <see cref="BotHands"/>, <see cref="BotWeaponAim"/>], <see cref="BotRuntimeController"/>
    /// последним — он читает остальных. Пассивный → живой (призрак ожил на месте) — <see cref="Upgrade"/>.
    /// </para>
    /// </summary>
    public static class BotKit
    {
        /// <summary>
        /// Собрать комплект на теле. Повторный вызов для тела с комплектом не добавляет второй экземпляр (компоненты —
        /// <c>DisallowMultipleComponent</c>): переиспользует найденные и предупреждает — это ошибка вызывающего.
        /// null — тела нет.
        /// </summary>
        public static BotKitRefs Attach(PlayerController avatar, BotKitKind kind)
        {
            if (avatar == null) return null;
            GameObject go = avatar.gameObject;
            bool passive = kind == BotKitKind.Passive;
            bool reused = false;

            BotBody body = GetOrAdd<BotBody>(go, ref reused);
            BotNavigator legs = GetOrAdd<BotNavigator>(go, ref reused);
            BotGunner gunner = GetOrAdd<BotGunner>(go, ref reused);
            BotAvatarDriver driver = GetOrAdd<BotAvatarDriver>(go, ref reused);
            BotCombatDriver combat = null;
            BotHands hands = null;
            BotWeaponAim aim = null;
            if (!passive)
            {
                combat = GetOrAdd<BotCombatDriver>(go, ref reused);
                hands = GetOrAdd<BotHands>(go, ref reused);
                aim = GetOrAdd<BotWeaponAim>(go, ref reused);
            }
            BotRuntimeController runtime = GetOrAdd<BotRuntimeController>(go, ref reused);
            runtime.SetKit(passive);

            if (reused)
                GameLog.Player.Warning($"[BotKit] {avatar.name}: на теле уже были бот-компоненты — комплект собран повторно.", avatar);

            return new BotKitRefs(avatar, body, legs, gunner, combat, driver, hands, aim, runtime, passive);
        }

        /// <summary>
        /// Пассивный комплект → живой на том же теле (призрак ожил на месте): добавляются Blaze, хват и поза оружия,
        /// порядок кадра переключается на полный. Живой комплект возвращается как есть.
        /// </summary>
        public static BotKitRefs Upgrade(BotKitRefs kit)
        {
            if (kit == null || !kit.Passive || kit.Avatar == null) return kit;
            GameObject go = kit.Avatar.gameObject;
            bool reused = false;
            if (kit.Input != null)
            {
                // Тело на вводе: у живого добавляется только Blaze; хват и прицел — ввод (источник берёт их из тела).
                BotCombatDriver inputCombat = GetOrAdd<BotCombatDriver>(go, ref reused);
                return new BotKitRefs(kit.Avatar, kit.Body, kit.Legs, kit.Gunner, inputCombat, null, null, null, null, false, kit.Input);
            }
            BotCombatDriver combat = GetOrAdd<BotCombatDriver>(go, ref reused);
            BotHands hands = GetOrAdd<BotHands>(go, ref reused);
            BotWeaponAim aim = GetOrAdd<BotWeaponAim>(go, ref reused);
            kit.Runtime.SetKit(false);
            return new BotKitRefs(kit.Avatar, kit.Body, kit.Legs, kit.Gunner, combat, kit.Driver, hands, aim, kit.Runtime, false);
        }

        /// <summary>
        /// Комплект тела на вводе устройства (bot-input): мозг прежний — <see cref="BotBody"/>, <see cref="BotNavigator"/>,
        /// <see cref="BotGunner"/>, [живой: <see cref="BotCombatDriver"/>], а вместо писателей прежнего пути — один
        /// <see cref="BotInputKit"/> с источником <paramref name="source"/> (общий для всех тел бота). null — тела нет.
        /// </summary>
        public static BotKitRefs AttachInput(PlayerController avatar, BotKitKind kind, BlazeInputSource source, bool keepHead)
        {
            if (avatar == null || source == null) return null;
            GameObject go = avatar.gameObject;
            bool passive = kind == BotKitKind.Passive;
            bool reused = false;

            BotBody body = GetOrAdd<BotBody>(go, ref reused);
            BotNavigator legs = GetOrAdd<BotNavigator>(go, ref reused);
            BotGunner gunner = GetOrAdd<BotGunner>(go, ref reused);
            BotCombatDriver combat = passive ? null : GetOrAdd<BotCombatDriver>(go, ref reused);
            // Устройство — последним: источник при привязке берёт мозг тела (ноги, стрелок, Blaze).
            BotInputKit input = BotInputKit.Attach(avatar, source, keepHead);

            if (reused)
                GameLog.Player.Warning($"[BotKit] {avatar.name}: на теле уже были бот-компоненты — комплект собран повторно.", avatar);

            return new BotKitRefs(avatar, body, legs, gunner, combat, null, null, null, null, passive, input);
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
