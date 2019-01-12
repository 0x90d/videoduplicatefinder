CD /D VideoDuplicateFinder.Console
dotnet publish -c Release -v q --self-contained -r win-x64 -f netcoreapp3.0 -o "..\Releases\VDF.Windows-x64"
dotnet publish -c Release -v q --self-contained -r win-x86 -f netcoreapp3.0 -o "..\Releases\VDF.Windows-x86"
dotnet publish -c Release -v q --self-contained -r linux-x64 -f netcoreapp2.2 -o "..\Releases\VDF.Linux-x64"
CD /D ..
CD /D VideoDuplicateFinder.Windows
dotnet publish -c Release -v q --self-contained -r win-x64 -f netcoreapp3.0 -o "..\Releases\VDF.Windows-x64"
dotnet publish -c Release -v q --self-contained -r win-x86 -f netcoreapp3.0 -o "..\Releases\VDF.Windows-x86"
CD /D ..
CD /D VideoDuplicateFinderLinux
dotnet publish -c Release -v q --self-contained -r linux-x64 -f netcoreapp2.2 -o "..\Releases\VDF.Linux-x64"