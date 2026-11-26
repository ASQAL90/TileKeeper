# TileKeeper documentation

- [`USER_GUIDE.md`](USER_GUIDE.md) — complete Markdown user guide.
- [`SUPPORTED-FILES.md`](SUPPORTED-FILES.md) — file types, association-based support plan, and limits.
- [`release-notes/1.2.2.md`](release-notes/1.2.2.md) — changes and Windows test checklist for v1.2.2.
- [`user-guide.html`](user-guide.html) — detailed, standalone HTML guide with quick start, plain-language explanations, responsive navigation, visual UI mockups, troubleshooting, and glossary.
- [`TileKeeper-architecture.pptx`](TileKeeper-architecture.pptx) — 20-slide beginner-friendly tutorial and architecture presentation, including four dedicated visual-example slides.
- [`create_presentation.py`](create_presentation.py) — reproducible 16-slide base presentation generator; run with `uv run --with python-pptx python docs/create_presentation.py`, then run the illustration script.
- [`add_illustrations.py`](add_illustrations.py) — creates and embeds tutorial illustrations; run with `uv run --with python-pptx --with pillow python docs/add_illustrations.py`.
- `illustrations/` — source PNG illustrations embedded into the presentation.

The documentation describes the current no-password build. The application runs from the Windows system tray, monitors configured programs, and restores their windows to assigned monitors and grid cells.