import type { PropertyValues } from 'lit';
import { LitElement } from 'lit';
import type { Route, RouterLocation } from '@vaadin/router';
import { state } from 'lit/decorators.js';
import { updateMetadata } from './html-meta-manager';
import {RouteMeta} from "../router/routes.ts";

export class PageElement extends LitElement {
  @state()
  protected location = {} as RouterLocation<RouteMeta>;

  updated(_changedProperties: PropertyValues) {
    super.updated(_changedProperties);

    this.updateMetadata();
  }

  protected metadata(route: Route<RouteMeta>) {
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
