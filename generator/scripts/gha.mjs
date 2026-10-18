// Minimal GitHub Actions and process helpers for the pipeline scripts (no dependencies).
import { appendFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';

/** Appends name=value pairs to $GITHUB_OUTPUT (heredoc form for multi-line values); prints them when not in Actions. @param {Record<string, string>} values */
export function setOutputs(values) {
  const file = process.env.GITHUB_OUTPUT;
  for (const [name, value] of Object.entries(values)) {
    if (!file) {
      console.log(`output ${name}=${value}`);
      continue;
    }
    if (value.includes('\n')) {
      const delimiter = `EOF_${randomUUID()}`;
      appendFileSync(file, `${name}<<${delimiter}\n${value}\n${delimiter}\n`);
    } else {
      appendFileSync(file, `${name}=${value}\n`);
    }
  }
}

/** Appends markdown to the job summary, or prints it outside Actions. @param {string} markdown */
export function appendSummary(markdown) {
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, markdown);
  else console.log(markdown);
}

/** Runs a command without a shell and returns its stdout. @param {string} command @param {string[]} args @param {{ cwd?: string | URL }} [options] */
export function run(command, args, options = {}) {
  return execFileSync(command, args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'], maxBuffer: 64 * 1024 * 1024, ...options });
}

/** GET a URL and parse JSON; throws with the status on failure. @param {string} url */
export async function fetchJson(url) {
  const response = await fetch(url, { headers: { accept: 'application/json' } });
  if (!response.ok) throw new Error(`GET ${url}: HTTP ${response.status}`);
  return response.json();
}

/** GET a URL as text, or undefined on 404. @param {string} url */
export async function fetchTextOrUndefined(url) {
  const response = await fetch(url);
  if (response.status === 404) return undefined;
  if (!response.ok) throw new Error(`GET ${url}: HTTP ${response.status}`);
  return response.text();
}

/** Runs main and turns a thrown error into a message and exit code 1. @param {() => unknown} main */
export async function runMain(main) {
  try {
    await main();
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exit(1);
  }
}
