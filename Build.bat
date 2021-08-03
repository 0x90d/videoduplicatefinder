@ECHO off
CD /D VDF.GUI
ECHO.
ECHO 1. Windows
ECHO 2. Linux
ECHO 3. macOS
ECHO 4. All
choice /C 1234 /M "Choose what to build"
If %ErrorLevel%==1 GoTo windows
If %ErrorLevel%==2 GoTo linux
If %ErrorLevel%==3 GoTo mac
If %ErrorLevel%==4 GoTo all
goto all
:windows
dotnet publish -c Release -v q --self-contained -r win-x64 -o "..\Releases\VDF.Windows-x64"
goto restOfScript
:linux
dotnet publish -c Release -v q --self-contained -r linux-x64 -o "..\Releases\VDF.Linux-x64"
goto restOfScript
:mac
dotnet publish -c Release -v q -r osx-x64 -o "..\Releases\VDF.MacOS-x64"
goto restOfScript
:all
dotnet publish -c Release -v q --self-contained -r win-x64 -o "..\Releases\VDF.Windows-x64"
dotnet publish -c Release -v q --self-contained -r linux-x64 -o "..\Releases\VDF.Linux-x64"
dotnet publish -c Release -v q -r osx-x64 -o "..\Releases\VDF.MacOS-x64"
:restOfScript
CD /D ..

REM Copy ffmpeg windows binaries
if not exist ".\Releases\ffmpeg\" goto :EOF
REM if not exist ".\Releases\VDF.Windows-x86\bin" mkdir ".\Releases\VDF.Windows-x86\bin"
REM xcopy /q /y ".\Releases\ffmpeg\x86\*.*" ".\Releases\VDF.Windows-x86\bin"
if not exist "Releases\VDF.Windows-x64\bin" mkdir ".\Releases\VDF.Windows-x64\bin"
xcopy /q /y ".\Releases\ffmpeg\x64\*.*" ".\Releases\VDF.Windows-x64\bin"

goto :EOF

:error
cmd /k
exit /b %errorlevel%