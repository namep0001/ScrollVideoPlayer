@echo off
setlocal

rem Works from either the repository root after building, or from an extracted
rem portable folder containing ScrollVideoPlayer.exe.
set "APP_DIR=%~dp0"

if not exist "%APP_DIR%ScrollVideoPlayer.exe" (
    if exist "%APP_DIR%dist\ScrollVideoPlayer\ScrollVideoPlayer.exe" (
        set "APP_DIR=%APP_DIR%dist\ScrollVideoPlayer\"
    )
)

if not exist "%APP_DIR%ScrollVideoPlayer.exe" (
    echo ScrollVideoPlayer.exe was not found.
    echo.
    echo When running from the source folder:
    echo   1. Run Build-Portable.bat.
    echo   2. Run this launcher again, or open dist\ScrollVideoPlayer\Run-Portable.bat.
    echo.
    echo When running the portable version:
    echo   Extract the complete portable ZIP and keep all files together.
    pause
    exit /b 1
)

start "" "%APP_DIR%ScrollVideoPlayer.exe" %*
