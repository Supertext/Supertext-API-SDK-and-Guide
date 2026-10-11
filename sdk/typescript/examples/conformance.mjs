#!/usr/bin/env node
// Conformance program for stand-in/conformance.mjs (see spec/conformance.json):
// reads one request as JSON on stdin, translates it with translateTexts and
// prints {"translations": {...}} or {"error": {"code", "status"}}.
// Needs the built package: `npm run build` first.
//
//   node stand-in/conformance.mjs -- node sdk/typescript/examples/conformance.mjs
import { SupertextClient, SupertextError } from '../dist/index.js'

let input = ''
for await (const chunk of process.stdin) input += chunk
const request = JSON.parse(input)

const client = new SupertextClient(process.env.SUPERTEXT_API_KEY ?? '', {
  baseUrl: process.env.SUPERTEXT_API_URL,
  pollIntervalMs: 100,
  userAgent: 'conformance',
})
// A Map keeps the input order for any key.
const segments = new Map(request.segments.map((s) => [s.key, { text: s.text, html: s.html }]))

let output
try {
  const translations = await client.translateTexts(segments, request.targetLang, {
    sourceLang: request.sourceLang,
    politeness: request.politeness,
  })
  output = { translations: Object.fromEntries(translations) }
} catch (error) {
  if (!(error instanceof SupertextError)) throw error
  output = { error: { code: error.code, status: error.status ?? null } }
}
process.stdout.write(`${JSON.stringify(output)}\n`)
