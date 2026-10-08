@echo off
REM ===========================================================================
REM  Exarion Engine - lancement du bac a sable
REM
REM  Usage :  lancer.bat              lance normalement
REM           lancer.bat --novsync    sans limite d'images (mesure de perf)
REM           lancer.bat --seconds 10 ferme tout seul apres 10 s
REM ===========================================================================

cd /d "%~dp0"

echo Compilation...
dotnet build -c Release --nologo -v quiet
if errorlevel 1 (
  echo.
  echo [ECHEC] La compilation a echoue.
  pause
  exit /b 1
)

echo.
REM PAS de --no-build, meme juste apres une compilation reussie : c'est le
REM piege qui a fait conclure plusieurs fois de travers sur ce projet (un
REM binaire perime lance a la place du nouveau). Sans lui, dotnet run refait
REM une compilation incrementale, quasi gratuite, et lance le bon binaire.
dotnet run --project src\Exarion.Game -c Release --no-launch-profile -- %*
