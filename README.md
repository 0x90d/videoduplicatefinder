# Video Duplicate Finder
Video Duplicate Finder is a cross-platform software to find duplicated video (and image) files on hard disk based on similiarity. That means unlike other duplicate finders this one does also finds duplicates which have a different resolution, frame rate and even watermarked.

# Features
- Cross-platform
- Fast scanning speed
- Ultra fast rescan
- Optional calling ffmpeg functions natively for even more speed
- Finds duplicate videos / images based on similarity
- Windows, Linux and MacOS GUI

# Requirements
FFmpeg and FFprobe is required. When using native ffmpeg mode you must download SHARED and stable version of ffmpeg, which is currently `avcodec-58.dll` etc

# Binaries

[Daily build v3 (unstable)](https://github.com/0x90d/videoduplicatefinder/releases/tag/3.0.x) (You need to download FFmpeg and FFprobe yourself, see below! Please note the attachments of this release are automatically created and replaced on every new commit.)

[Latest release v2 (stable but no support)](https://github.com/0x90d/videoduplicatefinder/releases/tag/2.0.8) (You need to download FFmpeg and FFprobe yourself, see below!)


# Requirements

#### FFmpeg & FFprobe:
Get latest package from https://ffmpeg.org/download.html I recommend the shared version

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
After installing FFmpeg & Ffprobe (see above), double click on ffmpeg. You will get an error that ffmpeg is unsigned. Now open macOS settings -> Security -> General and choose 'Always open'. Do the same for ffprobe. Now open Terminal in VDF folder and give VDF.GUI executable rights `sudo chmod -R 755 VDF.GUI`. Now right click on `VDF.GUI` and choose to open with terminal. Please remember that the steps above to allow running unsigned ffmpeg and ffprobe only last 30 minutes. Alternatively find signed versions, sign them yourself or search for other workarounds to permanently run unsigned apps on your macOS.

# Screenshots
#### v3
<img src="https://user-images.githubusercontent.com/46010672/129763067-8855a538-4a4f-4831-ac42-938eae9343bd.png" width="510">


#### v2
<details>
  <summary>Click</summary>
  
![windows](https://user-images.githubusercontent.com/46010672/50975469-97e5d900-14e5-11e9-9aba-5a843546ac2c.jpg)
![linux](https://user-images.githubusercontent.com/46010672/50975476-9e745080-14e5-11e9-8332-b0ac816458f4.jpg)

</details>

# License
Video Duplicate Finder is licensed under GPLv3  
Video Duplicate Finder uses ffmpeg / ffprobe (not included) which is licensed under LGPL 2.1 / GPL v2


# Building
### Please note
Master branch is used for v3 which is currently not recommended to be used in productive environments. If you build in order to use it you should get the code from v2 instead: https://github.com/0x90d/videoduplicatefinder/tree/c19190196588dd2982e269b28ff23abf78e8ff5e

- .NET Core 6.x SDK DAILY builds
- Visual Studio 2022
- Avalonia VS Extension is recommended but not required

# Committing
- Your pull request should only contain code for a single addition or fix
- Unless it referes to an existing issue, write into your pull request what it does
- Changes in VDF.Core should be discussed in issue section first
