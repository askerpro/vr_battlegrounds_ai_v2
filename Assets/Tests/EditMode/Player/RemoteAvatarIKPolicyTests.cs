using NUnit.Framework;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// «Режим экономии» IK чужих аватаров (патч 24 UltimateXR): кто решается каждый кадр, а
    /// кто — раз в <see cref="RemoteAvatarIKPolicy.InvisibleSolveInterval"/>. Главное, что
    /// здесь стережётся: на машине с авторитетом (сервер, хост, без сети) и для своего
    /// аватара экономии нет никогда — по этим позам считается урон.
    /// </summary>
    public class RemoteAvatarIKPolicyTests
    {
        private const int N = RemoteAvatarIKPolicy.InvisibleSolveInterval;

        // ── Кто экономит никогда ────────────────────────────────────────────

        [Test]
        public void LocalAvatar_AlwaysSolves_EvenInvisibleOffPhase()
        {
            for (int frame = 0; frame < N * 3; frame++)
            {
                Assert.IsTrue(RemoteAvatarIKPolicy.ShouldSolve(
                    isRemote: false, isVisible: false, wasVisible: false, frame, stagger: 1,
                    isAuthorityMachine: false, N), $"кадр {frame}");
            }
        }

        [Test]
        public void AuthorityMachine_AlwaysSolves_InvisibleRemote()
        {
            for (int frame = 0; frame < N * 3; frame++)
            {
                Assert.IsTrue(RemoteAvatarIKPolicy.ShouldSolve(
                    isRemote: true, isVisible: false, wasVisible: false, frame, stagger: 2,
                    isAuthorityMachine: true, N), $"кадр {frame}");
            }
        }

        [Test]
        public void VisibleRemote_AlwaysSolves()
        {
            for (int frame = 0; frame < N * 3; frame++)
            {
                Assert.IsTrue(RemoteAvatarIKPolicy.ShouldSolve(
                    isRemote: true, isVisible: true, wasVisible: true, frame, stagger: 3,
                    isAuthorityMachine: false, N), $"кадр {frame}");
            }
        }

        [Test]
        public void IntervalOneOrLess_SolvesEveryFrame()
        {
            for (int frame = 0; frame < 8; frame++)
            {
                Assert.IsTrue(RemoteAvatarIKPolicy.ShouldSolve(true, false, false, frame, 0, false, 1));
                Assert.IsTrue(RemoteAvatarIKPolicy.ShouldSolve(true, false, false, frame, 0, false, 0));
            }
        }

        // ── Невидимый чужой на чистом клиенте ───────────────────────────────

        [Test]
        public void InvisibleRemote_SolvesEveryNthFrame_OnItsPhase()
        {
            const int stagger = 1;
            int solved = 0;

            for (int frame = 0; frame < N * 10; frame++)
            {
                bool solve = RemoteAvatarIKPolicy.ShouldSolve(true, false, false, frame, stagger, false, N);
                Assert.AreEqual((frame + stagger) % N == 0, solve, $"кадр {frame}");
                if (solve) solved++;
            }

            Assert.AreEqual(10, solved);
        }

        [Test]
        public void InvisibleRemotes_WithDifferentStagger_SpreadOverFrames()
        {
            // N аватаров со сдвигами 0..N-1: в каждом кадре решается ровно один — нагрузка
            // ровная, без пика раз в N кадров.
            for (int frame = 0; frame < N * 5; frame++)
            {
                int solvedThisFrame = 0;
                for (int stagger = 0; stagger < N; stagger++)
                {
                    if (RemoteAvatarIKPolicy.ShouldSolve(true, false, false, frame, stagger, false, N)) solvedThisFrame++;
                }

                Assert.AreEqual(1, solvedThisFrame, $"кадр {frame}");
            }
        }

        [Test]
        public void BecomingVisible_SolvesImmediately_OffPhase()
        {
            const int stagger = 0;
            const int offPhaseFrame = 1; // (1 + 0) % 4 != 0

            Assert.IsFalse(RemoteAvatarIKPolicy.ShouldSolve(true, false, false, offPhaseFrame, stagger, false, N),
                "предусловие: кадр не свой");
            Assert.IsTrue(RemoteAvatarIKPolicy.ShouldSolve(true, true, false, offPhaseFrame, stagger, false, N));
        }

        [Test]
        public void JustBecameInvisible_SolvesOneMoreFrame()
        {
            Assert.IsTrue(RemoteAvatarIKPolicy.ShouldSolve(true, false, true, 1, 0, false, N));
            Assert.IsFalse(RemoteAvatarIKPolicy.ShouldSolve(true, false, false, 2, 0, false, N));
        }

        [Test]
        public void NegativeFrameOrStagger_KeepsPeriod()
        {
            int solved = 0;
            for (int frame = -N * 4; frame < 0; frame++)
            {
                if (RemoteAvatarIKPolicy.ShouldSolve(true, false, false, frame, -3, false, N)) solved++;
            }

            Assert.AreEqual(4, solved);
        }

        // ── Где живёт авторитет ─────────────────────────────────────────────

        [Test]
        public void IsAuthorityMachine_DedicatedServer() =>
            Assert.IsTrue(RemoteAvatarIKPolicy.IsAuthorityMachine(isBatchMode: false, serverActive: true, clientActive: false));

        [Test]
        public void IsAuthorityMachine_Host() =>
            Assert.IsTrue(RemoteAvatarIKPolicy.IsAuthorityMachine(isBatchMode: false, serverActive: true, clientActive: true));

        [Test]
        public void IsAuthorityMachine_BatchMode() =>
            Assert.IsTrue(RemoteAvatarIKPolicy.IsAuthorityMachine(isBatchMode: true, serverActive: false, clientActive: true));

        [Test]
        public void IsAuthorityMachine_NoNetwork() =>
            Assert.IsTrue(RemoteAvatarIKPolicy.IsAuthorityMachine(isBatchMode: false, serverActive: false, clientActive: false));

        [Test]
        public void IsAuthorityMachine_PureClient_IsFalse() =>
            Assert.IsFalse(RemoteAvatarIKPolicy.IsAuthorityMachine(isBatchMode: false, serverActive: false, clientActive: true));
    }
}
