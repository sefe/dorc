@echo off
cls
pushd %~dp0
popd
set MYDIR=%CD%
echo Directory of this batch fil: %MYDIR%

REM ALLOWED.ARTEFACT.HOSTS and ALLOWED.TERRAFORM.SOURCE.HOSTS must be JSON arrays of host
REM names, e.g. "[""tfs.corp.example.com"", ""dev.azure.com""]". A bare host name is written
REM into the configuration as a string, which the services reject at startup.

call msiexec /i "%MYDIR%\Setup.Dorc.msi" ^
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
ALLOWED.ARTEFACT.HOSTS="[]" ^
ALLOWED.TERRAFORM.SOURCE.HOSTS="[]" ^
DORC.CONFIG.KEYS.WITHHELD.FROM.SCRIPTS="[\"DORC_ProdDeployPassword\",\"DORC_WebDeployPassword\",\"DorcApiAccessPassword\"]" ^
/qb /L*v "%MYDIR%\Setup.Dorc.log"

echo Returncode: %ERRORLEVEL%
pause