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
