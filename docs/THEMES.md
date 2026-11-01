# Sable themes

All four themes use the same token names, so every control is theme-agnostic.

| Token | Noir | Graphite | Ivory | Porcelain |
|---|---|---|---|---|
| Background | `#0A0A0C` | `#17181B` | `#F8F4EF` | `#FAF9F7` |
| Background top | `#121216` | `#202226` | `#FFFFFF` | `#FFFFFF` |
| Card | `#141418` | `#222429` | `#FFFEFB` | `#FFFFFF` |
| Stroke | 10% white | 10% white | 10% ink | 9% ink |
| Text | `#F7F7F8` | `#F3F3F5` | `#171614` | `#111112` |
| Muted | `#A2A2AA` | `#A7A9B1` | `#585550` | `#5C5C63` |
| Dim | `#70707A` | `#767983` | `#6F6B63` | `#707077` |
| Accent | `#F7F7F8` | `#F5F5F7` | `#171614` | `#101012` |
| On accent | `#0A0A0C` | `#121316` | `#FAF8F3` | `#FFFFFF` |

## Geometry and motion

- Outer window radius: 30 (HUD), 26 (settings), 22 (mini/toast)
- Cards: 18–19; rows and chips: 9–14; icon buttons: circle or 10–11
- Drop shadow: black, blur 24–40, depth 6–10, opacity 0.45–0.55
- HUD entrance: 320 ms quintic ease-out from above; fade 170 ms
- HUD exit: 200 ms cubic ease-in, fade 160 ms
- Page change: 230 ms 7 px slide-up plus 170 ms fade
- Toast: 260 ms drop, 3.6 s hold, 220 ms fade
- Controller focus: 1.5 px halo 4 px outside the item, plus a 1.2% scale

## Why no colour accent

The brief asked for an elegant black-and-white HUD with nothing sharp, neon or purple.
Status is carried by brightness, weight and position instead of hue:

- playing: full-white dot
- paused: 30% dot
- muted: dimmed name plus mute glyph
- active toggle: inverted pill
- low-priority: dim text at 9.5–11 px

LiMusic's colour themes (Catppuccin, Tokyo Night, Neon…) inspired the idea of a theme
picker; Sable keeps the idea and removes the colour.
