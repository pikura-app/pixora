## Pixora 1.6.1

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

### Improvements
- Linux login falls back to a manual PHPSESSID entry dialog if the GTK WebKit cookie manager is unavailable on the current platform.
- "Add account" from the main window sidebar now also uses the GTK WebKit login path on Linux.

---

## Pixora 1.6.0

Enhanced ugoira (animated illustration) support and UI improvements.

### New features
- **Ugoira individual frames extraction** — New global settings to download ugoira frames as individual PNG images without creating video files.
  - Settings → Storage & Naming → "Save individual frames as PNG images" — Extracts all frames to a `{artworkId}_frames/` subfolder
  - "Frames only (skip video/GIF generation)" — When enabled alongside the above, skips MP4/GIF/WebM/WebP/APNG creation entirely
- **Download preset ugoira options** — Presets now support "Save processed frames to subfolder" and "Frames only" modes for ugoira downloads.

### Improvements
- Better preset UI synchronization — ugoira checkbox states now properly sync when selecting or saving presets.

---

## Pixora 1.5.1

Hotfix for v1.5.0.

### Fixes
- **Linux / macOS**: window caption icons (fullscreen, minimize, maximize, close) showed up as empty squares because they relied on the Windows-only `Segoe Fluent Icons` / `Segoe MDL2 Assets` fonts. Replaced with cross-platform Unicode glyphs so they render correctly on every platform.

> Tip for Linux users whose sidebar emoji icons (🔖 📥 🕐 📊 …) still appear blank: install an emoji font, e.g. on Arch:
> `sudo pacman -S noto-fonts noto-fonts-emoji`

---

## Pixora 1.5.0

A big quality-of-life release focused on downloads, image tooling and stability.

### New features
- **Background agent** (`Pixora.Agent`) with IPC for scheduled downloads that keep running when the UI is closed (Windows service / Linux systemd / macOS launchd helpers under `tools/`).
- **Image editor & resize presets** — new `ImageEditorWindow` and `ResizePreset` / `ImageResizeService` for cropping and resizing downloaded artwork.
- **Download presets** — save and reuse batch/search download configurations via the new `DownloadPresetWindow` and user preset store.
- **Crash reporter** — automatic crash capture with a friendly `CrashReportDialog` for sharing details.
- **Schedules overhaul** — richer schedule editor, settings overrides per schedule, and a more reliable executor.

### Improvements
- Rewritten **History view** with live thumbnails, progress and per-job actions.
- Reworked **Bookmarks**, **Discover** and **Rankings** views with faster loading and cleaner layouts.
- Settings page consolidated into a single screen (legacy `EnhancedSettingsView` removed).
- Multi-account login is more reliable, with safer cookie handling and faster gallery refresh after switching accounts.
- Gallery / inline viewer: smoother zoom & pan, better masonry layout, more responsive thumbnails.
- Hoshi (AI) flow: more robust image preprocessing and session handling.

### Fixes
- Several thread-safety fixes around login, downloads and image loading.
- Correct artist root resolution for "Open Folder".
- Numerous small UI polish fixes across dialogs and converters.

---

Full diff: https://github.com/pikura-app/pixora/compare/v1.1.1-beta.9...v1.5.0
