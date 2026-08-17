// Refreshes the committed Azure DevOps Build API spec
// (src/Dorc.AzureDevOps/build.json) from its official source.
//
// This is deliberately NOT part of `npm run api-gen`. Generation reads only
// the committed spec, so builds are hermetic: a change Microsoft publishes
// can never turn an unrelated pull request red, and the same commit always
// produces the same client. Instead the ado-build-spec-refresh workflow runs
// this on a schedule, regenerates, and opens a pull request when upstream has
// moved — so the official spec stays authoritative and every adoption of it
// is a reviewable change.
//
// Run it by hand with `npm run ado-build-spec-refresh` followed by
// `npm run api-gen`. On failure (offline, proxy, GitHub outage) it warns and
// leaves the committed copy untouched.
import { writeFileSync, readFileSync, appendFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const SPEC_URL =
  'https://raw.githubusercontent.com/MicrosoftDocs/vsts-rest-api-specs/master/specification/build/6.0/build.json';
const specPath = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '..',
  '..',
  'Dorc.AzureDevOps',
  'build.json'
);

let fetched = null;

try {
  const response = await fetch(SPEC_URL, {
    signal: AbortSignal.timeout(30_000)
  });
  if (!response.ok) {
    throw new Error(`HTTP ${response.status} ${response.statusText}`);
  }
  const body = await response.text();

  // Never let a truncated response, an error page, or a different document
  // served from that URL clobber the backup copy: the response has to parse
  // and to be recognisably the Swagger 2.0 Build spec this client is
  // generated from. Anything else falls through to the catch below and
  // leaves the committed copy in place.
  const spec = JSON.parse(body.replace(/^\uFEFF/, ''));
  if (spec?.swagger !== '2.0') {
    throw new Error(`expected a Swagger 2.0 document, got ${spec?.swagger ?? 'none'}`);
  }
  if (spec?.info?.title !== 'Build') {
    throw new Error(`expected the Build spec, got ${spec?.info?.title ?? 'no title'}`);
  }
  if (!spec.paths || Object.keys(spec.paths).length === 0) {
    throw new Error('spec declares no paths');
  }

  fetched = body;
} catch (error) {
  // Falling back to the committed copy is deliberate — generation must work
  // offline — but a fetch that fails forever (upstream moved, proxy blocks it)
  // would otherwise be indistinguishable from "upstream unchanged" and could
  // go unnoticed indefinitely. Surface it where a human will see it: the
  // workflow step summary when running in Actions, a warning otherwise.
  const message =
    `could not fetch the official Azure DevOps Build spec (${error.message}); ` +
    `using the committed backup copy at ${specPath}`;
  console.warn(`WARNING: ${message}`);
  if (process.env.GITHUB_STEP_SUMMARY) {
    appendFileSync(
      process.env.GITHUB_STEP_SUMMARY,
      `> [!WARNING]\n> Spec refresh fell back to the committed copy: ${message}\n`
    );
  }
}

// Deliberately outside the try above: a failure here is a failure to WRITE,
// not to fetch, and reporting it as "could not fetch, using the backup copy"
// would be false on both counts — the backup may now be a partial write. Let
// it throw and fail the run.
if (fetched !== null) {
  // Read-then-write with no prior existsSync check: the check would be a
  // separate stat that the write cannot rely on anyway (TOCTOU), and a
  // missing file is simply "no current content".
  let current = null;
  try {
    current = readFileSync(specPath, 'utf8');
  } catch {
    // No committed copy yet — treat as "no current content".
  }

  if (current === fetched) {
    console.log(`build.json already matches the official spec (${SPEC_URL})`);
  } else {
    writeFileSync(specPath, fetched);
    console.log(`build.json refreshed from the official spec (${SPEC_URL})`);
  }
}
