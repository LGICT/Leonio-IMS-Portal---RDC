@echo off
rem Starts the Leonio Group IMS Portal and opens it in your browser once it is ready.
title Leonio Group IMS Portal
set "HERE=%~dp0"
cd /d "%HERE%"
if not exist "%HERE%LeonioIMS.exe" call "%HERE%build.cmd"
if not exist "%HERE%LeonioIMS.exe" (
  echo Could not build LeonioIMS.exe.
  pause
  exit /b 1
)
"%HERE%LeonioIMS.exe" --port 8080 --open
echo.
echo The portal has stopped.
pause
