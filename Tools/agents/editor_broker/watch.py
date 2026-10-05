"""Ожидающий процесс для стандартного монитора результата команды."""
import math
import time


def watch_ticket(store, ticket_id, owner, until="offered", timeout=None):
    if until not in ("offered", "result"):
        raise ValueError("Неизвестное событие монитора")
    if timeout is not None and (not math.isfinite(float(timeout)) or float(timeout) < 0):
        raise ValueError("Timeout должен быть конечным неотрицательным числом")
    deadline = None if timeout is None else time.monotonic() + float(timeout)
    while True:
        remaining = 60 if deadline is None else max(0, min(60, deadline - time.monotonic()))
        ticket = store.wait(ticket_id, owner, timeout=remaining)
        phase = ticket["phase"]
        summary = {"ticket_id": ticket_id, "phase": phase,
                   "offer_expires_at": ticket.get("offer_expires_at"),
                   "result_sha": ticket.get("result_sha")}
        if ticket.get("blocked_by_recovery"):
            return 4, {**summary, "event": "recovery_required",
                       "blocking_ticket_id": ticket["blocking_ticket_id"]}
        if phase == "RECOVERY_REQUIRED":
            return 4, {**summary, "event": "recovery_required"}
        if phase in ("CANCELLED", "FAILED"):
            return 3, {**summary, "event": "cancelled" if phase == "CANCELLED" else "failed"}
        if until == "offered" and phase in ("OFFERED", "LEASED", "SWITCHING", "RUNNING", "CAPTURING", "RESTORING"):
            return 0, {**summary, "event": "offered" if phase == "OFFERED" else "already_owned"}
        if phase == "DONE":
            if until == "result" and ticket.get("result_sha"):
                return 0, {**summary, "event": "result_available"}
            return 3, {**summary, "event": "completed_without_requested_event"}
        if deadline is not None and time.monotonic() >= deadline:
            return 5, {**summary, "event": "timeout"}
        pause = .2 if deadline is None else min(.2, max(0, deadline - time.monotonic()))
        time.sleep(pause)
