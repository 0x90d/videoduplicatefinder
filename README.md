# Video Duplicate Finder
Video Duplicate Finder is a cross-platform software to find duplicated video (and image) files on hard disk based on similiarity. That means unlike other duplicate finders this one does also finds duplicates which have a different resolution, frame rate and even watermarked.

# Features
- Cross-platform
- Fast scanning speed
- Ultra fast rescan
- Finds duplicate videos / images based on similarity
- Windows, Linux and MacOS GUI

# Requirements
FFmpeg and FFprobe is required.

# Binaries

[Latest release](https://github.com/0x90d/videoduplicatefinder/releases)

Latest build: [![Build status](https://ci.appveyor.com/api/projects/status/github/0x90d/videoduplicatefinder?branch=master&svg=true)](https://ci.appveyor.com/project/0x90d/videoduplicatefinder/branch/master/artifacts) (You need to download FFmpeg and FFprobe yourself, see below!)

# Requirements

#### Windows user:
FFmpeg and FFprobe are already included except for AppVeyor builds. Otherwise get latest package from https://ffmpeg.org/download.html and download latest version (I recommend the shared version) and extract ffmpeg.exe and ffprobe into the same directory where VideoDuplicateFinderWindows.exe is placed in.

#### Linux user:
Install latest ffmpeg and ffprobe the usual way and verify PATH environment variable is set. Also make sure you got **libgdiplus** installed.

```
sudo apt-get update
sudo apt-get install ffmpeg
sudo apt-get install libgdiplus
```
#### MacOS user:
Visit https://ffmpeg.org/download.html and download ffmpeg and ffprobe for macos and extract them into the VDF folder
Open terminal in the folder where VDF.GUI.dll is and run this command `dotnet VDF.GUI.dll` or right click on `VDF.GUI` and choose to open with terminal

# Screenshots (outdated, screenshots show Version 2.x only)
![windows](https://user-images.githubusercontent.com/46010672/50975469-97e5d900-14e5-11e9-9aba-5a843546ac2c.jpg)
![linux](https://user-images.githubusercontent.com/46010672/50975476-9e745080-14e5-11e9-8332-b0ac816458f4.jpg)


# License
Video Duplicate Finder is licensed under GPLv3  
Video Duplicate Finder uses ffmpeg / ffprobe (not included) which is licesed under LGPL 2.1 / GPL v2


# Building
### Please note
Master branch is used for v3 which is still in development. If you build in order to use it you should get the code from v2 instead: https://github.com/0x90d/videoduplicatefinder/tree/c19190196588dd2982e269b28ff23abf78e8ff5e

- .NET Core 6.x SDK DAILY builds
- Visual Studio 2022
- Avalonia VS Extension is recommended but not required

# Committing
- Your pull request should only contain code for a single addition or fix
- Unles it referes to an existing issue, write into your pull request what it does
