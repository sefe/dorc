const SESSION_EXPIRED_MESSAGE = 'Session has expired, please refresh the page.';
const CONNECTION_ERROR_MESSAGE =
  'Unable to contact the server. Check your connection and try again.';

type ErrorResponse = {
  Message?: string;
  ExceptionMessage?: string;
  message?: string;
  errors?: string[] | Record<string, unknown>;
  title?: string;
};

type ErrorLike = {
  status?: number;
  response?: ErrorResponse | string | null;
  message?: string;
};

function normalizeTransportMessage(message: string): string {
  const ajaxError = message.match(
    /^(?:AjaxError:\s*)?ajax error(?:\s+(\d+))?$/i
  );
  if (!ajaxError) {
    return message;
  }

  const status = ajaxError[1] ? Number(ajaxError[1]) : 0;
  if (status === 401) {
    return SESSION_EXPIRED_MESSAGE;
  }
  if (status === 0) {
    return CONNECTION_ERROR_MESSAGE;
  }
  return `Request failed with status ${status}.`;
}

/**
 * Retrieves a user-friendly error message from an error object.
 * @param err The error object, which may contain various properties.
 * @param baseMessage An optional base message to return if no specific error message is found.
 * @returns A string containing the error message.
 */
export function retrieveErrorMessage(
  err: unknown,
  baseMessage?: string
): string {
  let errorMessage =
    baseMessage ??
    'An unexpected error occurred. Please try again or contact support.';
  if (!err) {
    return errorMessage;
  }

  if (typeof err === 'string') {
    return normalizeTransportMessage(err);
  }
  if (err === null) {
    return errorMessage;
  }
  if (typeof err !== 'object') {
    return errorMessage;
  }

  const error = err as ErrorLike;
  if (error.status === 401) {
    errorMessage = SESSION_EXPIRED_MESSAGE;
  } else if (error.status === 0) {
    errorMessage = CONNECTION_ERROR_MESSAGE;
  } else if (typeof error.response === 'object' && error.response?.Message) {
    errorMessage = error.response.Message;
  } else if (
    typeof error.response === 'object' &&
    error.response?.ExceptionMessage
  ) {
    errorMessage = error.response.ExceptionMessage;
  } else if (typeof error.response === 'object' && error.response?.message) {
    errorMessage = error.response.message;
  } else if (typeof error.response === 'object' && error.response?.errors) {
    let errMessages = '';
    if (
      Array.isArray(error.response.errors) &&
      error.response.errors.length > 0
    ) {
      errMessages = error.response.errors.join('; ');
    } else if (typeof error.response.errors === 'object') {
      errMessages = Object.values(error.response.errors).flat().join('; ');
    }
    errorMessage =
      `${error.message ?? 'Request failed'}, ` +
      `${error.response.title ?? ''} ${errMessages}`.trim();
  } else if (typeof error.response === 'string') {
    errorMessage = normalizeTransportMessage(error.response);
  } else if (error.status) {
    errorMessage = `Request failed with status ${error.status}.`;
  } else if (error.message) {
    errorMessage = normalizeTransportMessage(error.message);
  }

  return errorMessage;
}
