// spec/reporting.json and the Commando Center's ping cases (spec/commando/callback-vectors.json).
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import {
  DEFAULT_REPORT_URL,
  FAILURE_BACKOFF_MS,
  InstallationReporter,
  KEY_CHECK_TIMEOUT_MS,
  REPORT_TIMEOUT_MS,
  SupertextClient,
  apiKeyFingerprint,
  pingResponse,
  reporterState,
  reportsEnabled,
  type FetchLike,
  type ReporterState,
} from '../src/v1/index.js'

const spec = (name: string): any => JSON.parse(readFileSync(new URL(`../../../spec/${name}`, import.meta.url), 'utf8'))
const s = spec('reporting.json')

type Answer = { status?: number; body?: string; transportError?: boolean } | null | undefined

/** Fake HTTP: answers GET features and POST report, records which were called. */
function fakeHttp(features: Answer, report: Answer) {
  const requests: string[] = []
  const bodies: unknown[] = []
  const fetch: FetchLike = async (url, init) => {
    const kind = url.endsWith('/features') ? 'features' : 'report'
    requests.push(kind)
    if (kind === 'report') {
      expect(url).toBe('https://center.test/api/v1/report')
      expect(init.method).toBe('POST')
      bodies.push(JSON.parse(String(init.body)))
    }
    const answer = kind === 'features' ? features : report
    if (!answer) throw new Error(`unexpected ${kind} request`)
    if (answer.transportError) throw new TypeError('fetch failed')
    return new Response(answer.status === 204 ? null : (answer.body ?? ''), { status: answer.status ?? 200 })
  }
  return { fetch, requests, bodies }
}

describe('spec/reporting.json', () => {
  it('has the same defaults', () => {
    expect(DEFAULT_REPORT_URL).toBe(s.defaults.reportUrl)
    expect(REPORT_TIMEOUT_MS).toBe(s.defaults.reportTimeoutSeconds * 1000)
    expect(KEY_CHECK_TIMEOUT_MS).toBe(s.defaults.keyCheckTimeoutSeconds * 1000)
    expect(FAILURE_BACKOFF_MS).toBe(s.defaults.failureBackoffSeconds * 1000)
  })

  for (const c of s.fingerprint) it(`fingerprint ${JSON.stringify(c.apiKey)}`, () => expect(apiKeyFingerprint(c.apiKey)).toBe(c.out))

  for (const c of s.apiKeyCheck) {
    it(`apiKeyCheck ${JSON.stringify(c.answer)}`, async () => {
      const http = fakeHttp(c.answer, null)
      const client = new SupertextClient(c.apiKey, { baseUrl: 'https://api.test/v1/', fetch: http.fetch, sleep: async () => {}, now: () => Date.parse('2026-10-13T08:00:00.500Z') })
      const check = await client.checkApiKey()
      expect(check).toEqual({ status: c.status, httpStatus: c.httpStatus, errorCode: c.errorCode, checkedAt: '2026-10-13T08:00:00Z', fingerprint: apiKeyFingerprint(c.apiKey) })
      expect(http.requests.length).toBe(c.requests ?? 1)
    })
  }

  for (const c of s.enabled) it(`enabled ${JSON.stringify(c)}`, () => expect(reportsEnabled(c.setting, c.env)).toBe(c.out))

  for (const c of s.ping.cases) {
    it(`ping ${JSON.stringify(c.challenge)} enabled=${c.enabled}`, () => {
      const r = pingResponse(c.challenge, { plugin: s.ping.plugin, enabled: c.enabled })
      expect(r.status).toBe(c.status)
      expect(JSON.parse(r.body)).toEqual(c.body)
      expect(r.headers).toEqual({ 'Content-Type': 'application/json', 'Cache-Control': 'no-store' })
    })
  }

  for (const scenario of s.scenarios) {
    it(scenario.name, async () => {
      const config = { ...s.config, ...scenario.config }
      let state: ReporterState = reporterState(JSON.parse(JSON.stringify(scenario.state)))
      for (const step of scenario.steps) {
        const http = fakeHttp(step.features, step.report)
        const reporter = new InstallationReporter({
          ...config,
          reportUrl: 'https://center.test/api/v1/report',
          fetch: http.fetch,
          clientOptions: { baseUrl: 'https://api.test/v1/', fetch: http.fetch, sleep: async () => {} },
          now: () => Date.parse(step.now),
        })
        const r =
          step.action === 'afterSettingsSaved'
            ? await reporter.afterSettingsSaved(state, step.apiKey)
            : step.action === 'onRequest'
              ? await reporter.onRequest(state, step.apiKey)
              : step.action === 'authenticationFailed'
                ? await reporter.authenticationFailed(state, step.apiKey, step.httpStatus)
                : step.action === 'testConnection'
                  ? await reporter.testConnection(state, step.apiKey)
                  : await reporter.reportCheck(state)
        expect(http.requests, step.action).toEqual(step.expect.requests)
        expect(r.event).toBe(step.expect.event)
        expect(r.sent).toBe(step.expect.sent)
        expect(r.check).toEqual(step.expect.check)
        expect(r.state).toEqual(step.expect.state)
        if (step.expect.reportBody) expect(http.bodies[0]).toEqual(step.expect.reportBody)
        // The stored form survives a JSON round trip.
        state = reporterState(JSON.stringify(r.state))
        expect(state).toEqual(step.expect.state)
      }
    })
  }
})

describe('spec/commando', () => {
  const callback = spec('commando/callback-vectors.json')
  for (const c of callback.endpoint.cases) {
    it(`ping endpoint ${JSON.stringify(c.challenge)}`, () => {
      const r = pingResponse(c.challenge, { plugin: 'Some-Supertext-Translation', enabled: true })
      expect(r.status).toBe(c.status)
      const expected = c.body.plugin ? { ...c.body, plugin: 'Some-Supertext-Translation' } : c.body
      expect(JSON.parse(r.body)).toEqual(expected)
    })
  }

  it('builds reports with the fields of the center schema', () => {
    const schema = spec('commando/report-v1.schema.json')
    const reporter = new InstallationReporter({ ...s.config, callbackUrl: s.config.callbackUrl })
    const body = reporter.buildReport('settings_saved', { reportedVersions: null, reportedKeyStatus: null, lastFailureAt: null, keyCheck: null })
    for (const field of schema.required) expect(body).toHaveProperty(field)
    for (const field of Object.keys(body)) expect(schema.properties).toHaveProperty(field)
  })
})

describe('reporter', () => {
  it('never sends the key', async () => {
    const http = fakeHttp({ status: 200, body: '{}' }, { status: 202 })
    const reporter = new InstallationReporter({ ...s.config, reportUrl: 'https://center.test/api/v1/report', fetch: http.fetch, clientOptions: { baseUrl: 'https://api.test/v1/', fetch: http.fetch } })
    await reporter.afterSettingsSaved(null, 'secret-key-123')
    expect(JSON.stringify(http.bodies)).not.toContain('secret-key-123')
  })

  it('treats a broken stored state as empty', () => {
    expect(reporterState('not json')).toEqual({ reportedVersions: null, reportedKeyStatus: null, lastFailureAt: null, keyCheck: null })
    expect(reporterState(null).keyCheck).toBeNull()
  })

  it('detects the runtime', () => {
    const reporter = new InstallationReporter({ ...s.config, runtime: undefined })
    expect(reporter.buildReport('check', reporterState(null)).runtime).toEqual({ name: 'Node.js', version: process.versions.node })
  })
})
