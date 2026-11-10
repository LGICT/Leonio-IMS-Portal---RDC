@echo off
rem Builds LeonioIMS.exe with the C# compiler included in Windows (.NET Framework 4.x). No installation needed.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo The .NET Framework 4 C# compiler was not found on this computer.
  exit /b 1
)
"%CSC%" /nologo /optimize+ /target:exe /out:"%~dp0LeonioIMS.exe" /r:System.Web.Extensions.dll "%~dp0src\*.cs"
if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
echo Build succeeded: LeonioIMS.exe
