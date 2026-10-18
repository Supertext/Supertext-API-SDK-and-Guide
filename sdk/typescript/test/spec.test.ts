// The shared test vectors in spec/ (same files for every Supertext SDK).
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import {
  DEFAULT_MAX_CHARACTERS,
  HtmlDocument,
  Languages,
  SupertextError,
  type SupertextErrorCode,
  authHeader,
  errorForFileStatus,
  errorForStatus,
  planBatches,
  retryDelayMs,
} from '../src/v1/index.js'
import { TranslationFileError, TranslationFiles } from '../src/index.js'

const spec = (name: string): any => JSON.parse(readFileSync(new URL(`../../../spec/${name}`, import.meta.url), 'utf8'))

describe('spec/languages.json', () => {
  const s = spec('languages.json')
  it('has the same source languages', () => expect(Languages.SOURCES).toEqual(s.sources))
  it('has the same target languages', () => expect(Languages.TARGETS).toEqual(s.targets))
  for (const c of s.matchTarget) it(`matchTarget ${JSON.stringify(c.in)}`, () => expect(Languages.matchTarget(c.in)).toBe(c.out))
  it('has the same default targets', () => expect(Languages.DEFAULT_TARGETS).toEqual(s.defaultTargets))
  for (const c of s.defaultTarget) it(`defaultTarget ${JSON.stringify(c.in)}`, () => expect(Languages.defaultTarget(c.in)).toBe(c.out))
  for (const c of s.suggestTarget) it(`suggestTarget ${JSON.stringify(c.in)}`, () => expect(Languages.suggestTarget(c.in)).toBe(c.out))
  it('targetOptions are the targets sorted by name', () => {
    expect(Languages.targetOptions().map((t) => t.code)).toEqual(s.targetOptions)
    for (const t of Languages.targetOptions()) expect(t).toEqual(s.targets.find((x: any) => x.code === t.code))
  })
  it('no prototype keys', () => {
    for (const code of ['constructor', 'zh-constructor', '__proto__', 'tostring']) expect(Languages.defaultTarget(code)).toBeNull()
  })
  for (const c of s.matchSource) it(`matchSource ${JSON.stringify(c.in)}`, () => expect(Languages.matchSource(c.in)).toBe(c.out))
  const resolve = (fn: (code: string, mapping?: Record<string, string>) => string, c: any) => {
    if (c.error) {
      try {
        fn(c.in, c.mapping)
        expect.unreachable()
      } catch (e) {
        expect(e).toBeInstanceOf(SupertextError)
        expect((e as SupertextError).code).toBe(c.error)
      }
    } else expect(fn(c.in, c.mapping)).toBe(c.out)
  }
  for (const c of s.resolveTarget) it(`target ${JSON.stringify(c.in)} ${JSON.stringify(c.mapping ?? {})}`, () => resolve(Languages.target, c))
  for (const c of s.resolveSource) it(`source ${JSON.stringify(c.in)} ${JSON.stringify(c.mapping ?? {})}`, () => resolve(Languages.source, c))
  for (const c of s.supportsPoliteness) it(`supportsPoliteness ${JSON.stringify(c.in)}`, () => expect(Languages.supportsPoliteness(c.in)).toBe(c.out))
})

describe('spec/auth-header.json', () => {
  for (const c of spec('auth-header.json').cases) {
    it(JSON.stringify(c.in), () => {
      if (c.error) {
        expect(() => authHeader(c.in)).toThrow(SupertextError)
        try {
          authHeader(c.in)
        } catch (e) {
          expect((e as SupertextError).code).toBe(c.error)
        }
      } else {
        expect(authHeader(c.in)).toBe(c.out)
      }
    })
  }
})

describe('spec/retry-delay.json', () => {
  const s = spec('retry-delay.json')
  for (const c of s.cases) {
    it(`attempt ${c.attempt}, Retry-After ${JSON.stringify(c.retryAfter)}`, () => {
      // The jitter's extremes, and Math.random itself.
      for (const random of [() => 0, () => 0.999999, Math.random]) {
        const ms = retryDelayMs(c.attempt, c.retryAfter, random)
        expect(ms).toBeGreaterThanOrEqual(c.min)
        expect(ms).toBeLessThanOrEqual(c.max)
      }
    })
  }
})

describe('spec/errors.json', () => {
  const s = spec('errors.json')
  for (const c of s.http) {
    it(`HTTP ${c.status} ${c.body}`, () => {
      const e = errorForStatus(c.status, c.body)
      expect(e.code).toBe(c.code)
      expect(e.status).toBe(c.status)
      // null in the vectors = not set.
      expect(e.detail).toBe(c.detail ?? undefined)
      expect(e.apiErrorCode).toBe(c.apiErrorCode ?? undefined)
      expect(e.sourceLang).toBe(c.sourceLang ?? undefined)
      expect(e.targetLang).toBe(c.targetLang ?? undefined)
      expect(e.feature).toBe(c.feature ?? undefined)
      expect(e.message).toMatch(/\S/)
    })
  }
  for (const c of s.fileStatus) {
    it(`file status ${c.status}`, () => expect(errorForFileStatus(c.status)?.code ?? null).toBe(c.code))
  }
  it('detail is cut to 200 characters, tags removed', () => {
    const e = errorForStatus(500, `<p>${'x'.repeat(300)}</p>`)
    expect(e.detail).toBe('x'.repeat(200))
  })
  it('every code has its own English message', () => {
    const messages = Object.keys(s.codes).map((code) => new SupertextError(code as SupertextErrorCode).message)
    expect(new Set(messages).size).toBe(messages.length)
  })
})

describe('spec/html-document.json', () => {
  const s = spec('html-document.json')
  for (const c of s.build) it(`build: ${c.name}`, () => expect(HtmlDocument.build(c.segments)).toBe(c.html))
  for (const c of s.parse) it(`parse: ${c.name}`, () => expect(HtmlDocument.parse(c.html, c.segments)).toEqual(c.out))
  it('round trip of plain text with line breaks and special characters', () => {
    const segments = [{ text: "Fish & Chips\n<today> \"fresh\" it's 😀", html: false }]
    expect(HtmlDocument.parse(HtmlDocument.build(segments), segments)).toEqual([segments[0]!.text])
  })
})

describe('spec/batching.json', () => {
  const s = spec('batching.json')
  it('default limit', () => expect(DEFAULT_MAX_CHARACTERS).toBe(s.defaultMaxCharacters))
  for (const c of s.cases) {
    it(c.name, () => {
      const batches = planBatches(c.segments, c.maxCharacters).map((b) => b.map((seg: { key: string }) => seg.key))
      expect(batches).toEqual(c.batches)
    })
  }
  it('counts code points, not UTF-16 units', () => {
    const segments = [
      { key: 'a', text: '😀😀', html: false },
      { key: 'b', text: '😀😀', html: false },
    ]
    expect(planBatches(segments, 4).length).toBe(1)
  })
})

describe('spec/translation-file.json', () => {
  const s = spec('translation-file.json')
  const code = (fn: () => unknown): string | null => {
    try {
      fn()
      return null
    } catch (e) {
      expect(e).toBeInstanceOf(TranslationFileError)
      return (e as TranslationFileError).code
    }
  }
  for (const c of s.checksum) it(`checksum ${JSON.stringify(c.segment)}`, () => expect(TranslationFiles.checksum(c.segment)).toBe(c.out))
  for (const c of s.exportXliff)
    it(`exportXliff: ${c.name}`, () => (c.error ? expect(code(() => TranslationFiles.exportXliff(c.jobs))).toBe(c.error) : expect(TranslationFiles.exportXliff(c.jobs)).toBe(c.out)))
  for (const c of s.exportHtml)
    it(`exportHtml: ${c.name}`, () => (c.error ? expect(code(() => TranslationFiles.exportHtml(c.job))).toBe(c.error) : expect(TranslationFiles.exportHtml(c.job)).toBe(c.out)))
  for (const c of s.read)
    it(`read: ${c.name}`, () => {
      const content: string = c.repeat ? c.repeat.text.repeat(c.repeat.times) : c.content
      if (c.error) return expect(code(() => TranslationFiles.read(content))).toBe(c.error)
      expect(TranslationFiles.read(content).map((f) => ({ ...f }))).toEqual(c.files)
    })
  for (const c of s.import)
    it(`import: ${c.name}`, () => expect(TranslationFiles.importTranslations(TranslationFiles.read(c.content)[c.file ?? 0]!, c.current)).toEqual(c.out))
  it('a file not returned by read() is refused', () => {
    expect(code(() => TranslationFiles.importTranslations({ format: 'html', plugin: 'P', reference: 'r', sourceLanguage: 'de', targetLanguage: 'fr', units: [] }, []))).toBe('invalid_file')
  })
})
