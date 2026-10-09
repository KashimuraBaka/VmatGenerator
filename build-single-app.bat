@echo off
setlocal

rem ============================================================
rem  VmatGenerator single-file publish script (ASCII-only: batch
rem  files are re-read by cmd at byte offsets, so non-ASCII text
rem  around a chcp switch garbles parsing - keep this file plain
rem  ASCII and it works under any system code page).
rem
rem  What it does:
rem    dotnet publish GUI -c Release -r win-x64
rem    -> bin\publish\GUI.exe  (single file: app + third-party
rem       managed DLLs embedded; WPF/runtime DLLs come from the
rem       installed .NET 10 Desktop Runtime - framework-dependent
rem       mode, see GUI\GUI.csproj comments).
rem
rem  Usage: double-click, or "build-single-app.bat [/auto]"
rem    /auto = no pause at the end (automation / CI).
rem ============================================================

cd /d "%~dp0"

set "_pause=1"
if /i "%~1"=="/auto" set "_pause=0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet CLI not found. Install the .NET 10 SDK first:
    echo         https://dotnet.microsoft.com/download
    if "%_pause%"=="1" pause
    exit /b 1
)

rem Wipe the publish dir so stale artifacts can never linger next to the exe.
if exist "bin\publish" rd /s /q "bin\publish"

echo [1/2] Publishing single-file app (Release / win-x64 / framework-dependent)...
dotnet publish GUI\GUI.csproj -c Release -r win-x64 -o bin\publish --nologo -p:DebugType=none -p:DebugSymbols=false
if errorlevel 1 (
    echo.
    echo [ERROR] Publish failed, see the MSBuild output above.
    if "%_pause%"=="1" pause
    exit /b 1
)

if not exist "bin\publish\GUI.exe" (
    echo [ERROR] dotnet publish succeeded but bin\publish\GUI.exe is missing.
    if "%_pause%"=="1" pause
    exit /b 1
)

rem The csproj enables GenerateDocumentationFile, so publish also drops
rem GUI.xml / Lib.xml next to the exe. They are XML doc artifacts, not
rem runtime files - remove them so the folder really is "single app".
del /q "bin\publish\*.xml" 2>nul

echo [2/2] Artifact:
for %%F in ("bin\publish\GUI.exe") do echo        %%~fF  (%%~zF bytes)

echo.
echo Done. Run bin\publish\GUI.exe directly; the target machine needs the
echo .NET 10 Desktop Runtime installed (framework-dependent single file).
if "%_pause%"=="1" pause
exit /b 0
