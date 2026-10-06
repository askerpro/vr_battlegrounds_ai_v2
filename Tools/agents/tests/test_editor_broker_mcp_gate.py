"""Решение брокера по вызовам Unity MCP на настоящем Git; Unity подменён."""
import hashlib
import unittest

import test_editor_broker_service as fixture
from editor_broker.mcp_gate import classify, decide, instance_id, same_instance
from editor_broker.service import BrokerError


class ClassifyTests(unittest.TestCase):
    def test_reads_and_unknown_default_to_write(self):
        self.assertEqual(classify("read_console", {}), "read")
        self.assertEqual(classify("read_console", {"action": "clear"}), "write")
        self.assertEqual(classify("manage_scene", {"action": "get_hierarchy"}), "read")
        self.assertEqual(classify("manage_scene", {"action": "save"}), "write")
        self.assertEqual(classify("manage_scene", {}), "write")
        self.assertEqual(classify("execute_code", {"action": "execute"}), "write")
        self.assertEqual(classify("brand_new_tool", {"action": "get"}), "write")

    def test_instance_id_matches_mcp_for_unity_hash(self):
        root = "F:/CodexWorktrees/unity-editor-worker/Vr_Battlegrounds_ai"
        self.assertEqual(instance_id(root), "Vr_Battlegrounds_ai@72e96145498eb8a0")
        self.assertTrue(same_instance("72e961", instance_id(root)))
        self.assertFalse(same_instance("Other@72e96145498eb8a0", instance_id(root)))


class GateTests(unittest.TestCase):
    setUp = fixture.EditorBrokerTests.setUp
    git = staticmethod(fixture.EditorBrokerTests.git)

    def running(self, owner="a"):
        ticket = self.broker.request(owner, self.base, self.input, self.agent, [], "job-" + owner)
        lease = self.broker.claim(ticket["id"], owner)
        self.broker.begin(lease["id"], lease["token"])
        return lease

    def test_write_without_lease_is_refused_read_passes(self):
        refused = decide(self.broker, self.agent, "manage_scene", {"action": "save"})
        self.assertFalse(refused["allow"])
        self.assertIn("checkpoint", refused["reason"])
        read = decide(self.broker, self.agent, "read_console", {})
        self.assertEqual((read["allow"], read["unity_instance"]), (True, None))

    def test_own_running_lease_routes_everything_to_worker(self):
        self.running()
        for tool, arguments in (("manage_scene", {"action": "save"}), ("read_console", {})):
            decision = decide(self.broker, self.agent, tool, arguments, pinned="Mine@878b4a6962c686af")
            self.assertTrue(decision["allow"])
            self.assertEqual(decision["unity_instance"], instance_id(self.editor))

    def test_other_agent_is_refused_during_lease_and_told_holder(self):
        self.running("a")
        other = self.root / "other"
        self.git(self.repo, "worktree", "add", "--detach", str(other), self.base)
        refused = decide(self.broker, other, "execute_code", {"action": "execute"})
        self.assertFalse(refused["allow"])
        self.assertIn("«a»", refused["reason"])

    def test_write_is_refused_when_guard_fails(self):
        self.running()
        self.unity.session = "restarted"
        with self.assertRaises(BrokerError):
            decide(self.broker, self.agent, "manage_scene", {"action": "save"})

    def test_human_main_checkout_writes_own_unity_but_not_worker(self):
        own = decide(self.broker, self.repo, "manage_scene", {"action": "save"})
        self.assertEqual((own["allow"], own["unity_instance"]), (True, instance_id(self.repo)))
        worker = decide(self.broker, self.repo, "manage_scene", {"action": "save"},
                        pinned=instance_id(self.editor))
        self.assertFalse(worker["allow"])


if __name__ == "__main__":
    unittest.main()
