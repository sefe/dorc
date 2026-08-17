// Refreshes the Azure DevOps Build API spec from its official source before
// the C# client is generated. The committed copy at
// src/Dorc.AzureDevOps/build.json is the fallback: when the fetch fails
// (offline, proxy, GitHub outage) generation proceeds with it unchanged, so
// generation stays deterministic. When the fetch succeeds and upstream has
// changed, the refreshed spec makes the regenerated client differ from the
// committed one, and CI's in-sync gate turns that into a visible failure
// asking for the update to be committed.
import { writeFileSync, readFileSync } from 'node:fs';
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

  // Read-then-write with no prior existsSync check: the check would be a
  // separate stat that the write cannot rely on anyway (TOCTOU), and a
  // missing file is simply "no current content".
  let current = null;
  try {
    current = readFileSync(specPath, 'utf8');
  } catch {
    current = null;
  }

  if (current === body) {
    console.log(`build.json already matches the official spec (${SPEC_URL})`);
  } else {
    writeFileSync(specPath, body);
    console.log(`build.json refreshed from the official spec (${SPEC_URL})`);
  }
} catch (error) {
  console.warn(
    `WARNING: could not fetch the official Azure DevOps Build spec (${error.message}); ` +
      `using the committed backup copy at ${specPath}`
  );
}
