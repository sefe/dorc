import { css, html, nothing, TemplateResult } from 'lit';
import { Notification } from '@vaadin/notification';
import '@vaadin/icon';
import '@vaadin/icons/vaadin-icons';

const HEX_SHA_EXACT = /^[0-9a-f]{12,64}$/i;
const HEX_SHA_IN_TEXT = /\b([0-9a-f]{12,64})\b/gi;

export interface ShaBadgeOptions {
  label?: string;
  shortLength?: number;
}

export function isHexSha(value: string | null | undefined): boolean {
  if (!value) return false;
  return HEX_SHA_EXACT.test(value.trim());
}

export function formatShortSha(
  value: string | null | undefined,
  shortLength = 8
): string {
  if (!value) return '';
  const trimmed = value.trim();
  if (HEX_SHA_EXACT.test(trimmed)) {
    return trimmed.slice(0, shortLength).toLowerCase();
  }
  return trimmed;
}

export async function copyShaToClipboard(
  sha: string,
  triggerButton?: HTMLElement | null
): Promise<void> {
  const clean = sha.trim();
  if (!clean) return;

  try {
    if (navigator.clipboard?.writeText) {
      await navigator.clipboard.writeText(clean);
    } else {
      const textarea = document.createElement('textarea');
      textarea.value = clean;
      textarea.style.position = 'fixed';
      textarea.style.opacity = '0';
      document.body.appendChild(textarea);
      textarea.select();
      document.execCommand('copy');
      textarea.remove();
    }

    if (triggerButton) {
      triggerButton.setAttribute('data-copied', 'true');
      setTimeout(() => {
        triggerButton.removeAttribute('data-copied');
      }, 1500);
    }

    Notification.show(`Copied ${ formatShortSha(clean) } (${ clean })`, {
      position: 'bottom-start',
      duration: 2000
    });
  } catch {
    // Ignore clipboard permission errors in non-secure or headless test contexts
  }
}

export const shaBadgeStyles = css`
  .dorc-sha-meta {
    display: inline-flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 4px;
    font-size: var(--lumo-font-size-xs, 0.75rem);
    color: var(--dorc-text-secondary, var(--lumo-secondary-text-color));
    line-height: 1.3;
  }

  .dorc-sha-badge {
    display: inline-flex;
    align-items: center;
    gap: 3px;
    padding: 1px 5px;
    border-radius: 4px;
    border: 1px solid var(--lumo-contrast-10pct, rgba(0, 0, 0, 0.1));
    background: var(--lumo-contrast-5pct, rgba(0, 0, 0, 0.04));
    color: var(--dorc-text-secondary, var(--lumo-secondary-text-color));
    font-family: var(--lumo-font-family-monospace, monospace);
    font-size: var(--lumo-font-size-xxs, 0.6875rem);
    line-height: 1.25;
    cursor: pointer;
    opacity: 0.78;
    vertical-align: baseline;
    transition:
      opacity 0.12s ease,
      background-color 0.12s ease,
      border-color 0.12s ease;
  }

  .dorc-sha-badge:hover,
  .dorc-sha-badge:focus-visible {
    opacity: 1;
    background: var(--lumo-contrast-10pct, rgba(0, 0, 0, 0.08));
    border-color: var(--lumo-contrast-20pct, rgba(0, 0, 0, 0.2));
  }

  .dorc-sha-badge[data-copied='true'] {
    opacity: 1;
    color: var(--lumo-success-text-color, #15803d);
    border-color: var(--lumo-success-color-50pct, rgba(22, 163, 74, 0.4));
  }

  .dorc-sha-badge__label {
    font-family: var(--lumo-font-family, sans-serif);
    opacity: 0.8;
  }

  .dorc-sha-badge__icon {
    width: 10px;
    height: 10px;
    opacity: 0.65;
    flex-shrink: 0;
  }

  .dorc-sha-badge:hover .dorc-sha-badge__icon,
  .dorc-sha-badge:focus-visible .dorc-sha-badge__icon {
    opacity: 1;
  }
`;

export function renderShaBadge(
  sha: string | null | undefined,
  options?: ShaBadgeOptions
): TemplateResult | typeof nothing {
  if (!sha || !sha.trim()) return nothing;
  const full = sha.trim();
  const short = formatShortSha(full, options?.shortLength ?? 8);
  const label = options?.label?.trim();
  const titleText = label
    ? `${label}: ${full} — click to copy full SHA`
    : `${full} — click to copy full SHA`;
  const ariaLabel = label
    ? `Copy ${label} ${full}`
    : `Copy revision ${full}`;

  return html`<button
    type="button"
    class="dorc-sha-badge"
    data-sha=${full}
    title=${titleText}
    aria-label=${ariaLabel}
    @click=${(e: Event) => {
      e.stopPropagation();
      e.preventDefault();
      copyShaToClipboard(full, e.currentTarget as HTMLElement);
    }}
  >
    ${label ? html`<span class="dorc-sha-badge__label">${label}</span>` : nothing}
    <span class="dorc-sha-badge__value">${short}</span>
    <vaadin-icon
      class="dorc-sha-badge__icon"
      icon="vaadin:copy-o"
    ></vaadin-icon>
  </button>`;
}

/**
 * Formats a backend source evidence label (e.g.
 * "Observed PowerShell text SHA256 E3B0C442...",
 * "Submitted PowerShell definition E3B0C442...",
 * "Planned Catalog storage-account@1.0.0 · E3B0C442...")
 * so embedded 40/64-char hex SHAs become subtle, compact copyable badges
 * and duplicate DefinitionRevision hashes are not repeated twice.
 */
export function renderCompactSourceEvidence(
  sourceRevisionLabel: string | null | undefined,
  definitionRevision?: string | null
): TemplateResult {
  const rawLabel = (sourceRevisionLabel ?? '').trim();
  const cleanedLabel = rawLabel
    .replace(/\b(?:text|source)\s+SHA256\s+/gi, '')
    .replace(/\bSubmitted PowerShell definition\s+/i, 'PowerShell ')
    .replace(/\bSubmitted revision\s+/i, 'Revision ');

  const foundShas = new Set<string>();
  const parts: Array<string | TemplateResult | typeof nothing> = [];

  if (cleanedLabel) {
    let lastIndex = 0;
    HEX_SHA_IN_TEXT.lastIndex = 0;
    let match: RegExpExecArray | null;
    while ((match = HEX_SHA_IN_TEXT.exec(cleanedLabel)) !== null) {
      if (match.index > lastIndex) {
        parts.push(cleanedLabel.slice(lastIndex, match.index));
      }
      const sha = match[1];
      foundShas.add(sha.toLowerCase());
      parts.push(renderShaBadge(sha));
      lastIndex = match.index + sha.length;
    }
    if (lastIndex < cleanedLabel.length) {
      parts.push(cleanedLabel.slice(lastIndex));
    }
  } else {
    parts.push('Executed source not recorded');
  }

  const defRev = (definitionRevision ?? '').trim();
  const defAlreadyShown =
    !defRev ||
    foundShas.has(defRev.toLowerCase()) ||
    cleanedLabel.toLowerCase().includes(defRev.toLowerCase());

  return html`<span class="dorc-sha-meta">
    <span>${parts}</span>
    ${!defAlreadyShown
      ? html`${renderShaBadge(defRev, { label: 'def' })}`
      : nothing}
  </span>`;
}
