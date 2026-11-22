@echo off
rem Launch the cat desktop pet.
rem Uses the bundled .NET 8 runtime (.tools\dotnet) so it also works on machines
rem without the .NET 8 Desktop Runtime installed.
setlocal
set "ROOT=%~dp0"
set "EXE=%ROOT%src\CatPet\bin\Debug\net8.0-windows\CatPet.exe"

if not exist "%EXE%" (
  echo [CatPet] Build output not found. Run:
  echo     dotnet build "%ROOT%src\CatPet\CatPet.csproj"
  pause
  exit /b 1
)

if exist "%ROOT%..\.tools\dotnet\dotnet.exe" (
  set "DOTNET_ROOT=%ROOT%..\.tools\dotnet"
  set "PATH=%ROOT%..\.tools\dotnet;%PATH%"
)

start "" "%EXE%"
endlocal
