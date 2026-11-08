@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET 9 SDK or newer was not found.
    echo Install the .NET 9 SDK from https://dotnet.microsoft.com/download/dotnet/9.0
    pause
    exit /b 1
)

for /f "tokens=1 delims=." %%a in ('dotnet --version') do set "SDK_MAJOR=%%a"
if not defined SDK_MAJOR (
    echo Could not determine the installed .NET SDK version.
    pause
    exit /b 1
)
if %SDK_MAJOR% LSS 9 (
    echo This project requires the .NET 9 SDK or newer.
    echo Installed SDK: 
    dotnet --version
    echo Download: https://dotnet.microsoft.com/download/dotnet/9.0
    pause
    exit /b 1
)

echo Using .NET SDK:
dotnet --version
echo.
echo Building Scroll Video Player for Windows x64...
dotnet publish "src\ScrollVideoPlayer\ScrollVideoPlayer.csproj" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o "dist\ScrollVideoPlayer"
if errorlevel 1 (
    echo.
    echo Build failed. Review the error above.
    pause
    exit /b 1
)

echo.
echo Portable build created in:
echo %CD%\dist\ScrollVideoPlayer
echo Copy the complete folder to another Windows 10/11 PC or zip it.
pause
