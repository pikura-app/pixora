## Pixora 1.6.2

Bug fixes, stability improvements, and Linux sign-in support.

### Fixes
- **Linux sign-in** — Pixiv login now works on Linux using a native GTK WebKit browser window (`libwebkit2gtk-4.1`). Previously the embedded WebView would show a blank screen or fail to render due to missing WPE WebKit libraries on Ubuntu 24.04.
- **Linux rendering** — Added software rendering fallback env vars (`WEBKIT_DISABLE_DMABUF_RENDERER`, `WEBKIT_DISABLE_COMPOSITING_MODE`, `LIBGL_ALWAYS_SOFTWARE`) so the GTK WebKit window renders correctly in virtualized environments (VMware, VirtualBox).
- **Linux startup crash** — Fixed a silent startup failure where `Microsoft.Extensions.Hosting` would call `GetCwd()` on a deleted or unreachable working directory. The app now sets its working directory to `AppContext.BaseDirectory` before initializing the host.
- **Update banner overlap** — Fixed the "Update available", "Downloading", and "Ready to install" banners being hidden behind the title bar overlay. Added correct top margin so banners are always visible.
- **Start with Windows toggle** — Fixed a registry key name mismatch that caused the startup toggle in Settings to silently fail. The correct key name is now used and any legacy key is cleaned up automatically.
- **Accessibility settings threading** — Fixed `Settings.Changed` event handler in `AccessibilityService` not marshaling to the UI thread, which could cause cross-thread exceptions when applying font scaling or high contrast changes.
- **Ugoira locale bug** — Fixed a decimal separator formatting issue in the ffconcat duration string that caused ffmpeg to fail or produce incorrect animations on systems using a non-English locale.
- **Animated image race condition** — Fixed a race condition in `AnimatedImage` where a cancelled decode could overwrite the current source, causing stuck or incorrect animations.
- **Periodic update checks** — The app now re-checks for updates every 6 hours while running, so long-running sessions will notice new releases without requiring a restart.
- **Rankings "Download Selected" button** — Fixed the button remaining permanently disabled. The checkbox click handler was calling `e.Handled = true`, preventing the checkbox from toggling `IsSelected`. Selecting artworks now correctly enables the button.
- **Caption button icon sizes** — Fixed the fullscreen and maximize window buttons rendering smaller than the minimize and close buttons. All four caption buttons now share a uniform font size.

### Improvements
- Linux login falls back to a manual PHPSESSID entry dialog if the GTK WebKit cookie manager is unavailable on the current platform.
- "Add account" from the main window sidebar now also uses the GTK WebKit login path on Linux.

