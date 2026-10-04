// Чистая функция для functions.exec: без Node, файловой системы и сетевых вызовов.
// Полный отчёт сохраняет вызываемый инструмент ДО возврата результата.
function compactUnityResult(response) {
  const maxChars = 6000;
  let truncated = false;
  let visited = 0;
  const seen = new Set();
  function clip(value, limit = 240) {
    if (value.length <= limit) return value;
    truncated = true;
    return value.slice(0, limit) + '…';
  }
  function bound(value, depth = 0) {
    if (typeof value === 'string') return clip(value);
    if (value === null || typeof value !== 'object') {
      return typeof value === 'bigint' ? clip(String(value)) : value;
    }
    if (depth >= 5 || visited++ >= 160 || seen.has(value)) {
      truncated = true;
      return { omitted: true };
    }
    seen.add(value);
    let out;
    if (Array.isArray(value)) {
      if (value.length > 10) truncated = true;
      out = { total: value.length, sample: value.slice(0, 10).map(v => bound(v, depth + 1)) };
    } else {
      out = Object.create(null);
      const keys = Object.keys(value);
      // Сначала статус и путь: широкие диагностические объекты не должны их вытеснять.
      const priority = ['passed', 'failureCount', 'reportPath', 'ready', 'components', 'assets'];
      const ordered = [...priority.filter(k => keys.includes(k)), ...keys.filter(k => !priority.includes(k))];
      if (ordered.length > 20) truncated = true;
      for (const key of ordered.slice(0, 20)) {
        if (key === 'failures' && Array.isArray(value[key])) {
          const failures = value[key];
          out.failureCount = Number.isSafeInteger(value.failureCount) && value.failureCount >= 0
            ? Math.max(value.failureCount, failures.length) : failures.length;
          out.failureEntries = failures.length;
          out.passed = out.failureCount === 0 && (value.passed === undefined || value.passed === true);
          out.failureGroupsScope = out.failureCount > failures.length ? 'sample' : 'complete';
          out.failureSample = failures.slice(0, 10).map(v => bound(v, depth + 1));
          const groups = new Map();
          for (const failure of failures) {
            const category = typeof failure === 'string'
              ? clip(failure.replace(/\s+-?\d+$/, ''), 120) : '[structured failure]';
            groups.set(category, (groups.get(category) || 0) + 1);
          }
          out.failureGroupCount = groups.size;
          out.failureGroups = Array.from(groups, ([reason, count]) => ({ reason, count }))
            .sort((a, b) => b.count - a.count).slice(0, 10);
          if (failures.length > 10 || groups.size > 10) truncated = true;
        } else {
          out[clip(key, 120)] = bound(value[key], depth + 1);
        }
      }
      if (ordered.length > 20) out.omittedFieldCount = ordered.length - 20;
    }
    seen.delete(value);
    return out;
  }
  let body = response && response.structuredContent;
  if (!body && response && Array.isArray(response.content)) {
    const block = response.content.find(v => v.type === 'text' && typeof v.text === 'string');
    if (block) {
      try { body = JSON.parse(block.text); }
      catch (_) { body = { message: block.text }; }
    }
  }
  if (!body) body = response || {};
  const additionalContentBlocks = !response?.structuredContent && Array.isArray(response?.content)
    ? Math.max(0, response.content.length - 1) : 0;
  if (additionalContentBlocks > 0) truncated = true;
  const result = body.data && Object.prototype.hasOwnProperty.call(body.data, 'result')
    ? body.data.result : (body.data !== undefined ? body.data : body);
  const output = {
    isError: !!(response && response.isError),
    success: typeof body.success === 'boolean' ? body.success : undefined,
    message: typeof body.message === 'string' ? clip(body.message) : undefined,
    result: bound(result),
    truncated,
    additionalContentBlocks: additionalContentBlocks || undefined
  };
  if (JSON.stringify(output).length <= maxChars) return output;
  // Последний барьер: валидный JSON вместо слепого среза сериализованного ответа.
  const reduced = Object.create(null);
  for (const key of ['passed', 'failureCount', 'failureGroupCount', 'reportPath', 'ready', 'components', 'assets']) {
    if (output.result && Object.prototype.hasOwnProperty.call(output.result, key)) {
      const value = output.result[key];
      if (value === null || typeof value !== 'object') reduced[key] = value;
    }
  }
  reduced.preview = JSON.stringify(output.result).slice(0, 1200);
  reduced.omitted = true;
  return { isError: output.isError, success: output.success, message: output.message,
    result: reduced, truncated: true, additionalContentBlocks: output.additionalContentBlocks };
}
