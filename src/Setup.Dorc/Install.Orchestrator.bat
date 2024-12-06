
@echo off
cls
pushd %~dp0
popd
set MYDIR=%CD%
echo Directory of this batch fil: %MYDIR%

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
/qb /L*v "%MYDIR%\Setup.Dorc.log" 

echo Returncode: %ERRORLEVEL%
pause