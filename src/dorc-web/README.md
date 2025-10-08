# DOrc Web UI

Modern web interface for DOrc built with Lit 3, TypeScript, and Vaadin components.

## Quick Start

### Install the dependencies

```bash
npm install
```

### Start the development server

This command serves the app at `http://localhost:8888`:

```bash
npm run dev
```

### Build for production

```bash
npm run build
```

This creates an optimized production build using Vite.

## Project Structure

```
dorc-web/
├─ .husky/                  # Git hooks for code quality
├─ images/                  # Static image assets
├─ k6-tests/                # K6 load and performance tests
├─ public/                  # Static public assets
│  └─ health/               # Health check endpoint
├─ src/
│  ├─ apis/                 # Generated API clients
│  │  ├─ dorc-api/          # DOrc API client (TypeScript)
│  │  └─ azure-devops-build/ # Azure DevOps client
│  ├─ components/           # Reusable Web Components
│  │  ├─ add-*.ts           # Dialog components for adding entities
│  │  ├─ attached-*.ts      # Components showing related entities
│  │  └─ ...                # Various UI components
│  ├─ helpers/              # Utility functions and base classes
│  ├─ icons/                # SVG icon components
│  ├─ pages/                # Page-level components
│  │  ├─ page-deploy.ts     # Deployment page
│  │  ├─ page-projects-list.ts
│  │  ├─ page-environments-list.ts
│  │  └─ ...                # Other pages
│  ├─ router/               # Application routing
│  │  └─ routes.ts          # Route definitions
│  ├─ services/             # Business logic services
│  ├─ app-config.ts         # Application configuration
│  └─ global-cache.ts       # Global state management
├─ index.html               # Main application entry point
├─ signin.html              # OAuth sign-in page
├─ signin-callback.html     # OAuth callback page
├─ signout-callback.html    # OAuth sign-out callback
├─ package.json             # Dependencies and scripts
├─ tsconfig.json            # TypeScript configuration
├─ vite.config.js           # Vite build configuration
├─ eslint.config.js         # ESLint configuration
└─ web.config               # IIS configuration for deployment
```

### Key Directories

- **`src/apis/`** - Auto-generated API client libraries. Don't edit manually.
- **`src/components/`** - Reusable Web Components built with Lit. Used across multiple pages.
- **`src/pages/`** - Top-level page components. Each page represents a route in the application.
- **`src/router/`** - Vaadin Router configuration for client-side routing.
- **`src/services/`** - Business logic and API interaction services.
- **`src/helpers/`** - Utility functions, helper classes, and base components.
- **`images/`** - Static images served directly.
- **`public/`** - Static assets that don't require processing.
- **`k6-tests/`** - Performance and load testing scripts.

## Development Guides

### Available Scripts

- **`npm run dev`** - Start development server with hot reload
- **`npm run build`** - Build optimized production bundle
- **`npm run preview`** - Preview production build locally
- **`npm run format`** - Format code with Prettier
- **`npm run type-checking`** - Run TypeScript type checking
- **`npm run dorc-api-gen`** - Regenerate DOrc API client from swagger.json

### Build for Production

This command uses Vite to build an optimized version of the application for production:

```bash
npm run build
```

The output is generated in the `dist/` directory and includes:
- Optimized and minified JavaScript bundles
- Processed CSS
- Static assets
- Multiple HTML entry points (main app, sign-in pages)

### Adding Static Assets

Place static files in the `public/` directory - they will be copied as-is to the build output. For images and resources that should be processed by Vite, use the `images/` directory and import them in your components.

### Create a New Page

1. Create a new page component in the `src/pages/` folder:

   ```typescript
   import { LitElement, html, css, customElement } from 'lit';
   import { customElement } from 'lit/decorators.js';

   @customElement('page-explore')
   export class PageExplore extends LitElement {
     static styles = css`
       :host {
         display: block;
         padding: 1rem;
       }
     `;

     render() {
       return html`
         <h1>Explore</h1>
         <p>My new explore page!</p>
       `;
     }
   }
   ```

2. Register the route in `src/router/routes.ts`:

   ```typescript
   {
     path: '/explore',
     name: 'explore',
     component: 'page-explore',
     action: async () => {
       await import('../pages/page-explore');
     }
   }
   ```

3. Add navigation link in the appropriate component (e.g., navbar).

### Application Configuration

The application configuration is managed in `src/app-config.ts`. This file contains:

- API base URLs
- OAuth/OIDC settings
- Feature flags
- Environment-specific settings

To use configuration values in your components:

```typescript
import { appConfig } from './app-config';

// Access configuration
const apiUrl = appConfig.apiBaseUrl;
```

For environment-specific builds, you can set environment variables during the build process or modify the configuration file directly.

## Browser support

- Chrome
- Edge
- Firefox
- Safari

To run on other browsers, you need to use a combination of polyfills and transpilation.
This step is automated by the [build for production command](#build-for-production).

## API Client Generation

### DOrc API Client

The TypeScript client for the DOrc API is auto-generated from the OpenAPI/Swagger specification.

#### Prerequisites

Install the OpenAPI Generator CLI:

```bash
npm install @openapitools/openapi-generator-cli -g
```

#### Regenerate the Client

From the `dorc-web` directory:

```bash
npm run dorc-api-gen
```

Or manually:

```bash
openapi-generator-cli generate -g typescript-rxjs \
  -i ./src/apis/dorc-api/swagger.json \
  -o ./src/apis/dorc-api/ \
  --additional-properties=supportsES6=true,npmVersion=9.4.0,typescriptThreePlus=true \
  --skip-validate-spec
```

**Note:** The `swagger.json` file should be obtained from the running DOrc API at `/swagger/v1/swagger.json`.

### Azure DevOps Client

The Azure DevOps Build API client is also auto-generated. Azure DevOps API specifications come from: https://github.com/MicrosoftDocs/vsts-rest-api-specs

To regenerate (from the appropriate location):

```bash
openapi-generator-cli generate -g typescript-rxjs \
  -i ./build.json \
  --skip-validate-spec \
  --additional-properties=supportsES6=true
```


## Performance Testing with K6

DOrc uses [K6](https://k6.io/) by Grafana for load and performance testing. K6 can test both the API and browser interactions.

### Installation

**Windows:**
```bash
winget install k6 --source winget
```

**macOS:**
```bash
brew install k6
```

**Linux:**
See [K6 installation docs](https://grafana.com/docs/k6/latest/set-up/install-k6/)

### Configuration

Before running tests, update `k6-tests/test-config.json` with:
- Your DOrc API base URL
- Authentication credentials if needed

For load tests using multiple users, update `k6-tests/users.json` with test user credentials.

### Running Tests

Basic test run:
```bash
k6 run k6-tests/monitor-request-page-test.js
```

Load test with multiple users:
```bash
k6 run k6-tests/load-test-many-users.js
```

### Web Dashboard

View real-time test results in a web dashboard:

```bash
k6 run --out web-dashboard k6-tests/load-test-many-users.js
```

Access the dashboard at: http://127.0.0.1:5665/

### Troubleshooting

**HTTP/2 Error:** If you encounter `stream error: HTTP_1_1_REQUIRED`, disable HTTP/2:

Windows:
```cmd
SET GODEBUG=http2client=0
k6 run k6-tests/monitor-request-page-test.js
```

Linux/macOS:
```bash
GODEBUG=http2client=0 k6 run k6-tests/monitor-request-page-test.js
```

See [K6 Community Discussion](https://community.grafana.com/t/stream-error-stream-id-1-http-1-1-required-received-from-peer/96607/7) for more details.

## Technology Stack

- **Lit 3** - Modern, lightweight web components library
- **TypeScript** - Type-safe JavaScript
- **Vaadin Components 24** - Enterprise-grade UI components
- **Vite** - Fast build tool and dev server
- **Vaadin Router** - Client-side routing
- **RxJS** - Reactive programming for API calls
- **OIDC Client** - OAuth/OpenID Connect authentication
- **SignalR** - Real-time server communication
- **ECharts** - Data visualization
- **Ace Editor** - Code editing components
