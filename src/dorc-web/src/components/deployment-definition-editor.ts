import '@vaadin/checkbox';
import '@vaadin/combo-box';
import '@vaadin/text-field';
import { css, html, LitElement, nothing } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import {
  ComponentType,
  TerraformSourceType,
  type ComponentApiModel,
  type ProjectApiModel
} from '../apis/dorc-api';

@customElement('deployment-definition-editor')
export class DeploymentDefinitionEditor extends LitElement {
  @property({ attribute: false }) component: ComponentApiModel | undefined;
  @property({ attribute: false }) project: ProjectApiModel | undefined;
  @property({ type: Boolean }) readonly = false;
  @property({ type: Array }) powerShellVersions: string[] = [];

  static styles = css`
    :host {
      display: block;
      min-width: 0;
      color: var(--dorc-text-primary);
    }
    .fields {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--lumo-space-xs) var(--lumo-space-m);
    }
    vaadin-text-field,
    vaadin-combo-box {
      width: 100%;
      min-width: 0;
    }
    .full {
      grid-column: 1 / -1;
    }
    .flags {
      display: flex;
      flex-wrap: wrap;
      gap: var(--lumo-space-s) var(--lumo-space-m);
      margin: var(--lumo-space-s) 0;
    }
    p {
      margin: var(--lumo-space-xs) 0;
      max-width: 75ch;
      font-size: var(--lumo-font-size-s);
      color: var(--dorc-text-secondary-strong);
      overflow-wrap: anywhere;
    }
    code {
      overflow-wrap: anywhere;
    }
    a {
      color: var(--dorc-link-color);
    }
    @media (max-width: 768px) {
      .fields {
        grid-template-columns: 1fr;
      }
    }
  `;

  private change(patch: Partial<ComponentApiModel>) {
    if (this.readonly || !this.component) return;
    this.dispatchEvent(
      new CustomEvent('definition-changed', {
        detail: { ...this.component, ...patch },
        bubbles: true,
        composed: true
      })
    );
  }

  private text(
    label: string,
    key:
      | 'ComponentName'
      | 'ScriptPath'
      | 'TerraformGitBranch'
      | 'TerraformSubPath',
    helper = '',
    locked = false
  ) {
    return html`<vaadin-text-field
      .label=${label}
      .value=${this.component?.[key] ?? ''}
      .readonly=${this.readonly || locked}
      .helperText=${helper}
      @change=${(event: Event) => this.change({ [key]: (event.currentTarget as HTMLInputElement).value })}
    ></vaadin-text-field>`;
  }

  render() {
    const component = this.component;
    if (!component)
      return html`<p>Select a component to inspect its definition.</p>`;
    const terraform = component.ComponentType === ComponentType.Terraform;
    const catalog =
      terraform &&
      component.TerraformSourceType === TerraformSourceType.Catalog;
    const sourceType =
      component.TerraformSourceType ?? TerraformSourceType.SharedFolder;
    return html`
      <div class="fields">
        ${this.text('Component name', 'ComponentName', catalog ? 'State identity is stable. Catalog renames require an operator-reviewed migration.' : 'Unique component name.', catalog)}
        <vaadin-combo-box
          label="Execution type"
          .items=${Object.values(ComponentType)}
          .value=${component.ComponentType ?? ComponentType.PowerShell}
          .disabled=${this.readonly || Boolean(component.ComponentId)}
          helper-text="An existing execution identity is not converted to another type here."
          @change=${(event: Event) => {
            const value = (event.currentTarget as HTMLInputElement).value;
            if (
              value === ComponentType.PowerShell ||
              value === ComponentType.Terraform
            ) {
              this.change({ ComponentType: value });
            }
          }}
        ></vaadin-combo-box>
        ${
          terraform
            ? html`
                <vaadin-combo-box
                  label="Terraform source"
                  .items=${Object.values(TerraformSourceType).filter(source => catalog || source !== TerraformSourceType.Catalog)}
                  .value=${sourceType}
                  .disabled=${this.readonly || catalog}
                  @change=${(event: Event) => {
                    const value = (event.currentTarget as HTMLInputElement)
                      .value;
                    const source = Object.values(TerraformSourceType).find(
                      item => item === value
                    );
                    if (source) this.change({ TerraformSourceType: source });
                  }}
                ></vaadin-combo-box>
                ${this.text('Source subpath', 'TerraformSubPath', 'Relative path inside the selected source.')}
              `
            : html`
                <vaadin-combo-box
                  label="PowerShell version"
                  .items=${this.powerShellVersions}
                  .value=${component.PSVersion ?? ''}
                  .disabled=${this.readonly}
                  @change=${(event: Event) => this.change({ PSVersion: (event.currentTarget as HTMLInputElement).value })}
                ></vaadin-combo-box>
              `
        }
        ${
          !terraform || sourceType === TerraformSourceType.SharedFolder
            ? this.text(
                terraform ? 'Shared folder path' : 'Script path or JSON path',
                'ScriptPath',
                'Existing paths and JSON argument definitions are preserved. Use secure property references for secrets.'
              )
            : nothing
        }
        ${
          terraform && sourceType === TerraformSourceType.Git
            ? html`
                ${this.text('Git ref', 'TerraformGitBranch', 'The requested ref is recorded separately from the commit actually planned.')}
                <div class="full">
                  <p>
                    Repository inherited from this project:
                    <code
                      >${this.project?.TerraformGitRepoUrl || 'Not configured'}</code
                    >. Edit project source settings below or in Advanced JSON.
                  </p>
                </div>
              `
            : nothing
        }
        ${
          catalog
            ? html`
                <div class="full">
                  <p>
                    Catalog module:
                    <strong
                      >${component.TerraformTemplateName}@${component.TerraformTemplateVersion}</strong
                    >.
                  </p>
                  <p>
                    Use the module catalog to plan this version or explicitly
                    upgrade this component. Editing JSON cannot bypass upgrade
                    checks.
                  </p>
                </div>
              `
            : nothing
        }
        ${
          terraform &&
          (sourceType === TerraformSourceType.AzureArtifact ||
            sourceType === TerraformSourceType.GitHubArtifact)
            ? html`
                <div class="full">
                  <p>
                    Artifact source inherited from this project:
                    <code
                      >${this.project?.ArtefactsUrl || 'Not configured'}</code
                    >. Select a concrete build or run when deploying; history
                    records the submitted and executed source.
                  </p>
                </div>
              `
            : nothing
        }
      </div>
      <div class="flags">
        <vaadin-checkbox
          label="Enabled"
          .checked=${component.IsEnabled !== false}
          .disabled=${this.readonly}
          @change=${(event: Event) => this.change({ IsEnabled: (event.currentTarget as HTMLInputElement).checked })}
        ></vaadin-checkbox>
        <vaadin-checkbox
          label="Stop on failure"
          .checked=${component.StopOnFailure === true}
          .disabled=${this.readonly}
          @change=${(event: Event) => this.change({ StopOnFailure: (event.currentTarget as HTMLInputElement).checked })}
        ></vaadin-checkbox>
        ${
          !terraform
            ? html`<vaadin-checkbox
                label="Non-production only"
                .checked=${component.NonProdOnly === true}
                .disabled=${this.readonly}
                @change=${(event: Event) => this.change({ NonProdOnly: (event.currentTarget as HTMLInputElement).checked })}
              ></vaadin-checkbox>`
            : nothing
        }
      </div>
    `;
  }
}
