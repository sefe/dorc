import '@vaadin/button';
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
import {
  findGenericScriptTemplate,
  GENERIC_SCRIPT_TEMPLATES,
  parseScriptPathValue,
  serializeScriptPathValue,
  type GenericScriptTemplate
} from '../helpers/generic-scripts';

const CUSTOM_SCRIPT_OPTION = '__custom__';

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
    .generic-script-card {
      grid-column: 1 / -1;
      border: 1px solid var(--dorc-border-color);
      border-radius: var(--lumo-border-radius-m);
      background: var(--dorc-bg-secondary);
      padding: var(--lumo-space-s) var(--lumo-space-m);
      display: flex;
      flex-direction: column;
      gap: var(--lumo-space-xs);
    }
    .generic-script-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: var(--lumo-space-xs);
    }
    .generic-script-header strong {
      font-size: var(--lumo-font-size-s);
    }
    .generic-category-badge {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 999px;
      font-size: var(--lumo-font-size-xs);
      font-weight: 600;
      background: var(--dorc-chip-bg);
      color: var(--dorc-chip-text);
    }
    .generic-args-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--lumo-space-xs) var(--lumo-space-m);
    }
    .custom-arg-row {
      grid-column: 1 / -1;
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1.4fr) auto;
      gap: var(--lumo-space-xs);
      align-items: end;
    }
    .env-props {
      font-size: var(--lumo-font-size-xs);
      color: var(--dorc-text-secondary-strong);
      margin: 0;
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
      .fields,
      .generic-args-grid,
      .custom-arg-row {
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

  private selectGenericTemplate(templatePath: string) {
    if (this.readonly || !this.component) return;
    if (!templatePath || templatePath === CUSTOM_SCRIPT_OPTION) {
      const parsed = parseScriptPathValue(this.component.ScriptPath);
      if (findGenericScriptTemplate(parsed.scriptPath)) {
        this.change({ ScriptPath: '' });
      }
      return;
    }
    const template = findGenericScriptTemplate(templatePath);
    if (!template) return;
    const parsed = parseScriptPathValue(this.component.ScriptPath);
    const nextScriptPath = serializeScriptPathValue(
      template.scriptPath,
      parsed.arguments
    );
    const nextName =
      !this.component.ComponentName ||
      GENERIC_SCRIPT_TEMPLATES.some(
        item => item.defaultStepName === this.component?.ComponentName
      )
        ? template.defaultStepName
        : this.component.ComponentName;
    this.change({
      ComponentName: nextName,
      ScriptPath: nextScriptPath
    });
  }

  private setGenericArgument(
    baseScriptPath: string,
    currentArgs: Array<{ key: string; value: string }>,
    paramName: string,
    paramValue: string,
    isJsonPath: boolean
  ) {
    const updated = [...currentArgs];
    const existingIndex = updated.findIndex(
      item => item.key.toLowerCase() === paramName.toLowerCase()
    );
    if (existingIndex >= 0) {
      updated[existingIndex] = { key: paramName, value: paramValue };
    } else if (paramValue.trim().length > 0) {
      updated.push({ key: paramName, value: paramValue });
    }
    this.change({
      ScriptPath: serializeScriptPathValue(baseScriptPath, updated, isJsonPath)
    });
  }

  private renderPowerShellGenericSection(component: ComponentApiModel) {
    const parsed = parseScriptPathValue(component.ScriptPath);
    const template: GenericScriptTemplate | undefined =
      findGenericScriptTemplate(parsed.scriptPath);
    const templateItems = [
      {
        label: 'Custom project script path',
        value: CUSTOM_SCRIPT_OPTION
      },
      ...GENERIC_SCRIPT_TEMPLATES.map(item => ({
        label: `${item.name} (${item.scriptPath})`,
        value: item.scriptPath
      }))
    ];
    const selectedTemplateValue = template
      ? template.scriptPath
      : CUSTOM_SCRIPT_OPTION;
    const knownParamNames = new Set(
      (template?.parameters ?? []).map(p => p.name.toLowerCase())
    );
    const extraArgs = parsed.arguments.filter(
      arg => !knownParamNames.has(arg.key.toLowerCase())
    );

    return html`
      <div class="full">
        <vaadin-combo-box
          label="Predefined generic script (project-scripts / 00 Generic)"
          .items=${templateItems}
          item-label-path="label"
          item-value-path="value"
          .value=${selectedTemplateValue}
          .disabled=${this.readonly}
          helper-text="Choose a reusable 00 Generic PowerShell script to configure its GenericArguments, or leave as Custom project script path."
          @change=${(event: Event) =>
            this.selectGenericTemplate(
              (event.currentTarget as HTMLInputElement).value
            )}
        ></vaadin-combo-box>
      </div>
      ${
        template || parsed.isJsonPath
          ? html`
              <div
                class="generic-script-card"
                role="region"
                aria-label="Generic script arguments"
              >
                <div class="generic-script-header">
                  <strong>
                    ${
                      template
                        ? `${template.name} — ${template.scriptPath}`
                        : `JSON ScriptPath arguments — ${parsed.scriptPath}`
                    }
                  </strong>
                  ${
                    template
                      ? html`<span class="generic-category-badge"
                          >${template.category}</span
                        >`
                      : nothing
                  }
                </div>
                ${template ? html`<p>${template.description}</p>` : nothing}
                ${
                  template && template.parameters.length > 0
                    ? html`
                        <div class="generic-args-grid">
                          ${template.parameters.map(param => {
                            const currentVal =
                              parsed.arguments.find(
                                a =>
                                  a.key.toLowerCase() ===
                                  param.name.toLowerCase()
                              )?.value ?? '';
                            return param.options
                              ? html`<vaadin-combo-box
                                  .label=${param.label}
                                  .items=${param.options}
                                  .value=${currentVal}
                                  .readonly=${this.readonly}
                                  .helperText=${param.helperText ?? ''}
                                  ?required=${Boolean(param.required)}
                                  @change=${(event: Event) =>
                                    this.setGenericArgument(
                                      template.scriptPath,
                                      parsed.arguments,
                                      param.name,
                                      (event.currentTarget as HTMLInputElement)
                                        .value,
                                      parsed.isJsonPath
                                    )}
                                ></vaadin-combo-box>`
                              : html`<vaadin-text-field
                                  .label=${param.label}
                                  .placeholder=${param.placeholder ?? ''}
                                  .value=${currentVal}
                                  .readonly=${this.readonly}
                                  .helperText=${param.helperText ?? ''}
                                  ?required=${Boolean(param.required)}
                                  @change=${(event: Event) =>
                                    this.setGenericArgument(
                                      template.scriptPath,
                                      parsed.arguments,
                                      param.name,
                                      (event.currentTarget as HTMLInputElement)
                                        .value,
                                      parsed.isJsonPath
                                    )}
                                ></vaadin-text-field>`;
                          })}
                        </div>
                      `
                    : nothing
                }
                ${extraArgs.map((arg, extraIdx) => {
                  const rawIndex = parsed.arguments.indexOf(arg);
                  return html`<div class="custom-arg-row">
                    <vaadin-text-field
                      label="Additional argument name"
                      .value=${arg.key}
                      .readonly=${this.readonly}
                      @change=${(event: Event) => {
                        const nextArgs = [...parsed.arguments];
                        nextArgs[rawIndex] = {
                          key: (event.currentTarget as HTMLInputElement).value,
                          value: arg.value
                        };
                        this.change({
                          ScriptPath: serializeScriptPathValue(
                            template?.scriptPath ?? parsed.scriptPath,
                            nextArgs,
                            true
                          )
                        });
                      }}
                    ></vaadin-text-field>
                    <vaadin-text-field
                      label="Argument value"
                      .value=${arg.value}
                      .readonly=${this.readonly}
                      @change=${(event: Event) => {
                        const nextArgs = [...parsed.arguments];
                        nextArgs[rawIndex] = {
                          key: arg.key,
                          value: (event.currentTarget as HTMLInputElement).value
                        };
                        this.change({
                          ScriptPath: serializeScriptPathValue(
                            template?.scriptPath ?? parsed.scriptPath,
                            nextArgs,
                            true
                          )
                        });
                      }}
                    ></vaadin-text-field>
                    <vaadin-button
                      theme="tertiary"
                      .disabled=${this.readonly}
                      aria-label="Remove argument ${extraIdx + 1}"
                      @click=${() => {
                        const nextArgs = parsed.arguments.filter(
                          (_, idx) => idx !== rawIndex
                        );
                        this.change({
                          ScriptPath: serializeScriptPathValue(
                            template?.scriptPath ?? parsed.scriptPath,
                            nextArgs
                          )
                        });
                      }}
                      >Remove</vaadin-button
                    >
                  </div>`;
                })}
                ${
                  template?.requiredEnvironmentProperties?.length
                    ? html`<p class="env-props">
                        Consumes DOrc environment properties:
                        <code
                          >${template.requiredEnvironmentProperties.join(
                            ', '
                          )}</code
                        >
                      </p>`
                    : nothing
                }
              </div>
            `
          : nothing
      }
    `;
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
                ${this.renderPowerShellGenericSection(component)}
              `
        }
        ${
          !terraform || sourceType === TerraformSourceType.SharedFolder
            ? html`<div class="full">
                ${this.text(
                  terraform ? 'Shared folder path' : 'Script path or JSON path',
                  'ScriptPath',
                  'Existing paths and JSON argument definitions are preserved. Use secure property references for secrets.'
                )}
              </div>`
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
