@echo off
rem Double-clickable hot-reload launcher.
rem
rem Use this instead of running watch.ps1 directly: right-click "Run with PowerShell"
rem launches the script with -File, which CLOSES the window the moment the script ends
rem or errors -- so you never see what happened. The -NoExit below keeps the window open.
rem
rem   Double-click watch.cmd   (or run it from any shell)
rem
powershell -NoExit -ExecutionPolicy Bypass -File "%~dp0watch.ps1"
