## Pixora 1.6.4

Image editor improvements, new Opacity slider, and batch download fixes.

### New Features
- **Image editor — Opacity slider** — New slider in Basic Adjustments (0 – 100%) lets you make the entire image more or less transparent. The alpha channel is preserved on export to PNG.

### Improvements
- **Image editor — color overlay color picker** — Replaced the default Fluent `ColorPicker` with a clean full-width color swatch button that matches the slider layout. Clicking opens a `ColorView` flyout. The flyout now correctly renders in dark mode (white text and icons) via a `ThemeVariantScope`.
- **Image editor — color overlay opacity** — Fixed: moving the Opacity slider now applies the overlay immediately even before a color has been explicitly chosen (defaults to black).
- **Image editor — live preview performance** — Further reductions to preview lag: preview resolution reduced to 600 px wide; slider ticks coalesced to ~60 fps (16 ms debounce); `WriteableBitmap` reused across frames to eliminate per-frame ~6 MB allocations; color-matrix adjustments (brightness, contrast, saturation, hue, temperature, tint, highlights, shadows) composed into a single GPU filter pass; blur uses a downscale → blur → upscale strategy for large radii (~4× faster).

### Fixes
- **Batch download — search results limit** — Fixed: searches were always capped at 60 results regardless of the "Max Results" setting. The downloader now paginates through Pixiv's 60-per-page API until the requested limit is reached.
- **Batch download — search mode/sort order** — Fixed: the Safe/R-18/All mode selector and the Newest/Popular sort order selector were always behaving as "All" / default. Bindings now correctly use `SelectedValueBinding="{ReflectionBinding Tag}"` to round-trip the string tag values.
- **Gallery — followed artists count badge** — Fixed: the count badge at the top of the Gallery was showing the unreliable Pixiv API `total` (which includes deleted/hidden accounts). It now shows the actual number of artists loaded (`Artists.Count`), matching the status bar.
- **Artist select dialog — followed artists count** — The "Add Followed Artists" dialog in batch download now reuses the Gallery's already-loaded followed-artists list (same source, same count) instead of fetching independently, so the count is always consistent.

---

## Pixora 1.6.3

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
- **In-app update installer** — Fixed the "Download & Install" update flow not restarting the app after installation. The installer now receives the correct install directory, exits cleanly, and relaunches the updated app automatically.

### Improvements
- Linux login falls back to a manual PHPSESSID entry dialog if the GTK WebKit cookie manager is unavailable on the current platform.
- "Add account" from the main window sidebar now also uses the GTK WebKit login path on Linux.

