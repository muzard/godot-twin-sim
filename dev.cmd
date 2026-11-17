@echo off
REM Team command line. See tools/dev.cs.
REM DEV_CALLER_DIR keeps the directory you ran this from, so a relative GODOT path still works after the cd.
setlocal
set "DEV_CALLER_DIR=%CD%"
cd /d "%~dp0" && dotnet run tools/dev.cs -- %*
