import { check } from 'k6';
import { vu } from 'k6/execution';
import http from 'k6/http';
import { config } from './test-config.js';
import { SharedArray } from 'k6/data';

// Load users from users.json
const users = new SharedArray('users', function () {
  return JSON.parse(open('./users.json'));
});

export const options = {
  vus: 50,
  duration: '5m',
  //iterations: 3,
};

export default function main() {
  const user = users[vu.idInInstance % users.length]; // Pick a user for this iteration. Use the modulo operator to distribute users among VUs

  RunMonitorRequestsPageRequests(config.api.baseUrl, user);
  RunMonitorResultsPageRequests(config.api.baseUrl, user, 2);
}

function RunMonitorRequestsPageRequests(baseApiAddress, user) {
  runSuccessGetRequest(baseApiAddress, '/MakeLikeProd/NotifyEmailAddress', user);
  runSuccessGetRequest(baseApiAddress, '/RefDataRoles', user);
  runSuccessPutRequest(baseApiAddress, '/RequestStatuses?page=1&limit=50', {"Filters":[],"SortOrders":[{"Path":"Id","Direction":"desc"}]}, user);
  runSuccessPutRequest(baseApiAddress, '/RequestStatuses?page=2&limit=50', {"Filters":[],"SortOrders":[{"Path":"Id","Direction":"desc"}]}, user);
}

function RunMonitorResultsPageRequests(baseApiAddress, user, count = 1) {
  for (let i = 0; i < count; i++) {
    runSuccessGetRequest(baseApiAddress, '/ApiConfig', user);
    runSuccessGetRequest(baseApiAddress, '/RefDataRoles', user);
    runSuccessGetRequest(baseApiAddress, '/RefDataUsers', user);
    runSuccessGetRequest(baseApiAddress, '/Metadata', user);

    var requestId = Math.floor(Math.random() * (1738416 - 1724812 + 1)) + 1724812;
    runSuccessGetRequest(baseApiAddress, `/RequestStatuses?requestId=${requestId}`, user);
    runSuccessGetRequest(baseApiAddress, `/ResultStatuses?requestId=${requestId}`, user);
    runSuccessGetRequest(baseApiAddress, `/RefDataProjects/Endur`, user);
  }
}

function runSuccessGetRequest(baseApiAddress, uri, user) {
  let response = http.get(`${baseApiAddress}${uri}`, {
    auth: 'ntlm',
    headers: {
      'user-agent': config.api.userAgent,
      'x-requested-with': 'XMLHttpRequest',
      'Authorization': `Bearer ${user.token}`
    },
  });

  checkResponse(response);
}

function runSuccessPutRequest(baseApiAddress, uri, body, user) {
  let response = http.put(`${baseApiAddress}${uri}`, JSON.stringify(body), {
    auth: 'ntlm',
    headers: {
      'user-agent': config.api.userAgent,
      'x-requested-with': 'XMLHttpRequest',
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${user.token}`
    },
  });

  checkResponse(response);
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
    let urlWithoutPass = response.request.url.replace(/:\/\/.*:.*@/, '://');
    console.error(
      `${urlWithoutPass} error, statusCode: ${response.status} ${response.status_text}, body: ${response.body}`
    );
  }
}