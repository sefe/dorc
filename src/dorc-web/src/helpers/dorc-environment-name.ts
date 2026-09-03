export function dorcEnvironmentNameFromMetadata(
  metadata: unknown
): string | undefined {
  if (typeof metadata !== 'string') return undefined;
  return metadata.split('-')[0].trim();
}
