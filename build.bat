@echo off
rem QuickBar build script (uses csc.exe bundled with Windows / .NET Framework 4.x)
rem Output: dist\QuickBar.exe
rem Usage : build.bat [/lang:en or /lang:ja] [/nopause]
setlocal EnableDelayedExpansion
cd /d "%~dp0"

set "QB_LANG="
set "NOPAUSE="
for %%A in (%*) do (
  if /i "%%~A"=="/lang:en" set "QB_LANG=en"
  if /i "%%~A"=="/lang:ja" set "QB_LANG=ja"
  if /i "%%~A"=="/nopause" set "NOPAUSE=1"
)

if not defined QB_LANG (
  echo Select language:
  echo   [1] English
  echo   [2] Japanese
  choice /c 12 /n /m "Choose 1-2: "
  if errorlevel 2 (set "QB_LANG=ja") else (set "QB_LANG=en")
)
echo Language: %QB_LANG%
echo.

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo csc.exe not found. .NET Framework 4.x is required.
  goto :fail
)

set "SRC=src\QuickBar"
set "ICON=%SRC%\Resources\app.ico"

rem Default language of the exe (can be changed later with "language" in settings)
set "DEFINE="
if "%QB_LANG%"=="ja" set "DEFINE=/define:QUICKBAR_LANG_JA"

rem Embed every lang\*.json as a fallback
set "LANGRES="
for %%F in (lang\*.json) do set "LANGRES=!LANGRES! /resource:%%F,QuickBar.lang.%%~nxF"

if not exist "dist" mkdir "dist"

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 %DEFINE% ^
  /win32icon:%ICON% /resource:%ICON%,QuickBar.app.ico %LANGRES% ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /out:dist\QuickBar.exe /recurse:%SRC%\*.cs
if errorlevel 1 (
  echo.
  echo Build FAILED.
  goto :fail
)

rem User-editable files: copy only when missing (never overwrite user changes)
if not exist "dist\data" mkdir "dist\data"
for %%F in (data\%QB_LANG%\*.json) do (
  if not exist "dist\data\%%~nxF" copy /y "%%F" "dist\data\" >nul
)
if not exist "dist\lang" mkdir "dist\lang"
for %%F in (lang\*.json) do (
  if not exist "dist\lang\%%~nxF" copy /y "%%F" "dist\lang\" >nul
)

rem Not user-edited: always update
copy /y "data\apps.schema.json" "dist\data\" >nul
copy /y "README.md" "dist\" >nul
if exist "LICENSE.md" copy /y "LICENSE.md" "dist\" >nul

echo.
echo Build OK: dist\QuickBar.exe
if not defined NOPAUSE pause
exit /b 0

:fail
if not defined NOPAUSE pause
exit /b 1
