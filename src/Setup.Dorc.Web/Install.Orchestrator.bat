@echo off
cls
pushd %~dp0
popd
set MYDIR=%CD%
echo Directory of this batch fil: %MYDIR%

rem One property block for all four packages: MSI ignores properties a
rem package does not declare, so the manual helper stays a single list
rem rather than four that drift apart. Order matches MsiFileNames in
rem DeploySettings.template.json.
for %%P in (Setup.Dorc.Api.msi Setup.Dorc.Web.msi Setup.Dorc.Monitors.msi Setup.Dorc.Cli.msi) do call msiexec /i "%MYDIR%\%%P" ^
SERVICE.IDENTITY="" ^
SERVICE.PASSWORD="" ^
DEPLOYMENT.DBSERVER=. ^
DEPLOYMENT.DB="" ^
SVC.ACCOUNTPROD="" ^
SVC.PASSWORDPROD=""  ^
SVC.ACCOUNTNONPROD="" ^
SVC.PASSWORDNONPROD="" ^
DB.CONNECTIONSTRING="" ^
SCRIPT.FOLDER="" ^
WEB.BACKGROUND.COLOUR=Grey ^
PAUSE.DEPLOYMENT.ENABLED="false" ^
KAFKA.ENABLED="true" ^
KAFKA.BOOTSTRAPSERVERS="" ^
KAFKA.SASL.USERNAME="" ^
KAFKA.SASL.PASSWORD="" ^
KAFKA.SSLCA.LOCATION="" ^
KAFKA.SCHEMAREGISTRY.URL="" ^
KAFKA.SCHEMAREGISTRY.BASICAUTH.USERNAME="" ^
KAFKA.SCHEMAREGISTRY.BASICAUTH.PASSWORD="" ^
KAFKA.TOPICS.LOCKS="dorc.locks" ^
KAFKA.TOPICS.REQUESTSNEW="dorc.requests.new" ^
KAFKA.TOPICS.REQUESTSSTATUS="dorc.requests.status" ^
KAFKA.TOPICS.RESULTSSTATUS="dorc.results.status" ^
KAFKA.TOPICS.REQUESTSNEWDLQ="dorc.requests.new.dlq" ^
KAFKA.LOCKS.CONSUMERGROUPID.PROD="dorc.monitor.locks.prod" ^
KAFKA.LOCKS.CONSUMERGROUPID.NONPROD="dorc.monitor.locks.nonprod" ^
/qb /L*v "%MYDIR%\%%~nP.log"

echo Returncode: %ERRORLEVEL%
pause