import type { PropertyValues } from 'lit';
import { LitElement } from 'lit';
import type { Route, RouterLocation } from '@vaadin/router';
import { state } from 'lit/decorators.js';
import type { MetadataOptions } from './html-meta-manager';
import { updateMetadata } from './html-meta-manager';
import AppConfig from '../app-config';

// Add metadata options to the @vaadin/router BaseRoute
declare module '@vaadin/router/dist/vaadin-router' {
  export interface BaseRoute {
    metadata?: MetadataOptions;
  }
}

export class PageElement extends LitElement {
  @state()
  protected location = {} as RouterLocation;

  private defaultTitleTemplate = `%s | ${new AppConfig().appName}`;

  updated(_changedProperties: PropertyValues) {
    super.updated(_changedProperties);

    this.updateMetadata();
  }

  protected metadata(route: Route) {
    return route.metadata;
  }

  private getTitleTemplate(titleTemplate?: string | null) {
    return titleTemplate || titleTemplate === null
      ? titleTemplate
      : this.defaultTitleTemplate;
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
        titleTemplate: this.getTitleTemplate(metadata.titleTemplate)
      };

      updateMetadata({
        ...defaultMetadata,
        ...metadata
      });
    }
  }
}
