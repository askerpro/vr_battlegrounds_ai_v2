using System;
using System.Collections.Generic;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    public enum BotStandScenarioId { V01, V02, T01, T02, T03, T04, T05, T06, T07, T08, T09, T10, L01 }
    public enum BotStandProfile { Production, Capability }

    [Serializable]
    public sealed class BotStandRunOptions
    {
        public string OutputDirectory;
        public BotStandProfile Profile;
        public int[] Seeds = { 101 };
        public float CaseSeconds = 10f;
        public bool Capture = true;
        public bool Rifle;
        public bool HandFit;
        public string WeaponName;
        public int AvatarIndex = -1;
        /// <summary>
        /// Режим тела проверяемых ботов: false — гибрид dev (по умолчанию), true — вариант P «кукловод»
        /// (<c>BotBodyMode.PlayerFlow</c>, этап pf-prototype). Матч от этого не меняется.
        /// </summary>
        public bool PlayerFlow;
        /// <summary>
        /// Только стенд, режим P: точка прицеливания ИИ — ровно центр тела цели (без разброса ИИ). Компенсация отдачи в
        /// BotWeaponAim не меняется. По умолчанию выключено.
        /// </summary>
        public bool ZeroAiAimSpread;
        /// <summary>
        /// Только стенд: у оружия ботов стенда нет отдачи — корневую отдачу SDK запрещает политика
        /// <c>IUxrFirearmRootRecoilPolicy</c> на экземпляре, <c>RecoilAccumulator</c> экземпляра выключен. Префабы не
        /// меняются. По умолчанию выключено.
        /// </summary>
        public bool NoRecoil;
        /// <summary>
        /// Только стенд, мультипроцессный запуск (этап pf-network): сдвиг по X всей раскладки case — места бота и
        /// контрольной цели и её перемещений по сценарию, метры. Клиенты-игроки без команды режима (Respawn — выбор
        /// игрока, разминка без команд) появляются в начале координат (на сцене стенда нет нейтральной зоны и
        /// NetworkStartPosition) — на линии огня T07 (бот z=−5, цель z=+5). Укрытия не сдвигаются: для сценариев с
        /// укрытиями (T01, T03–T05, T08) смещение меняет сценарий. По умолчанию 0 — раскладка прежняя.
        /// </summary>
        public float LaneOffsetX;
        /// <summary>
        /// Только стенд, мультипроцессный запуск (net-round1 attempt-6, A/B): клиентские наблюдатели вешают
        /// <see cref="BotStandNoRecoilPolicy"/> на свою локальную копию оружия в руке бота — корневая отдача SDK при
        /// повторе выстрела у клиента не применяется. Сервер это поле не исполняет: значение доходит до клиентов через
        /// заявку наблюдателя (<see cref="BotStandNetObserverRequest.ClientNoRecoil"/>, пишет B3 до Play), здесь — запись
        /// в summary и сверка очереди B4 с заявкой. По умолчанию выключено.
        /// </summary>
        public bool ClientNoRecoil;

        // ── Снаряжение проверяемого бота (этап pf-equipment, только режим P; исполняет BotStandEquipmentScript) ──

        /// <summary>
        /// Оружие (WeaponName/Rifle, только пистолет) выдаётся в кобуру штатной выдачей стартового пистолета
        /// (<c>PlayerLoadoutManager.ServerGiveWeapon</c>, как <c>StartingSidearmPolicy</c>); бот берёт его сам (<c>Arm(null)</c>).
        /// </summary>
        public bool HolsterStart;
        /// <summary>Без оружия (Unarmed): кобура пуста, <c>Arm(null)</c> — бот ничего не берёт, Blaze не создаётся.</summary>
        public bool Unarmed;
        /// <summary>Имя WeaponInfo покупки из <c>BotCombatStand.Weapons</c>; пусто — без покупки.</summary>
        public string PurchaseWeaponName;
        /// <summary>
        /// Когда (секунды от начала case) оплатить покупку штатной экономикой (<c>BotShopper.TryPay</c>) и отдать её боту
        /// (<c>BotGunner.OfferPurchase</c>: пистолет в руке сначала убирается в кобуру). &lt; 0 — не покупать.
        /// </summary>
        public float PurchaseAtSeconds = -1f;
        /// <summary>Когда нанести проверяемому боту смертельный урон (<c>UxrActor.ReceiveDamage</c>, штатная смерть). &lt; 0 — нет.</summary>
        public float KillAtSeconds = -1f;
        /// <summary>
        /// Когда оживить погибшего (<c>PlayerController.Respawn</c>, как раунд Elimination; в Respawn-режиме призрак сам не
        /// оживает) — новое тело получает снаряжение так же, как в начале case. &lt; 0 — нет.
        /// </summary>
        public float RespawnAtSeconds = -1f;

        public BotStandScenarioId[] Cases;
    }

    [Serializable]
    public sealed class BotStandRunHandle
    {
        public string RunId, OutputDirectory;
    }

    [Serializable]
    public sealed class BotStandRunStatus
    {
        public string RunId, CurrentCase, ReportPath, Error;
        public bool IsFinished;
        public bool? Passed;
        public int Total, Completed, Failed, NeedsReview, InvalidFixture, Unsupported;
    }

    [Serializable]
    public sealed class BotStandFrame
    {
        public int Frame, BodyId, Team, TargetTeam, Shots;
        public float Time;
        public string Name, State, Stage, Weapon, Category;
        public float[] Feet;
        public float[] TargetFeet;
        public bool Alive, Combat, NavOnMesh, CompletePath, Author;
        public bool Visible, ProtectedHead, ProtectedChest, ProtectedPelvis, InsideSolid, LegalTarget;
        public float TargetDistance, NearestBotDistance;
    }

    [Serializable]
    public sealed class BotStandCaseResult
    {
        public string Id, Status, Error;
        public int Seed;
        public float Duration;
        public BotStandProfile Profile;
        public bool Capture;
        public List<string> Findings = new List<string>();
        public Dictionary<string, object> Metrics = new Dictionary<string, object>();
        public List<BotStandFrame> Frames = new List<BotStandFrame>();
        public List<string> Images = new List<string>();
    }
}
