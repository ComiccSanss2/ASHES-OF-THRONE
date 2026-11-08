# Gothic Manor Pixel UI Kit – Ravenhollow Lite (free)

A complete small kit taken from the full Ravenhollow pack: crimson velvet, wrought iron,
silver edges and pointed arches for gothic, horror and dark-fantasy games. It is enough to
finish a real small screen: a title or pause menu, a settings window, a dialogue box, a small
inventory or a HUD.

Version 1.0. 152 native PNG files in 64 elements, at 1x and an exact
2x copy, under the **same licence as the full pack**, so commercial use is allowed.

## What is inside

| Group | Elements | Native files |
| --- | ---: | ---: |
| Windows and dialogue | 7 | 14 |
| Buttons | 4 | 14 |
| Controls | 14 | 54 |
| Bars | 7 | 7 |
| Slots | 2 | 6 |
| Cursors, badges and a ribbon | 6 | 8 |
| Icons | 24 | 49 |

- **Windows and dialogue:** the fleur window (resizable), a plain resizable panel, the ornate
  speech box, name plate and portrait frame, dialogue choice rows in 6 states and the animated
  continue arrow (3 frames).
- **Buttons:** the primary and secondary buttons in every state (normal, hover, pressed,
  disabled, focus, selected, selected and focused). Both stretch to any width.
- **Controls:** tabs, a dropdown with its list and items, a key-binding cap, a slider (track,
  fill and knob), a text field with its caret, checkboxes and toggles, on and off.
- **Bars:** health, mana and XP frames with their fills and the tick overlay.
- **Slots:** the 40 px item slot (idle, hover, focus, selected, selected and focused, disabled).
- **Cursors:** the arrow pointer and the hand, idle and click (16 px; the 2x copies are 32 px).
- **Badges:** "new", check and counter badges and a velvet ribbon.
- **24 icons at 24 px**, each with a disabled version: bat, candle, character, check, chest, close, coffin, coin, gear, gem, heart, info, inventory, key, mana potion, map, mute, potion, save, scroll, shield, skull, sound, sword. The dialogue
  example also uses the 48 px character icon, which is included.

| Folder or file | Files | What it holds |
| --- | ---: | --- |
| `1x/` | 594 | Native sprites, their 9-slice pieces (`pieces/`) and ready-made sizes (`presized/`) |
| `2x/` | 594 | The same files as exact 2x nearest-neighbour copies |
| `screens/` | 8 | Two example screens (settings, dialogue): layouts, 960x540 and 3x images, traces |
| `fonts/` | 14 | Two bitmap fonts (atlases and metrics), the original TTFs and their OFL licences |
| `palette/` | 5 | The palette as JSON, GIMP `.gpl`, `.hex` and swatch images |
| `manifest.json` | 1 | Every file: size, state, 9-slice insets, fill mode and usage notes |
| `catalog.json` | 1 | The same index without the palette and font details |
| `LLM_GUIDE.md` | 1 | A short guide for AI coding assistants |

## How to use it

Use nearest-neighbour filtering and integer positions. Work on a 960x540 screen at 1x and
scale by a whole number (2x fills 1080p). Resize only elements that have `nine_slice` in
`manifest.json`, with its insets and fill mode (`stretch` or `tile`); everything else is used
at its own size. Insets are always in the order **left, top, right, bottom** (keys `l t r b`).
Each file's `native` block in the manifest gives its label box, body and placement notes; the
example layouts in `screens/layout/` show every element in place.

Your game renders the text. For the exact look use the bitmap atlases in `fonts/fonts/`:
Jacquard 12 for short headings, Jersey 10 for body text and numbers.

## What the full pack adds

The full Gothic Manor Pixel UI Kit – Ravenhollow has **3,226 native PNG files in 1,041 elements** across
8 categories, at 1x, 2x, 3x, 4x:

- 318 icon subjects at 24 and 48 px, each with a disabled version;
- every button style: hero buttons with three ornaments, large, danger and ghost buttons, round
  and square icon buttons, arrow buttons and steppers;
- more windows and panels: three corner ornaments, windows with a close button, cards, tooltips,
  header and footer strips, insets and dividers;
- more controls: radios, number and multiline fields, scroll bars, pill tabs, every field state;
- thick, thin, vertical, boss and loading bars, bar caps, flask and radial meters;
- the hotbar slot, cursed slot states, rarity frames and portrait frames;
- banners, more ribbons and badges, ornaments, brackets and arch caps;
- 40 cursor elements at 16 and 32 px with hotspots, mouse and gamepad prompts;
- animations with timings: bar flashes, candle flicker, coin flip, page turn, spinners and more;
- skill-tree nodes and connectors, numerals and masks;
- 8 example screens with layouts, spritesheets with JSON indexes, a working
  example page and the full LLM guide.

See the full pack ($4.99): https://brunkagames.itch.io/gothic-manor-pixel-ui-kit-ravenhollow

## Licence in short

- Use the sprites in any number of personal and commercial projects, with no royalties. Credit
  is appreciated, not required.
- Modify, recolour, crop, animate and combine them for those projects.
- Do not resell or redistribute the sprites themselves, even modified, and do not upload them to
  public repositories or asset stores.
- Do not use them to train, fine-tune or evaluate generative AI models.

The full text is in `LICENSE.txt`. The fonts carry their own SIL Open Font Licence notices.

Not affiliated with, endorsed or sponsored by Sony, Microsoft or Nintendo.

This pack was made with an AI-assisted workflow: AI-generated images served as references
and base layers for the pixel art, and the tool code and this documentation were written
with AI assistance. Every count in this file is computed from `manifest.json` when the kit is
built. Requests are welcome: leave a comment on the itch.io page.
