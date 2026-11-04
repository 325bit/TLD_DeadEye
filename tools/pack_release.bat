@echo off
rem Double-click wrapper for tools\pack_release.py.
rem Runs from the repo root no matter where it was started; every argument is
rem forwarded (-n skip build, -c Debug, --no-docs, --strict, -o <dir>).
setlocal
cd /d "%~dp0.."

set "PY="
where py >nul 2>&1 && set "PY=py -3"
if not defined PY (
    where python >nul 2>&1 && set "PY=python"
)
if not defined PY (
    echo [error] Python 3 was not found on PATH.
    echo         Install Python 3.8 or newer, then run: python tools\pack_release.py
    echo.
    echo Packaging FAILED.
    pause
    exit /b 1
)

%PY% "tools\pack_release.py" %*
set "RC=%ERRORLEVEL%"

echo.
if not "%RC%"=="0" (
    echo Packaging FAILED ^(exit %RC%^).
) else (
    echo Done. The zip is in dist\.
)
pause
exit /b %RC%
