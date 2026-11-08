# Third-party notices

Scroll Video Player uses the following third-party components:

- **LibVLCSharp.WPF 3.10.2** — managed WPF integration for the VLC media engine.
  Project: https://code.videolan.org/videolan/LibVLCSharp
  License: LGPL-2.1-or-later.
- **VideoLAN.LibVLC.Windows 3.0.24** — native VLC media engine and its plugins.
  Project: https://www.videolan.org/vlc/
  License: LGPL-2.1-or-later (the package may include separately licensed plugins/codecs; see the upstream distribution notices).

The VLC engine and its plugin files are distributed as separate native libraries alongside the application. The application does not modify those libraries. Refer to the upstream projects and the license files distributed with their packages for the complete license terms and source-code availability.

This project does not bundle proprietary codecs beyond those present in the referenced VLC distribution.
