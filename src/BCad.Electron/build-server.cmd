@echo off
set serverdir=%~dp0..\BCad.Server
set nodebindir=%~dp0node_modules
dotnet restore %serverdir%\BCad.Server.csproj
dotnet publish %serverdir%\BCad.Server.csproj
rd /s /q %nodebindir%\BCad.Server
robocopy %serverdir%\bin\Debug\netcoreapp1.1\publish\ %nodebindir%\BCad.Server
