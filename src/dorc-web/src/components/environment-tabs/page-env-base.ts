import { PropertyValues } from 'lit';
import { LitElement } from 'lit';
import '@vaadin/grid/vaadin-grid-sort-column';
import '@vaadin/grid/vaadin-grid';
import { customElement, property } from 'lit/decorators.js';
import '../add-edit-environment';
import { Router } from '@vaadin/router/dist/vaadin-router';
import {
  EnvironmentContentApiModel,
  RefDataEnvironmentsApi,
  RefDataEnvironmentsDetailsApi
} from '../../apis/dorc-api';
import type { EnvironmentApiModel } from '../../apis/dorc-api';
import { ErrorNotification } from '../notifications/error-notification';
import '../notifications/error-notification';
import { EnvironmentContentBuildsApiModelExtended } from '../model-extensions/EnvironmentContentBuildsApiModelExtended';

let _envContent: EnvironmentContentApiModel | undefined;
let _environment: EnvironmentApiModel;

@customElement('page-env-base')
export class PageEnvBase extends LitElement {
  @property({ type: Boolean }) protected envLoaded = false;

  @property({ type: Boolean }) protected slotLoaded = false;

  protected environmentName = '';

  @property({ type: Object })
  protected get environment(): EnvironmentApiModel {
    return _environment;
  }

  protected set environment(value: EnvironmentApiModel) {
    const oldValue = _environment;
    _environment = value;
    this.notifyEnvironmentReady();
    this.requestUpdate('environment', oldValue);
  }

  @property({ type: Object })
  protected get envContent(): EnvironmentContentApiModel | undefined {
    return <EnvironmentContentApiModel>_envContent;
  }

  protected set envContent(value: EnvironmentContentApiModel | undefined) {
    const oldValue = _envContent;
    _envContent = value;
    this.notifyEnvironmentContentReady();
    this.requestUpdate('envContent', oldValue);
  }

  @property({ type: String }) protected envFilter: string | undefined;

  @property({ type: Boolean }) protected isEndur = false;

  @property({ type: Number }) protected environmentId = -1;

  public loadEnvironmentInfo() {
    const envName = location.pathname.split('/')[2];
    console.log(`Browser Location.pathname: ${location.pathname}`);
    this.environmentName = decodeURIComponent(envName);

    if (
      this.environment !== undefined &&
      this.environmentName === this.environment.EnvironmentName &&
      this.environmentName === this.envContent?.EnvironmentName
    ) {
      this.envLoaded = true;
      this.envFilter =
        this.environment?.Details?.ThinClient !== null
          ? this.environment?.Details?.ThinClient
          : undefined;
      this.environmentId = this.environment.EnvironmentId ?? -1;

      const event = new CustomEvent('environment-loaded', {
        detail: {
          environment: this.environment,
          envContent: this.envContent
        },
        bubbles: true,
        composed: true
      });
      this.dispatchEvent(event);

      this.notifyEnvironmentReady();
      this.notifyEnvironmentContentReady();
      this.envLoaded = true;
      return;
    }
    this.forceLoadEnvironmentInfo();
  }

  protected forceLoadEnvironmentInfo() {
    const api2 = new RefDataEnvironmentsApi();
    api2.refDataEnvironmentsGet({ env: this.environmentName }).subscribe({
      next: (data: EnvironmentApiModel[]) => {
        if (data[0] !== null) {
          this.environment = data[0];
          this.envFilter =
            this.environment?.Details?.ThinClient !== null
              ? this.environment?.Details?.ThinClient
              : undefined;
          this.environmentId = this.environment.EnvironmentId ?? -1;

          this.refreshEnvDetails(this.environment);
        } else {
          console.log(
            `Base-Sending to not found for null data from env Get ${
              this.environmentName
            }`
          );
          Router.go('not-found');
        }
      },
      error: (err: any) => {
        if (err.status === 403) {
          const notification = new ErrorNotification();
          notification.setAttribute('errorMessage', err.response);
          this.shadowRoot?.appendChild(notification);
          notification.open();
        }
        console.error(err);
      },
      complete: () => console.log('done loading environment')
    });
  }

  protected refreshEnvDetails(env: EnvironmentApiModel) {
    this.dispatchEvent(
      new CustomEvent('environment-loading', {
        bubbles: true,
        composed: true,
        detail: {}
      })
    );
    const api = new RefDataEnvironmentsDetailsApi();
    api
      .refDataEnvironmentsDetailsIdGet({
        id: env.EnvironmentId ?? 0
      })
      .subscribe({
        next: (data: EnvironmentContentApiModel) => {
          this.envContent = data;

          this.envContent.Builds?.map(b => this.getDate(b));

          this.envLoaded = true;
          this.dispatchEvent(
            new CustomEvent('environment-loaded', {
              bubbles: true,
              composed: true,
              detail: {
                environment: this.environment,
                envContent: this.envContent
              }
            })
          );
        },
        error: (err: string) => console.error(err),
        complete: () => console.log('done loading env content')
      });
  }

  protected getDate(element: EnvironmentContentBuildsApiModelExtended): void {
    const idx = element.UpdateDate?.indexOf('/');
    let dateSeparator = '/';
    if (idx === -1) {
      dateSeparator = '-';
    }

    const splitDT: string[] = element.UpdateDate?.split('T') as string[];
    const splitDate = splitDT[0].split(dateSeparator);
    const splitTime = splitDT[1].split(':');

    const year: number = parseInt(splitDate[0], 10);
    const month: number = parseInt(splitDate[1], 10) - 1;
    const day: number = parseInt(splitDate[2], 10);
    const hour: number = parseInt(splitTime[0], 10) - 1;
    const minutes: number = parseInt(splitTime[1], 10);
    const seconds = parseInt(splitTime[2].split('.')[0], 10);

    element.UpdatedDate = new Date(year, month, day, hour, minutes, seconds);
  }

  public slotChangeComplete() {
    this.slotLoaded = true;
  }

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener('error-alert', this.onErrorAlert as EventListener);
  }

  protected onErrorAlert(event: CustomEvent) {
    const notification = new ErrorNotification();

    let msg = '';
    if (event && event.detail) {
      if (
        event.detail.result &&
        event.detail.result.ExceptionMessage !== undefined
      )
        msg = `${event.detail.description} - ${
          event.detail.result.ExceptionMessage
        }`;
      else if (
        event.detail.result &&
        event.detail.result.response !== undefined
      )
        msg = `${event.detail.description} - ${event.detail.result.response}`;
      else if (event.detail.result && event.detail.result.Message !== undefined)
        msg = `${event.detail.description} - ${event.detail.result.Message}`;
      else msg = event.detail.description;

      notification.setAttribute('errorMessage', msg);
      this.shadowRoot?.appendChild(notification);
      notification.open();
    }
  }

  notifyEnvironmentContentReady() {}

  notifyEnvironmentReady() {}
}
