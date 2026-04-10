// Polyfill CSS APIs for happy-dom (used by Vaadin component-base and lumo-modules)
(globalThis as any).CSS = {
  ...(typeof CSS !== 'undefined' ? CSS : {}),
  registerProperty: () => {},
  supports: typeof CSS !== 'undefined' && CSS.supports ? CSS.supports.bind(CSS) : () => false,
  escape: typeof CSS !== 'undefined' && CSS.escape ? CSS.escape.bind(CSS) : (s: string) => s
};

// Polyfill CSSImportRule for Vaadin lumo-modules.js
if (typeof globalThis.CSSImportRule === 'undefined') {
  (globalThis as any).CSSImportRule = class CSSImportRule {};
}

// Stub XMLHttpRequest to prevent sync network calls (e.g., app-config.ts loading appconfig.json)
const OrigXHR = globalThis.XMLHttpRequest;
class StubXMLHttpRequest extends OrigXHR {
  open(_method: string, _url: string | URL, _async?: boolean) {
    // no-op
  }
  send() {
    // Simulate a failed response (no appconfig.json in test)
    Object.defineProperty(this, 'status', { value: 0, writable: true });
    Object.defineProperty(this, 'responseText', { value: '{}', writable: true });
  }
}
(globalThis as any).XMLHttpRequest = StubXMLHttpRequest;

// Suppress unhandled rejections from Vaadin Lumo theme injection in tests
const origListeners = process.listeners('unhandledRejection');
process.removeAllListeners('unhandledRejection');
process.on('unhandledRejection', (reason: any) => {
  const msg = String(reason?.message ?? reason ?? '');
  if (
    msg.includes('CSSImportRule') ||
    msg.includes('lumo') ||
    msg.includes('ECONNREFUSED') ||
    msg.includes('rule.style')
  ) {
    return;
  }
  for (const listener of origListeners) {
    (listener as Function)(reason);
  }
});

// Polyfill ShadowRoot adoptedStyleSheets
const origAttachShadow = HTMLElement.prototype.attachShadow;
HTMLElement.prototype.attachShadow = function (init: ShadowRootInit) {
  const shadow = origAttachShadow.call(this, init);
  if (!shadow.adoptedStyleSheets) {
    Object.defineProperty(shadow, 'adoptedStyleSheets', {
      value: [],
      writable: true,
      configurable: true
    });
  }
  return shadow;
};
