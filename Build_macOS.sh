#/bin/bash

cd VideoDuplicateFinder.Console &&
dotnet publish -c Release -v q --self-contained -r osx.10.15-x64 -o "..\Releases\VDF.OSX-x64" &&
cd -