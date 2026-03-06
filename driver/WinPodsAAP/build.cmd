@echo off
echo === WinPodsAAP Build Script ===
echo This builds the driver using WDK NuGet package (no VS 2022 required).
echo.

REM Install WDK NuGet if needed
if not exist "packages\Microsoft.Windows.WDK.x64" (
    echo Installing WDK NuGet package...
    nuget install Microsoft.Windows.WDK.x64 -OutputDirectory packages -Source https://api.nuget.org/v3/index.json
)

REM Find msbuild
set MSBUILD=
for /f "delims=" %%i in ('where msbuild 2^>nul') do set MSBUILD=%%i
if "%MSBUILD%"=="" (
    for /f "delims=" %%i in ('dir /s /b "C:\Program Files\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\amd64\MSBuild.exe" 2^>nul') do set MSBUILD=%%i
)
if "%MSBUILD%"=="" (
    echo ERROR: MSBuild not found. Run from a Developer Command Prompt.
    exit /b 1
)

echo Using MSBuild: %MSBUILD%
echo.

"%MSBUILD%" WinPodsAAP.vcxproj /p:Configuration=Release /p:Platform=x64
echo.
if %errorlevel%==0 (
    echo === BUILD SUCCEEDED ===
    echo Output: x64\Release\WinPodsAAP.sys
) else (
    echo === BUILD FAILED ===
)
