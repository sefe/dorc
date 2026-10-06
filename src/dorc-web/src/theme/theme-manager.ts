const THEME_KEY = 'dorc-theme';

export type Theme = 'light' | 'dark';

class ThemeManager {
  private listeners: Array<(theme: Theme) => void> = [];

  /** Returns the effective theme: explicit user choice, or system preference. */
  get current(): Theme {
    const stored = localStorage.getItem(THEME_KEY) as Theme | null;
    if (stored) return stored;
    return window.matchMedia('(prefers-color-scheme: dark)').matches
      ? 'dark'
      : 'light';
  }

  apply(theme: Theme) {
    const html = document.documentElement;

    if (theme === 'dark') {
      html.setAttribute('theme', 'dark');
    } else {
      html.removeAttribute('theme');
    }

    this.listeners.forEach(fn => fn(theme));
  }

  /** Explicit user toggle – persists the choice to localStorage. */
  toggle() {
    const next = this.current === 'dark' ? 'light' : 'dark';
    localStorage.setItem(THEME_KEY, next);
    this.apply(next);
  }

  onChange(fn: (theme: Theme) => void) {
    this.listeners.push(fn);
    return () => {
      this.listeners = this.listeners.filter(l => l !== fn);
    };
  }

  /** Call once at app startup to restore the saved preference or follow system. */
  init() {
    this.apply(this.current);

    // React to OS theme changes (only when user hasn't explicitly chosen)
    window
      .matchMedia('(prefers-color-scheme: dark)')
      .addEventListener('change', () => {
        if (!localStorage.getItem(THEME_KEY)) {
          this.apply(this.current);
        }
      });

    // NB: no overlay theme propagation is needed. Vaadin 25 nests a dialog's
    // overlay inside the <vaadin-dialog> element's own shadow root rather than
    // appending it to document.body, so a document-level query could never
    // reach one — verified against the running app. Lumo custom properties
    // inherit through shadow boundaries, so dark mode reaches overlays anyway.
  }
}

export const themeManager = new ThemeManager();
