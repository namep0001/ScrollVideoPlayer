# Scroll Video Player

A Windows desktop player built for browsing a folder as a continuous, swipe-driven video feed. Open a folder once, start watching, and move through the clips without opening them one by one.

## Features

- **Folder queue:** loads supported videos from one folder, with optional recursive subfolder scanning.
- **Automatic playback:** starts with the first video and advances to the next when a clip finishes.
- **Natural filename order:** names such as `clip2` and `clip10` are sorted the way Windows Explorer users expect.
- **Feed gestures:** scroll or swipe up for the next video, down for the previous video, and left to delete the current file.
- **Safe deletion default:** deleted files are sent to the Windows Recycle Bin. Permanent deletion is an explicit setting.
- **Player controls:** play/pause, timeline seeking, volume, mute, playback speed, audio tracks, subtitle tracks, aspect ratio, fullscreen and snapshots.
- **Playlist panel:** inspect the loaded queue and jump directly to a clip.
- **Queue settings:** recursive scan, name/date sorting, shuffle, loop, automatic next, delete confirmation and deletion mode.
- **Portable mode:** the portable ZIP contains the app, .NET runtime, VLC engine and plugins. No .NET runtime installation is required on the target PC.
- **Installer:** a standard Windows installer is generated alongside the portable ZIP.

## Windows support

The application targets **Windows 10 and Windows 11, 64-bit (x64)**. It is built with .NET 10 WPF and LibVLCSharp using the native VLC media engine.

The player recognises common video containers including:

`3GP, ASF, AVI, DIVX, FLV, M2TS, M4V, MKV, MOV, MP4, MPEG, MPG, MTS, OGV, RM, RMVB, TS, VOB, WEBM, WMV`

Actual playback depends on the codecs and demuxers included with the VLC distribution. The extension list is used to find candidate files; it is not a guarantee that every file with that extension is decodable.

## Gestures and keyboard

| Action | Mouse | Touch | Keyboard |
|---|---|---|---|
| Next video | Mouse wheel up | Swipe up | Up arrow |
| Previous video | Mouse wheel down | Swipe down | Down arrow |
| Delete current video | Swipe left | Swipe left | Delete |
| Play / pause | Player button | Player button | Space |
| Seek backward / forward 5 seconds | Timeline | Timeline | Left / Right |
| Mute | Speaker button | Speaker button | M |
| Fullscreen | Fullscreen button | Fullscreen button | F or F11 |
| Open folder | Open folder button | Open folder button | Ctrl+O |
| Toggle playlist | Playlist button | Playlist button | P |
| Exit fullscreen | — | — | Esc |

The swipe recogniser ignores the player controls and playlist items, so dragging the seek bar or choosing a queue item does not trigger a delete gesture.

## Deletion behaviour

The default action is **Move to Windows Recycle Bin**. You can change this in Settings to permanently delete files. You can also enable a confirmation prompt before every deletion.

Swipe-left and the Delete key act on the currently playing file. When a file is removed, the player advances to the next available entry. Permanent deletion cannot be undone.

## Run the portable build

1. Download `ScrollVideoPlayer-Portable-win-x64.zip` from the Releases page.
2. Extract the **entire ZIP** to a folder of your choice.
3. Run `Run-Portable.bat` or `ScrollVideoPlayer.exe`.
4. Choose a folder containing videos.

Keep the extracted files together. The VLC engine and its plugin directory are required at runtime. Portable settings are saved in `settings.json` beside the executable.

## Install normally

Download and run `ScrollVideoPlayer-Setup.exe` from the Releases page. The installer creates Start Menu shortcuts and can optionally create a desktop shortcut. Installed settings are stored in the current Windows user's AppData folder.

## Build from source

Requirements:

- Windows 10/11 x64
- .NET 10 SDK
- Internet access during the first build, so NuGet can restore the VLC and LibVLCSharp packages

Run `Build-Portable.bat` from the repository root. It publishes a self-contained build to `dist\ScrollVideoPlayer`.

The publish step used by CI is:

```powershell
dotnet publish src/ScrollVideoPlayer/ScrollVideoPlayer.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o dist/ScrollVideoPlayer
```

GitHub Actions builds the portable ZIP and Inno Setup installer on pushes, pull requests and manual runs. Pushing a tag such as `v1.0.0` also attaches both packages to a GitHub Release.

## Project structure

- `src/ScrollVideoPlayer` — WPF application, VLC playback, folder scanning and settings.
- `.github/workflows/windows-build.yml` — Windows CI and release packaging.
- `installer/ScrollVideoPlayer.iss` — Inno Setup installer definition.
- `Build-Portable.bat` — local self-contained publish script.
- `Run-Portable.bat` — portable launch helper.

## Third-party components and licensing

The application code is released under the MIT License. The VLC engine and LibVLCSharp are separate third-party components distributed under their respective licenses. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) before redistributing modified packages.
