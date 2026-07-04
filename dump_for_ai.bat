@echo off
chcp 65001 >nul
color a
title Dump for AI Launcher
echo =========================
echo   AITISPEC - Dump for AI
echo =========================
echo.

set "PROJECT_DIR=%~dp0"
pushd "%PROJECT_DIR%..\tests"
if errorlevel 1 (
    echo [ОШИБКА] Папка tests не найдена рядом с папкой проекта!
    pause
    exit /b 1
)

if not exist ".venv\Scripts\activate.bat" (
    echo [ОШИБКА] Виртуальное окружение не найдено в tests!
    popd
    pause
    exit /b 1
)

call .venv\Scripts\activate.bat
popd

python "%PROJECT_DIR%..\tests\dump_for_ai.py"
