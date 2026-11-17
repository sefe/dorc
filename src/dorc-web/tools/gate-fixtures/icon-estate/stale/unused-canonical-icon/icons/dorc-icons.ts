export const DORC_ICON_NAMES = ['edit', 'orphan-icon'] as const;

const DORC_ICON_DEFS = {
  edit: { viewBox: '0 0 24 24', svg: '<path d="M3 17v4h4z"></path>' },
  'orphan-icon': {
    viewBox: '0 0 24 24',
    svg: '<path d="M4 4h16v16H4z"></path>'
  }
} as const;

export { DORC_ICON_DEFS };
