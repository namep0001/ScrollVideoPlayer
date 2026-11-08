@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET 10 SDK was not found.
    echo Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

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
