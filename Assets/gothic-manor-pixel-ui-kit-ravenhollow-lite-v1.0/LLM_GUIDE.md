# Gothic Manor Pixel UI Kit – Ravenhollow Lite: guide for AI coding assistants

This short guide lets a coding assistant use the Lite kit without opening the images. When in
doubt, `manifest.json` wins. The full pack ships a longer guide with screen recipes and motion
data for every element.

Kit: 152 native PNG files in 64 elements, scales 1x, 2x.

## Rules

1. Work in 1x pixels on a 960x540 canvas, then scale by a whole number with nearest-neighbour
   filtering. The 2x files are exact pixel doublings.
2. `manifest.files` maps `<category>/<file>.png` to its record; the image is `1x/<category>/<file>.png`
   (or `2x/...`). `manifest.assets` lists the elements: `id`, `category`, `files`, `states`,
   `sizes` and `nine_slice` (true when the element is resizable).
3. Resize only files whose `native` block has `nine_slice` insets, using `derived.recipe`
   (`insets`, `minimum`, `modes`) or the cut pieces in `derived.pieces`. Everything else is
   fixed size. Prebuilt sizes are listed in `derived.presized`.
4. **Inset order:** always **left, top, right, bottom** (keys `l`, `t`, `r`, `b`), in 1x pixels;
   multiply by the scale. CSS `border-image-slice` takes them in another order (top, right,
   bottom, left).
5. States are named in each record (`state`); some elements call the rest state `normal`,
   others `idle`. Never invent a state the manifest does not list.
6. Grid: 8 px grid, 4 px padding and gap. A 24 px icon sits in the 40 px slot or a 32 px cell.
7. Your game renders text on palette colours (`manifest.palette`). For the exact look use
   the bitmap atlases in `fonts/fonts/` (each JSON gives cells, offsets and advances): Jacquard 12
   for headings, Jersey 10 for body text and numbers. A button's `label_box` and `label_rule`
   say where its label goes.
8. Cursors: the hotspot is in the cursor's `native` block; at 2x, double it.
9. Animation: `typed_semantics.recipes` gives the continue arrow's frame order and timings.

## Elements

| Element | Folder | Canvas | Insets l/t/r/b | Fill | States |
|---|---|---|---|---|---|
| `badge_check` | `chrome` | 13x13 | - | fixed size | - |
| `badge_counter` | `chrome` | 20x20 | 7/0/7/0 | `stretch` / `stretch` | - |
| `badge_new` | `chrome` | 13x13 | - | fixed size | - |
| `cursor_hand_16` | `chrome` | 16x16 | - | fixed size | click, idle |
| `cursor_pointer_16` | `chrome` | 16x16 | - | fixed size | click, idle |
| `ribbon_velvet` | `chrome` | 80x32 | 16/0/16/0 | `stretch` / `stretch` | - |
| `dlg_choice_row` | `containers` | 68x27 | 16/0/4/0 | `stretch` / `stretch` | disabled, focus, hover, idle, selected, selected_focus |
| `dlg_continue_arrow` | `containers` | 13x9 | - | fixed size | frame00, frame01, frame02 |
| `dlg_nameplate_ornate` | `containers` | 48x24 | 12/0/12/0 | `stretch` / `stretch` | idle |
| `dlg_portrait_frame_ornate` | `containers` | 66x82 | - | fixed size | idle |
| `dlg_speech_ornate` | `containers` | 42x42 | 16/16/18/18 | `stretch` / `stretch` | idle |
| `panel_clean` | `containers` | 32x32 | 12/12/12/12 | `stretch` / `stretch` | idle |
| `window_fleur` | `containers` | 122x130 | 40/60/42/42 | `stretch` / `stretch` | idle |
| `dropdown` | `inputs` | 100x28 | 13/0/26/0 | `stretch` / `stretch` | disabled, focus, hover, normal, open, open_focus, pressed |
| `dropdown_item` | `inputs` | 32x20 | 8/0/4/0 | `stretch` / `stretch` | hover, normal, selected, selected_hover |
| `dropdown_list` | `inputs` | 32x32 | 4/4/4/4 | `stretch` / `stretch` | - |
| `keycap` | `inputs` | 36x28 | 8/0/8/0 | `stretch` / `stretch` | disabled, focus, hover, listening, normal |
| `slider_fill` | `inputs` | 8x4 | 0/0/1/0 | `stretch` / `stretch` | disabled, normal |
| `slider_knob_round` | `inputs` | 20x20 | - | fixed size | disabled, focus, hover, normal, pressed |
| `slider_track` | `inputs` | 24x9 | 4/0/4/0 | `stretch` / `stretch` | disabled, normal |
| `tab_top` | `inputs` | 68x26 | 8/0/8/0 | `stretch` / `stretch` | disabled, focus, hover, normal, pressed, selected, selected_focus |
| `bar_fill_hp` | `slice` | 16x7 | 0/0/1/0 | `stretch` / `stretch` | - |
| `bar_fill_mana` | `slice` | 16x7 | 0/0/1/0 | `stretch` / `stretch` | - |
| `bar_fill_xp` | `slice` | 16x7 | 0/0/1/0 | `stretch` / `stretch` | - |
| `bar_frame_hp` | `slice` | 128x15 | 24/0/16/0 | `stretch` / `stretch` | - |
| `bar_frame_mana` | `slice` | 128x15 | 24/0/16/0 | `stretch` / `stretch` | - |
| `bar_frame_xp` | `slice` | 128x15 | 24/0/16/0 | `stretch` / `stretch` | - |
| `bar_ticks` | `slice` | 7x7 | - | fixed size | - |
| `button_primary` | `slice` | 100x28 | 11/0/11/0 | `stretch` / `stretch` | disabled, focus, hover, normal, pressed, selected |
| `button_primary_selected` | `slice` | 100x28 | 11/0/11/0 | `stretch` / `stretch` | selected_focus |
| `button_secondary` | `slice` | 84x28 | 11/0/11/0 | `stretch` / `stretch` | disabled, focus, hover, normal, pressed, selected |
| `button_secondary_selected` | `slice` | 84x28 | 11/0/11/0 | `stretch` / `stretch` | selected_focus |
| `checkbox_off` | `slice` | 20x20 | - | fixed size | disabled, focus, hover, idle |
| `checkbox_on` | `slice` | 20x20 | - | fixed size | disabled, focus, hover, idle |
| `field` | `slice` | 100x24 | 4/4/4/4 | `stretch` / `stretch` | disabled, focus, hover, idle |
| `field_caret` | `slice` | 1x13 | - | fixed size | - |
| `slot_40` | `slice` | 44x44 | - | fixed size | disabled, focus, hover, idle, selected |
| `slot_40_selected` | `slice` | 44x44 | - | fixed size | selected_focus |
| `toggle_off` | `slice` | 36x20 | - | fixed size | disabled, focus, hover, idle |
| `toggle_on` | `slice` | 36x20 | - | fixed size | disabled, focus, hover, idle |

Icons (`icons/`, 24x24, states `idle` and `disabled`, file `icon_<name>_24.png` and
`icon_<name>_24_disabled.png`): `icon_bat_24`, `icon_candle_24`, `icon_character_24`, `icon_check_24`, `icon_chest_24`, `icon_close_24`, `icon_coffin_24`, `icon_coin_24`, `icon_gear_24`, `icon_gem_24`, `icon_heart_24`, `icon_info_24`, `icon_inventory_24`, `icon_key_24`, `icon_mana_potion_24`, `icon_map_24`, `icon_mute_24`, `icon_potion_24`, `icon_save_24`, `icon_scroll_24`, `icon_shield_24`, `icon_skull_24`, `icon_sound_24`, `icon_sword_24`; plus `icon_character_48.png`.

## Examples

`screens/layout/settings.json` and `screens/layout/dialogue.json` place every element with its
`box` (x, y, w, h) on a 960x540 screen; `screens/1x/` holds the rendered result. A box larger
than the file's canvas means the file is used as a 9-slice at that size.

## Example prompt

"Using the Ravenhollow Lite kit in this folder, build a 960x540 pause menu: the fleur window
centred, a Jacquard 12 title, three primary buttons stacked 8 px apart with the selected one in
`selected`, and health, mana and XP bars at the top left. Read sizes, insets and label boxes from
manifest.json; scale 2x with nearest-neighbour filtering."
