// The emote wheel's hotkey (docs/adr/ADR-075 D1). B opens and closes the wheel, Escape closes it. Both fire on a key press only, never per frame,
// and never while a field takes the keys (a form, the chat box: data-text-entry on the page). The key is reserved in KeyBindings, so no action of
// the engine's key table (renderer.js) takes it. The component passes itself in (a DotNetObjectReference).

let host = null;

function typing(e) {
    if (document.documentElement.hasAttribute("data-text-entry")) { return true; }
    const target = e.target;
    return Boolean(target) && (target.tagName === "INPUT" || target.tagName === "TEXTAREA" || target.isContentEditable === true);
}

function onKey(e) {
    if (!host || e.repeat || e.ctrlKey || e.altKey || e.metaKey || e.isComposing || typing(e)) { return; }
    if (e.code === "KeyB") {
        e.preventDefault();
        host.invokeMethodAsync("ToggleFromKey");
    } else if (e.code === "Escape") {
        host.invokeMethodAsync("CloseFromKey");
    }
}

export function attach(target) {
    host = target;
    window.addEventListener("keydown", onKey);
}

export function detach() {
    window.removeEventListener("keydown", onKey);
    host = null;
}
