@echo off
if "%ANDREW_OPTIMIZED%"=="1" (
  set CORE_ROOT=%~dp0artifacts\tests\coreclr\Windows.x64.Release\Tests\Core_Root
) else (
  set CORE_ROOT=%~dp0artifacts\tests\coreclr\Windows.x64.Debug\Tests\Core_Root
)
