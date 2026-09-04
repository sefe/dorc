/**
 * Utility functions for handling tags.
 *
 * Tags are a set — stored as rows server-side and carried as arrays through the
 * API. The delimited form survives only where the API still hands one over.
 */

/**
 * Normalises a tag set: trims, drops empties, dedupes, sorts for stable display.
 * Accepts the delimited form too, for the few places the API still returns one.
 */
export function normaliseTags(
  tags: string[] | string | undefined | null
): string[] {
  if (!tags) {
    return [];
  }

  const raw = typeof tags === 'string' ? tags.split(';') : tags;

  return Array.from(
    new Set(raw.map(tag => tag.trim()).filter(tag => tag.length > 0))
  ).sort((a, b) => a.localeCompare(b));
}

/**
 * Splits a semicolon-separated string of tags into a sorted array of non-empty
 * tags. Retained for the delimited values that still arrive from elsewhere.
 */
export function splitTags(
  tags: string[] | string | undefined | null
): string[] {
  return normaliseTags(tags);
}

/**
 * Joins a tag set into a semicolon-separated string, for the surfaces that still
 * take one.
 */
export function joinTags(tags: string[] | undefined): string {
  if (!tags || tags.length === 0) {
    return '';
  }

  return tags.join(';');
}

/**
 * Tag membership (mirrors the backend contract): exact match after trimming, and
 * a null/empty/whitespace tag or tag set never matches.
 */
export function hasTag(
  tags: string[] | string | undefined | null,
  tag: string | undefined | null
): boolean {
  if (!tag || tag.trim().length === 0) {
    return false;
  }
  const sought = tag.trim();
  return normaliseTags(tags).some(t => t === sought);
}
