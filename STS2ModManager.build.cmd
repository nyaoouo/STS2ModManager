@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
set "PROJECT_FILE=%SCRIPT_DIR%STS2ModManager.csproj"
set "DIST_DIR=%SCRIPT_DIR%.dist"
set "PUBLISH_ROOT=%SCRIPT_DIR%.build\publish"
set "FRAMEWORK_OUTPUT=ModManager.FrameworkDependent.exe"
set "AOT_OUTPUT=ModManager.NativeAot.exe"
set "NONINTERACTIVE=%STS2_MODMANAGER_NONINTERACTIVE%"

if /i "%CI%"=="true" set "NONINTERACTIVE=1"
if /i "%GITHUB_ACTIONS%"=="true" set "NONINTERACTIVE=1"

set "MODE=%~1"
set "BUILD_VERSION=%~2"
if /i "%MODE%"=="" set "MODE=all"

if /i not "%MODE%"=="framework" if /i not "%MODE%"=="default" if /i not "%MODE%"=="aot" if /i not "%MODE%"=="all" (
    if /i "%BUILD_VERSION%"=="" (
        set "BUILD_VERSION=%MODE%"
        set "MODE=all"
    )
)

if /i "%BUILD_VERSION%"=="" set "BUILD_VERSION=dev"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo dotnet SDK was not found on PATH.
    call :maybe_pause
    exit /b 1
)

if not exist "%PROJECT_FILE%" (
    echo Project file not found: "%PROJECT_FILE%".
    call :maybe_pause
    exit /b 1
)

if not exist "%DIST_DIR%" mkdir "%DIST_DIR%"

if /i "%MODE%"=="framework" goto build_framework
if /i "%MODE%"=="default"   goto build_framework
if /i "%MODE%"=="aot"       goto build_aot
if /i "%MODE%"=="all"       goto build_all

echo Usage: %~nx0 [framework^|aot^|all] [version]
call :maybe_pause
exit /b 1

:build_all
call :build_variant framework "%FRAMEWORK_OUTPUT%"
if errorlevel 1 goto fail
call :build_variant aot "%AOT_OUTPUT%"
if errorlevel 1 goto fail
goto success_all

:build_framework
call :build_variant framework "%FRAMEWORK_OUTPUT%"
if errorlevel 1 goto fail
goto success_framework

:build_aot
call :build_variant aot "%AOT_OUTPUT%"
if errorlevel 1 goto fail
goto success_aot

:build_variant
set "VARIANT=%~1"
set "OUTPUT_NAME=%~2"
set "PUBLISH_DIR=%PUBLISH_ROOT%\%VARIANT%"

if exist "%PUBLISH_DIR%" rd /s /q "%PUBLISH_DIR%"
if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"

echo Building %VARIANT% publish (version %BUILD_VERSION%)...
if /i "%VARIANT%"=="aot" (
    dotnet publish "%PROJECT_FILE%" -c Release -r win-x64 ^
        -p:PublishAot=true ^
        -p:DebugType=None ^
        -p:InformationalVersion=%BUILD_VERSION% ^
        -o "%PUBLISH_DIR%"
) else (
    dotnet publish "%PROJECT_FILE%" -c Release -r win-x64 --self-contained false ^
        -p:PublishSingleFile=true ^
        -p:DebugType=None ^
        -p:InformationalVersion=%BUILD_VERSION% ^
        -o "%PUBLISH_DIR%"
)
if errorlevel 1 exit /b 1

if exist "%DIST_DIR%\%OUTPUT_NAME%" del /f /q "%DIST_DIR%\%OUTPUT_NAME%"
copy /y "%PUBLISH_DIR%\ModManager.exe" "%DIST_DIR%\%OUTPUT_NAME%" >nul
if errorlevel 1 exit /b 1

exit /b 0

:success_all
echo Built "%DIST_DIR%\%FRAMEWORK_OUTPUT%"
echo Built "%DIST_DIR%\%AOT_OUTPUT%"
call :maybe_pause
exit /b 0

:success_framework
echo Built "%DIST_DIR%\%FRAMEWORK_OUTPUT%"
call :maybe_pause
exit /b 0

:success_aot
echo Built "%DIST_DIR%\%AOT_OUTPUT%"
call :maybe_pause
exit /b 0

:fail
call :maybe_pause
exit /b 1

:maybe_pause
if defined NONINTERACTIVE exit /b 0
pause
exit /b 0
