@echo off
rem Builds FlipClock.exe next to this file. Needs the .NET 8 SDK. Only needed after changing the code.
cd /d "%~dp0"
dotnet publish -c Release -o publish
if errorlevel 1 (
    echo.
    echo Build failed. See the messages above.
    pause
    exit /b 1
)
copy /y "publish\FlipClock.exe" "FlipClock.exe" >nul
echo.
echo Done. Double-click FlipClock.exe to start the clock.
pause
