import { LitElement } from 'lit';
import { state } from 'lit/decorators.js';

type Constructor<T = {}> = new (...args: any[]) => T;

const NARROW_BREAKPOINT = 768;

/**
 * Mixin that provides a reactive `_narrowScreen` boolean property.
 * Updates automatically on window resize via matchMedia.
 * Use it to conditionally hide grid columns or adjust layouts.
 */
export const ResponsiveMixin = <T extends Constructor<LitElement>>(
  superClass: T
) => {
  class ResponsiveElement extends superClass {
    @state()
    protected _narrowScreen = false;

    private _mediaQuery: MediaQueryList | undefined;
    private _mediaHandler = (e: MediaQueryListEvent) => {
      this._narrowScreen = e.matches;
    };

    connectedCallback() {
      super.connectedCallback();
      this._mediaQuery = window.matchMedia(
        `(max-width: ${NARROW_BREAKPOINT}px)`
      );
      this._narrowScreen = this._mediaQuery.matches;
      this._mediaQuery.addEventListener('change', this._mediaHandler);
    }

    disconnectedCallback() {
      super.disconnectedCallback();
      this._mediaQuery?.removeEventListener('change', this._mediaHandler);
    }
  }

  return ResponsiveElement as unknown as Constructor<{
    _narrowScreen: boolean;
  }> & T;
};
