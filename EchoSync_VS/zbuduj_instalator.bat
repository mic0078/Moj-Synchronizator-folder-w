@echo off
rem Buduje EchoSync_Setup.exe z programu skompilowanego w Visual Studio (bin\Release).
cd /d "%~dp0"
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist EchoSync\bin\Release\EchoSync.exe (
  echo Najpierw zbuduj projekt w Visual Studio w konfiguracji Release.
  pause & exit /b 1
)
copy /y EchoSync\bin\Release\EchoSync.exe EchoSync.exe >nul
%CSC% /nologo /codepage:65001 /target:winexe /optimize+ /out:EchoSync_Setup.exe /win32icon:EchoSync\app.ico /resource:EchoSync\app.ico,app.ico /resource:EchoSync.exe,payload.exe /resource:EchoSync.exe.config,payload.config ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll Instalator.cs
if errorlevel 1 ( echo BLAD KOMPILACJI INSTALATORA & pause & exit /b 1 )
echo OK: EchoSync_Setup.exe
