@echo off
echo === Building Rust physics core ===
cd sandbox_core
cargo build --release
if errorlevel 1 ( echo Rust build failed & exit /b 1 )
copy /Y target\release\sandbox_core.dll ..\SandboxEngine\sandbox_core.dll
echo === Building C# frontend ===
cd ..\SandboxEngine
dotnet build -c Release
if errorlevel 1 ( echo C# build failed & exit /b 1 )
echo === Done. Run SandboxEngine\bin\Release\net8.0-windows\SandboxEngine.exe ===
