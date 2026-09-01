// Test setup, applied to every test file via vitest.config.ts setupFiles.

import { afterEach } from 'vitest';
import { _cleanupFixtures, resetTheme } from './_helpers';

// The --dorc-* theme tokens are declared in src/theme/dorc-tokens.css, which
// index.html links. vitest uses its own tester HTML, so without this import the
// tokens do not exist in the test document: getPropertyValue('--dorc-bg-primary')
// returns '', every var() falls back to transparent, and a contrast assertion
// passes vacuously in BOTH themes while the app fails. Importing it here is what
// makes the contrast criteria mean anything.
import '../src/theme/dorc-tokens.css';

// Remove fixture containers between tests so DOM state doesn't leak.
//
// Vaadin 25 dialog overlays live inside the <vaadin-dialog> element's own
// shadow root rather than being appended to document.body, so removing the
// host removes them too. Two things do NOT follow that rule and were leaking
// past this hook:
//
//  - Notifications. `Notification.show` mounts a
//    <vaadin-notification-container> on document.body and leaves it there.
//    Three files end with a card still in it, which is exactly the "next test
//    finds the wrong element" shape.
//  - `document.body.style.pointerEvents`. A dialog's `_enterModalState()` sets
//    it to 'none' and only `opened = false` clears it, so a test that ends
//    with a dialog open leaves the page inert. Harmless today only because
//    nothing in the suite uses real browser input — every click is `el.click()`
//    or a dispatched event, neither of which pointer-events blocks. The first
//    `userEvent.click` added would inherit a dead page.
const BODY_LEVEL_LEFTOVERS =
  'vaadin-notification, vaadin-notification-container, vaadin-confirm-dialog';

afterEach(() => {
  _cleanupFixtures();
  resetTheme();
  for (const node of document.body.querySelectorAll(BODY_LEVEL_LEFTOVERS)) {
    node.remove();
  }
  document.body.style.pointerEvents = '';
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
  // The router throws this from urlForName when a link renders before the
  // route table is installed.
  /^Route "[^"]+" not found$/,
  // Tagify is loaded via a CDN <script> in index.html and isn't available in
  // the test runner; tags-input's firstUpdated calls `new window.Tagify(...)`.
  /^window\.Tagify is not a constructor$/
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
