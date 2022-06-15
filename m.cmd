@echo off

setlocal
if "%ANDREW_OPTIMIZED%"=="1" (
  REM build.cmd -rc release -lc release -arch x64 -vs coreclr.sln
  set ANDREW_ARCH_PART=artifacts\obj\coreclr\windows.x64.Release\ide\vm\wks\cee_wks_core.vcxproj
) else (
  REM build.cmd -rc debug -lc release -arch x64 -vs coreclr.sln
  set ANDREW_ARCH_PART=artifacts\obj\coreclr\windows.x64.Debug\ide\vm\wks\cee_wks_core.vcxproj
)
msbuild %ANDREW_ARCH_PART%
endlocal

