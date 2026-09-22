/**
 * Utility functions for handling application tags
 */

/**
 * Splits a semicolon-separated string of tags into a sorted array of non-empty tags
 * @param tagsString The semicolon-separated string of tags
 * @returns Array of sorted, non-empty tags
 */
export function splitTags(tagsString: string | undefined | null): string[] {
  if (!tagsString || tagsString.length === 0) {
    return [];
  }

  return tagsString
    .split(';')
    .filter(tag => tag.trim().length > 0)
    .sort((a, b) => a.localeCompare(b));
}

/**
 * Joins an array of tags into a semicolon-separated string
 * @param tags Array of tags to join
 * @returns Semicolon-separated string of tags
 */
export function joinTags(tags: string[] | undefined): string {
  if (!tags || tags.length === 0) {
    return '';
  }

  return tags.join(';');
}
