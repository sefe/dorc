import { css, LitElement } from 'lit';
import '@vaadin/list-box/vaadin-list-box.js';
import '@vaadin/item';
import '@vaadin/icons';
import '@vaadin/icon';
import '@vaadin/button';
import '@vaadin/combo-box';
import { ListBox } from '@vaadin/list-box';
import { Item } from '@vaadin/item';
import { ComboBox } from '@vaadin/combo-box';
import { customElement, property } from 'lit/decorators.js';
import { html } from 'lit/html.js';
import { EnvironmentApiModel } from '../apis/dorc-api';
import { ErrorNotification } from './notifications/error-notification';
import {
  RefDataEnvironmentsApi,
  RefDataProjectEnvironmentMappingsApi
} from '../apis/dorc-api';
import { retrieveErrorMessage } from '../helpers/errorMessage-retriever';

@customElement('attach-environment')
export class AttachEnvironment extends LitElement {
  @property({ type: String })
  public projectName = '';

  @property({ type: Array })
  private environments: EnvironmentApiModel[] | undefined;

  @property({ type: Array })
  private shortlist: EnvironmentApiModel[] | undefined;

  private selectedEnvironment: any;

  private environmentsMap:
    | Map<number | undefined, EnvironmentApiModel>
    | undefined;

  constructor() {
    super();

    const api = new RefDataEnvironmentsApi();
    api.refDataEnvironmentsGet({ env: '' }).subscribe(
      (data: EnvironmentApiModel[]) => {
        this.setEnvironmentDetails(data);
      },
      (err: any) => console.error(err),
      () => console.log('done loading environments')
    );
  }

  static get styles() {
    return css``;
  }

  render() {
    return html`
      <vaadin-horizontal-layout>
        <vaadin-combo-box
          id="environments"
          label="Environments"
          item-value-path="EnvironmentId"
          item-label-path="EnvironmentName"
          @value-changed="${this.setSelectedEnvironment}"
          .items="${this.environments}"
          filter-property="EnvironmentName"
          placeholder="Select Environment"
          helper-text="Shortlist all environments before attaching"
          style="width: 300px; text-align: left"
          clear-button-visible
        ></vaadin-combo-box>
        <vaadin-button
          style="margin-top: 37px; margin-left: 5px"
          title="Shortlist Environment"
          theme="icon"
          @click="${this.shortListEnv}"
        >
          <vaadin-icon
            icon="vaadin:list-select"
            style="color: cornflowerblue"
          ></vaadin-icon>
        </vaadin-button>
      </vaadin-horizontal-layout>
      <vaadin-horizontal-layout>
        <vaadin-list-box
          id="shortlist"
          style="height: 200px; width:300px; background-color: #f3f5f7"
        >
          ${this.shortlist?.map(
            env =>
              html` <vaadin-item .data="${env}"
                >${env.EnvironmentName}
              </vaadin-item>`
          )}
        </vaadin-list-box>
        <vaadin-button
          style="margin-left: 5px; margin-top: 0"
          title="Remove Environment"
          theme="icon"
          @click="${this.removeEnv}"
        >
          <vaadin-icon
            icon="vaadin:close-small"
            style="color: cornflowerblue"
          ></vaadin-icon>
        </vaadin-button>
      </vaadin-horizontal-layout>

      <vaadin-button
        title="Attach Environment(s)"
        theme="icon"
        @click="${this.attachEnvironment}"
      >
        <vaadin-icon
          icon="vaadin:link"
          style="color: cornflowerblue"
        ></vaadin-icon>
        Attach Environment(s)
      </vaadin-button>
    `;
  }

  setSelectedEnvironment(data: any) {
    this.selectedEnvironment = data.currentTarget.value;
  }

  shortListEnv() {
    const env = this.environmentsMap?.get(this.selectedEnvironment);
    if (env !== undefined) {
      if (!this.shortlist?.includes(env)) {
        const freshArray: EnvironmentApiModel[] = [];
        this.shortlist?.forEach(val => freshArray.push({ ...val }));
        freshArray.push(env);
        this.shortlist = freshArray;
      }
    }
  }

  removeEnv() {
    const listbox = this.shadowRoot?.getElementById('shortlist') as ListBox;

    if (listbox.selected !== undefined && listbox.selected !== null) {
      const freshArray: EnvironmentApiModel[] = [];
      this.shortlist?.forEach(val => freshArray.push({ ...val }));
      freshArray.splice(listbox.selected, 1);
      this.shortlist = freshArray;
    }
  }

  setEnvironmentDetails(environments: EnvironmentApiModel[]) {
    this.environments = environments.sort(this.compareEnvs);
    this.environmentsMap = new Map(
      environments.map(obj => [obj.EnvironmentId, obj])
    );
  }

  compareEnvs(a: EnvironmentApiModel, b: EnvironmentApiModel) {
    const nameA: string =
      a.EnvironmentName !== undefined && a.EnvironmentName !== null
        ? a.EnvironmentName?.toUpperCase()
        : ''; // ignore upper and lowercase
    const nameB: string =
      b.EnvironmentName !== undefined && b.EnvironmentName !== null
        ? b.EnvironmentName?.toUpperCase()
        : ''; // ignore upper and lowercase
    if (nameA < nameB) {
      return -1;
    }
    if (nameA > nameB) {
      return 1;
    }
    // names must be equal
    return 0;
  }

  cleanupUI() {
    this.shortlist = [];
    const environments = this.shadowRoot?.getElementById(
      'environments'
    ) as ComboBox;
    environments.clear();
  }

  attachEnvironment() {
    const listbox = this.shadowRoot?.getElementById('shortlist') as ListBox;
    if (listbox.items?.length == 0) {
      const notification = new ErrorNotification();
      notification.setAttribute(
        'errorMessage',
        'Please Select an Environment to Shortlist before attempting to attach'
      );
      this.shadowRoot?.appendChild(notification);
      notification.open();
      console.error(`No Environment has been shortlisted`);
      return;
    }
    const items = listbox.items as Item[];
    const envs =
      items
        ?.map((elem: Item) => {
          // eslint-disable-next-line @typescript-eslint/ban-ts-comment
          // @ts-ignore
          const env = elem.data as EnvironmentApiModel;
          return env.EnvironmentName;
        })
        .join(';') || '';
    const api = new RefDataProjectEnvironmentMappingsApi();
    api
      .refDataProjectEnvironmentMappingsPost({
        project: this.projectName,
        environment: envs
      })
      .subscribe({
        next: () => {
          const event = new CustomEvent('attach-env-completed', {
            detail: {
              message: 'Attach Environments completed successfully!'
            },
            bubbles: true,
            composed: true
          });
          this.dispatchEvent(event);
          console.log('done adding mapping');
        },
        error: (err: any) => {
          const notification = new ErrorNotification();

          const errorMessage = retrieveErrorMessage(err);

          notification.setAttribute('errorMessage', errorMessage);
          this.shadowRoot?.appendChild(notification);
          notification.open();
          console.error(`error adding mappings: ${errorMessage}`, err);
        },
        complete: () => {
          this.cleanupUI();
        }
      });
  }
}
