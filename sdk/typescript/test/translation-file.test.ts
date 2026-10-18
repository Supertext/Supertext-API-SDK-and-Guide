// Translation files: robustness beyond the shared vectors.
import { it, expect } from 'vitest'
import { TranslationFiles as T } from '../src/index.js'
const time = (f: () => unknown) => { const t = Date.now(); try { f() } catch {} ; return Date.now() - t }
// Hostile files must not exhaust memory or the stack, or take long.
it('pathological inputs stay fast', () => {
  const head = '<!DOCTYPE html>\n<html><head><meta name="supertext:plugin" content="P"><meta name="supertext:reference" content="r"><meta name="supertext:source-language" content="de"><meta name="supertext:target-language" content="fr"><meta name="supertext:segment" content="999999999 0123abcd k"></head><body>'
  const cases: [string, () => unknown][] = [
    ['huge position', () => T.read(head + '<div data-st-id="999999999">x</div></body></html>')],
    ['unclosed tag with many attrs (export)', () => T.exportXliff([{ plugin: 'P', reference: 'r', sourceLanguage: 'de', targetLanguage: 'fr', segments: [{ key: 'k', html: true, text: '<a' + ' b=c'.repeat(50000) + ' "' }] }])],
    ['unclosed tag with spaces', () => T.exportHtml({ plugin: 'P', reference: 'r', sourceLanguage: 'de', targetLanguage: 'fr', segments: [{ key: 'k', html: true, text: '<a' + ' '.repeat(200000) + 'x' }] })],
    ['meta without end', () => T.read(head.replace('<body>', '<meta ' + 'a="b" '.repeat(100000)))],
    ['deep nesting', () => T.read('<xliff><file original="r" source-language="de" target-language="fr" product-name="P"><body>' + '<group>'.repeat(100000) + '</group>'.repeat(100000) + '</body></file></xliff>')],
    ['deep mrk', () => T.read('<xliff><file original="r" source-language="de" target-language="fr" product-name="P"><body><trans-unit id="k"><source>x</source><target>' + '<mrk>'.repeat(100000) + 'x' + '</mrk>'.repeat(100000) + '</target></trans-unit></body></file></xliff>')],
  ]
  for (const [name, f] of cases) { const ms = time(f); expect(ms).toBeLessThan(3000) }
  const f = T.read(head + '<div data-st-id="999999999">x</div></body></html>')[0]!
  expect(T.importTranslations(f, [{ key: 'k', text: 'y' }])).toEqual([{ key: 'k', status: 'source_changed', text: 'x' }])
})
