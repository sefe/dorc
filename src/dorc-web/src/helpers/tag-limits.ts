/**
 * UI mirror of the backend tag capacity (Dorc.ApiModel/TagLimits.cs — the two cannot
 * share a symbol across the C#/TypeScript boundary). Agreement with the API contract is
 * proven by tests/helpers/tag-limits.test.ts, which asserts the committed swagger.json
 * maxLength for the tag fields equals this constant.
 */
export const MAX_TAG_STRING_LENGTH = 4000;
