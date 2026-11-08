@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0ScrollVideoPlayer.exe" (
    echo ScrollVideoPlayer.exe was not found in this folder.
    echo Extract the complete portable ZIP before running this file.
    pause
    exit /b 1
)
start "" "%~dp0ScrollVideoPlayer.exe" %*
