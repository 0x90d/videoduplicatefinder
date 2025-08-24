# Video Duplicate Finder
Video Duplicate Finder is a cross-platform software to find duplicated video (and image) files on hard disk based on similiarity. That means unlike other duplicate finders this one does also finds duplicates which have a different resolution, frame rate and even watermarked.

# Features
- Cross-platform
- Fast scanning speed
- Ultra fast rescan
- Optional calling ffmpeg functions natively for even more speed
- Finds duplicate videos / images based on similarity (new: optional scan against pHash at zero cost)
- Windows, Linux and MacOS GUI

# Binaries

[Daily build](https://github.com/0x90d/videoduplicatefinder/releases/tag/3.0.x) (You need to download FFmpeg and FFprobe yourself, see below! Please note the attachments of this release are automatically created and replaced on every new commit.)


# Requirements

#### FFmpeg & FFprobe:

Native ffmpeg binding works only with a specific ffmpeg version. Never use master version. Currently it works with ffmpeg 7.x (might change)

#### Windows user:
Get latest package from https://ffmpeg.org/download.html I recommend the full (GPL) shared version. If you want to use native ffmpeg binding you **must** use the shared version.

Extract ffmpeg and ffprobe into the same directory of VDF.GUI.dll or into a sub folder called `bin`. Or make sure it can be found in `PATH` system environment variable

#### Linux user:
Installing ffmpeg:
```
sudo apt-get update
sudo apt-get install ffmpeg
```
Open terminal in VDF folder and execute `./VDF.GUI`
You may need to set execute permission first `sudo chmod 777 VDF.GUI`

#### MacOS user:
Install ffmpeg / ffprobe using homebrew

Open terminal in VDF folder and execute `./VDF.GUI` or if you have .NET installed `dotnet VDF.GUI.dll`

You may get a permission error. Open system settings of your Mac, go to `Privacy & Security` and then `Developer Tools`. Now add `Terminal` to the list.

If the process is immediately killed (something like `zsh: killed`), the binary likely needs to be signed. Run `codesign --force --sign - ./VDF.GUI` and try again.

# Screenshots (slightly outdated)
<img src="https://user-images.githubusercontent.com/46010672/129763067-8855a538-4a4f-4831-ac42-938eae9343bd.png" width="510">

# License
Video Duplicate Finder is licensed under AGPLv3

# Credits / Third Party
- [Avalonia](https://github.com/AvaloniaUI/Avalonia)
- [ActiPro Avalonia Controls (Free Edition)](https://github.com/Actipro/Avalonia-Controls)
- [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen)
- [protobuf-net](https://github.com/protobuf-net/protobuf-net)
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp)

# Building
- .NET 9.x
- Visual Studio 2022 is recommended

# Committing
- Create a pull request for each addition or fix - do NOT merge them into one PR
- Unless it refers to an existing issue, write into your pull request what it does
- For larger PRs I recommend you create an issue for discussion first
