/**
 * UI mirror of the backend tag limits (Dorc.ApiModel/TagLimits.cs — the two cannot
 * share a symbol across the C#/TypeScript boundary). Agreement is proven by
 * tests/helpers/tag-limits.test.ts.
 */

/**
 * Longest a SINGLE tag may be — the width of deploy.DatabaseTag.Tag and
 * deploy.ServerTag.Tag, and what the API's [TagSet] validation enforces.
 */
export const MAX_TAG_LENGTH = 400;

/**
 * Longest the DEPRECATED joined form may be. Applies only where a delimited
 * string still crosses a boundary; it goes with the columns it describes.
 */
export const MAX_TAG_STRING_LENGTH = 4000;
