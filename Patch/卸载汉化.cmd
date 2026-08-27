@echo off
setlocal EnableExtensions
title ECHOES 简体中文补丁卸载程序

set "GAME_ROOT=%~dp0"
set "PLUGIN_DIR=%GAME_ROOT%BepInEx\plugins\EchoesChinese"

if not exist "%GAME_ROOT%ECHOES.exe" (
    echo 未找到 ECHOES.exe。
    echo 请将本脚本放在游戏根目录后再运行。
    pause
    exit /b 1
)

if not exist "%PLUGIN_DIR%" (
    echo 未找到汉化插件，可能已经卸载。
    pause
    exit /b 0
)

echo 即将删除：
echo %PLUGIN_DIR%
echo.
echo 只会删除 ECHOES 汉化插件，不会删除其他 BepInEx 插件、游戏存档或游戏文件。
choice /C YN /N /M "确认卸载汉化？[Y/N] "
if errorlevel 2 (
    echo 已取消。
    exit /b 0
)

rmdir /S /Q "%PLUGIN_DIR%"
if exist "%PLUGIN_DIR%" (
    echo 卸载失败，部分文件可能正在被占用。请退出游戏后重试。
    pause
    exit /b 1
)

echo 汉化插件已卸载。BepInEx 核心文件已保留。
pause
exit /b 0
