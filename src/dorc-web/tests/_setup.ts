// Test setup, applied to every test file via vitest.config.ts setupFiles.

import { afterEach } from 'vitest';
import { _cleanupFixtures } from './_helpers';

// Remove fixture containers between tests so DOM state doesn't leak.
afterEach(() => {
  _cleanupFixtures();
});

// Silence known unhandled errors thrown from SUT modules that aren't fully
// stubbed in tests (CDN-loaded globals, missing API responses, missing router
// routes). The SUT init path fires these as unhandled promise rejections;
// they aren't relevant to assertions but would otherwise be reported as
// unhandled errors and pollute the run.
//
// Patterns are anchored to specific known messages so a future regression
// with a similar substring isn't silently swallowed.
const SUPPRESS_PATTERNS: RegExp[] = [
  // RxJS AjaxError (thrown from API calls in component constructors).
  // Matches both the bare message ("ajax error 404") and the stringified
  // form ("AjaxError: ajax error").
  /^ajax error\b/,
  /^AjaxError(?::|\s|$)/,
  // Vaadin Router throws this from urlForName when no routes are registered.
  /^Route "[^"]+" not found$/,
  // Tagify is loaded via a CDN <script> in index.html and isn't available in
  // the test runner; tags-input's firstUpdated calls `new window.Tagify(...)`.
  /^window\.Tagify is not a constructor$/,
];

const matches = (msg: unknown): boolean =>
  typeof msg === 'string' && SUPPRESS_PATTERNS.some(p => p.test(msg));

window.addEventListener('unhandledrejection', (e: PromiseRejectionEvent) => {
  const reason = e.reason as { message?: unknown } | string | undefined;
  const msg =
    typeof reason === 'object' && reason !== null && 'message' in reason
      ? String(reason.message)
      : String(reason);
  if (matches(msg)) {
    e.preventDefault();
  }
});

window.addEventListener('error', (e: ErrorEvent) => {
  if (matches(e.message)) {
    e.preventDefault();
  }
});
