# Quiet Pulse

The Voice Input mark is a waveform that resolves into a text cursor: four rounded bars of uneven height rise and fall, then one steadier, brighter stroke stands still. Sound becomes writing; nothing else is drawn.

The bars sweep from violet through rose to amber, the same colours the overlay uses for the stages of a dictation. They sit on a deep indigo tile with a very soft highlight on its upper edge and a faint luminous halo around the mark. The cursor is almost white with a warm core, so it stays the brightest point at every size.

Detail is earned only when it survives reduction. At 32 pixels and below the halo disappears, the mark uses fewer, thicker bars and the cursor moves closer to the edge, so the tray glyph still reads as "waveform plus cursor". The tile is a superellipse, not a rounded rectangle, which sits correctly next to Windows 11 app tiles; a hairline rim keeps its edge readable on both light and dark taskbars.

One source drives every surface: executable, tray, installer, Start menu, desktop shortcut, Installed Apps entry, the window headers and the release artwork. `tools/render-icon.py` draws all sizes (16 to 256 px) from code, so the mark never drifts between files.
