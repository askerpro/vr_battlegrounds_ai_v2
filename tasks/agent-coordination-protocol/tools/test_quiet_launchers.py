"""Граница запуска реальных заглушок: скрыто на Windows, stdio и argv сохранены."""
import importlib.util
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]


class QuietLaunchersTests(unittest.TestCase):
    def runtime_case(self, platform):
        source = ROOT / "Tools/agents/broker_runtime.py"
        spec = importlib.util.spec_from_file_location("runtime_probe", source)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        with tempfile.TemporaryDirectory() as directory:
            common = Path(directory) / ".git"
            with patch.object(module, "os", types.SimpleNamespace(name=platform), create=True), \
                    patch.object(module.subprocess, "check_output", return_value=str(common)) as call:
                result = module.runtime_dir(ROOT)
            self.assertEqual(result, common.parent / ".agent-state/editor-broker/runtime")
            self.assertTrue(call.call_args.kwargs["text"])
            self.assertEqual(call.call_args.kwargs.get("creationflags", 0),
                             subprocess.CREATE_NO_WINDOW if platform == "nt" else 0)

    def proxy_case(self, platform):
        with tempfile.TemporaryDirectory() as directory:
            runtime = Path(directory)
            fake = types.ModuleType("broker_runtime")
            fake.require = lambda: runtime
            original_path = sys.path[:]
            try:
                with patch.dict(sys.modules, {"broker_runtime": fake, "os": types.SimpleNamespace(name=platform)}), \
                        patch.object(subprocess, "check_output", return_value=str(ROOT)) as git, \
                        patch.object(subprocess, "call", return_value=23) as invoke, \
                        patch.object(sys, "argv", ["unity_mcp_proxy.py", "--probe"]):
                    with self.assertRaises(SystemExit) as result:
                        runpy.run_path(str(ROOT / "Tools/agents/unity_mcp_proxy.py"), run_name="__main__")
                self.assertEqual(result.exception.code, 23)
            finally:
                sys.path[:] = original_path
            flags = subprocess.CREATE_NO_WINDOW if platform == "nt" else 0
            self.assertEqual(git.call_args.kwargs.get("creationflags", 0), flags)
            self.assertEqual(invoke.call_args.kwargs.get("creationflags", 0), flags)
            self.assertEqual({k: v for k, v in invoke.call_args.kwargs.items() if k != "creationflags"}, {})
            self.assertEqual(invoke.call_args.args[0], ["uv", "run", "--quiet",
                str(runtime / "unity_mcp_proxy.py"), "--repo", str(ROOT), "--probe"])

    def test_runtime_windows(self):
        self.runtime_case("nt")

    def test_runtime_unix(self):
        self.runtime_case("posix")

    def test_proxy_windows(self):
        self.proxy_case("nt")

    def test_proxy_unix(self):
        self.proxy_case("posix")


if __name__ == "__main__":
    unittest.main()
