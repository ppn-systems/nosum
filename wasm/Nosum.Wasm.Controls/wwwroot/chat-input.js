// The keyboard side of the chat line (docs/adr/ADR-029 D7). Three jobs, all on key presses (never per frame):
//
// 1. Enter opens the input box when nothing else wants the key (the game has the keyboard; a form field, a button or a link does not).
// 2. Once the box is open it takes the keys: Enter sends, Escape closes, Tab changes channel. The box is a form field, so the engine's key
//    handler (renderer.js) already ignores WASD, the number keys and the panel keys while it has focus; Escape is the exception it acts on
//    from a field, so the box stops that one here, or closing the box would also open the game menu.
// 3. Focus: the box takes focus when it opens.
//
// The component passes itself in (a DotNetObjectReference) and gets Open / Submit / Close / NextChannel calls back.

let host = null;
let enabled = false;
let attached = null;
let pointerInChat = false;

function wantsEnter(target) {
    if (!target) { return true; }
    const tag = target.tagName;
    if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || tag === "BUTTON" || tag === "A" || target.isContentEditable) {
        return false;
    }
    if (typeof document !== "undefined" && document.querySelector("[role='dialog'], .hud-center > *, .hud-overlay > *")) {
        return false;
    }
    return true;
}

function onWindowPointerDown(e) {
    const t = e.target;
    pointerInChat = Boolean(t && typeof t.closest === "function" && t.closest(".game-panel.chat, .chat-log"));
    if (!pointerInChat && attached && typeof document !== "undefined" && document.activeElement !== attached) {
        setTimeout(() => { if (host) { host.invokeMethodAsync("CloseFromKey"); } }, 0);
    }
}

function onWindowKey(e) {
    if (!enabled || !host || e.key !== "Enter" || e.repeat || e.ctrlKey || e.altKey || e.metaKey || e.isComposing) { return; }
    if (!wantsEnter(e.target)) { return; }
    e.preventDefault();
    host.invokeMethodAsync("OpenFromKey");
}

function onInputKey(e) {
    if (!host || e.isComposing) { return; }
    if (e.key === "Enter") {
        e.preventDefault();
        e.stopPropagation();
        host.invokeMethodAsync("SubmitFromKey", e.target.value);
    } else if (e.key === "Escape") {
        e.preventDefault();
        e.stopPropagation();
        host.invokeMethodAsync("CloseFromKey");
    } else if (e.key === "Tab") {
        e.preventDefault();
        e.stopPropagation();
        host.invokeMethodAsync("NextChannelFromKey");
    }
}

// The blur also fires when Blazor removes the focused box while it applies a render batch (closing the box, sending a line); the .NET heap is locked
// then and a call into .NET throws "heap is currently locked". Calling back after the batch (a macrotask) is safe, and closing an already closed box is a no-op.
function onInputBlur(e) {
    const related = e && e.relatedTarget;
    if (pointerInChat || (related && typeof related.closest === "function" && related.closest(".game-panel.chat, .chat-log"))) {
        pointerInChat = false;
        return;
    }
    setTimeout(() => { if (host) { host.invokeMethodAsync("CloseFromKey"); } }, 0);
}

export function init(reference) {
    host = reference;
    window.addEventListener("keydown", onWindowKey);
    window.addEventListener("pointerdown", onWindowPointerDown, true);
}

export function setEnabled(value) {
    enabled = value;
}

// Focuses the box (found by id) and listens to its keys; called after the render that opened it.
export function focusInput(id) {
    const input = document.getElementById(id);
    if (!input) { return; }
    if (attached !== input) {
        detach();
        input.addEventListener("keydown", onInputKey);
        input.addEventListener("blur", onInputBlur);
        attached = input;
    }
    input.focus();
}

function detach() {
    if (attached) {
        attached.removeEventListener("keydown", onInputKey);
        attached.removeEventListener("blur", onInputBlur);
        attached = null;
    }
    pointerInChat = false;
}

export function dispose() {
    window.removeEventListener("keydown", onWindowKey);
    window.removeEventListener("pointerdown", onWindowPointerDown, true);
    detach();
    host = null;
}
