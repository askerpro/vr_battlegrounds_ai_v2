"""Git LFS: лёгкие ворктри агентов, полные main и зарегистрированный Unity worker."""
import argparse
import json
import os
from pathlib import Path
import subprocess


def git(root, *args, check=True):
    result = subprocess.run(["git", "--no-pager", "-C", str(root), *args], capture_output=True, text=True, encoding="utf-8")
    if check and result.returncode:
        raise ValueError(f"git exit {result.returncode}: {result.stderr.strip()} {result.stdout.strip()}")
    return result.stdout.strip()


def context(root):
    root = Path(git(root, "rev-parse", "--show-toplevel")).resolve()
    common = Path(git(root, "rev-parse", "--path-format=absolute", "--git-common-dir")).resolve()
    primary = common.parent
    policy = json.loads((root / "Tools/agents/lfs-worktree-policy.json").read_text(encoding="utf-8-sig"))
    registry = primary / policy["workerRegistry"]
    worker = None
    if registry.is_file():
        data = json.loads(registry.read_text(encoding="utf-8-sig"))
        worker = Path(data["editor_root"]).resolve()
    role = "main" if root == primary else "worker" if root == worker else "agent"
    return root, primary, worker, role


def filters(root, scope, full):
    git(root, "config", scope, "filter.lfs.clean", "git-lfs clean -- %f")
    git(root, "config", scope, "filter.lfs.required", "true")
    git(root, "config", scope, "filter.lfs.smudge", "git-lfs smudge " + ("" if full else "--skip ") + "-- %f")
    git(root, "config", scope, "filter.lfs.process", "git-lfs filter-process" + ("" if full else " --skip"))


def apply(root, install_defaults=False):
    root, primary, worker, role = context(root)
    git(root, "lfs", "version")
    if install_defaults:
        if role != "main":
            raise ValueError("Общие defaults устанавливает интегратор из основного checkout")
        if git(root, "config", "--local", "--get", "core.worktree", check=False):
            raise ValueError("core.worktree требует отдельной миграции config.worktree")
        if git(root, "config", "--local", "--get", "core.bare", check=False) == "true":
            raise ValueError("Bare repository не поддерживается этим профилем")
        git(root, "config", "--local", "extensions.worktreeConfig", "true")
        filters(root, "--local", False)
        # Исключения задаются до следующего checkout, без изменения файлов Assets.
        # worktree add наследует фильтры вызывающего main. Main должен оставлять
        # smudge выключенным; его post-checkout отдельно извлекает локальный кеш.
        filters(primary, "--worktree", False)
        if worker is not None:
            if Path(git(worker, "rev-parse", "--path-format=absolute", "--git-common-dir")).resolve() != primary / ".git":
                raise ValueError("Worker относится к другому репозиторию")
            filters(worker, "--worktree", True)
    if git(root, "config", "--local", "--get", "extensions.worktreeConfig", check=False) != "true":
        raise ValueError("Сначала интегратор выполняет apply --install-defaults")
    filters(root, "--worktree", role == "worker")
    skip_environment = os.environ.get("GIT_LFS_SKIP_SMUDGE", "").lower() not in {"", "0", "false"}
    if role == "worker" and skip_environment:
        raise ValueError("GIT_LFS_SKIP_SMUDGE в окружении запрещает полное извлечение main/worker; убрать для их Git-команд")
    return {"root": str(root), "role": role, "hydration": "skip" if role == "agent" else "full_after_checkout" if role == "main" else "full",
            "process": git(root, "config", "--get", "filter.lfs.process")}


def after_checkout(root):
    result = apply(root)
    if result["role"] in {"main", "worker"}:
        git(root, "lfs", "checkout")
        state = json.loads(git(root, "lfs", "ls-files", "--json"))
        pending = [item["name"] for item in state.get("files", []) if not item.get("checkout", False)]
        if pending:
            raise ValueError(f"LFS-кеш не содержит {len(pending)} файлов; до Unity выполнить адресный git lfs pull")
        result["hydrated"] = True
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["apply", "status", "checkout"])
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--install-defaults", action="store_true")
    args = parser.parse_args()
    if args.action == "apply":
        result = apply(args.root, args.install_defaults)
    elif args.action == "checkout":
        result = after_checkout(args.root)
    else:
        root, _, _, role = context(args.root)
        result = {"root": str(root), "role": role, "process": git(root, "config", "--get", "filter.lfs.process", check=False)}
    print(json.dumps(result, ensure_ascii=False))
