@echo off
REM Build script for BizTalk to Logic Apps MCP Server

echo ========================================
echo BizTalk to Logic Apps MCP Server Build
echo ========================================
echo.

REM Check for MSBuild
where msbuild >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ERROR: MSBuild not found in PATH
    echo Please run this from Visual Studio Developer Command Prompt
    echo Or add MSBuild to your PATH
    pause
    exit /b 1
)

REM Check for NuGet
where nuget >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo WARNING: NuGet not found in PATH
    echo Attempting to download NuGet.exe...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "& {[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile '%~dp0nuget.exe'}"
    if %ERRORLEVEL% neq 0 (
        echo ERROR: Failed to download NuGet
        echo Please download NuGet.exe manually from https://www.nuget.org/downloads
        echo and place it in this directory or add it to your PATH
        pause
        exit /b 1
    )
    set NUGET_CMD=%~dp0nuget.exe
) else (
    set NUGET_CMD=nuget
)

echo.
echo Step 1: Restore NuGet packages...
echo.
%NUGET_CMD% restore BizTalkToLogicApps.MCP.csproj
if %ERRORLEVEL% neq 0 (
    echo ERROR: NuGet restore failed
    pause
    exit /b 1
)

echo.
echo Step 2: Build solution...
echo.
msbuild BizTalkToLogicApps.MCP.csproj /t:Rebuild /p:Configuration=Release /v:minimal
if %ERRORLEVEL% neq 0 (
    echo ERROR: Build failed
    pause
    exit /b 1
)

echo.
echo ========================================
echo Build completed successfully!
echo ========================================
echo.
echo Output: bin\Release\BizTalkToLogicApps.MCP.exe
echo.
echo Next steps:
echo   1. Configure in Claude Desktop (see QUICKSTART.md)
echo   2. Run test-mcp-server.ps1 for validation
echo   3. Check README.md for detailed documentation
echo.

pause
