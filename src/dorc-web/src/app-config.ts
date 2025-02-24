let executed = false;
let dorcApi = 'NotSet';
let routePrefix = 'NotSet';
let azureDevOpsAccessToken = 'NotSet';
let dorcHelperPage = "NotSet";

export default class AppConfig {
  public appName = 'DOrc';
  public appDescription = 'DOrc Web UI';
  public dorcApi = 'NotSet';
  public routePrefix = 'NotSet';
  public azureDevOpsAccessToken = 'NotSet';
  public dorcHelperPage = "NotSet"

  constructor() {
    if (!executed) {
      const filePath = `./appconfig.json`;

      let result: {
        api: string;
        routePrefix: string;
        AzureDevOpsAccessToken: string;
        dorcHelperPage: string;
      };
      const xmlHttp = new XMLHttpRequest();
      xmlHttp.open('GET', filePath, false);
      xmlHttp.send();
      if (xmlHttp.status === 200) {
        result = JSON.parse(xmlHttp.responseText);
        dorcApi = result.api;
        routePrefix = result.routePrefix;
        azureDevOpsAccessToken = result.AzureDevOpsAccessToken;
        dorcHelperPage = result.dorcHelperPage;
        executed = true;
      }
    }
    this.routePrefix = routePrefix;
    this.dorcApi = dorcApi;
    this.azureDevOpsAccessToken = azureDevOpsAccessToken;
    this.dorcHelperPage = dorcHelperPage;
  }
}

export const appConfig = new AppConfig();