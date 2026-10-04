// Проверки границы вывода: данные остаются в инструменте, в контекст идёт сводка.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../compact-result.js'), 'utf8');
const compact = vm.runInNewContext(source + '\ncompactUnityResult;', {});
let checks = 0;
function check(name, test) {
  test();
  checks++;
  console.log('PASS ' + name);
}
function wrapped(result, success = true) {
  const body = { success, message: 'Code executed successfully.', data: { result } };
  return { content: [{ type: 'text', text: JSON.stringify(body) }], structuredContent: body, isError: false };
}
check('8880 failures: count, sample, grouping, report and no mutation', () => {
  const failures = Array.from({ length: 8880 }, (_, i) => 'native component delta ' + (-24536354 - i));
  const result = { components: 17416, assets: 5, failures, ready: true, reportPath: 'tmp/full.json' };
  const response = wrapped(result);
  const output = compact(response);
  assert.equal(output.success, true);
  assert.equal(output.result.failureCount, 8880);
  assert.equal(output.result.passed, false);
  assert.equal(output.result.failureSample.length, 10);
  assert.equal(output.result.failureGroups[0].count, 8880);
  assert.equal(output.result.reportPath, 'tmp/full.json');
  assert.equal(output.result.components, 17416);
  assert.ok(JSON.stringify(output).length <= 6000);
  assert.equal(failures.length, 8880);
  assert.equal(result.failures, failures);
  assert.ok(!JSON.stringify(output).includes('structuredContent'));
});
check('content-only response and successful empty report', () => {
  const response = wrapped({ failures: [], ready: true });
  delete response.structuredContent;
  const output = compact(response);
  assert.equal(output.result.failureCount, 0);
  assert.equal(output.result.passed, true);
});
check('transport error stays an error', () => {
  const output = compact({ isError: true, content: [{ type: 'text', text: 'Connection failed ' + 'x'.repeat(100000) }] });
  assert.equal(output.isError, true);
  assert.notEqual(output.success, true);
  assert.ok(JSON.stringify(output).length <= 6000);
});
check('large nested arrays, strings and wide objects respect total bound', () => {
  const rows = Array.from({ length: 200 }, () => Object.fromEntries(Array.from({ length: 100 }, (_, i) => ['key' + i, 'x'.repeat(2000)])));
  const output = compact(wrapped({ passed: false, rows, reportPath: 'tmp/wide.json' }));
  assert.equal(output.result.passed, false);
  assert.equal(output.result.reportPath, 'tmp/wide.json');
  assert.equal(output.truncated, true);
  assert.ok(JSON.stringify(output).length <= 6000);
});
check('explicit failure result, scalar result and missing payload', () => {
  assert.equal(compact(wrapped({ passed: false, failures: [] })).result.passed, false);
  assert.equal(compact(wrapped({ passed: true, failures: ['error'] })).result.passed, false);
  assert.equal(compact(wrapped({ passed: 'false', failures: [] })).result.passed, false);
  assert.equal(compact(wrapped(42)).result, 42);
  assert.notEqual(compact(null).success, true);
});
check('cyclic payload cannot cause unbounded traversal', () => {
  const result = { passed: false };
  result.self = result;
  assert.ok(JSON.stringify(compact({ structuredContent: { success: true, data: { result } } })).length <= 6000);
});
check('additional content blocks are explicitly marked as omitted', () => {
  const output = compact({ content: [
    { type: 'text', text: JSON.stringify({ success: true, data: { result: 42 } }) },
    { type: 'text', text: 'additional diagnostics' }
  ] });
  assert.equal(output.result, 42);
  assert.equal(output.additionalContentBlocks, 1);
  assert.equal(output.truncated, true);
});
check('explicit failure total survives an already sampled failures array', () => {
  const output = compact(wrapped({ passed: true, failureCount: 8880, failures: ['sample failure'] }));
  assert.equal(output.result.failureCount, 8880);
  assert.equal(output.result.passed, false);
  assert.equal(compact(wrapped({ failureCount: 8880, failures: [] })).result.failureCount, 8880);
});
if (process.argv[2]) {
  check('actual saved report stays complete while context receives a summary', () => {
    const report = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
    const original = JSON.stringify(report);
    const output = compact(wrapped(report));
    assert.equal(output.result.failureCount, report.failures.length);
    assert.ok(output.result.failureSample.length <= 10);
    assert.ok(JSON.stringify(output).length <= 6000);
    assert.equal(JSON.stringify(report), original);
    console.log(JSON.stringify({ failureCount: report.failures.length, rawReportChars: original.length, summaryChars: JSON.stringify(output).length }));
  });
}
console.log(`${checks} checks passed`);
