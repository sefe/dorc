### Install the dependencies

    npm install

### Start the development server

This command serves the app at `http://localhost:8888`:

    npm run dev

### Project structure

```
├─ images/
├─ patches/
├─ server/
├─ src/
│  ├─ components/
│  │  ├─ dorc-navbar.ts
│  │  └─ ···
│  ├─ helpers/
│  │  ├─ page-element.ts
│  │  └─ ···
│  ├─ pages/
│  │  ├─ page-deploy.ts
│  │  └─ ···
│  ├─ router/
│  │  └─ routes.ts
│  └─ app-config.ts
├─ index.html
├─ manifest.webmanifest
├─ package.json
├─ robots.txt
├─ rollup.config.mjs
└─ tsconfig.json
```

- `images`: is use to store the static resourced used by your application.
- `patches`: contains the patches to apply in the different packages mentioned [here](#things-to-be-aware). It will be removed at some point.
- `server`: contains the logic to serve the application. And is where you are going to create your `dist/` folder containing the bundle of your application.
- `src`
  - `components`: contains your custom Web Components. Inside this folder you will find the `dorc-navbar.ts` file, main root of your application following the PRPL patern.
  - `helpers`: contains two interesting features: `PageElement` and `html-meta-manager`. Go more in-depth with them [here](#create-a-new-page).
  - `pages`: where you create the pages for your application.
  - `router`: where you create the routes for your application.
  - `app-config.ts`: stores the application configuration variables. Go more in-depth with it [here](#environment-configuration).
- `index.html`: the application entry point.

## Guides

### Build for production

This command use Rollup to build an optimized version of the application for production:

    npm run build

It has two outputs: in addition to outputting a regular build, it outputs a legacy build which is compatible with older browsers down to IE11.

At runtime it is determined which version should be loaded, so that legacy browsers don't force to ship more and slower code to most users on modern browsers.

Note: If you need to add static files to the build, like the `images` folder or the `manifest.webmanifest`, you should register them in the `copy()` plugin of the `rollup.config.mjs`.

### Create a new page

1. Create the new page component (extending from `PageElement` helper) in the `pages` folder. For example a `page-explore.ts`.

   ```typescript
   import { html, customElement } from 'lit';

   import { PageElement } from '../helpers/page-element';

   @customElement('page-explore')
   export class PageExplore extends PageElement {
     render() {
       return html`
         <h1>Explore</h1>
         <p>My new explore page!</p>
       `;
     }
   }
   ```

2. Register the new route in the `routes.ts`:

   ```typescript
   {
     path: '/explore',
     name: 'explore',
     component: 'page-explore',
     metadata: {
       title: 'Explore',
       description: 'Explore page description'
     },
     action: async () => {
       await import('../pages/page-explore');
     }
   },
   ```

With SEO in mind, this project offers you the `PageElement` base class to help you to deal with it; it has a `metadata()` method that edits the HTML meta tags of the specific page with the `metadata` property defined in the route. And if you need dynamic information, you also can override the `metadata()` method.

### Environment configuration

This project allows different configurations per environment. The file that manages that configuration is `src/app-config.ts`. If you are interested in overwrite any of the configuration variables depending of the environment, you can create a file following the rule `src/config.{NODE_ENV}.ts`. Take into account that you don't need to replicate all the variables, just change the variable that you need to be different this way:

```typescript
import config from './config';

export default {
  ...config,
  environment: 'staging'
};
```

In the build process the references in the project (but not in the configuration files) of `./config` will be replaced to `./config.{NODE_ENV}` loading the expected configuration file for the target environment.

Lastly, the way to use that configuration is quite simple. You only need to import it:

```typescript
import config from '../config';
```

And use it where you need it:

```typescript
render() {
  return html`
    <footer>
      <span>Environment: ${config.environment}</span>
    </footer>
  `;
}
```

## Browser support

- Chrome
- Edge
- Firefox
- Safari

To run on other browsers, you need to use a combination of polyfills and transpilation.
This step is automated by the [build for production command](#build-for-production).

## Open API generator for dorc API

https://openapi-generator.tech/docs/installation/

```cmd
npm install @openapitools/openapi-generator-cli -g
cd src
cd /apis/
mkdir dorc-api-gen
cd dorc-api-gen
```

Save the latest json file to swagger.json first
then cd into a new directory where you want the output
use command:

```cmd
openapi-generator-cli generate -g typescript-rxjs -i ..\dorc-api\swagger.json --skip-validate-spec --additional-properties=supportsES6=true
```

azure json specs come from: https://github.com/MicrosoftDocs/vsts-rest-api-specs


## K6 load tests
K6 grafana javascript framework is used to test the performance of the DOrc.
It can test both API and browser page, see https://grafana.com/docs/k6/latest

### Install K6 on Windows:

```cmd
winget install k6 --source winget
```

### Run tests
In order to run tests, test-config.json should be updated with proper baseUrl.

example command:
```cmd
k6 run k6-tests/monitor-request-page-test.js
```

For load test, there is also file users.json with the list of users to use for requests. It also should be updated before running test.

It's possible to see realtime dashboard, or output to csv or json file to see later (with tool GNUplot for example).
To make output to web-dashboard, add __--out web-dashboard__ option, example: 

```cmd
k6 run --out web-dashboard ./k6-tests/load-test-many-users.js 
```
URL for web dashboard: http://127.0.0.1:5665/

### K6 test issues
If getting error __stream error: stream ID 19; HTTP_1_1_REQUIRED;__, set env variable CODEBUG to http2client=0:
Windows:
```cmd
SET GODEBUG=http2client=0
```
see https://community.grafana.com/t/stream-error-stream-id-1-http-1-1-required-received-from-peer/96607/7

## Help

https://wiki/display/gdq/DOrc+Help
