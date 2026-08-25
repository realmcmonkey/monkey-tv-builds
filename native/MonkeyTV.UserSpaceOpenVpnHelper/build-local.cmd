@echo off
setlocal EnableExtensions

set "ROOT=%~dp0"
set "BUILD_DIR=%ROOT%out\ninja"

if not "%~1"=="" set "VCPKG_ROOT=%~f1"

if not defined VCPKG_ROOT (
    echo ERROR: vcpkg was not configured.
    echo Set VCPKG_ROOT to your vcpkg checkout, or pass its path as the first argument:
    echo   build-local.cmd C:\path\to\vcpkg
    exit /b 2
)

if not exist "%VCPKG_ROOT%\scripts\buildsystems\vcpkg.cmake" (
    echo ERROR: The vcpkg toolchain was not found at:
    echo   %VCPKG_ROOT%\scripts\buildsystems\vcpkg.cmake
    echo Set VCPKG_ROOT correctly or pass the vcpkg root as the first argument.
    exit /b 2
)

if not exist "%VCPKG_ROOT%\vcpkg.exe" (
    echo ERROR: vcpkg.exe was not found under VCPKG_ROOT:
    echo   %VCPKG_ROOT%\vcpkg.exe
    echo Bootstrap that vcpkg checkout before building the helper.
    exit /b 2
)

where cl.exe >nul 2>nul
if errorlevel 1 call :setup_msvc
if errorlevel 1 exit /b %errorlevel%

where cl.exe >nul 2>nul
if errorlevel 1 (
    echo ERROR: The MSVC x64 compiler is unavailable after environment setup.
    echo Install Visual Studio 2022 Build Tools with the Desktop development with C++ workload.
    exit /b 2
)

call :find_tool CMAKE_EXE cmake.exe "Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
if errorlevel 1 exit /b %errorlevel%

call :find_tool NINJA_EXE ninja.exe "Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
if errorlevel 1 exit /b %errorlevel%

echo Using MSVC:  cl.exe
echo Using CMake: %CMAKE_EXE%
echo Using Ninja: %NINJA_EXE%
echo Using vcpkg: %VCPKG_ROOT%

"%CMAKE_EXE%" --version >nul 2>nul
if errorlevel 1 (
    echo ERROR: CMake could not be started. Monkey TV requires CMake 3.25 or newer.
    exit /b 2
)

"%NINJA_EXE%" --version >nul 2>nul
if errorlevel 1 (
    echo ERROR: Ninja could not be started.
    exit /b 2
)

"%CMAKE_EXE%" -S "%ROOT%" -B "%BUILD_DIR%" -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_MAKE_PROGRAM="%NINJA_EXE%" -DCMAKE_TOOLCHAIN_FILE="%VCPKG_ROOT%\scripts\buildsystems\vcpkg.cmake"
if errorlevel 1 (
    echo.
    echo ERROR: Native-helper configuration failed.
    echo Confirm that CMake 3.25+, Ninja, the MSVC x64 tools, and the vcpkg manifest dependencies are available.
    echo Required vcpkg libraries: OpenSSL, LZ4, and xxHash. The manifest also restores Asio and the other declared build inputs.
    exit /b 1
)

"%CMAKE_EXE%" --build "%BUILD_DIR%" --config Release
if errorlevel 1 (
    echo ERROR: Native-helper compilation failed. Review the compiler output above.
    exit /b 1
)

echo Native helper build completed successfully.
exit /b 0

:setup_msvc
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo ERROR: MSVC was not found and vswhere.exe is unavailable.
    echo Install Visual Studio 2022 Build Tools with the Desktop development with C++ workload,
    echo or run this script from an x64 Native Tools Command Prompt.
    exit /b 2
)

set "VS_INSTALL="
for /f "usebackq tokens=*" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VS_INSTALL=%%I"
if not defined VS_INSTALL (
    echo ERROR: Visual Studio Build Tools with the MSVC x64 component were not found.
    echo Install the Desktop development with C++ workload.
    exit /b 2
)

if not exist "%VS_INSTALL%\VC\Auxiliary\Build\vcvars64.bat" (
    echo ERROR: vcvars64.bat was not found beneath:
    echo   %VS_INSTALL%
    exit /b 2
)

call "%VS_INSTALL%\VC\Auxiliary\Build\vcvars64.bat"
if errorlevel 1 (
    echo ERROR: The Visual Studio x64 build environment could not be initialized.
    exit /b 2
)
exit /b 0

:find_tool
set "%~1="
for /f "delims=" %%I in ('where %~2 2^>nul') do if not defined %~1 set "%~1=%%I"
if defined %~1 exit /b 0

if defined VS_INSTALL if exist "%VS_INSTALL%\%~3" (
    set "%~1=%VS_INSTALL%\%~3"
    exit /b 0
)

echo ERROR: %~2 was not found on PATH or in the detected Visual Studio installation.
if /i "%~2"=="cmake.exe" echo Install CMake 3.25 or newer and add it to PATH.
if /i "%~2"=="ninja.exe" echo Install Ninja and add it to PATH.
exit /b 2
