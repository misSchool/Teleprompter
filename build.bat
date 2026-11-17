@echo off
rem Builds Teleprompter.exe using the C# compiler included with Windows (.NET Framework 4.x).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /out:"%~dp0Teleprompter.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0src\Teleprompter.cs"
if errorlevel 1 (echo Build failed. & exit /b 1)
echo Built Teleprompter.exe
