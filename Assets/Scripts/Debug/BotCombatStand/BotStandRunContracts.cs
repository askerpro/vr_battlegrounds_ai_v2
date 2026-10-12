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
