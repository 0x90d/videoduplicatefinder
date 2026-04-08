# Video Duplicate Finder
Video Duplicate Finder is a cross-platform software to find duplicated video (and image) files on hard disk based on similarity. Unlike other duplicate finders this one also finds duplicates which have a different resolution, frame rate and even watermarked.

# Features
- Cross-platform
- Fast scanning speed
- Ultra fast rescan
- Optional calling ffmpeg functions natively for even more speed
- Finds duplicate videos / images based on similarity (optional scan against pHash at zero cost)
- Partial clip detection — finds when a shorter video is a partial clip of a longer one (audio fingerprinting)
- Desktop GUI (Windows, Linux, macOS)
- Headless CLI for scripting and automation
- Web UI for remote/headless/NAS use
- Docker image for easy self-hosting

# Partial Clip Detection

VDF can detect when a shorter video is a partial clip of a longer one — for example, a scene ripped from a movie, or a clip saved from a longer recording. This works even when there is no visual overlap between the two files.

It runs as an **optional second phase** after the normal visual duplicate scan, using an audio fingerprinting pipeline (Chromaprint-style chroma extraction + sliding-window Hamming similarity matching). Matched pairs appear in the duplicate list with a **Clip Offset** column showing where in the source the clip starts.

### Enabling it

In **Settings → Partial Clip Detection**, check **Enable Partial Clip Detection** and adjust:

| Setting | Default | Description |
|---------|---------|-------------|
| Min clip / source ratio (%) | 10 | Minimum clip duration as a percentage of the source duration. Clips shorter than this are ignored. |
| Min audio similarity (%) | 80 | Minimum average Hamming similarity for the sliding-window fingerprint match to be accepted. |

> **Note:** Partial clip detection requires audio tracks in both files. Videos without audio are skipped.

---

# Downloads

[Daily build](https://github.com/0x90d/videoduplicatefinder/releases/tag/3.0.x) — attachments are automatically rebuilt and replaced on every commit.

Available packages per platform:
- `GUI-<platform>` — desktop application
- `CLI-<platform>` — command-line tool
- `Web-<platform>` — self-contained web server

---

# Desktop GUI

### Requirements

FFmpeg and FFprobe are required. On first launch VDF attempts to download them automatically.
Native FFmpeg binding requires FFmpeg 8.x shared libraries (not the master branch).

#### Windows
Download the latest FFmpeg GPL shared package from https://ffmpeg.org/download.html
Extract `ffmpeg.exe` and `ffprobe.exe` into the same folder as `VDF.GUI.exe`, a subfolder named `bin`, or ensure they are on your `PATH`.

#### Linux
```bash
sudo apt-get update && sudo apt-get install ffmpeg
```
Then run:
```bash
chmod +x VDF.GUI
./VDF.GUI
```

**Optional: add to your application menu**

The Linux archive includes `videoduplicatefinder.desktop` and `icon.png`. To register the app with your desktop environment (GNOME, KDE, XFCE, etc.):

```bash
# Edit the Exec= and Icon= paths to match where you extracted the archive, e.g.:
sed -i "s|/opt/videoduplicatefinder|$(pwd)|g" videoduplicatefinder.desktop

# Install for the current user
mkdir -p ~/.local/share/applications
cp videoduplicatefinder.desktop ~/.local/share/applications/
```

The app will then appear in your application launcher with its icon.

#### macOS
```bash
brew install ffmpeg
```
Extract the archive — it contains `Video Duplicate Finder.app`. Double-click it to launch.

If macOS blocks the app with "cannot be opened because the developer cannot be verified", right-click the `.app` and choose **Open**, then confirm. You only need to do this once.

If the process is immediately killed (`zsh: killed`), the binary needs to be signed. Run:
```bash
codesign --force --sign - "Video Duplicate Finder.app/Contents/MacOS/VDF.GUI"
```

---

# CLI (Command-line Interface)

The CLI is useful for scripting, scheduled tasks, and headless servers where no display is available.

### Requirements

Same as the GUI: FFmpeg and FFprobe must be on your `PATH` or in the same directory as the `vdf-cli` binary.

### Installation

Download `CLI-<platform>` from the [releases page](https://github.com/0x90d/videoduplicatefinder/releases/tag/3.0.x) and extract it.

On Linux/macOS, make the binary executable:
```bash
chmod +x vdf-cli
```

### Usage

#### Scan and compare in one step
```bash
vdf-cli scan-and-compare --include /path/to/media
```

#### Scan multiple directories, save results as JSON
```bash
vdf-cli scan-and-compare \
  --include /mnt/movies \
  --include /mnt/series \
  --exclude /mnt/movies/extras \
  --format json \
  --output results.json
```

#### Common options
| Flag | Description | Default |
|------|-------------|---------|
| `--include <path>` | Directory to scan (repeatable) | required |
| `--exclude <path>` | Directory to exclude (repeatable) | — |
| `--threshold <n>` | Hash difference threshold | 5 |
| `--percent <n>` | Minimum similarity % to report | 96 |
| `--parallelism <n>` | Parallel hashing threads | 1 |
| `--include-images` | Also scan image files | off |
| `--use-phash` | Use perceptual hashing | off |
| `--partial-clip-detection` | Enable partial clip detection (audio fingerprinting) | off |
| `--partial-clip-min-ratio <n>` | Min clip/source duration ratio (0.0–1.0) | 0.10 |
| `--partial-clip-similarity <n>` | Min audio fingerprint similarity (0.0–1.0) | 0.80 |
| `--format json\|text\|csv` | Output format | text |
| `--output <file>` | Write results to file instead of stdout | stdout |
| `--settings <file>` | Load full settings from a JSON file | — |

#### Auto-mark and delete duplicates
```bash
# Dry run — shows what would be deleted, no changes made (default)
vdf-cli scan-and-compare --include /mnt/media --action lowest-quality --dry-run

# Move duplicates to trash (safer)
vdf-cli scan-and-compare --include /mnt/media --action lowest-quality --delete

# Permanently delete (use with care)
vdf-cli scan-and-compare --include /mnt/media --action lowest-quality --delete-permanent
```

Available `--action` strategies:

| Strategy | Keeps |
|----------|-------|
| `lowest-quality` | Highest bitrate/resolution per group |
| `smallest-file` | Largest file per group |
| `shortest-duration` | Longest duration per group |
| `worst-resolution` | Highest resolution per group |
| `100-percent-only` | Only acts on 100% identical groups |

> **Note:** Automatic deletion is not recommended. Always review results with `--dry-run` first.

---

# Web UI

The Web UI runs as a local web server and is accessed from your browser. It is designed for headless machines, NAS devices, and remote management.

> **Security note:** The Web UI is password-protected but intended for local/Docker use only. Do not expose it to the internet.

### Authentication

On first launch, a random password is generated and printed to the console:

```
============================================
  Web UI password:  aB3xK9mQ7p
============================================
```

Enter this password in your browser to log in. A "Remember me" cookie keeps you logged in for 30 days.

**Docker users:** Run `docker logs vdf-web` to see the password.

| Environment variable | Description |
|---------------------|-------------|
| `VDF_WEB_PASSWORD` | Set your own password instead of the auto-generated one |
| `VDF_WEB_AUTH=false` | Disable authentication entirely |

### Requirements

FFmpeg and FFprobe are required. When running outside Docker, VDF.Web will attempt to download them automatically on first launch. You can also install them manually via your system package manager or place them on your `PATH`.

### Installation (self-contained archive)

Download `Web-<platform>` from the [releases page](https://github.com/0x90d/videoduplicatefinder/releases/tag/3.0.x) and extract it.

On Linux/macOS:
```bash
chmod +x VDF.Web
./VDF.Web
```

On Windows:
```
VDF.Web.exe
```

Then open **http://localhost:5000** in your browser and enter the password shown in the console.

To change the port:
```bash
ASPNETCORE_URLS=http://+:8080 ./VDF.Web
```

Settings and the scan database are saved to:
- Windows: `%APPDATA%\VDF\`
- Linux: `~/.config/VDF/`
- macOS: `~/Library/Preferences/VDF/`

---

# Docker (Web UI)

Docker is the easiest way to run the Web UI on a NAS, home server, or any Linux machine. FFmpeg is included in the image — no separate installation needed.

### Requirements

- [Docker](https://docs.docker.com/get-docker/) installed

### Quick start

```bash
docker run -d \
  --name vdf-web \
  -p 8080:8080 \
  -v vdf-db:/root/.config/VDF \
  -v /path/to/your/media:/media:ro \
  ghcr.io/0x90d/vdf-web:latest
```

Then open **http://localhost:8080** in your browser.
Check the password with `docker logs vdf-web` and enter it to log in.
Inside the Web UI, add `/media` (or whatever path you mounted) as a scan directory.

To set your own password:
```bash
docker run -d \
  --name vdf-web \
  -p 8080:8080 \
  -e VDF_WEB_PASSWORD=mysecretpassword \
  -v vdf-db:/root/.config/VDF \
  -v /path/to/your/media:/media:ro \
  ghcr.io/0x90d/vdf-web:latest
```

### docker compose (recommended for permanent installs)

1. Download [`docker-compose.yml`](docker-compose.yml) from this repository.

2. Edit the file and add your media volume mounts. Optionally set your own password:
```yaml
environment:
  - VDF_WEB_PASSWORD=mysecretpassword    # optional — otherwise check docker logs
volumes:
  - /mnt/nas/movies:/mnt/nas/movies:ro
  - /mnt/nas/series:/mnt/nas/series:ro
```

3. Start the service:
```bash
docker compose up -d
```

4. Open **http://localhost:8080** in your browser and enter the password (check `docker logs` if you didn't set one).

5. To update to the latest image:
```bash
docker compose pull && docker compose up -d
```

### Volume reference

| Volume | Purpose |
|--------|---------|
| `/root/.config/VDF` | Settings and scan database — mount a named volume here so data persists across container updates |
| Your media paths | Mount each media directory you want to scan. Read-only (`:ro`) is recommended. |

### Notes

- The container image is built for `linux/amd64` and `linux/arm64` (Raspberry Pi / NAS ARM boards).
- The image is published to [GitHub Container Registry](https://github.com/0x90d/videoduplicatefinder/pkgs/container/vdf-web) and updated automatically on every commit.

---

# Screenshots (outdated)
<img src="https://user-images.githubusercontent.com/46010672/129763067-8855a538-4a4f-4831-ac42-938eae9343bd.png" width="510">

# License
Video Duplicate Finder is licensed under AGPLv3

# Credits / Third Party
- [Avalonia](https://github.com/AvaloniaUI/Avalonia)
- [ActiPro Avalonia Controls (Free Edition)](https://github.com/Actipro/Avalonia-Controls)
- [FFmpeg.AutoGen](https://github.com/Ruslan-B/FFmpeg.AutoGen)
- [protobuf-net](https://github.com/protobuf-net/protobuf-net)
- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp)
- [AcoustID.NET by wo80](https://github.com/wo80/AcoustID.NET) — the audio fingerprinting pipeline (Chromaprint-style chroma extraction, FIR smoothing, and fingerprint encoding) used for partial clip detection is derived from this library, licensed under LGPL 2.1

# Building
- .NET 9.x
- Visual Studio 2022 or later is recommended

# Contributing
- Create a pull request for each addition or fix — do not merge them into one PR
- Unless it refers to an existing issue, write into your pull request what it does
- For larger PRs, open an issue for discussion first
