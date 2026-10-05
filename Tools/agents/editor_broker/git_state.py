"""Технические Git checkpoint без изменения индекса и ветки автора."""
from contextlib import contextmanager
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import tempfile
import uuid


class GitStateError(ValueError):
    """Небезопасный путь, конфликт снимка или ошибка Git."""


class GitState:
    RESERVED_BRIDGE = "Assets/Editor/VR_Battlegrounds/Debug/EditorBrokerLocal"
    FORBIDDEN = {".git", ".plastic", "library", "temp"}

    def __init__(self, root):
        self.root = Path(root).resolve()
        actual = Path(self._git("rev-parse", "--show-toplevel").decode().strip()).resolve()
        if self.root != actual:
            raise GitStateError("Требуется корень Git worktree")

    def _git(self, *args, data=None, index=None, check=True):
        env = os.environ.copy()
        # Наследованный alternate index/директория не должны перенаправлять операцию.
        for name in ("GIT_INDEX_FILE", "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR"):
            env.pop(name, None)
        env["GIT_OPTIONAL_LOCKS"] = "0"
        env["GIT_LITERAL_PATHSPECS"] = "1"
        # check-ignore принимает имена файлов, а не pathspec; literal magic для него недопустим.
        if args and args[0] == "check-ignore":
            env.pop("GIT_LITERAL_PATHSPECS", None)
        if index is not None:
            env["GIT_INDEX_FILE"] = str(index)
        # update-ref также вызывает reference-transaction hook; отключаем все hooks
        # только для этого процесса, без записи в конфигурацию репозитория.
        with tempfile.TemporaryDirectory(prefix="broker-empty-hooks-") as hooks:
            result = subprocess.run(["git", "--no-pager", "-c", "core.hooksPath=" + hooks, *args],
                                    cwd=self.root, env=env, input=data, stdout=subprocess.PIPE,
                                    stderr=subprocess.PIPE, check=False)
        if check and result.returncode:
            raise GitStateError(result.stderr.decode("utf-8", "replace").strip())
        return result.stdout if check else result

    def _commit(self, sha):
        return self._git("rev-parse", "--verify", "--end-of-options", str(sha) + "^{commit}").decode().strip()

    def head(self):
        return self._commit("HEAD")

    def common_dir(self):
        path = Path(self._git("rev-parse", "--git-common-dir").decode().strip())
        return str((self.root / path).resolve()) if not path.is_absolute() else str(path.resolve())

    def is_clean(self):
        with self._inspection_index() as index:
            return not self._git("status", "--porcelain=v1", "-z", "--untracked-files=all", index=index)

    def is_ancestor(self, base, sha):
        result = self._git("merge-base", "--is-ancestor", self._commit(base), self._commit(sha), check=False)
        if result.returncode not in (0, 1):
            raise GitStateError(result.stderr.decode("utf-8", "replace"))
        return result.returncode == 0

    def _path(self, name, allow_link=False, checked=None, physical=True):
        name = str(name)
        parts = PurePosixPath(name).parts
        if (not name or "\\" in name or "\x00" in name or "\n" in name or "\r" in name
                or name.startswith("/") or ":" in name or name != "/".join(parts)
                or any(part in (".", "..") for part in parts)):
            raise GitStateError("Небезопасный относительный путь: " + repr(name))
        if any(part.casefold() in self.FORBIDDEN for part in parts):
            raise GitStateError("Служебный путь запрещён: " + name)
        bridge = self.RESERVED_BRIDGE.casefold()
        if name.casefold() == bridge or name.casefold().startswith(bridge + "/"):
            raise GitStateError("Локальный Unity-мост не входит в checkpoint: " + name)
        if not physical:
            return name
        current = self.root
        for offset, part in enumerate(parts):
            current = current / part
            if checked is not None and current in checked:
                continue
            try:
                metadata = current.lstat()
            except (FileNotFoundError, NotADirectoryError):
                if checked is not None:
                    checked.add(current)
                continue
            is_link = stat.S_ISLNK(metadata.st_mode)
            if is_link:
                if not (allow_link and offset == len(parts) - 1
                        and current.resolve().is_relative_to(self.root)):
                    raise GitStateError("Ссылка в пути запрещена: " + name)
            # Python 3.10 также должен отвергать Windows junction/reparse point.
            if (getattr(metadata, "st_file_attributes", 0) & 0x400
                    and not (allow_link and offset == len(parts) - 1 and is_link)):
                raise GitStateError("Reparse point в пути запрещён: " + name)
            if checked is not None and not is_link:
                checked.add(current)
        return name

    def _validate_names(self, names, allowed_links=(), physical=True):
        seen = {}
        checked = set()
        for name in names:
            self._path(name, allow_link=name in allowed_links, checked=checked, physical=physical)
            for count in range(1, len(name.split("/")) + 1):
                prefix = "/".join(name.split("/")[:count])
                folded = prefix.casefold()
                if folded in seen and seen[folded] != prefix:
                    raise GitStateError("Коллизия регистра путей: " + prefix)
                seen[folded] = prefix

    def _tree(self, sha):
        entries = {}
        for item in self._git("ls-tree", "-r", "-z", self._commit(sha)).split(b"\x00"):
            if not item:
                continue
            header, name = item.split(b"\t", 1)
            mode, kind, oid = header.decode().split()
            name = os.fsdecode(name)
            if kind != "blob" or mode not in ("100644", "100755", "120000"):
                raise GitStateError("Подмодули не поддерживаются: " + name)
            entries[name] = (mode, oid)
        self._validate_names(entries, physical=False)
        return entries

    def _snapshot(self, parent, write=False):
        baseline = self._tree(parent)
        names = set(baseline)
        tracked = {os.fsdecode(name) for name in self._git("ls-files", "--cached", "-z").split(b"\x00") if name}
        names.update(tracked)
        names.update(os.fsdecode(name) for name in self._git(
            "ls-files", "--others", "--exclude-standard", "-z").split(b"\x00") if name)
        links = {name for name, entry in baseline.items() if entry[0] == "120000"}
        self._validate_names(names, links)
        changed = self._changed(parent)
        for name in changed:
            self._path(name)
            if name in links:
                raise GitStateError("Изменение существующей ссылки запрещено: " + name)
        if not changed:
            return baseline
        # diff использует stat cache штатного индекса, поэтому неизменённые 26 GB
        # не перечитываются. add применяет clean/LFS/text filters лишь к дельте.
        with self._index(tempfile.gettempdir()) as index:
            self._git("read-tree", parent, index=index)
            self._git("add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul",
                      data=b"".join(os.fsencode(name) + b"\x00" for name in changed), index=index)
            attr_dirs = {str(PurePosixPath(name).parent) for name in changed
                         if PurePosixPath(name).name == ".gitattributes"}
            candidates = sorted(name for name in tracked | baseline.keys() if name not in links
                                and any(folder == "." or name.startswith(folder + "/") for folder in attr_dirs)) if attr_dirs else []
            affected = self._attribute_changes(parent, candidates, index) if candidates else []
            affected = [name for name in affected if (self.root / name).is_file()]
            if affected:
                # Изменившаяся политика влияет и на файлы с действующим stat cache.
                # --renormalize принудительно применяет clean filters в частном индексе.
                self._git("add", "--renormalize", "--pathspec-from-file=-", "--pathspec-file-nul",
                          data=b"".join(os.fsencode(name) + b"\x00" for name in affected), index=index)
            current = {}
            for record in self._git("ls-files", "--stage", "-z", index=index).split(b"\x00"):
                if record:
                    header, name = record.split(b"\t", 1)
                    mode, oid, stage = header.decode().split()
                    if stage != "0":
                        raise GitStateError("Неразрешённый конфликт в частном индексе")
                    current[os.fsdecode(name)] = (mode, oid)
        return current

    def _attribute_changes(self, parent, names, index):
        data = b"".join(os.fsencode(name) + b"\x00" for name in names)
        attributes = ("text", "eol", "filter", "working-tree-encoding", "ident", "crlf")

        def policy(raw):
            values = raw.split(b"\x00")
            return {(os.fsdecode(values[offset]), values[offset + 1]): values[offset + 2]
                    for offset in range(0, len(values) - 1, 3)}

        old = policy(self._git("--attr-source=" + parent, "check-attr", "-z", "--stdin", *attributes, data=data))
        new = policy(self._git("check-attr", "-z", "--stdin", *attributes, data=data, index=index))
        # Сравнение эффективной политики не перечитывает бинарники и не запускает
        # clean filters. Изменение узкого правила в корне не нормализует весь SDK.
        return [name for name in names if any(old.get((name, attribute.encode())) != new.get((name, attribute.encode()))
                                             for attribute in attributes)]

    def _changed(self, base):
        with self._inspection_index() as index:
            names = {os.fsdecode(name) for name in self._git(
                "diff", "--name-only", "--no-renames", "--no-ext-diff", "--no-textconv", "-z", self._commit(base), "--",
                index=index).split(b"\x00") if name}
        names.update(os.fsdecode(name) for name in self._git(
            "ls-files", "--others", "--exclude-standard", "-z").split(b"\x00") if name)
        return sorted(names)

    @contextmanager
    def _inspection_index(self):
        flagged = [record[2:] for record in self._git("ls-files", "-v", "-z").split(b"\x00")
                   if record and (record[:1].islower() or record[:1].upper() == b"S")]
        if not flagged:
            yield None
            return
        native = Path(self._git("rev-parse", "--git-path", "index").decode().strip())
        if not native.is_absolute():
            native = self.root / native
        with self._index(tempfile.gettempdir()) as index:
            shutil.copyfile(native, index)
            # Снимаем флаги только в копии: индекс автора сохраняется побайтно.
            for flag in ("--no-assume-unchanged", "--no-skip-worktree"):
                self._git("update-index", flag, "-z", "--stdin",
                          data=b"\x00".join(flagged) + b"\x00", index=index)
            yield index

    @staticmethod
    def _delta(before, after):
        return sorted(name for name in before.keys() | after.keys() if before.get(name) != after.get(name))

    def changed_paths(self, base):
        return self._delta(self._tree(base), self._snapshot(base))

    @contextmanager
    def _index(self, state_dir):
        directory = Path(state_dir)
        directory.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="git-index-", dir=directory) as private:
            yield Path(private) / "index"

    @staticmethod
    def _selected(name, roots):
        return any(name == root or name.startswith(root + "/") or name == root + ".meta" for root in roots)

    def checkpoint(self, state_dir, label, paths=None, parent=None):
        parent = self._commit(parent if parent is not None else self.head())
        roots = None if paths is None else [self._path(path) for path in paths]
        if roots is not None:
            self._validate_names(roots)
        before = self._tree(parent)
        snapshot = self._snapshot(parent, write=True)
        changed = self._delta(before, snapshot)
        if roots is not None:
            changed = [name for name in changed if self._selected(name, roots)]
        with self._index(state_dir) as index:
            self._git("read-tree", parent, index=index)
            records = []
            for name in changed:
                mode, oid = snapshot.get(name, ("0", "0" * 40))
                records.append((mode + " " + oid + "\t").encode() + os.fsencode(name) + b"\x00")
            if records:
                self._git("update-index", "-z", "--index-info", data=b"".join(records), index=index)
            tree = self._git("write-tree", index=index).decode().strip()
        sha = self._git("-c", "user.name=Editor Broker", "-c", "user.email=editor-broker@local.invalid",
                        "commit-tree", tree, "-p", parent,
                        data=("Технический checkpoint: " + str(label) + "\n").encode("utf-8")).decode().strip()
        safe_label = re.sub(r"[^a-zA-Z0-9_-]", "-", str(label))[:40] or "snapshot"
        ref = "refs/heads/codex/tmp/" + safe_label + "-" + uuid.uuid4().hex
        self._git("update-ref", ref, sha, "0" * len(sha))
        return {"sha": sha, "ref": ref, "paths": changed, "tree": tree}

    def capture_result(self, state_dir, input_sha, output_roots, label):
        roots = [self._path(path) for path in output_roots]
        self._validate_names(roots)
        result = self.checkpoint(state_dir, label, parent=input_sha)
        unexpected = [name for name in result["paths"] if not self._selected(name, roots)]
        return {**result, "unexpected": unexpected, "accepted": not unexpected}

    def switch_detached(self, sha):
        sha = self._commit(sha)
        current, target = self._tree(self.head()), self._tree(sha)
        for name in self._delta(current, target):
            if any(tree.get(name, ("",))[0] == "120000" for tree in (current, target)):
                raise GitStateError("Переключение изменённых ссылок запрещено: " + name)
        if not self.is_clean():
            raise GitStateError("Переключение грязного worktree запрещено")
        # Принятие ранее ignored ассета допустимо только с точным содержимым C.
        # Иной локальный кэш сохраняется; blanket overwrite без проверки запрещён.
        adopted = []
        for name in sorted(target.keys() - current.keys()):
            path = self.root / self._path(name)
            for parent in path.parents:
                if parent == self.root:
                    break
                if parent.exists() and not parent.is_dir():
                    raise GitStateError("Файл перекрывает новый каталог: " + name)
            if not path.exists():
                continue
            ignored = self._git("check-ignore", "--no-index", "-q", "--", name, check=False)
            if ignored.returncode != 0 or not self._matches(name, target[name], sha):
                raise GitStateError("Локальный ignored ассет отличается от новой базы: " + name)
            adopted.append(name)
        # Clean/smudge может занимать время: повторная проверка до checkout.
        for name in adopted:
            if not self._matches(name, target[name], sha):
                raise GitStateError("Ассет изменился во время принятия в Git: " + name)
        policy = "--overwrite-ignore" if adopted else "--no-overwrite-ignore"
        self._git("checkout", "--detach", policy, sha)

    def _pinned(self, sha):
        refs = self._git("for-each-ref", "--format=%(objectname)", "refs/heads/codex/tmp/").decode().splitlines()
        if sha not in refs:
            raise GitStateError("Результат не закреплён под codex/tmp")

    def _matches(self, name, entry, source):
        path = self.root / self._path(name)
        if entry is None:
            return not path.exists()
        if not path.is_file():
            return False
        mode, oid = entry
        initial = path.stat()
        actual = self._git("--attr-source=" + source, "hash-object", "--path=" + name,
                           "--stdin", data=path.read_bytes()).decode().strip()
        self._path(name)
        if not path.is_file():
            return False
        final = path.stat()
        identity = lambda value: (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns)
        return (identity(initial) == identity(final) and actual == oid
                and (os.name == "nt" or bool(final.st_mode & stat.S_IXUSR) == (mode == "100755")))

    def _needs_apply(self, name, before, after, source, target, resumable):
        if self._matches(name, before.get(name), source):
            return True
        if resumable and self._matches(name, after.get(name), target):
            return False
        raise GitStateError("Локальные изменения пересекают результат: " + name)

    def _apply(self, before, after, changed, source, target, resumable=False):
        # Все исходные байты и коллизии проверяются прежде первой записи.
        local = set()
        for args in (("ls-files", "--cached", "-z"), ("ls-files", "--others", "--exclude-standard", "-z")):
            local.update(os.fsdecode(name) for name in self._git(*args).split(b"\x00") if name)
        links = {name for name in before.keys() & after.keys()
                 if before[name] == after[name] and before[name][0] == "120000"}
        self._validate_names(set(before) | set(after) | local, links)
        pending = []
        for name in changed:
            if any(tree.get(name, ("",))[0] == "120000" for tree in (before, after)):
                raise GitStateError("Доставка изменённых ссылок запрещена: " + name)
            if not self._needs_apply(name, before, after, source, target, resumable):
                continue
            pending.append(name)
            for parent in (self.root / name).parents:
                if parent == self.root:
                    break
                if parent.exists() and not parent.is_dir():
                    raise GitStateError("Файл перекрывает каталог результата: " + name)
        payloads = {name: self._git("--attr-source=" + target, "cat-file", "--filters",
                                   "--path=" + name, after[name][1]) for name in pending if name in after}
        prepared = set(pending)
        pending = []
        # LFS/smudge могут выполняться долго. После их завершения повторяем
        # проверку всего затронутого набора до первой мутации worktree.
        for name in changed:
            if self._needs_apply(name, before, after, source, target, resumable):
                if name not in prepared:
                    raise GitStateError("Файл изменился во время подготовки доставки: " + name)
                pending.append(name)
        for name in pending:
            path = self.root / name
            if name not in after:
                if not self._needs_apply(name, before, after, source, target, resumable):
                    continue
                path.unlink()
                # Удаляем лишь опустевшие каталоги этих файлов; ignored содержимое сохраняется.
                for parent in path.parents:
                    if parent == self.root:
                        break
                    try:
                        parent.rmdir()
                    except OSError:
                        break
        for name, content in payloads.items():
            if name not in pending:
                continue
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            # Каждый файл публикуется целиком: после I/O отказа получатель видит
            # либо A, либо R и может повторить доставку после потерянного ack.
            handle, temporary = tempfile.mkstemp(prefix=".broker-delivery-", dir=path.parent)
            try:
                with os.fdopen(handle, "wb") as stream:
                    stream.write(content)
                    stream.flush()
                    os.fsync(stream.fileno())
                if os.name != "nt":
                    os.chmod(temporary, 0o755 if after[name][0] == "100755" else 0o644)
                if self._needs_apply(name, before, after, source, target, resumable):
                    os.replace(temporary, path)
            finally:
                if os.path.exists(temporary):
                    os.unlink(temporary)

    def restore_captured(self, result_sha, baseline_sha):
        result_sha, baseline_sha = self._commit(result_sha), self._commit(baseline_sha)
        self._pinned(result_sha)
        result, baseline = self._tree(result_sha), self._tree(baseline_sha)
        if self._snapshot(result_sha) != result:
            raise GitStateError("Worktree изменился после capture; откат запрещён")
        changed = self._delta(result, baseline)
        self._apply(result, baseline, changed, result_sha, baseline_sha)
        # HEAD/index редактора возвращаются к B только после сохранения и проверки R.
        # Команда read-tree не вызывает hooks и не удаляет ignored файлы.
        self._git("read-tree", baseline_sha)
        self._git("update-ref", "--no-deref", "HEAD", baseline_sha)
        if not self.is_clean() or self._snapshot(baseline_sha) != baseline:
            raise GitStateError("Не удалось подтвердить восстановление B")

    def receive_result(self, input_sha, result_sha):
        input_sha, result_sha = self._commit(input_sha), self._commit(result_sha)
        parents = self._git("rev-list", "--parents", "-n", "1", result_sha).decode().strip().split()[1:]
        if parents != [input_sha]:
            raise GitStateError("Результат должен иметь единственного родителя A")
        before, after = self._tree(input_sha), self._tree(result_sha)
        changed = self._delta(before, after)
        self._apply(before, after, changed, input_sha, result_sha, resumable=True)
        return {"input_sha": input_sha, "result_sha": result_sha, "paths": changed, "received": True}
