"""Отладочный журнал брокера на время активной разработки и интеграции.

Пишется в <state>/logs/broker-YYYYMMDD.log. Файл посуточный, без ротации: в него пишут
несколько процессов, а переименование при ротации на Windows ломается на открытом файле.
Токены аренды в журнал не попадают: логируются только id билетов и имена операций.
"""
from contextlib import contextmanager
import functools
import logging
from pathlib import Path
import time

LOGGER = logging.getLogger("editor_broker")
LOGGER.addHandler(logging.NullHandler())
LOGGER.setLevel(logging.DEBUG)
LOGGER.propagate = False
# Сбой записи журнала не должен портить JSON-вывод CLI или stdio MCP.
logging.raiseExceptions = False
_configured = None


class _AppendHandler(logging.Handler):
    """Файл открывается на каждую запись: процессы не держат его занятым (Windows), а строки
    нескольких процессов не перемешиваются внутри буфера."""

    def __init__(self, path):
        super().__init__()
        self.path = path

    def emit(self, record):
        try:
            line = self.format(record) + "\n"
            with open(self.path, "a", encoding="utf-8") as stream:
                stream.write(line)
        except Exception:  # noqa: BLE001 — сбой журнала не должен ломать брокер
            self.handleError(record)


def configure(state_dir, name="broker"):
    """Подключить файл журнала; повторный вызов с тем же каталогом ничего не меняет."""
    global _configured
    path = Path(state_dir).resolve() / "logs" / f"{name}-{time.strftime('%Y%m%d')}.log"
    if _configured == path:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    handler = _AppendHandler(path)
    handler.setFormatter(logging.Formatter(
        "%(asctime)s.%(msecs)03d pid=%(process)d %(levelname)s %(name)s: %(message)s", "%Y-%m-%d %H:%M:%S"))
    for old in [item for item in LOGGER.handlers if not isinstance(item, logging.NullHandler)]:
        LOGGER.removeHandler(old)
        old.close()
    LOGGER.addHandler(handler)
    _configured = path


def get(name):
    return LOGGER.getChild(name)


@contextmanager
def timed(logger, what, **fields):
    """begin/end/fail с длительностью: по журналу видно, на каком шаге ушло время."""
    details = " ".join(f"{key}={value}" for key, value in fields.items())
    started = time.perf_counter()
    logger.debug("begin %s %s", what, details)
    try:
        yield
    except BaseException as error:
        logger.warning("fail %s %.3fs %s %s: %s", what, time.perf_counter() - started, details,
                       type(error).__name__, error)
        raise
    logger.debug("end %s %.3fs %s", what, time.perf_counter() - started, details)


def traced(logger, what):
    """Шаг метода: логируется только первый аргумент (id билета/владелец/SHA), не токен."""
    def wrap(function):
        @functools.wraps(function)
        def inner(self, *args, **kwargs):
            with timed(logger, what, subject=args[0] if args else ""):
                return function(self, *args, **kwargs)
        return inner
    return wrap
