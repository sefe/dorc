/**
 * Icon mapping utility for migrating from old icon sets to DOrc custom icons
 */

export interface IconMapping {
  oldIcon: string;
  newIcon: string;
  description: string;
}

/**
 * Map of old icon names to new DOrc icon names
 */
export const ICON_MAPPING: Record<string, string> = {
  // Action icons
  'lumo:edit': 'dorc:edit',
  'editor:mode-edit': 'dorc:edit',
  'icons:delete': 'dorc:delete',
  'icons:save': 'dorc:save',
  'icons:refresh': 'dorc:refresh',
  'icons:content-copy': 'dorc:copy',
  'icons:clear': 'dorc:clear',
  'lumo:cross': 'dorc:close',
  
  // Media/Control icons
  'av:play': 'dorc:play',
  'vaadin:play': 'dorc:play',
  'av:stop': 'dorc:stop',
  'vaadin:stop': 'dorc:stop',
  'av:repeat': 'dorc:repeat',
  
  // Security icons
  'vaadin:lock': 'dorc:lock',
  'vaadin:unlock': 'dorc:unlock',
  'vaadin:key': 'dorc:key',
  'vaadin:safe': 'dorc:safe',
  
  // Connectivity icons
  'vaadin:link': 'dorc:link',
  'icons:link': 'dorc:link',
  'vaadin:unlink': 'dorc:unlink',
  'vaadin:connect': 'dorc:connect',
  
  // Infrastructure icons
  'vaadin:server': 'dorc:server',
  'vaadin:database': 'dorc:database',
  'hardware:developer-board': 'dorc:environment',
  'hardware:desktop-windows': 'dorc:desktop',
  'vaadin:cube': 'dorc:container',
  'vaadin:cubes': 'dorc:container',
  
  // User management icons
  'vaadin:user': 'dorc:user',
  'vaadin:users': 'dorc:users',
  'social:group': 'dorc:group',
  'social:group-add': 'dorc:group-add',
  'vaadin:calendar-user': 'dorc:user',
  
  // Configuration icons
  'vaadin:cog': 'dorc:settings',
  'vaadin:cogs': 'dorc:settings',
  'vaadin:options': 'dorc:settings',
  'vaadin:automation': 'dorc:automation',
  'vaadin:compile': 'dorc:settings',
  'vaadin:curly-brackets': 'dorc:code',
  
  // Data management icons
  'vaadin:archive': 'dorc:archive',
  'vaadin:archives': 'dorc:archive',
  'vaadin:package': 'dorc:package',
  'vaadin:records': 'dorc:list',
  'vaadin:list': 'dorc:list',
  'vaadin:list-select': 'dorc:list',
  'vaadin:chart-grid': 'dorc:list',
  
  // Navigation icons
  'lumo:menu': 'dorc:menu',
  'vaadin:expand-square': 'dorc:expand',
  'vaadin:ellipsis-dots-h': 'dorc:more',
  'maps:directions-bus': 'dorc:automation',
  
  // Status/Info icons
  'vaadin:info-circle': 'dorc:info',
  'notification:sms-failed': 'dorc:error',
  'icons:history': 'dorc:history',
  'vaadin:pin': 'dorc:settings',
  
  // Communication icons
  'vaadin:at': 'dorc:notification',
  'vaadin:clipboard': 'dorc:clipboard',
  'vaadin:clipboard-pulse': 'dorc:clipboard',
  'vaadin:search': 'dorc:search',
  
  // Custom icons
  'inline:powershell-icon': 'dorc:powershell',
  'inline:variables-icon': 'dorc:variables',
  'line awesome-svg:chess-king-solid': 'dorc:admin',
  
  // Additional mappings for missing icons
  'vaadin:close-small': 'dorc:close-small',
  'vaadin:refresh': 'dorc:refresh',
  'vaadin:child': 'dorc:child',
  
  // Vaadin tags (keeping as settings for now)
  'vaadin:tags': 'dorc:settings',
};

/**
 * Get the new DOrc icon name for an old icon
 * @param oldIcon The old icon name (e.g., 'vaadin:server')
 * @returns The new DOrc icon name (e.g., 'dorc:server')
 */
export function mapIcon(oldIcon: string): string {
  const newIcon = ICON_MAPPING[oldIcon];
  if (!newIcon) {
    console.warn(`No mapping found for icon: ${oldIcon}. Using dorc:info as fallback.`);
    return 'dorc:info';
  }
  return newIcon;
}

/**
 * Get all mapped icons for debugging/validation
 */
export function getAllMappedIcons(): IconMapping[] {
  return Object.entries(ICON_MAPPING).map(([oldIcon, newIcon]) => ({
    oldIcon,
    newIcon,
    description: `${oldIcon} → ${newIcon}`
  }));
}

/**
 * Validate that all required DOrc icons exist for the mappings
 */
export function validateIconMappings(): string[] {
  const uniqueNewIcons = [...new Set(Object.values(ICON_MAPPING))];
  const missingIcons: string[] = [];
  
  // This would be extended with actual validation against the iconset
  uniqueNewIcons.forEach(iconName => {
    const iconId = iconName.replace('dorc:', '');
    // Basic validation that the icon name is properly formatted
    if (!iconId || iconId.length === 0) {
      missingIcons.push(iconName);
    }
  });
  
  return missingIcons;
}