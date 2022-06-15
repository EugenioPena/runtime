@echo off
setlocal
if "%ANDREW_OPTIMIZED%"=="1" (
  set ANDREW_ARCH_PART=/p:RuntimeConfiguration=Release
) else (
  set ANDREW_ARCH_PART=/p:RuntimeConfiguration=Debug
)
call build.cmd -c release -s libs %ANDREW_ARCH_PART%
endlocal