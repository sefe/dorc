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

    // Propagate theme to any Vaadin overlay containers already in the DOM
    document
      .querySelectorAll('vaadin-dialog-overlay, vaadin-overlay')
      .forEach(el => {
        if (theme === 'dark') {
          el.setAttribute('theme', 'dark');
        } else {
          el.removeAttribute('theme');
        }
      });

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

    // Watch for dynamically created Vaadin overlays and propagate theme
    const observer = new MutationObserver(mutations => {
      const theme = this.current;
      if (theme !== 'dark') return;
      for (const m of mutations) {
        m.addedNodes.forEach(node => {
          if (node instanceof HTMLElement) {
            const tag = node.tagName?.toLowerCase() ?? '';
            if (tag.includes('overlay')) {
              node.setAttribute('theme', 'dark');
            }
          }
        });
      }
    });
    observer.observe(document.body, { childList: true });
  }
}

export const themeManager = new ThemeManager();
