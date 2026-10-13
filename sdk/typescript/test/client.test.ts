import { describe, expect, it } from 'vitest'
import { HtmlDocument, SupertextClient, SupertextError, type SupertextClientOptions } from '../src/v1/index.js'

interface Call {
  method: string
  url: string
  headers: Record<string, string>
  /** The multipart body as sent, and as a server parses it. */
  raw?: string
  form?: FormData
}
type Reply = Response | Error | ((call: Call) => Response | Error | Promise<Response>)

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json', ...headers } })
const text = (body: string, status = 200) => new Response(body, { status })

/** A fake fetch answering from a queue, and a fake clock that sleeping advances. */
function harness(replies: Reply[], apiKey = 'secret-key-123', options: SupertextClientOptions = {}) {
  const calls: Call[] = []
  const sleeps: number[] = []
  let clock = 0
  const client = new SupertextClient(apiKey, {
    baseUrl: 'https://api.test/v1',
    fetch: async (url, init) => {
      const call: Call = { method: init.method ?? 'GET', url, headers: init.headers as Record<string, string> }
      if (init.body) {
        call.raw = new TextDecoder().decode(init.body as ArrayBuffer)
        call.form = await new Response(init.body, { headers: { 'Content-Type': call.headers['Content-Type']! } }).formData()
      }
      calls.push(call)
      const next = replies.shift()
      if (!next) throw new Error(`unexpected request ${call.method} ${url}`)
      const reply = typeof next === 'function' ? await next(call) : next
      if (reply instanceof Error) throw reply
      return reply
    },
    sleep: async (ms) => {
      sleeps.push(ms)
      clock += ms
    },
    now: () => clock,
    ...options,
  })
  return { client, calls, sleeps, advance: (ms: number) => (clock += ms) }
}

async function caught(promise: Promise<unknown>): Promise<SupertextError> {
  try {
    await promise
  } catch (e) {
    expect(e).toBeInstanceOf(SupertextError)
    return e as SupertextError
  }
  throw new Error('expected a SupertextError')
}

/** A stand-in answer: the uploaded document with `[x] ` before each segment's text. */
async function translatedEcho(call: Call): Promise<string> {
  const file = call.form!.get('file') as Blob
  return (await file.text()).replace(/(<div data-st-id="\d+">)/g, '$1[x] ')
}

describe('submit', () => {
  it('sends the multipart fields in order, with an exact text/html file part', async () => {
    const { client, calls } = harness([json({ file_id: 'f1' })])
    expect(await client.submit('<p>Hi</p>', 'de-DE', { sourceLang: 'en', politeness: 'more' })).toBe('f1')
    const call = calls[0]!
    expect(call.method).toBe('POST')
    expect(call.url).toBe('https://api.test/v1/translate/ai/file')
    expect([...call.form!.keys()]).toEqual(['target_lang', 'source_lang', 'politeness', 'file'])
    expect(call.form!.get('target_lang')).toBe('de-DE')
    expect(call.form!.get('source_lang')).toBe('en')
    expect(call.form!.get('politeness')).toBe('more')
    const raw = call.raw!
    expect(raw).toContain('Content-Disposition: form-data; name="file"; filename="content.html"\r\nContent-Type: text/html\r\n\r\n<p>Hi</p>')
    expect(raw).not.toMatch(/charset/i)
    const boundary = call.headers['Content-Type']!.split('boundary=')[1]
    expect(raw).toBe(
      [
        `--${boundary}\r\nContent-Disposition: form-data; name="target_lang"\r\n\r\nde-DE\r\n`,
        `--${boundary}\r\nContent-Disposition: form-data; name="source_lang"\r\n\r\nen\r\n`,
        `--${boundary}\r\nContent-Disposition: form-data; name="politeness"\r\n\r\nmore\r\n`,
        `--${boundary}\r\nContent-Disposition: form-data; name="file"; filename="content.html"\r\nContent-Type: text/html\r\n\r\n<p>Hi</p>\r\n`,
        `--${boundary}--\r\n`,
      ].join(''),
    )
  })

  it('sends the document as UTF-8', async () => {
    const { client, calls } = harness([json({ file_id: 'f1' })])
    await client.submit('<p>Käse 日本語 😀</p>', 'de')
    expect(await (calls[0]!.form!.get('file') as Blob).text()).toBe('<p>Käse 日本語 😀</p>')
  })

  it('leaves out an empty source and the default politeness', async () => {
    const { client, calls } = harness([json({ file_id: 'f1' }), json({ file_id: 'f2' })])
    await client.submit('x', 'fr-CH')
    await client.submit('x', 'pt-BR', { sourceLang: '', politeness: 'default' })
    expect([...calls[0]!.form!.keys()]).toEqual(['target_lang', 'file'])
    expect(calls[0]!.form!.get('target_lang')).toBe('fr-CH')
    expect([...calls[1]!.form!.keys()]).toEqual(['target_lang', 'file'])
    expect(calls[1]!.form!.get('target_lang')).toBe('pt-BR')
  })

  it('sends the language codes as given, without mapping (only trimmed)', async () => {
    const { client, calls } = harness([json({ file_id: 'f1' })])
    await client.submit('x', ' de ', { sourceLang: ' en-US ' })
    expect(calls[0]!.form!.get('target_lang')).toBe('de')
    expect(calls[0]!.form!.get('source_lang')).toBe('en-US')
  })

  it('refuses an empty target without a request', async () => {
    const { client, calls } = harness([])
    expect((await caught(client.submit('x', '  '))).code).toBe('unsupported_language')
    expect(calls).toHaveLength(0)
  })

  it('lists the language pairs of GET features', async () => {
    const { client } = harness([
      json({ supported_features: [{ source_lang: 'en', target_lang: 'de-CH', features: ['supports_politeness'] }, { source_lang: 'en' }, 'x'] }),
    ])
    expect(await client.getFeatures()).toEqual([{ sourceLang: 'en', targetLang: 'de-CH', features: ['supports_politeness'] }])
  })

  it('sends the headers; the User-Agent option is appended', async () => {
    const plain = harness([json({ file_id: 'f' }), json({})], 'Supertext-Auth-Key  abc ')
    await plain.client.submit('x', 'de-DE')
    expect(plain.calls[0]!.headers).toEqual({
      Authorization: 'Supertext-Auth-Key abc',
      Accept: 'application/json',
      'User-Agent': 'supertext-sdk-typescript',
      'Content-Type': expect.stringMatching(/^multipart\/form-data; boundary=\S+$/),
    })
    await plain.client.delete('f')
    // Requests without a body carry no Content-Type.
    expect(plain.calls[1]!.headers).toEqual({
      Authorization: 'Supertext-Auth-Key abc',
      Accept: 'application/json',
      'User-Agent': 'supertext-sdk-typescript',
    })
    const named = harness([json({ file_id: 'f' })], 'abc', { userAgent: 'Payload-Supertext-Translation/1.2.0' })
    await named.client.submit('x', 'de-DE')
    expect(named.calls[0]!.headers['User-Agent']).toBe('supertext-sdk-typescript Payload-Supertext-Translation/1.2.0')
  })

  it('no_file_id when the answer has none', async () => {
    for (const reply of [json({}), json({ file_id: '' }), text('not json')]) {
      const { client } = harness([reply])
      expect((await caught(client.submit('x', 'de'))).code).toBe('no_file_id')
    }
  })
})

describe('HTTP handling', () => {
  it('retries a 429 with Retry-After, then succeeds', async () => {
    const { client, calls, sleeps } = harness([json({ error_code: 'RATE_LIMIT_EXCEEDED' }, 429, { 'Retry-After': '3' }), json({ file_id: 'f1' })])
    expect(await client.submit('x', 'de-DE')).toBe('f1')
    expect(calls).toHaveLength(2)
    expect(sleeps).toEqual([3000])
    // The same form is sent again.
    expect(calls[1]!.form!.get('target_lang')).toBe('de-DE')
  })

  it('gives up after maxRetries with too_many_requests, backing off 1/2/4/8 s', async () => {
    const limited = () => json({ error_code: 'RATE_LIMIT_EXCEEDED' }, 429)
    const { client, calls, sleeps } = harness([limited(), limited(), limited(), limited(), limited()])
    const e = await caught(client.validateApiKey())
    expect(e.code).toBe('too_many_requests')
    expect(e.status).toBe(429)
    expect(e.apiErrorCode).toBe('RATE_LIMIT_EXCEEDED')
    expect(calls).toHaveLength(5)
    expect(sleeps).toHaveLength(4)
    ;[1000, 2000, 4000, 8000].forEach((base, i) => {
      expect(sleeps[i]).toBeGreaterThanOrEqual(base)
      expect(sleeps[i]).toBeLessThan(base + 250)
    })
  })

  it('maxRetries 0 does not retry', async () => {
    const { client, calls } = harness([json({}, 429)], 'k', { maxRetries: 0 })
    expect((await caught(client.validateApiKey())).code).toBe('too_many_requests')
    expect(calls).toHaveLength(1)
  })

  it('maps status errors, with the language pair the API reports', async () => {
    const { client } = harness([json({ error_code: 'INVALID_LANGUAGE_PAIR', source_lang: 'en', target_lang: 'xx' }, 400)])
    const e = await caught(client.submit('x', 'xx', { sourceLang: 'en' }))
    expect(e).toMatchObject({ code: 'invalid_language_pair', status: 400, apiErrorCode: 'INVALID_LANGUAGE_PAIR', sourceLang: 'en', targetLang: 'xx' })
    for (const [status, code] of [[401, 'authentication_failure'], [404, 'not_found'], [413, 'payload_too_large'], [503, 'service_unavailable'], [415, 'unexpected_status']] as const) {
      const h = harness([text('', status)])
      expect((await caught(h.client.validateApiKey())).code).toBe(code)
    }
  })

  it('network failures and request timeouts are transport_error with the reason as detail', async () => {
    const refused = new TypeError('fetch failed', { cause: new Error('connect ECONNREFUSED 127.0.0.1:1') })
    const e = await caught(harness([refused]).client.validateApiKey())
    expect(e.code).toBe('transport_error')
    expect(e.status).toBeUndefined()
    expect(e.detail).toBe('fetch failed: connect ECONNREFUSED 127.0.0.1:1')

    // A real fetch that never answers within requestTimeoutMs.
    const slow = new SupertextClient('k', {
      requestTimeoutMs: 20,
      fetch: (_url, init) =>
        new Promise((_resolve, reject) => init.signal!.addEventListener('abort', () => reject(init.signal!.reason))),
    })
    expect((await caught(slow.validateApiKey())).code).toBe('transport_error')
  })

  it('uses GET features to validate the key and URL-encodes file ids', async () => {
    const { client, calls } = harness([json({ features: [] }), json({ status: 'done' }), text('<p>x</p>'), json({})])
    await client.validateApiKey()
    await client.getStatus('a/b c')
    await client.download('a/b c')
    await client.delete('a/b c')
    expect(calls.map((c) => `${c.method} ${c.url}`)).toEqual([
      'GET https://api.test/v1/features',
      'GET https://api.test/v1/translate/ai/file/a%2Fb%20c/status',
      'GET https://api.test/v1/translate/ai/file/a%2Fb%20c/translation',
      'DELETE https://api.test/v1/translate/ai/file/a%2Fb%20c',
    ])
  })
})

describe('missing API key and key secrecy', () => {
  it('a blank key sends nothing and throws missing_api_key', async () => {
    for (const key of ['', '   ', 'Supertext-Auth-Key ']) {
      const { client, calls } = harness([], key)
      expect((await caught(client.translateTexts({ a: 'Hello' }, 'de'))).code).toBe('missing_api_key')
      expect((await caught(client.validateApiKey())).code).toBe('missing_api_key')
      await client.delete('f') // never throws
      expect(calls).toHaveLength(0)
    }
  })

  it('the key never appears in errors or when the client is printed', async () => {
    const key = 'top-secret-key-4711'
    const echo = (call: Call) => text(`<pre>Authorization: ${call.headers.Authorization}</pre>`, 403)
    const { client } = harness([echo, new Error(`proxy said ${key}`)], key)
    for (const e of [await caught(client.validateApiKey()), await caught(client.validateApiKey())]) {
      expect(e.message).not.toContain(key)
      expect(e.detail ?? '').not.toContain(key)
      expect(String(e)).not.toContain(key)
      expect(JSON.stringify(e)).not.toContain(key)
    }
    const { inspect } = await import('node:util')
    expect(inspect(client, { depth: 5 })).not.toContain(key)
    expect(JSON.stringify(client)).not.toContain(key)
  })
})

describe('waitUntilDone', () => {
  it('polls right away, then every pollIntervalMs; unknown statuses keep polling', async () => {
    const { client, calls, sleeps } = harness([json({ status: 'translating' }), json({ status: 'queued' }), json({ status: 'done' })], 'k', { pollIntervalMs: 500 })
    await client.waitUntilDone('f')
    expect(calls).toHaveLength(3)
    expect(sleeps).toEqual([500, 500])
  })

  it('fails on error, limit_exceeded and deleted', async () => {
    for (const [status, code] of [['error', 'translation_error'], ['limit_exceeded', 'quota_exceeded'], ['deleted', 'file_deleted']]) {
      const { client } = harness([json({ status })])
      expect((await caught(client.waitUntilDone('f'))).code).toBe(code)
    }
  })

  it('times out when the next poll would start after timeoutMs', async () => {
    const replies = Array.from({ length: 20 }, () => json({ status: 'translating' }))
    const { client, calls, sleeps } = harness(replies, 'k', { pollIntervalMs: 2000, timeoutMs: 6000 })
    expect((await caught(client.waitUntilDone('f'))).code).toBe('timeout')
    // Polls at 0, 2, 4 and 6 s (6 s is not after the timeout); 8 s would be.
    expect(calls).toHaveLength(4)
    expect(sleeps).toEqual([2000, 2000, 2000])
  })

  it('counts the time the requests take', async () => {
    let h: ReturnType<typeof harness>
    const slowPoll = () => {
      h.advance(4000)
      return json({ status: 'translating' })
    }
    h = harness([slowPoll, slowPoll, slowPoll], 'k', { pollIntervalMs: 1000, timeoutMs: 9000 })
    expect((await caught(h.client.waitUntilDone('f'))).code).toBe('timeout')
    expect(h.calls).toHaveLength(2) // 0→4 s, 5→9 s; the next would start at 10 s
  })
})

describe('download and delete', () => {
  it('an empty translation is incomplete_response', async () => {
    const { client } = harness([text(' \n ')])
    expect((await caught(client.download('f'))).code).toBe('incomplete_response')
  })

  it('delete never throws', async () => {
    const { client } = harness([text('', 500), new Error('down')])
    await expect(client.delete('f')).resolves.toBeUndefined()
    await expect(client.delete('f')).resolves.toBeUndefined()
  })
})

describe('translateHtml', () => {
  it('uploads, polls, downloads and deletes', async () => {
    const { client, calls } = harness([json({ file_id: 'f1' }), json({ status: 'translating' }), json({ status: 'done' }), text('<p>Hallo</p>'), json({})])
    expect(await client.translateHtml('<p>Hello</p>', 'de-CH', { sourceLang: 'en' })).toBe('<p>Hallo</p>')
    expect(calls.map((c) => c.method)).toEqual(['POST', 'GET', 'GET', 'GET', 'DELETE'])
  })

  it('deletes the file after a failed translation, an empty download and a network error; a failing delete is ignored', async () => {
    const cases: [Reply[], string][] = [
      [[json({ file_id: 'f' }), json({ status: 'error' }), text('', 500)], 'translation_error'],
      [[json({ file_id: 'f' }), json({ status: 'done' }), text(''), json({})], 'incomplete_response'],
      [[json({ file_id: 'f' }), new Error('reset'), new Error('still down')], 'transport_error'],
    ]
    for (const [replies, code] of cases) {
      const { client, calls } = harness(replies)
      expect((await caught(client.translateHtml('x', 'de'))).code).toBe(code)
      expect(calls.at(-1)!.method).toBe('DELETE')
    }
  })

  it('nothing to delete when the upload fails', async () => {
    const { client, calls } = harness([text('', 401)])
    expect((await caught(client.translateHtml('x', 'de'))).code).toBe('authentication_failure')
    expect(calls).toHaveLength(1)
  })
})

describe('translateTexts', () => {
  /** Replies for `documents` round trips whose downloads echo the uploads. */
  function echoHarness(documents: number, options: SupertextClientOptions = {}) {
    let lastUpload: Call | undefined
    const replies: Reply[] = []
    for (let i = 0; i < documents; i++) {
      replies.push((call) => ((lastUpload = call), json({ file_id: `f${i}` })))
      replies.push(json({ status: 'done' }))
      replies.push(async () => text(await translatedEcho(lastUpload!)))
      replies.push(json({}))
    }
    return harness(replies, 'k', options)
  }

  it('returns the keys in input order, blank texts unchanged, plain and HTML read back', async () => {
    const { client, calls } = echoHarness(1)
    const result = await client.translateTexts(
      { title: 'Fish & Chips', blank: '  ', body: { text: '<p>A <b>bold</b> start</p>', html: true }, lines: { text: 'Line 1\nLine 2' } },
      'de',
      { sourceLang: 'en', politeness: 'less' },
    )
    expect(Object.keys(result)).toEqual(['title', 'blank', 'body', 'lines'])
    expect(result).toEqual({ title: '[x] Fish & Chips', blank: '  ', body: '[x] <p>A <b>bold</b> start</p>', lines: '[x] Line 1\nLine 2' })
    expect(calls[0]!.form!.get('politeness')).toBe('less')
  })

  it('keeps the order of numeric keys with a Map', async () => {
    const { client } = echoHarness(1)
    const result = await client.translateTexts(new Map([['10', { text: 'ten' }], ['2', { text: 'two' }]]), 'de')
    expect(result).toBeInstanceOf(Map)
    expect([...result]).toEqual([['10', '[x] ten'], ['2', '[x] two']])
  })

  it('splits into documents translated one after the other', async () => {
    const { client, calls } = echoHarness(2)
    const result = await client.translateTexts({ a: '12345', b: '12345', c: '1' }, 'de', { maxCharacters: 10 })
    expect(result).toEqual({ a: '[x] 12345', b: '[x] 12345', c: '[x] 1' })
    expect(calls.map((c) => c.method)).toEqual(['POST', 'GET', 'GET', 'DELETE', 'POST', 'GET', 'GET', 'DELETE'])
    const first = await (calls[0]!.form!.get('file') as Blob).text()
    expect(first).toBe(HtmlDocument.build([{ text: '12345' }, { text: '12345' }]))
  })

  it('sends nothing when every text is blank', async () => {
    const { client, calls } = harness([])
    expect(await client.translateTexts({ a: '', b: { text: ' \n ', html: true } }, 'de')).toEqual({ a: '', b: ' \n ' })
    expect(calls).toHaveLength(0)
  })

  it('missing segments are incomplete_response, after the file was deleted', async () => {
    const answer = '<div data-st-id="1">zwei</div>'
    const { client, calls } = harness([json({ file_id: 'f' }), json({ status: 'done' }), text(answer), json({})])
    const e = await caught(client.translateTexts({ one: 'one', two: 'two', three: 'three' }, 'de'))
    expect(e.code).toBe('incomplete_response')
    expect(e.detail).toBe('missing: one, three')
    expect(calls.at(-1)!.method).toBe('DELETE')
  })
})
