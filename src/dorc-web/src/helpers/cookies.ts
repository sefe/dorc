/*
 * General utils for managing cookies in Typescript.
 */
export function setCookie(name: string, val: string) {
  const date = new Date();
  const encodedValue = encodeURIComponent(val);

  // Set to expire in 7 days
  date.setTime(date.getTime() + 7 * 24 * 60 * 60 * 1000);

  // Only add Secure flag when not on localhost (for dev flexibility)
  const isLocalhost =
    window.location.hostname === 'localhost' ||
    window.location.hostname === '127.0.0.1';
  const secureFlag = isLocalhost ? '' : ' Secure;';

  document.cookie = `${name}=${encodedValue}; expires=${date.toUTCString()}; path=/;${secureFlag} SameSite=Lax`;
}

export function getCookie(name: string): string {
  const value = `; ${document.cookie}`;
  const parts = value.split(`; ${name}=`);

  if (parts.length === 2) {
    const cookieValue = parts.pop()?.split(';').shift();
    return cookieValue ? decodeURIComponent(cookieValue) : '';
  }
  return '';
}

export function deleteCookie(name: string) {
  const date = new Date();

  // Set to expire in the past to delete
  date.setTime(date.getTime() - 24 * 60 * 60 * 1000);

  // Match security attributes with setCookie
  const isLocalhost =
    window.location.hostname === 'localhost' ||
    window.location.hostname === '127.0.0.1';
  const secureFlag = isLocalhost ? '' : ' Secure;';

  document.cookie = `${name}=; expires=${date.toUTCString()}; path=/;${secureFlag} SameSite=Lax`;
}
