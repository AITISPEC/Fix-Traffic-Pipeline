@echo off & color a
chcp 65001 >nul

set "FROM_DIR=%~dp0"
set "TO_DIR=../../../../"

if not exist "%TO_DIR%" (
    echo Целевая папка отсутствует!
    pause
    exit /b 1
)

:: 1. Файлы – перемещаем с заменой
move /Y "%FROM_DIR%\PlatformLauncher.dll" "%TO_DIR%\"
move /Y "%FROM_DIR%\PlatformLauncher.exe" "%TO_DIR%\"
move /Y "%FROM_DIR%\PlatformLauncher.runtimeconfig.json" "%TO_DIR%\"

:: 2. Папка libs – копируем с заменой, потом удаляем исходную
if exist "%FROM_DIR%\libs" (
    xcopy "%FROM_DIR%\libs" "%TO_DIR%\libs\" /E /I /Y >nul
    rd /S /Q "%FROM_DIR%\libs"
)

echo Перенос успешно завершен!
pause