@echo off
setlocal enabledelayedexpansion

echo Building runtime...
pushd Runtime
call build.bat
popd
if errorlevel 1 goto :fail

echo STD...
dotnet run STD
if errorlevel 1 goto :fail

echo Building package...
dotnet run Example
if errorlevel 1 goto :fail

REM cls
echo Running runtime...
Runtime\bin\runtime.exe Example\run
exit /b 0

:fail
echo.
echo Build failed. Stopping.
exit /b 1
