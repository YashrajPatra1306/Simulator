@echo off
REM Build script for Sandbox Engine
REM Requires: Rust (cargo), .NET 8 SDK

setlocal enabledelayedexpansion

echo ============================================
echo Building Sandbox Rendering Engine
echo ============================================
echo.

REM Check for Rust
where cargo >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ERROR: Rust/Cargo not found. Please install Rust from https://rustup.rs/
    exit /b 1
)

REM Check for .NET
where dotnet >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ERROR: .NET SDK not found. Please install .NET 8 from https://dotnet.microsoft.com/
    exit /b 1
)

REM Build Rust core
echo [1/3] Building Rust physics core...
cd rust-core
cargo build --release
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to build Rust core
    cd ..
    exit /b 1
)

REM Copy DLL to C# output directory
echo [2/3] Copying sandbox_core.dll to C# frontend...
if exist "target\release\sandbox_core.dll" (
    copy /Y "target\release\sandbox_core.dll" "..\cs-frontend\"
    echo Copied sandbox_core.dll
) else (
    echo WARNING: Could not find sandbox_core.dll in target\release
    echo Looking for alternative locations...
    if exist "target\debug\sandbox_core.dll" (
        copy /Y "target\debug\sandbox_core.dll" "..\cs-frontend\"
        echo Copied debug version
    ) else (
        echo ERROR: No sandbox_core.dll found
        cd ..
        exit /b 1
    )
)
cd ..

REM Build C# frontend
echo [3/3] Building C# Windows Forms frontend...
cd cs-frontend
dotnet restore
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to restore NuGet packages
    cd ..
    exit /b 1
)

dotnet build --configuration Release
if %ERRORLEVEL% neq 0 (
    echo ERROR: Failed to build C# frontend
    cd ..
    exit /b 1
)
cd ..

echo.
echo ============================================
echo Build Complete!
echo ============================================
echo.
echo Output location: cs-frontend\bin\Release\net8.0-windows\
echo.
echo To run the application:
echo   cd cs-frontend
echo   dotnet run --configuration Release
echo.
echo Or directly execute:
echo   cs-frontend\bin\Release\net8.0-windows\SandboxEngine.exe
echo.

endlocal
