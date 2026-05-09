@echo off
setlocal

echo Building Sandbox Rendering Engine...
echo.

REM Build Rust core
echo [1/3] Building Rust physics core...
cd rust-core
call cargo build --release
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Rust build failed!
    exit /b 1
)

REM Copy DLL to C# output
echo [2/3] Copying sandbox_core.dll to C# project...
copy /Y target\release\sandbox_core.dll ..\cs-frontend\bin\Release\net8.0-windows\ >nul 2>&1
mkdir ..\cs-frontend\bin\Release\net8.0-windows\ 2>nul
copy /Y target\release\sandbox_core.dll ..\cs-frontend\bin\Release\net8.0-windows\

REM Build C# frontend
echo [3/3] Building C# Windows Forms frontend...
cd ..\cs-frontend
dotnet build -c Release
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: C# build failed!
    exit /b 1
)

echo.
echo ========================================
echo Build complete!
echo Output: cs-frontend\bin\Release\net8.0-windows\SandboxEngine.exe
echo ========================================

endlocal
