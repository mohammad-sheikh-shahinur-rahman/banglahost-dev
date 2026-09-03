@echo off
title BanglaHost SourceForge Auto-Releaser
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0release.ps1"
pause
