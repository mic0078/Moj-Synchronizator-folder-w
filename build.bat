@echo off
cd /d "%~dp0"
set CSC="%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll

rem 1) ikona generowana z Logo.cs
%CSC% /nologo /codepage:65001 /target:exe /out:IconGen.exe /r:System.Drawing.dll IconGen.cs Logo.cs
if errorlevel 1 goto err
.\IconGen.exe app.ico logo_podglad.png
if errorlevel 1 goto err
del IconGen.exe

rem 2) program
%CSC% /nologo /codepage:65001 /target:winexe /optimize+ /out:EchoSync.exe /win32manifest:app.manifest /win32icon:app.ico /resource:app.ico,app.ico ^
  %REFS% /r:System.Xml.dll /r:Microsoft.VisualBasic.dll Synchronizator.cs Logo.cs RoboForm.cs
if errorlevel 1 goto err

rem 3) instalator (z programem w srodku)
%CSC% /nologo /codepage:65001 /target:winexe /optimize+ /out:EchoSync_Setup.exe /win32icon:app.ico /resource:app.ico,app.ico /resource:EchoSync.exe,payload.exe /resource:EchoSync.exe.config,payload.config ^
  %REFS% /r:Microsoft.CSharp.dll Instalator.cs
if errorlevel 1 goto err

echo OK: EchoSync.exe + EchoSync_Setup.exe
goto :eof
:err
echo BLAD KOMPILACJI
pause
