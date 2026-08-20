@echo off
setlocal
call C:\BuildTools\VC\Auxiliary\Build\vcvars64.bat
if errorlevel 1 exit /b %errorlevel%
set ROOT=%~dp0.
set VCPKG_ROOT=%ROOT%\tooling\vcpkg
set CMAKE=%ROOT%\tooling\vcpkg\downloads\tools\cmake-4.4.0-windows\cmake-4.4.0-windows-x86_64\bin\cmake.exe
if not exist "%CMAKE%" set CMAKE=C:\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe
set NINJA=C:\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe
"%CMAKE%" -S "%ROOT%" -B "%ROOT%\out\ninja" -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_MAKE_PROGRAM="%NINJA%" -DCMAKE_TOOLCHAIN_FILE="%VCPKG_ROOT%\scripts\buildsystems\vcpkg.cmake"
if errorlevel 1 exit /b %errorlevel%
"%CMAKE%" --build "%ROOT%\out\ninja"
