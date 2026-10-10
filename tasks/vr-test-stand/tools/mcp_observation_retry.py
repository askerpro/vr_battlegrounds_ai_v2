"""Ограниченный повтор наблюдений при пустом отказе или подтверждённом disconnect."""
import asyncio
import json
from pathlib import Path
import re
import time

LAUNCH = 'VrBattlegrounds.EditorTools.TestStand.PlayLaunch'
STAND = 'VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand'


def verify_observation_contract(editor_root):
    path = Path(editor_root) / 'Assets/Editor/VR_Battlegrounds/Testing/PlayModeTestStand.cs'
    source = path.read_text(encoding='utf-8-sig')
    match = re.search(r'public\s+static\s+string\s+Status\(\)\s*\{', source)
    if not match:
        raise RuntimeError('Не найден Status для проверки контракта наблюдений: ' + str(path))
    depth = 1
    end = match.end()
    while depth and end < len(source):
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    body = source[match.end():end - 1]
    if depth or re.search(r'\bTick\s*\(', body) or not re.search(r'EditorApplication\.update\s*\+=\s*Tick\s*;', source):
        raise RuntimeError('Повтор Status запрещён: продвижение стенда должно принадлежать EditorApplication.update')
    return {'sourcePath': str(path), 'statusCallsTick': False, 'tickOwnedByEditorUpdate': True}


def observation_allowed(expression):
    if expression == LAUNCH + '.Status()':
        return True
    prefix = STAND + '.Operation('
    if not expression.startswith(prefix) or not expression.endswith(')'):
        return False
    try:
        request_id = json.loads(expression[len(prefix):-1])
        return isinstance(request_id, str) and bool(re.fullmatch(r'[A-Za-z0-9_.:-]+', request_id))
    except (ValueError, TypeError):
        return False


def empty_failure(reply):
    if getattr(reply, 'isError', False):
        return False
    texts = [c.text for c in reply.content if hasattr(c, 'text')]
    if len(texts) != 1 or len(reply.content) != 1:
        return False
    try:
        value = json.loads(texts[0])
    except (ValueError, TypeError):
        return False
    structured = getattr(reply, 'structuredContent', None)
    if structured is not None and structured != value:
        return False
    return (isinstance(value, dict) and value.get('success') is False
            and {'success', 'message', 'data'}.issubset(value)
            and set(value).issubset({'success', 'message', 'data', 'error'})
            and all(value.get(k) is None for k in ('message', 'data', 'error')))


def plugin_disconnected_failure(reply):
    """Принимает только наблюдавшийся отказ plugin с явным разрешением retry."""
    if getattr(reply, 'isError', False):
        return False
    content = getattr(reply, 'content', None)
    if not isinstance(content, (list, tuple)) or len(content) != 1:
        return False
    text = getattr(content[0], 'text', None)
    if not isinstance(text, str):
        return False
    try:
        value = json.loads(text)
    except (ValueError, TypeError):
        return False
    if not isinstance(value, dict) or set(value) != {'success', 'message', 'data', 'error', 'hint'}:
        return False
    structured = getattr(reply, 'structuredContent', None)
    if structured is not None and structured != value:
        return False
    message = value['message']
    return (value['success'] is False and value['data'] is None and value['hint'] == 'retry'
            and isinstance(message, str) and value['error'] == message
            and re.fullmatch(r'Unity plugin session [0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12} disconnected while awaiting command_result', message) is not None)


async def execute_observation(expression, send, unpack, record, save, *, mutation=False,
                              budget=45.0, interval=1.0, clock=time.monotonic, sleep=asyncio.sleep):
    allowed = not mutation and observation_allowed(expression)
    record['observationRetryAllowed'] = allowed
    attempts = record.setdefault('attempts', [])
    deadline = clock() + budget
    while True:
        attempt = {'started': time.time(), 'monotonicStarted': clock()}
        attempts.append(attempt)
        save()
        try:
            # Ограничение бюджета касается только разрешённых наблюдений.
            if allowed:
                remaining = deadline - clock()
                if remaining <= 0:
                    attempt['retryRejectedReason'] = 'observation_budget_expired'
                    raise TimeoutError('Истёк бюджет разрешённого MCP-наблюдения')
                reply = await asyncio.wait_for(send(), remaining)
            else:
                reply = await send()
            attempt['response'] = reply.model_dump(mode='json')
            if empty_failure(reply):
                attempt['emptyFailureEnvelope'] = True
                if not allowed:
                    raise RuntimeError('MCP execute вернул пустой отказ: success=false, message/error/data=null; выражение не допускает повтор')
                remaining = deadline - clock()
                if remaining <= interval:
                    raise TimeoutError('Пустой отказ MCP сохраняется после ограниченного ожидания наблюдения')
                attempt['retryDelaySeconds'] = interval
                attempt['finished'] = time.time()
                attempt['elapsedSeconds'] = clock() - attempt['monotonicStarted']
                save()
                await sleep(interval)
                continue
            if plugin_disconnected_failure(reply):
                attempt['pluginDisconnectedEnvelope'] = True
                if not allowed:
                    attempt['retryRejectedReason'] = 'expression_not_allowed'
                    return unpack(reply)
                remaining = deadline - clock()
                if remaining <= interval:
                    attempt['retryRejectedReason'] = 'observation_budget_expired'
                    raise TimeoutError('Disconnect Unity plugin сохраняется после ограниченного ожидания наблюдения')
                attempt['retryDelaySeconds'] = interval
                attempt['finished'] = time.time()
                attempt['elapsedSeconds'] = clock() - attempt['monotonicStarted']
                save()
                await sleep(interval)
                continue
            return unpack(reply)
        except Exception as error:
            attempt['error'] = type(error).__name__ + ': ' + str(error)
            raise
        finally:
            attempt.setdefault('finished', time.time())
            attempt.setdefault('elapsedSeconds', clock() - attempt['monotonicStarted'])
            save()
