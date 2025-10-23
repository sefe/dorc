import { AjaxError } from "rxjs/ajax";

/**
 * Retrieves a user-friendly error message from an error object.
 * @param err The error object, which may contain various properties.
 * @param baseMessage An optional base message to return if no specific error message is found.
 * @returns A string containing the error message.
 */
export function retrieveErrorMessage(
  err: AjaxError | string,
  baseMessage?: string
): string {
  let errorMessage = baseMessage ?? 'An unexpected error occurred. Please try again or contact support.';
  if (!err) {
    return errorMessage;
  }

  if (typeof err === 'string') {
      errorMessage = err;
  } else if (err.response?.Message) {
    errorMessage = err.response.Message;
  } else if (err.response?.ExceptionMessage) {
    errorMessage = err.response.ExceptionMessage;
  } else if (err.response && typeof err.response === 'string') {
    errorMessage = err.response;
  } else if (err.message) {
    errorMessage = err.message;
  }
  
  return errorMessage;
}