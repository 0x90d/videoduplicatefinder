# Video Duplicate Finder
Video Duplicate Finder is a cross-platform software to find duplicated video (and image) files on hard disk based on similiarity. That means unlike other duplicate finders this one does also finds duplicates which have a different resolution, frame rate and even watermarked.

# Features
- Cross-platform
- Fast scanning speed
- Ultra fast rescan
- Finds duplicate videos / images based on similarity
- Windows GUI, Linux GUI and multi-OS Console

# Requirements
FFmpeg and FFprobe is required.

# Binaries

[Latest release](https://github.com/0x90d/videoduplicatefinder/releases)

Latest build: [![Build status](https://ci.appveyor.com/api/projects/status/github/0x90d/videoduplicatefinder?branch=master&svg=true)](https://ci.appveyor.com/project/0x90d/videoduplicatefinder/branch/master/artifacts) (You need to download FFmpeg and FFprobe yourself, see below!)

# Requirements

#### Windows user:
FFmpeg and FFprobe are already included except for AppVeyor builds. Otherwise get latest package from https://ffmpeg.zeranoe.com/builds/ and extract ffmpeg.exe and ffprobe into the same directory where VideoDuplicateFinderWindows.exe is placed in.

#### Linux user:
Install latest ffmpeg and ffprobe the usual way and verify PATH environment variable is set. Also make sure you got **libgdiplus** installed.

```
sudo apt-get update
sudo apt-get install ffmpeg
sudo apt-get install libgdiplus
```

#### Mac OS X user (ONLY console version):
Install latest dotnet, ffmpeg and ffprobe and then run build command.

```
brew install dotnet ffmpeg
chmod 755 Build_macOS.sh && ./Build_macOS.sh
```

In order to run, use compiled version from Release folder:
```
cd Releases/VDF.OSX-x64 && ./VideoDuplicateFinderConsole
```

After running command you should see inside build directory file `output.html` with all details about same videos.

# Console

## Supported arguments

| Flag   | Default       | Description                                                  |
| -----: | ------------- | ------------------------------------------------------------ |
| -i     | -             | Path to the folder to include for scan*                      |
| -e     | -             | Path to the folder to exclude for scan*                      |
| -r     | -             | Include all sub folders                                      |
| -q     | -             | Scan progress will not be shown                              |
| -clean | -             | Cleanup Database                                             |
| -j     | -             | Include images                                               |
| -p     | 100           | Similarity (in %) to be considered as duplicate, range 0-100 |
| -o     | ./output.html | Path to output html file with results                        |

\* - relative to execution directory or abstract path

## Examples

This example execution should happen from release folder:

```bash
./VideoDuplicateFinderConsole -r -p 50 -i ~/Movies/PhoneBackup -o ~/duplicatedPhoneVideos.html
```

# Screenshots (UI)
![windows](https://user-images.githubusercontent.com/46010672/50975469-97e5d900-14e5-11e9-9aba-5a843546ac2c.jpg)
![linux](https://user-images.githubusercontent.com/46010672/50975476-9e745080-14e5-11e9-8332-b0ac816458f4.jpg)


# License
Video Duplicate Finder is licensed under GPLv3  
Video Duplicate Finder uses ffmpeg / ffprobe (not included) which is licesed under LGPL 2.1 / GPL v2


# Compiling
- Daily .NET Core 3.x SDK builds https://github.com/dotnet/core-setup#daily-builds
- Visual Studio 2019
- For Linux: Avalonia
