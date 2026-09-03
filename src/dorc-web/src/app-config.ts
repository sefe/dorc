let dorcApi = 'NotSet';
let routePrefix = 'NotSet';
let dorcHelperPage = 'NotSet';
let _authenticationScheme: string = 'NotSet';

try {
  const response = await fetch('./appconfig.json');
  if (response.ok) {
    const result: {
      api: string;
      routePrefix: string;
      AzureDevOpsAccessToken: string;
      dorcHelperPage: string;
    } = await response.json();
    dorcApi = result.api;
    routePrefix = result.routePrefix;
    dorcHelperPage = result.dorcHelperPage;
  }
} catch {
  console.error('Failed to load appconfig.json');
}

export default class AppConfig {
  public appName = 'DOrc';
  public appDescription = 'DOrc Web UI';
  public dorcApi = dorcApi;
  public routePrefix = routePrefix;
  public dorcHelperPage = dorcHelperPage;

  get authenticationScheme() {
    return _authenticationScheme ?? 'NotSet';
  }

  set authenticationScheme(value: string) {
    _authenticationScheme = value;
  }

  pauseDeploymentEnabled = false;
  isProduction = false;
}

export const appConfig = new AppConfig();