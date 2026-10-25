// The browser side of the client settings (docs/adr/ADR-033). Called when a setting changes and once at start, never from the frame.
//
// - readSettings / writeSettings: one text entry in local storage. Storage can be blocked (private window, site data off, a sandboxed frame): every access is
//   wrapped, a blocked read is "nothing stored" and a blocked write is dropped, so the settings then last for this visit only.
// - applyDisplay: the theme switches (high contrast, reduced motion) are attributes on <html>; nosum-theme.css reads them.
// - applyScale: the zoom the player picked; the theme module turns it into the UI pixel (--u) and the engine reads the same number for the world.
import { setUnitChoice } from "./nosum-theme.js";

const KEY = "nosum.settings.v1";

export function readSettings() {
    try {
        return window.localStorage.getItem(KEY);
    } catch {
        return null;
    }
}

export function writeSettings(text) {
    try {
        window.localStorage.setItem(KEY, text);
    } catch {
        // Blocked or full: keep playing with the settings in memory.
    }
}

export function applyDisplay(highContrast, reduceMotion) {
    const root = document.documentElement;
    if (highContrast) { root.setAttribute("data-contrast", "high"); } else { root.removeAttribute("data-contrast"); }
    if (reduceMotion) { root.setAttribute("data-motion", "reduced"); } else { root.removeAttribute("data-motion"); }
}

export function applyScale(scale) {
    setUnitChoice(scale);
}
