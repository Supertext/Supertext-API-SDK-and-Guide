/**
 * Version reporting to the Supertext Commando Center, the automatic API key check
 * and the callback ping (spec/reporting.json; protocol: Commando-Center-Supertext-Translation
 * → docs/DEVELOPER.md → Report protocol).
 *
 * The reporter is stateless: the plugin loads its `ReporterState` (a plain JSON
 * object), passes it in, and stores the state it gets back.
 */
import { apiKeyFingerprint, isoSeconds, type ApiKeyCheck } from './api-key-check.js'
import { SupertextClient, type FetchLike, type SupertextClientOptions } from './client.js'
import { SDK_VERSION } from '../version.js'

export const DEFAULT_REPORT_URL = 'https://commandocenter-production.up.railway.app/api/v1/report'
export const REPORT_TIMEOUT_MS = 3_000
export const FAILURE_BACKOFF_MS = 3_600_000

export type ReportEvent = 'installed' | 'settings_saved' | 'upgraded' | 'api_key_check' | 'check'

/** What the plugin stores between requests (as JSON, unchanged). */
export interface ReporterState {
  /** `<plugin>|<system>|<runtime>` versions last reported successfully. */
  reportedVersions: string | null
  /** The key status last reported successfully. */
  reportedKeyStatus: string | null
  /** When the last report failed (no new attempt for an hour). */
  lastFailureAt: string | null
  /** The latest key check. */
  keyCheck: ApiKeyCheck | null
}

export interface NameVersion {
  name: string
  version?: string | null
}

export interface ReporterOptions {
  /** The "Send version information to Supertext" setting combined with the environment: see `reportsEnabled()`. */
  enabled: boolean
  /** Base URL of the installation, as the system knows it. */
  siteUrl: string
  /** `name` = the plugin's GitHub repository name; `version` from its official version source. */
  plugin: { name: string; version: string }
  /** The CMS, PIM, shop or application, in its own spelling. */
  system: NameVersion
  /** Default: Node.js (or Deno/Bun) and its version. */
  runtime?: NameVersion
  /** Default: this SDK's version. */
  sdkVersion?: string
  /** The plugin's public ping endpoint; null for hosted apps without one. */
  callbackUrl?: string | null
  /** Default: `SUPERTEXT_REPORT_URL` from the environment, else the live center. */
  reportUrl?: string
  /** For the key check (base URL, fetch, sleep for tests). */
  clientOptions?: SupertextClientOptions
  /** HTTP for the report (tests). Default: the global `fetch`. */
  fetch?: FetchLike
  /** Clock in milliseconds (tests). */
  now?: () => number
}

export interface ReporterResult {
  /** Store this. */
  state: ReporterState
  /** The latest key check (show it on the settings or status page). */
  check: ApiKeyCheck | null
  /** The event that was reported (or tried); null when nothing was due. */
  event: ReportEvent | null
  /** Whether the center accepted the report. */
  sent: boolean
}

export const EMPTY_REPORTER_STATE: ReporterState = Object.freeze({ reportedVersions: null, reportedKeyStatus: null, lastFailureAt: null, keyCheck: null })

/** A stored state (parsed JSON, or anything) as a `ReporterState`; unknown or broken values become empty. */
export function reporterState(value: unknown): ReporterState {
  if (typeof value === 'string') {
    try {
      value = JSON.parse(value)
    } catch {
      value = null
    }
  }
  const o = value && typeof value === 'object' ? (value as Record<string, unknown>) : {}
  const s = (v: unknown) => (typeof v === 'string' ? v : null)
  const k = o.keyCheck && typeof o.keyCheck === 'object' ? (o.keyCheck as Record<string, unknown>) : null
  const keyCheck: ApiKeyCheck | null =
    k && typeof k.status === 'string' && typeof k.checkedAt === 'string'
      ? {
          status: k.status as ApiKeyCheck['status'],
          checkedAt: k.checkedAt,
          httpStatus: typeof k.httpStatus === 'number' ? k.httpStatus : null,
          errorCode: s(k.errorCode),
          fingerprint: s(k.fingerprint) ?? '',
        }
      : null
  return { reportedVersions: s(o.reportedVersions), reportedKeyStatus: s(o.reportedKeyStatus), lastFailureAt: s(o.lastFailureAt), keyCheck }
}

/**
 * Whether version reports are on: `SUPERTEXT_VERSION_REPORTS` (0/false/off/no, 1/true/on/yes)
 * wins over the setting; the setting defaults to on.
 */
export function reportsEnabled(setting?: boolean | null, env: string | null | undefined = processEnv('SUPERTEXT_VERSION_REPORTS')): boolean {
  const value = (env ?? '').trim().toLowerCase()
  if (['0', 'false', 'off', 'no'].includes(value)) return false
  if (['1', 'true', 'on', 'yes'].includes(value)) return true
  return setting ?? true
}

const CHALLENGE = /^[A-Za-z0-9_-]{16,64}$/

export interface PingResponse {
  status: 200 | 400 | 404
  headers: Record<string, string>
  body: string
}

/**
 * The answer of the plugin's public ping endpoint (`GET <callback>?challenge=<c>`).
 * Only while reporting is on (else 404). Nothing a caller sends is reflected unless it matches the pattern.
 */
export function pingResponse(challenge: string | null | undefined, options: { plugin: string; enabled: boolean }): PingResponse {
  const headers = { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }
  if (!options.enabled) return { status: 404, headers, body: '{"ok":false}' }
  if (typeof challenge !== 'string' || !CHALLENGE.test(challenge)) return { status: 400, headers, body: '{"ok":false}' }
  return { status: 200, headers, body: JSON.stringify({ ok: true, plugin: options.plugin, challenge }) }
}

export class InstallationReporter {
  readonly #options: ReporterOptions
  readonly #now: () => number
  readonly #fetch: FetchLike
  readonly reportUrl: string

  constructor(options: ReporterOptions) {
    this.#options = options
    this.#now = options.now ?? Date.now
    this.#fetch = options.fetch ?? ((input, init) => globalThis.fetch(input, init))
    this.reportUrl = options.reportUrl || processEnv('SUPERTEXT_REPORT_URL') || DEFAULT_REPORT_URL
  }

  /** Whether reports are sent (the ping endpoint answers only then). */
  get enabled(): boolean {
    return this.#options.enabled
  }

  /** `<plugin>|<system>|<runtime>` versions. */
  get versions(): string {
    return [this.#options.plugin.version, this.#options.system.version ?? '', this.#runtime().version ?? ''].join('|')
  }

  /** After the plugin's settings were saved: checks the key, reports `installed` or `settings_saved`. */
  async afterSettingsSaved(state: ReporterState | null, apiKey: string): Promise<ReporterResult> {
    const current = { ...(state ?? EMPTY_REPORTER_STATE), keyCheck: await this.#check(apiKey) }
    return this.#reportIfDue(current, current.reportedVersions === null ? 'installed' : 'settings_saved')
  }

  /** On back-end requests that run the plugin's code: checks a changed key, reports what changed. */
  async onRequest(state: ReporterState | null, apiKey: string): Promise<ReporterResult> {
    let current = state ?? EMPTY_REPORTER_STATE
    if (current.keyCheck === null || current.keyCheck.fingerprint !== apiKeyFingerprint(apiKey)) current = { ...current, keyCheck: await this.#check(apiKey) }
    return this.#reportIfDue(current, this.#dueEvent(current))
  }

  /** A translation was rejected with 401/403: the key is invalid (no request to Supertext). */
  async authenticationFailed(state: ReporterState | null, apiKey: string, httpStatus = 401): Promise<ReporterResult> {
    const keyCheck: ApiKeyCheck = { status: 'invalid', checkedAt: isoSeconds(this.#now()), httpStatus, errorCode: null, fingerprint: apiKeyFingerprint(apiKey) }
    const current = { ...(state ?? EMPTY_REPORTER_STATE), keyCheck }
    return this.#reportIfDue(current, this.#dueEvent(current))
  }

  /** The "Test connection" button: checks the key, reports a change. */
  async testConnection(state: ReporterState | null, apiKey: string): Promise<ReporterResult> {
    const current = { ...(state ?? EMPTY_REPORTER_STATE), keyCheck: await this.#check(apiKey) }
    return this.#reportIfDue(current, this.#dueEvent(current))
  }

  /** Sends a `check` report now (commands, tests), even after a recent failure. */
  async reportCheck(state: ReporterState | null): Promise<ReporterResult> {
    return this.#send(state ?? EMPTY_REPORTER_STATE, 'check')
  }

  /** The report body for an event and state. */
  buildReport(event: ReportEvent, state: ReporterState): Record<string, unknown> {
    const o = this.#options
    const runtime = this.#runtime()
    const body: Record<string, unknown> = {
      schema: 1,
      event,
      site_url: o.siteUrl,
      plugin: { name: o.plugin.name, version: o.plugin.version },
      system: nameVersion(o.system),
      runtime: nameVersion(runtime),
      sdk_version: o.sdkVersion ?? SDK_VERSION,
    }
    const check = state.keyCheck
    if (check) {
      body.api_key_configured = check.fingerprint !== ''
      const c: Record<string, unknown> = { status: check.status, checked_at: check.checkedAt }
      if (check.httpStatus !== null) c.http_status = check.httpStatus
      if (check.errorCode !== null) c.error_code = check.errorCode
      body.api_key_check = c
    }
    if (o.callbackUrl) body.callback = { url: o.callbackUrl }
    return body
  }

  #runtime(): NameVersion {
    return this.#options.runtime ?? detectRuntime()
  }

  #dueEvent(state: ReporterState): ReportEvent | null {
    if (state.reportedVersions === null) return 'installed'
    if (state.reportedVersions !== this.versions) return 'upgraded'
    if (state.keyCheck !== null && state.keyCheck.status !== state.reportedKeyStatus) return 'api_key_check'
    return null
  }

  async #check(apiKey: string): Promise<ApiKeyCheck> {
    return new SupertextClient(apiKey, { ...this.#options.clientOptions, now: this.#now }).checkApiKey()
  }

  async #reportIfDue(state: ReporterState, event: ReportEvent | null): Promise<ReporterResult> {
    const idle = { state, check: state.keyCheck, event: null, sent: false }
    if (event === null || !this.#options.enabled) return idle
    if (state.lastFailureAt !== null) {
      const failed = Date.parse(state.lastFailureAt)
      if (!Number.isNaN(failed) && this.#now() - failed < FAILURE_BACKOFF_MS) return idle
    }
    return this.#send(state, event)
  }

  async #send(state: ReporterState, event: ReportEvent): Promise<ReporterResult> {
    if (!this.#options.enabled) return { state, check: state.keyCheck, event: null, sent: false }
    let ok = false
    try {
      const response = await this.#fetch(this.reportUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json', 'User-Agent': `supertext-sdk-typescript ${this.#options.plugin.name}/${this.#options.plugin.version}` },
        body: JSON.stringify(this.buildReport(event, state)),
        signal: AbortSignal.timeout(REPORT_TIMEOUT_MS),
      })
      ok = response.status >= 200 && response.status < 300
      await response.body?.cancel().catch(() => undefined)
    } catch {
      ok = false // never fails the caller: the center is optional
    }
    const next: ReporterState = ok
      ? { ...state, reportedVersions: this.versions, reportedKeyStatus: state.keyCheck?.status ?? state.reportedKeyStatus, lastFailureAt: null }
      : { ...state, lastFailureAt: isoSeconds(this.#now()) }
    return { state: next, check: next.keyCheck, event, sent: ok }
  }
}

function nameVersion(value: NameVersion): Record<string, string> {
  return value.version ? { name: value.name, version: value.version } : { name: value.name }
}

function detectRuntime(): NameVersion {
  const g = globalThis as { Deno?: { version?: { deno?: string } }; Bun?: { version?: string }; process?: { versions?: { node?: string } } }
  if (g.Deno?.version?.deno) return { name: 'Deno', version: g.Deno.version.deno }
  if (g.Bun?.version) return { name: 'Bun', version: g.Bun.version }
  return { name: 'Node.js', version: g.process?.versions?.node ?? null }
}

function processEnv(name: string): string | undefined {
  return (globalThis as { process?: { env?: Record<string, string | undefined> } }).process?.env?.[name]
}
