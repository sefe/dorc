@echo off
cls
pushd %~dp0
popd
set MYDIR=%CD%
echo Directory of this batch fil: %MYDIR%

rem Order matches MsiFileNames in DeploySettings.template.json, and it is not
rem arbitrary: monitor configuration embeds DEPLOYAPI.ENDPOINT and the UI reads
rem $.api, so the API has to be in place first.
rem
rem Stops at the first failure. Carrying on would leave a machine part-installed
rem in an order-dependent chain, and reporting only the last package's exit code
rem would call that a success.
for %%P in (Setup.Dorc.Api.msi Setup.Dorc.Web.msi Setup.Dorc.Monitors.msi Setup.Dorc.Cli.msi) do (
    call :install "%%P"
    if errorlevel 1 goto :failed
)

echo.
echo All four packages installed.
pause
exit /b 0

rem One property block for all four packages: MSI ignores properties a package
rem does not declare, so the manual helper stays a single list rather than four
rem that drift apart.
:install
set PACKAGE=%~1
echo.
echo Installing %PACKAGE% ...
msiexec /i "%MYDIR%\%PACKAGE%" ^
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
/qb /L*v "%MYDIR%\%~n1.log"
exit /b %ERRORLEVEL%

:failed
echo.
echo FAILED: %PACKAGE% returned %ERRORLEVEL%. See %MYDIR%\%PACKAGE:.msi=%.log
echo Later packages were not attempted, so this machine is part-installed.
pause
exit /b 1
