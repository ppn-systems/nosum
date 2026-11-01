# Toast dedupe (Vietnamese)

Same text already on the toast line: it is not added again. The toast shows "text ×N", keeps its place and its id, and its
three-second timer starts over. A different text still stacks as a second line (max two, docs/design/ui-standard.md section 5).

Captured by [`capture-toast-dedupe.cjs`](capture-toast-dedupe.cjs) against a local host (`server/Nosum.Host`, PostgreSQL 16) and a
client published with `dotnet publish wasm/Nosum.Wasm -c Release -p:NosumDevTools=true`, served as static files on port 5830.
The script refuses the seed on the farmhouse twice within a second, then saves the line and checks that exactly one toast reads "×2".

| Viewport | Screenshot | Result |
| :-- | :-- | :-- |
| 1280×720 (vi) | [toast-repeat-vi-1280x720.png](toast-repeat-vi-1280x720.png) | one toast, "Hãy xới đất trước. ×2" on one line |
| 1920×1080 (vi) | [toast-repeat-vi-1920x1080.png](toast-repeat-vi-1920x1080.png) | one toast, "Hãy xới đất trước. ×2" on one line |
| 390×844 (vi) | [toast-repeat-vi-390x844.png](toast-repeat-vi-390x844.png) | one toast, "Hãy xới đất trước. ×2" on one line |

The count reads "×" in the VT323 font, which draws it like an X. The text comes from `Toast_Repeat` in `Language.resx` and `Language.en.resx`.
The machine-readable result is in [capture-report.json](capture-report.json).
