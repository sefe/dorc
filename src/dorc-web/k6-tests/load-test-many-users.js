import { check, __VU } from 'k6';
import http from 'k6/http';
import { config } from './test-config.js';
import { SharedArray } from 'k6/data';

// Load users from users.json
const users = new SharedArray('users', function () {
  return JSON.parse(open('./users.json'));
});

export const options = {
  vus: 10,
  duration: '1m',
  //iterations: 10,
};

export default function main() {
  // Pick a user for this iteration
  const user = users[__VU % users.length]; // Use the modulo operator to distribute users among VUs

  let ntlmUser = `${encodeURIComponent(user.username)}:${encodeURIComponent(user.password)}@`;
  let baseApiAddress = config.api.baseUrl.replace('://', `://${ntlmUser}`);

  RunGetRequests(baseApiAddress, user);
  RunFailedGetRequests(baseApiAddress);
}

function RunGetRequests(baseApiAddress, user) {
  if (user.isAdmin) {
    runSuccessGetRequest(baseApiAddress, '/PropertyValues?propertyName=IDF_ClientSecret&environmentName=IDF%20DV%2001');
    runSuccessGetRequest(baseApiAddress, '/RefDataEnvironments?env=Endur%20FF%2002');
  }

  runSuccessGetRequest(baseApiAddress, '/');
  runSuccessGetRequest(baseApiAddress, '/Metadata');
  runSuccessGetRequest(baseApiAddress, '/DaemonStatus/Coral%20PP%2002');
  runSuccessGetRequest(baseApiAddress, '/MakeLikeProd/NotifyEmailAddress');
  runSuccessGetRequest(baseApiAddress, '/RefDataProjects/EmGen');
  runSuccessGetRequest(baseApiAddress, '/RefDataProjects/Pricing%20Service');
}

function runSuccessGetRequest(baseApiAddress, uri) {
  let response = http.get(`${baseApiAddress}${uri}`, {
    auth: 'ntlm',
    headers: {
      'user-agent': config.api.userAgent,
      'x-requested-with': 'XMLHttpRequest',
    },
  });

  checkResponse(response);
}

function RunFailedGetRequests(baseApiAddress) {
  let response = http.get(`${baseApiAddress}/DaemonStatus/`, {
    auth: 'ntlm',
    headers: {
      'user-agent': config.api.userAgent,
      'x-requested-with': 'XMLHttpRequest',
    },
  });

  check(response, {
    'response code was 500': (res) => res.status == 500,
  });
}

function checkResponse(response) {
  if (
    !check(response, {
      'response received': () => !!response,
    })
  ) {
    console.error(`no response`);
    return;
  }

  check(response, {
    'response code was 200': (res) => res.status == 200,
  });

  if (response.error_code) {
    console.error(
      `${response.url} error, statusCode: ${response.status} ${response.status_text}, body: ${response.body}`
    );
  }
}