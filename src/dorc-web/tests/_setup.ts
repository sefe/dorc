// Test setup: silence known unhandled errors from SUT modules that aren't
// stubbed (CDN-loaded globals, API calls, router routes). Tests opt into
// running these modules; their internal init paths fire-and-forget calls
// that aren't relevant to the assertions.

const SUPPRESS_PATTERNS = [
  'ajax error',
  'AjaxError',
  'urlForName',
  'Route "',
  'Tagify is not a constructor',
];

const matches = (msg: unknown): boolean =>
  typeof msg === 'string' && SUPPRESS_PATTERNS.some(p => msg.includes(p));

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
