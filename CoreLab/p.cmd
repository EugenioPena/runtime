@echo off
rd /s/q %~dp0..\artifacts\bin\CoreLab
dotnet publish -r win-x64 --self-contained
xcopy /s/y %~dp0..\artifacts\bin\coreclr\windows.x64.Debug %~dp0..\artifacts\bin\CoreLab\Debug\net6.0\win-x64\publish\ > nul
