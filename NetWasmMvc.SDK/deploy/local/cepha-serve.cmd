@echo off
REM 🧬 Cepha — Local Deployment Script
REM Builds, publishes, and serves the Cepha app locally

echo.
echo  🧬 Cepha — Local Deployment
echo  ══════════════════════════════
echo.

REM Build and publish
dotnet publish -c Release -o publish
if %ERRORLEVEL% NEQ 0 (
    echo ❌ Publish failed!
    exit /b 1
)

echo.
echo  ✅ Published successfully!
echo  📁 Output: publish\wwwroot
echo.

REM Check if Cepha server (Node.js) should start
if exist "publish\AppBundle\main.mjs" (
    echo  🧬 Starting Cepha Server...
    start "Cepha Server" cmd /c "cd publish\AppBundle && node main.mjs"
    timeout /t 2 /nobreak >nul
    echo  ✅ Cepha Server running on http://localhost:3000
)

REM Serve the client
echo  🌐 Starting Client on http://localhost:5000
dotnet serve -p 5000 --fallback-file index.html -d publish\wwwroot
