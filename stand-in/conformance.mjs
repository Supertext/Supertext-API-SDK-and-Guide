#!/usr/bin/env node
/**
 * Runs an SDK's conformance program against the stand-in for every scenario in
 * spec/conformance.json.
 *
 *   node stand-in/conformance.mjs -- <command> [args…]
 *
 * The command is started once per scenario with SUPERTEXT_API_URL and
 * SUPERTEXT_API_KEY set and the scenario's request as JSON on stdin. It must
 * print the result as JSON on stdout (see spec/conformance.json).
 */
import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { isDeepStrictEqual } from 'node:util';
import { createStandIn } from './stand-in.mjs';

const separator = process.argv.indexOf('--');
const command = separator >= 0 ? process.argv.slice(separator + 1) : [];
if (command.length === 0) {
  console.error('usage: node stand-in/conformance.mjs -- <command> [args…]');
  process.exit(2);
}
const spec = JSON.parse(readFileSync(new URL('../spec/conformance.json', import.meta.url), 'utf8'));

const run = (env, input) =>
  new Promise((resolve, reject) => {
    const child = spawn(command[0], command.slice(1), { env: { ...process.env, ...env }, stdio: ['pipe', 'pipe', 'pipe'] });
    let stdout = '';
    let stderr = '';
    child.stdout.on('data', (d) => (stdout += d));
    child.stderr.on('data', (d) => (stderr += d));
    child.on('error', reject);
    child.on('close', (code) => resolve({ code, stdout, stderr }));
    child.stdin.end(JSON.stringify(input));
  });

let failures = 0;
for (const scenario of spec.scenarios) {
  const server = createStandIn({ rateLimit: scenario.rateLimit });
  await new Promise((r) => server.listen(0, '127.0.0.1', r));
  const base = `http://127.0.0.1:${server.address().port}/v1/`;
  const problems = [];
  try {
    const result = await run({ SUPERTEXT_API_URL: base, SUPERTEXT_API_KEY: scenario.apiKey }, scenario.request);
    let output;
    try {
      output = JSON.parse(result.stdout);
    } catch {
      problems.push(`output is not JSON (exit ${result.code}):\n${result.stdout}\n${result.stderr}`);
    }
    if (result.code !== 0) problems.push(`exit code ${result.code}\n${result.stderr}`);
    if (output !== undefined) {
      if (output.error && typeof output.error === 'object') {
        output.error = { code: output.error.code, status: output.error.status ?? null };
      }
      if (!isDeepStrictEqual(output, scenario.expected)) {
        problems.push(`expected ${JSON.stringify(scenario.expected)}\n     got ${JSON.stringify(output)}`);
      }
    }
    const stats = await (await fetch(`${base}../__stand-in/stats`)).json();
    for (const [name, value] of Object.entries(scenario.expectedStats ?? {})) {
      if (stats[name] !== value) problems.push(`stand-in ${name}: expected ${value}, got ${stats[name]}`);
    }
    for (const [name, value] of Object.entries(scenario.expectedUpload ?? {})) {
      if (stats.lastUpload?.[name] !== value) problems.push(`upload ${name}: expected ${JSON.stringify(value)}, got ${JSON.stringify(stats.lastUpload?.[name])}`);
    }
  } finally {
    server.close();
  }
  if (problems.length) {
    failures++;
    console.log(`✗ ${scenario.name}\n  ${problems.join('\n  ')}`);
  } else {
    console.log(`✓ ${scenario.name}`);
  }
}
console.log(failures ? `\n${failures} of ${spec.scenarios.length} scenarios failed` : `\nAll ${spec.scenarios.length} scenarios passed`);
process.exit(failures ? 1 : 0);
