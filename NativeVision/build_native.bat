@echo off
setlocal

REM ── NativeVision build script ──
REM Compiles NativeVision.cpp → NativeVision.dll using MSVC cl.exe with AVX2+FMA

set "CL_EXE=C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.50.35717\bin\Hostx64\x64\cl.exe"
set "MSVC_INC=C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.50.35717\include"
set "MSVC_LIB=C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.50.35717\lib\x64"

set "SDK_VER=10.0.26100.0"
set "SDK_INC=C:\Program Files (x86)\Windows Kits\10\Include\%SDK_VER%"
set "SDK_LIB=C:\Program Files (x86)\Windows Kits\10\Lib\%SDK_VER%"

REM Verify cl.exe exists
if not exist "%CL_EXE%" (
    echo ERROR: cl.exe not found at: %CL_EXE%
    echo Please update the path in this script to match your Visual Studio installation.
    exit /b 1
)

pushd "%~dp0"

echo Building NativeVision.dll ...

"%CL_EXE%" /O2 /arch:AVX2 /fp:fast /LD /EHsc /MD /openmp ^
    /I"%MSVC_INC%" ^
    /I"%SDK_INC%\ucrt" ^
    /I"%SDK_INC%\um" ^
    /I"%SDK_INC%\shared" ^
    NativeVision.cpp ^
    /Fe:NativeVision.dll ^
    /link ^
    /LIBPATH:"%MSVC_LIB%" ^
    /LIBPATH:"%SDK_LIB%\ucrt\x64" ^
    /LIBPATH:"%SDK_LIB%\um\x64"

if errorlevel 1 (
    echo BUILD FAILED
    popd
    exit /b 1
)

echo Build succeeded: NativeVision.dll

REM Copy to C# output directory
set "OUTDIR=..\BODA VISION AI\bin\Debug\net8.0-windows7.0"
if exist "%OUTDIR%" (
    copy /Y NativeVision.dll "%OUTDIR%\" >nul
    echo Copied to %OUTDIR%\NativeVision.dll
)

popd
echo Done.
