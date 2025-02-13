import { check } from 'k6';
import { browser } from 'k6/browser';
import { Trend } from 'k6/metrics';
import { config } from './test-config.js';

export const options = {
  scenarios: {
    pageloaded20times: {
      executor: 'shared-iterations',
      vus: 2,
      iterations: 20,
      options: {        
        browser: {
          type: 'chromium',
        },
      },
    },    
  },
  thresholds: {
    browser_web_vital_lcp: ['p(95)<15000'],
    page_render_time: ['avg<7000', 'min>0'],
    checks: ['rate==1.0'],
  },
};

const myTrend = new Trend('page_render_time', true);

export default async function main() {
  const page = await browser.newPage();
  const pageAddr = `${config.web.baseUrl}/monitor-requests`;

  try {
    let totalRenderTime = await getRenderPageTime(page, 'monitor-requests', pageAddr, 'vaadin-button[title="View Detailed Results"]');
    if (totalRenderTime) myTrend.add(totalRenderTime);
  } finally {
    await page.close();
  }
}

async function getRenderPageTime(page, name, address, selectorToWait) {
  let resp = await page.goto(address);

  if (!check(resp, {
    'response received': () => !!resp,
  })) {
    console.error(`page not loaded, no response`)
    return;
  }

  if (!resp.ok()) {
    console.error(`page ${resp.url()} not available, statusCode: ${resp.status()} ${resp.statusText()}, body: ${resp.body()}`)
    return;
  }

  await page.evaluate(() => window.performance.mark(`${name}_page_render_start`));

  await page.waitForSelector(selectorToWait);

  await page.evaluate(() => window.performance.mark(`${name}_page_render_end`));

  // uncomment to see the loaded page screenshot
  //await page.screenshot({ path: `screenshots/${name}_1.png` });

  await page.evaluate(() => window.performance.measure(`${name}_page_render`, `${name}_page_render_start`, `${name}_page_render_end`)
  );

  return page.evaluate(
    () => JSON.parse(JSON.stringify(window.performance.getEntriesByName(`${name}_page_render`)))[0]
      .duration
  ); 
}

