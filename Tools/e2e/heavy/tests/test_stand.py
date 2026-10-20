"""Состояния стенда и свежесть кода — чистые функции дирижёра, без Unity и MCP."""
from subprocess import CompletedProcess
import unittest

from heavy import editor_profile as E
from heavy import freshness as F
from heavy import verdict as V


def participant(pid, server, **extra):
    base = {"ParticipantId": pid, "RunId": "r", "ProcessSessionId": pid + "-s", "Active": True, "Playing": True,
            "LastSeenAgeSeconds": 0.5, "Scene": "TestMap2", "MapPlayable": True, "MapRunValid": True,
            "Server": server, "Client": not server, "Connected": not server,
            "SessionNetId": 0 if server else 7, "AvatarNetId": 0 if server else 9, "ServerSessions": 2 if server else 0}
    base.update(extra)
    return base


def status(*participants, **extra):
    base = {"Ready": True, "Owner": "heavy-1", "RunId": "r", "State": "Ready", "Playing": True,
            "Participants": list(participants)}
    base.update(extra)
    return base


READY = status(participant("main", True), participant("Player 2", False), participant("Player 3", False))


class ReadyTests(unittest.TestCase):
    def test_server_and_two_clients_with_avatars_is_ready(self):
        self.assertTrue(E.is_ready(READY, "heavy-1", "r", 2, "TestMap2"))

    def test_foreign_owner_missing_client_or_avatar_is_not_ready(self):
        self.assertFalse(E.is_ready(READY, "heavy-2", "r", 2, "TestMap2"))
        two = status(participant("main", True, ServerSessions=1), participant("Player 2", False))
        self.assertFalse(E.is_ready(two, "heavy-1", "r", 2, "TestMap2"))
        no_avatar = status(participant("main", True), participant("Player 2", False),
                           participant("Player 3", False, AvatarNetId=0))
        self.assertFalse(E.is_ready(no_avatar, "heavy-1", "r", 2, "TestMap2"))

    def test_stale_heartbeat_or_wrong_scene_is_not_ready(self):
        stale = status(participant("main", True), participant("Player 2", False),
                       participant("Player 3", False, LastSeenAgeSeconds=12))
        self.assertFalse(E.is_ready(stale, "heavy-1", "r", 2, "TestMap2"))
        self.assertFalse(E.is_ready(READY, "heavy-1", "r", 2, "TestMap1"))

    def test_finished_requires_same_sessions_all_finished(self):
        expected = {(p["ParticipantId"], p["ProcessSessionId"]) for p in READY["Participants"]}
        done = status(*[dict(p, E2EFinished=True) for p in READY["Participants"]])
        self.assertTrue(E.is_finished(done, "heavy-1", "r", expected))
        reloaded = status(*[dict(p, E2EFinished=True, ProcessSessionId="new") for p in READY["Participants"]])
        self.assertFalse(E.is_finished(reloaded, "heavy-1", "r", expected))


class StoppedTests(unittest.TestCase):
    def test_idle_with_stale_last_heartbeat_is_stopped(self):
        # Реальный статус после Cancel (аренда 871): клоны помнят playing=true давностью в минуты.
        idle = status(participant("main", True, Active=False, Playing=False, LastSeenAgeSeconds=128),
                      participant("Player 2", False, Active=False, Playing=True, LastSeenAgeSeconds=128),
                      State="Idle", Owner="", Playing=False, Ready=False)
        self.assertTrue(E.is_stopped(idle))

    def test_failed_after_own_timeout_is_stopped(self):
        failed = status(participant("main", True, Active=False, Playing=False, LastSeenAgeSeconds=133),
                        State="Failed", Error="LaunchNotReady", Owner="", Playing=False, Ready=False)
        self.assertTrue(E.is_stopped(failed))

    def test_running_or_fresh_participant_is_not_stopped(self):
        self.assertFalse(E.is_stopped(READY))
        fresh = status(participant("Player 2", False), State="Idle", Owner="", Playing=False)
        self.assertFalse(E.is_stopped(fresh))


def fake_git(ancestor, behind="0", changed=""):
    def git(*args):
        if args[0] == "merge-base":
            return CompletedProcess(args, ancestor, "", "fatal: bad object" if ancestor == 128 else "")
        if args[0] == "rev-list":
            return CompletedProcess(args, 0, behind + "\n", "")
        return CompletedProcess(args, 0, changed, "")
    return git


class FreshnessTests(unittest.TestCase):
    def test_contains_origin_passes(self):
        info = F.drift(fake_git(0), "h", "o")
        self.assertEqual(F.check("x", info, "h", "o")["verdict"], V.PASS)

    def test_dev_ahead_only_with_docs_passes_with_warning(self):
        info = F.drift(fake_git(1, "3", "Docs/a.md\ntasks/t/Readme.md\nTools/e2e/x.py\n"), "h", "o")
        result = F.check("x", info, "h", "o")
        self.assertEqual(result["verdict"], V.PASS)
        self.assertIn("без игровых путей", result["detail"])

    def test_dev_ahead_with_game_paths_fails(self):
        info = F.drift(fake_git(1, "4", "Docs/a.md\nAssets/URPDefaultResources/PC.asset\n"), "h", "o")
        self.assertEqual(info["gamePaths"], ["Assets/URPDefaultResources/PC.asset"])
        self.assertEqual(F.check("x", info, "h", "o")["verdict"], V.FAIL)

    def test_unknown_object_is_inconclusive(self):
        self.assertEqual(F.check("x", F.drift(fake_git(128), "h", "o"), "h", "o")["verdict"], V.INCONCLUSIVE)


if __name__ == "__main__":
    unittest.main()
