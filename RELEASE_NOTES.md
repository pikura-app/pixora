## Pixora 1.6.4

Image editor improvements, new Opacity slider, and batch download fixes.

### New Features
- **Image editor — Opacity slider** — New slider in Basic Adjustments (0 – 100%) lets you make the entire image more or less transparent. The alpha channel is preserved on export to PNG.
- **Download notifications** — Windows toast notifications for download jobs (individual, batch, and scheduled). Three separate toggles in **Settings → Config → Download Notifications**: *Download started*, *Download completed*, *Download failed*. Each toast shows the artist's profile photo as a hero image.
- **Update notifications** — Toast notifications when a new Pixora version is detected, with the Pixora icon in the top-left corner.

### Improvements
- **Image editor — color overlay color picker** — Replaced the default Fluent `ColorPicker` with a clean full-width color swatch button that matches the slider layout. Clicking opens a `ColorView` flyout. The flyout now correctly renders in dark mode (white text and icons) via a `ThemeVariantScope`.
- **Image editor — color overlay opacity** — Fixed: moving the Opacity slider now applies the overlay immediately even before a color has been explicitly chosen (defaults to black).
- **Image editor — live preview performance** — Further reductions to preview lag: preview resolution reduced to 600 px wide; slider ticks coalesced to ~60 fps (16 ms debounce); `WriteableBitmap` reused across frames to eliminate per-frame ~6 MB allocations; color-matrix adjustments (brightness, contrast, saturation, hue, temperature, tint, highlights, shadows) composed into a single GPU filter pass; blur uses a downscale → blur → upscale strategy for large radii (~4× faster).
- **Changelog dialog** — Increased dialog height and made it resizable so long release notes are fully readable without truncation.
- **Toast icon** — App icon now appears as a small square in the top-left of notifications (matching the reference Windows toast style), not cropped in a circle.

### Fixes
- **Batch download — search results limit** — Fixed: searches were always capped at 60 results regardless of the "Max Results" setting. The downloader now paginates through Pixiv's 60-per-page API until the requested limit is reached.
- **Batch download — search mode/sort order** — Fixed: the Safe/R-18/All mode selector and the Newest/Popular sort order selector were always behaving as "All" / default. Bindings now correctly use `SelectedValueBinding="{ReflectionBinding Tag}"` to round-trip the string tag values.
- **Gallery — followed artists count badge** — Fixed: the count badge at the top of the Gallery was showing the unreliable Pixiv API `total` (which includes deleted/hidden accounts). It now shows the actual number of artists loaded (`Artists.Count`), matching the status bar.
- **Artist select dialog — followed artists count** — The "Add Followed Artists" dialog in batch download now reuses the Gallery's already-loaded followed-artists list (same source, same count) instead of fetching independently, so the count is always consistent.


