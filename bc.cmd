@echo off
setlocal
if "%ANDREW_OPTIMIZED%"=="1" (
  set ANDREW_ARCH_PART=-c release
) else (
  set ANDREW_ARCH_PART=
)
call build.cmd -s clr %ANDREW_ARCH_PART%
endlocal
