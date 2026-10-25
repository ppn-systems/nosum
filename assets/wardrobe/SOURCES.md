# wardrobe/ — sources

Clothing and accessory layers for the paper-doll character (64 × 64, same canvas and anchor
as `characters/villager_idle_<direction>.png`). Each layer is transparent except the garment,
so it is drawn straight over the body. Colour variants are made at runtime by shifting the
garment's hue; outline pixels (max channel ≤ 70) keep their colour.

How a layer is made: PixelLab `create_character_state` dresses the base body; the layer keeps
the dressed sprite's pixels that are (a) in the garment's hue window, (b) changed from the base
body by more than 40 (sum of RGB differences), and (c) part of a connected region of at least
20 px, plus dark outline pixels touching them.

| Files | Generator | Prompt | Extraction | Date | PixelLab id |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `jacket_<south,east,north,west>.png` | `create_character_state` on `9badade9-9dfe-4008-bf63-6a519aa349ae` | wearing a short red work jacket over the shirt, same pose, same trousers, same shoes, same hair | hue 330°–25°, saturation > 0.35 | 2026-10-25 | character state `9703572b-c0d4-4eb2-b978-921fb2cd8e03` |
