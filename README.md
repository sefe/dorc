# DOrc (DevOps Deployment Orchestrator)
## Description
This project is a DevOps Engine for running Powershell deployments whilst managing environments & configuration 

## Getting Started

### Dependencies

* .NET 8 / .NET Framework  4.8
* WiX 5
* Lit-element
* Vaadin Components
* Vite

### Installing

* Both the web page components and .NET componnets need building before installing
* A install.cmd file is located in the Installer folder to allow for installation of the suite

### Executing program

* How to run the program
* Run the API project first in Development in Visual Studio
* Run the Web page 
```
npm run dev
```

### Update client libraries
* DOrc API
```
openapi-generator-cli generate -g typescript-rxjs -i ..\dorc-api\swagger.json --skip-validate-spec
```
* Azure DevOps Build - To regenerate the Azure DevOps libaries use: 
```
openapi-generator-cli generate -g csharp -i .\build.json --skip-validate-spec
```
Azure DevOps json specs come from: https://github.com/MicrosoftDocs/vsts-rest-api-specs

## Contributions

SEFE welcomes contributions into this solution; please refer to the CONTRIBUTING.md file for details

## Authors

The solution is designed and built by SEFE Securing Energy for Europe Gmbh.

SEFE - [Visit us online](https://www.sefe.eu/)

## License

This project is licensed under the [Apache 2.0] License - see the LICENSE-2.0.txt file for details
