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

#### Windows user:
FFmpeg and FFprobe are already included. Otherwise get latest package from https://ffmpeg.zeranoe.com/builds/ and extract ffmpeg.exe and ffprobe into the same directory where VideoDuplicateFinderWindows.exe is placed in.

#### Linux user:
Install latest ffmpeg and ffprobe the usual way and verify PATH environment variable is set. Also make sure you got **libgdiplus** installed.

```
sudo apt-get update
sudo apt-get install ffmpeg
sudo apt-get install libgdiplus
```

# Screenshots
![windows](https://user-images.githubusercontent.com/46010672/50975469-97e5d900-14e5-11e9-9aba-5a843546ac2c.jpg)
![linux](https://user-images.githubusercontent.com/46010672/50975476-9e745080-14e5-11e9-8332-b0ac816458f4.jpg)


# License
Video Duplicate Finder is licensed under GPLv3  
Video Duplicate Finder uses ffmpeg / ffprobe (not included) which is licesed under LGPL 2.1 / GPL v2
