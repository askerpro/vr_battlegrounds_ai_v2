"""Git LFS: лёгкие ворктри агентов, полные main, зарегистрированный Unity worker и сборочные worktree."""
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


def resolve_build_worktrees(primary, policy):
    items = policy.get("buildWorktrees") or []
    if isinstance(items, dict):
        items = list(items.values())
    resolved = []
    for entry in items:
        path = Path(entry)
        resolved.append((path if path.is_absolute() else (primary / path)).resolve())
    return resolved


def load_policy(root, common):
    policy_path = root / "Tools/agents/lfs-worktree-policy.json"
    policy = json.loads(policy_path.read_text(encoding="utf-8-sig"))
    local_policy_path = Path(__file__).resolve().with_name("lfs-worktree-policy.json")
    if "buildWorktrees" not in policy and local_policy_path.is_file():
        script_common = git(local_policy_path.parent, "rev-parse", "--path-format=absolute", "--git-common-dir", check=False)
        if script_common and Path(script_common).resolve() == common:
            local_policy = json.loads(local_policy_path.read_text(encoding="utf-8-sig"))
            if "buildWorktrees" in local_policy:
                policy["buildWorktrees"] = local_policy["buildWorktrees"]
            if "buildHydration" in local_policy:
                policy["buildHydration"] = local_policy["buildHydration"]
    return policy


def context(root):
    root = Path(git(root, "rev-parse", "--show-toplevel")).resolve()
    common = Path(git(root, "rev-parse", "--path-format=absolute", "--git-common-dir")).resolve()
    primary = common.parent
    policy = load_policy(root, common)
    registry = primary / policy["workerRegistry"]
    worker = None
    if registry.is_file():
        data = json.loads(registry.read_text(encoding="utf-8-sig"))
        worker = Path(data["editor_root"]).resolve()
    build_worktrees = resolve_build_worktrees(primary, policy)
    if root == primary:
        role = "main"
    elif root == worker:
        role = "worker"
    elif root in build_worktrees:
        role = "build"
    else:
        role = "agent"
    return root, primary, worker, role


def filters(root, scope, full, role=None):
    git(root, "config", scope, "filter.lfs.clean", "git-lfs clean -- %f")
    git(root, "config", scope, "filter.lfs.required", "true")
    git(root, "config", scope, "filter.lfs.smudge", "git-lfs smudge " + ("" if full else "--skip ") + "-- %f")
    git(root, "config", scope, "filter.lfs.process", "git-lfs filter-process" + ("" if full else " --skip"))
    if role:
        git(root, "config", scope, "vrb.lfs.role", role)


def apply(root, install_defaults=False):
    root, primary, worker, role = context(root)
    policy = load_policy(root, primary / ".git")
    build_worktrees = resolve_build_worktrees(primary, policy)
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
        filters(primary, "--worktree", False, role="main")
        if worker is not None:
            if Path(git(worker, "rev-parse", "--path-format=absolute", "--git-common-dir")).resolve() != primary / ".git":
                raise ValueError("Worker относится к другому репозиторию")
            filters(worker, "--worktree", True, role="worker")
        for build_root in build_worktrees:
            if build_root.is_dir() and (build_root / ".git").exists():
                if Path(git(build_root, "rev-parse", "--path-format=absolute", "--git-common-dir")).resolve() != primary / ".git":
                    raise ValueError(f"Build worktree относится к другому репозиторию: {build_root}")
                filters(build_root, "--worktree", True, role="build")
    if git(root, "config", "--local", "--get", "extensions.worktreeConfig", check=False) != "true":
        raise ValueError("Сначала интегратор выполняет apply --install-defaults")
    filters(root, "--worktree", role in {"worker", "build"}, role=role)
    skip_environment = os.environ.get("GIT_LFS_SKIP_SMUDGE", "").lower() not in {"", "0", "false"}
    if role in {"worker", "build"} and skip_environment:
        raise ValueError("GIT_LFS_SKIP_SMUDGE в окружении запрещает полное извлечение main/worker/build; убрать для их Git-команд")
    return {"root": str(root), "role": role, "hydration": "skip" if role == "agent" else "full_after_checkout" if role == "main" else "full",
            "process": git(root, "config", "--get", "filter.lfs.process")}


def after_checkout(root):
    result = apply(root)
    if result["role"] in {"main", "worker", "build"}:
        git(root, "lfs", "checkout")
        state = json.loads(git(root, "lfs", "ls-files", "--json"))
        pending = [item["name"] for item in state.get("files") or [] if not item.get("checkout", False)]
        if pending:
            raise ValueError(f"LFS-кеш не содержит {len(pending)} файлов; до Unity выполнить адресный git lfs pull")
        result["hydrated"] = True
    return result


def hydrate(root):
    """post-rewrite/post-merge: та же гидратация, что after_checkout, только для main/worker/build.

    Агентский worktree (hydration skip) не настраивается и не извлекается: только чтение роли."""
    root, _, _, role = context(root)
    if role == "agent":
        return {"root": str(root), "role": role, "hydration": "skip", "hydrated": False}
    return after_checkout(root)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["apply", "status", "checkout", "hydrate"])
    parser.add_argument("--root", "--worktree", dest="root", type=Path, default=Path.cwd())
    parser.add_argument("--install-defaults", action="store_true")
    args = parser.parse_args()
    if args.action == "apply":
        result = apply(args.root, args.install_defaults)
    elif args.action == "checkout":
        result = after_checkout(args.root)
    elif args.action == "hydrate":
        result = hydrate(args.root)
    else:
        root, _, _, role = context(args.root)
        result = {"root": str(root), "role": role, "process": git(root, "config", "--get", "filter.lfs.process", check=False)}
    print(json.dumps(result, ensure_ascii=False))

