import { readFile } from 'node:fs/promises'
import assert from 'node:assert/strict'
import { test } from 'node:test'

const template = await readFile(new URL('../../assets/claude-bridge/register.js.template', import.meta.url), 'utf8')
let sequence = 0
async function harness(response = { ok: true, status: 200, headers: {}, text: '{"five_hour":{"utilization":17},"seven_day":{"utilization":64}}' }, authorization = { kind: 'bearer', handle: 'opaque-fixture' }) {
  // A fresh module models one plugin instance. It uses only an in-memory SDK double.
  const source = template.replace('__USAGE_OUTPUT_JSON__', JSON.stringify('fixture-only/usage.json'))
  const mod = await import('data:text/javascript;base64,' + Buffer.from(source + `\n// test-instance-${sequence++}`).toString('base64'))
  const handlers = new Map(), timers = []
  let now = 10000, calls = 0, written, request
  const on = (event, filter, callback) => handlers.set(event, callback ?? filter)
  const sdk = {
    clock: { now: async () => now, every: (interval, fn) => timers.push({ interval, fn }) },
    session: { authorize: async () => authorization },
    command: { register: async () => {} },
    http: { fetch: async (url, init) => { calls++; request = { url, init }; return response } },
    fs: { write: async (path, text) => { assert.equal(path, 'fixture-only/usage.json'); written = JSON.parse(text) } },
  }
  mod.register(on)
  return { mod, timers, get calls() { return calls }, get written() { return written }, get request() { return request },
    run: () => handlers.get('command.run')(sdk),
    start: () => handlers.get('session.start')(sdk, {}, async e => e),
    advance: async ms => { now += ms; for (const timer of timers) await timer.fn() },
  }
}

test('exports only normalized quota fields using an opaque engine-held handle', async () => {
  const h = await harness(); await h.run()
  assert.equal(h.request.url, 'https://api.anthropic.com/api/oauth/usage')
  assert.equal(h.request.init.auth, 'opaque-fixture')
  assert.deepEqual(h.written.windows.map(w => w.percentUsed), [17, 64])
  assert.ok(!JSON.stringify(h.written).includes('opaque-fixture'))
})
test('zero is real; invalid and unrelated windows are excluded', async () => {
  const h = await harness()
  assert.deepEqual(h.mod.parseUsage({ limits: [
    { kind: 'five_hour', percent: 0 }, { kind: 'seven_day', percent: true }, { kind: 'spend', percent: 20 },
  ] }), [{ kind: 'five_hour', percentUsed: 0, resetsAt: null }])
})
test('no engine login means no network request and no fabricated zero', async () => {
  const h = await harness(undefined, null); await h.run()
  assert.equal(h.calls, 0); assert.deepEqual(h.written.windows, [])
  assert.equal(h.written.sampledAt, null)
})
test('authentication failure never exports response bodies', async () => {
  const h = await harness({ ok: false, status: 401, headers: {}, text: 'sensitive-fixture-response' })
  await h.run(); assert.deepEqual(h.written.windows, [])
  assert.ok(!JSON.stringify(h.written).includes('sensitive-fixture-response'))
  await h.advance(120000); await h.run(); assert.equal(h.calls, 1)
})
test('one local timer refreshes every two minutes without model calls', async () => {
  const h = await harness(); await h.start()
  assert.equal(h.calls, 1); assert.equal(h.timers.length, 1); assert.equal(h.timers[0].interval, 120000)
  await h.advance(119999); assert.equal(h.calls, 1)
  await h.advance(1); assert.equal(h.calls, 2)
})
test('HTTP 429 honors retry-after', async () => {
  const h = await harness({ ok: false, status: 429, headers: { 'retry-after': '360' }, text: '' })
  await h.start(); await h.advance(120000); assert.equal(h.calls, 1)
  await h.advance(240000); assert.equal(h.calls, 2)
})
