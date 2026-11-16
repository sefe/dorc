/**
 * Mirrors Monitor's TerraformStateKeySanitizer.Sanitize, including escaping
 * original UTF-16 code units (not lowercased Unicode or whole code points).
 */
export const sanitizeTerraformStateKeyPart = (raw: string): string =>
  raw
    ? raw
        .split('')
        .map(character => {
          const lower = character.toLowerCase();
          return /^[a-z0-9-]$/.test(lower)
            ? lower
            : `_${character.charCodeAt(0).toString(16).padStart(4, '0')}`;
        })
        .join('')
    : 'unknown';

/** Only a resource-group ARM identity, not a display label or child resource. */
export const resourceGroupNameFromArmId = (
  identity: string | null | undefined
): string | null => {
  const match = identity?.match(
    /^\/subscriptions\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\/resourceGroups\/([\p{L}\p{M}\p{N}_.()-]{1,90})$/iu
  );
  return match && match[0] === identity && !match[1].endsWith('.')
    ? match[1]
    : null;
};
