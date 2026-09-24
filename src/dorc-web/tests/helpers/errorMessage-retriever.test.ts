import { describe, expect, it } from 'vitest';
import { retrieveErrorMessage } from '../../src/helpers/errorMessage-retriever';

const ajaxError = (status: number, message = `ajax error ${status}`) => ({
  status,
  message,
  response: null
});

describe('retrieveErrorMessage', () => {
  it('explains that a 401 response means the session expired', () => {
    expect(retrieveErrorMessage(ajaxError(401))).toBe(
      'Session has expired, please refresh the page.'
    );
  });

  it('uses a connection message for status-zero transport errors', () => {
    expect(retrieveErrorMessage(ajaxError(0, 'ajax error'))).toBe(
      'Unable to contact the server. Check your connection and try again.'
    );
  });

  it('does not expose the generic AjaxError message for HTTP failures', () => {
    expect(retrieveErrorMessage(ajaxError(503))).toBe(
      'Request failed with status 503.'
    );
  });

  it('preserves a lowercase API response message', () => {
    expect(
      retrieveErrorMessage({
        status: 500,
        response: { message: 'Daemon already exists' }
      })
    ).toBe('Daemon already exists');
  });

  it('formats validation problem details without the transport message', () => {
    expect(
      retrieveErrorMessage({
        status: 400,
        message: 'ajax error 400',
        response: {
          title: 'One or more validation errors occurred.',
          errors: { Name: ['The Name field is required.'] }
        }
      })
    ).toBe(
      'One or more validation errors occurred. The Name field is required.'
    );
  });

  it('falls back to the HTTP status for an empty response body', () => {
    expect(
      retrieveErrorMessage({
        status: 503,
        message: 'ajax error 503',
        response: ''
      })
    ).toBe('Request failed with status 503.');
  });

  it.each([
    ['ajax error 401', 'Session has expired, please refresh the page.'],
    [
      'AjaxError: ajax error',
      'Unable to contact the server. Check your connection and try again.'
    ],
    ['ajax error 500', 'Request failed with status 500.']
  ])('normalizes a displayed transport string', (message, expected) => {
    expect(retrieveErrorMessage(message)).toBe(expected);
  });
});
