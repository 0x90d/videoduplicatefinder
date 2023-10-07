# Video Duplicate Finder
Video Duplicate Finder is a cross-platform software to find duplicated video (and image) files on hard disk based on similiarity. That means unlike other duplicate finders this one does also finds duplicates which have a different resolution, frame rate and even watermarked.

# Features
- Cross-platform
- Fast scanning speed
- Ultra fast rescan
- Optional calling ffmpeg functions natively for even more speed
- Finds duplicate videos / images based on similarity
- Windows, Linux and MacOS GUI

# Binaries

[Daily build](https://github.com/0x90d/videoduplicatefinder/releases/tag/3.0.x) (You need to download FFmpeg and FFprobe yourself, see below! Please note the attachments of this release are automatically created and replaced on every new commit.)


# Requirements

#### FFmpeg & FFprobe:

#### Windows user:
Get latest package from https://ffmpeg.org/download.html I recommend the full (GPL) shared version. If you want to use native ffmpeg binding for much faster speed you must download the **5.0 stable shared** version.

Extract ffmpeg and ffprobe into the same directory of VDF.GUI.dll. Or into a sub folder called `bin`. Or make sure it can be found in `PATH` system environment variable

#### Linux user:
Installing ffmpeg:
```
sudo apt-get update
sudo apt-get install ffmpeg
```
Open terminal in VDF folder and execute `./VDF.GUI`
You may need to set execute permission first `sudo chmod 777 VDF.GUI`

#### MacOS user:
Install ffmpeg & ffprobe using homebrew
<details>
  <summary>If homebrew is not installed, open Terminal.app and execute</summary>

###
  ```js
  /bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
  ```
and then
</details>

```
brew install ffmpeg
```
Open `System Settings.app` go to `Privacy & Security` > `Developer Tools` and add `Terminal.app`.
Afterwards open `Terminal.app` in videoduplicatefinder root directory. If you have .NET installed execute `dotnet VDF.GUI.dll` otherwise execute
```
xattr -rc ./VDF.GUI && codesign --force --deep --sign - ./VDF.GUI
chmod +x ./VDF.GUI
./VDF.GUI
```
# Screenshots (slightly outdated)
<img width="685" src="https://github.com/0x90d/videoduplicatefinder/assets/54822494/99b013ac-aad9-4284-97ac-6691018e9d86">

# License
Video Duplicate Finder is licensed under GPLv3  
Video Duplicate Finder uses ffmpeg / ffprobe (not included) which is licensed under LGPL 2.1 / GPL v2

# Building
- .NET Core 6.x
- Visual Studio 2022
- Avalonia VS Extension is recommended but not required

# Committing
- Your pull request should only contain code for a single addition or fix
- Unless it refers to an existing issue, write into your pull request what it does
