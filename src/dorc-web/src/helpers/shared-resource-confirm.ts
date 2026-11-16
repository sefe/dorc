import { confirmPrompt } from '../components/confirm-prompt';

export function confirmSharedResourceChange(
  item: { Name?: string | null; EnvironmentNames?: string[] | null },
  action: 'edit' | 'edit tags' | 'delete'
) {
  const environments = item.EnvironmentNames?.join(', ') || 'none';
  const consequence =
    action === 'delete'
      ? 'This permanently deletes the shared definition and removes its attachments from ALL environments. This is metadata only: it does NOT destroy deployed infrastructure.'
      : 'This changes the shared definition globally for ALL attached environments, not just this environment.';
  return confirmPrompt(
    `${action === 'delete' ? 'Delete' : 'Update'} ${item.Name ?? 'definition'}?\n\n${consequence}\n\nAttached environments: ${environments}`,
    {
      header: 'Shared definition',
      confirmText: action === 'delete' ? 'Delete definition' : 'Update globally'
    }
  );
}
