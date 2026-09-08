export function getShortLogonName(username: string | null | undefined): string {
  if (username) {
    if (username.includes('\\')) {
      return username.split('\\')[1];
    } else {
      return username;
    }
  }
  return '';
}
