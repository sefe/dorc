import type { PropertyValues } from 'lit';
import { LitElement } from 'lit';
import { state } from 'lit/decorators.js';
import { updateMetadata } from './html-meta-manager';
import type { AppRoute, RouteMetadata } from '../router/route-config';

type PageMetadata = RouteMetadata;

/** The subset of the router location that routed pages actually read. */
export interface PageLocation {
  route?: AppRoute | null;
  pathname: string;
}

export class PageElement extends LitElement {
  @state()
  protected location: PageLocation = {} as PageLocation;

  updated(_changedProperties: PropertyValues) {
    super.updated(_changedProperties);

    this.updateMetadata();
  }

  protected metadata(route: AppRoute): PageMetadata | undefined {
    return route.metadata;
  }

  private updateMetadata() {
    const { route } = this.location;

    if (!route) {
      return;
    }

    const metadata = this.metadata(route);

    if (metadata) {
      const defaultMetadata = {
        url: window.location.href,
        description: metadata.description,
      };

      updateMetadata({
        ...defaultMetadata,
        ...metadata
      });
    }
  }
}
