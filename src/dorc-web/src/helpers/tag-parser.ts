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

/**
 * Tag membership over a semicolon-separated tag string (mirrors the backend
 * TagString.HasTag contract): exact per-entry match after trimming, and a
 * null/empty/whitespace tag or tag string never matches.
 * @param tagsString The semicolon-separated string of tags
 * @param tag The single tag sought
 * @returns true when the tag list contains an entry equal to the trimmed tag
 */
export function hasTag(
  tagsString: string | undefined | null,
  tag: string | undefined | null
): boolean {
  if (!tag || tag.trim().length === 0) {
    return false;
  }
  const sought = tag.trim();
  return splitTags(tagsString).some(t => t.trim() === sought);
} 