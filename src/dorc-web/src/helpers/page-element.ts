import type { PropertyValues } from 'lit';
import { LitElement } from 'lit';
import type { Route } from '@vaadin/router';
import { state } from 'lit/decorators.js';
import { updateMetadata } from './html-meta-manager';
import {RouteMeta} from "../router/routes.ts";

interface PageMetadata {
  title: string;
  description: string;
}

// Simplified location type to avoid TS 6 deep recursion with RouterLocation<RouteMeta>
export interface PageLocation {
  route?: Route<RouteMeta> | null;
  pathname: string;
}

export class PageElement extends LitElement {
  @state()
  protected location: PageLocation = {} as PageLocation;

  updated(_changedProperties: PropertyValues) {
    super.updated(_changedProperties);

    this.updateMetadata();
  }

  protected metadata(route: Route<RouteMeta>): PageMetadata | undefined {
    return route.metadata as PageMetadata | undefined;
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
