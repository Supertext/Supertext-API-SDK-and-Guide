// spec/api-key-check.json: the API key check Supertext Plugin Health reports.
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { KEY_CHECK_TIMEOUT_MS, SupertextClient, apiKeyFingerprint, type FetchLike } from '../src/v1/index.js'

const s = JSON.parse(readFileSync(new URL('../../../spec/api-key-check.json', import.meta.url), 'utf8'))

type Answer = { status?: number; body?: string; transportError?: boolean } | null | undefined

/** Fake HTTP: answers GET features, counts the requests. */
function fakeHttp(features: Answer) {
  const requests: string[] = []
  const fetch: FetchLike = async (url) => {
    requests.push(url)
    if (!features) throw new Error(`unexpected request ${url}`)
    if (features.transportError) throw new TypeError('fetch failed')
    return new Response(features.status === 204 ? null : (features.body ?? ''), { status: features.status ?? 200 })
  }
  return { fetch, requests }
}

describe('spec/api-key-check.json', () => {
  it('has the same timeout', () => expect(KEY_CHECK_TIMEOUT_MS).toBe(s.defaults.keyCheckTimeoutSeconds * 1000))

  for (const c of s.fingerprint) it(`fingerprint ${JSON.stringify(c.apiKey)}`, () => expect(apiKeyFingerprint(c.apiKey)).toBe(c.out))

  for (const c of s.apiKeyCheck) {
    it(`apiKeyCheck ${JSON.stringify(c.answer)}`, async () => {
      const http = fakeHttp(c.answer)
      const client = new SupertextClient(c.apiKey, { baseUrl: 'https://api.test/v1/', fetch: http.fetch, sleep: async () => {}, now: () => Date.parse('2026-10-13T08:00:00.500Z') })
      const check = await client.checkApiKey()
      expect(check).toEqual({ status: c.status, httpStatus: c.httpStatus, errorCode: c.errorCode, checkedAt: '2026-10-13T08:00:00Z', fingerprint: apiKeyFingerprint(c.apiKey) })
      expect(http.requests.length).toBe(c.requests ?? 1)
    })
  }
})
