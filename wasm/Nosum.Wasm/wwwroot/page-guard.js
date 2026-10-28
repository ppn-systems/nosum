// Page behaviour of a game, not of a web page (docs: wasm/README.md "Device gate and page behaviour"). A classic script loaded first by index.html,
// before Blazor starts, so it also covers the boot screen and the "use a computer" notice. It does four things:
//
// 1. Reads the device signals for the C# device gate (Device/BrowserDevice.cs): UA Client Hints, user agent, pointer/hover, touch points, width.
// 2. Takes the browser's page-like defaults away outside text fields: context menu, text selection, image drag, copy/cut, middle-click autoscroll,
//    F5/Ctrl+R/Backspace navigation, Ctrl+wheel and Safari pinch zoom. Text inputs and elements marked data-selectable (the chat log) keep everything,
//    paste included. The CSS half (user-select, touch-action, overscroll, image-rendering) is in css/app.css.
// 3. Asks before the tab is closed or reloaded while the player is in the game (beforeunload); C# turns this on after sign-in and off when a session ends.
// 4. Reports the width crossing the minimum window width (a media query change event: nothing per frame, nothing per resize).
//
// Deliberately NOT done: hiding or "protecting" the WASM bundle, blocking devtools or Ctrl+U/Ctrl+S. The bundle is public by design and the server decides
// every outcome; such tricks only annoy players.
(function () {
    "use strict";

    // A field the player types in: it keeps the context menu, selection, clipboard and Backspace. data-selectable text (chat lines) keeps selection and copy.
    function inField(el) {
        return !!el && typeof el.closest === "function" && !!el.closest("input, textarea, select, [contenteditable=''], [contenteditable='true']");
    }

    function inSelectable(el) {
        return !!el && typeof el.closest === "function" && !!el.closest("[data-selectable]");
    }

    function typing() {
        return inField(document.activeElement);
    }

    const hints = navigator.userAgentData;

    window.nosumPage = {
        // 1 = mobile, 0 = not mobile, -1 = this browser has no UA Client Hints.
        mobileHint: () => (hints && typeof hints.mobile === "boolean") ? (hints.mobile ? 1 : 0) : -1,
        userAgent: () => navigator.userAgent || "",
        platform: () => navigator.platform || "",
        pointerCoarse: () => window.matchMedia("(pointer: coarse)").matches,
        canHover: () => window.matchMedia("(hover: hover)").matches,
        maxTouchPoints: () => navigator.maxTouchPoints || 0,
        viewportWidth: () => window.innerWidth,

        // Calls back with true/false when the window becomes at least minWidth wide / narrower. Fires on the crossing only.
        watchWidth: function (minWidth, callback) {
            const query = window.matchMedia("(min-width: " + minWidth + "px)");
            query.addEventListener("change", e => callback(e.matches));
        },

        // The player is in the game: closing or reloading the tab asks first.
        unloadGuard: false,
        setUnloadGuard: function (on) { window.nosumPage.unloadGuard = !!on; },
    };

    // Capture phase, so nothing in the page can reopen what is blocked. Fields are left alone in every handler.
    document.addEventListener("contextmenu", e => { if (!inField(e.target)) { e.preventDefault(); } }, true);
    document.addEventListener("selectstart", e => { if (!inField(e.target) && !inSelectable(e.target)) { e.preventDefault(); } }, true);
    document.addEventListener("dragstart", e => { if (!inField(e.target)) { e.preventDefault(); } }, true);
    document.addEventListener("copy", e => { if (!typing() && !inSelectable(document.activeElement) && !inSelectable(window.getSelection()?.anchorNode?.parentElement)) { e.preventDefault(); } }, true);
    document.addEventListener("cut", e => { if (!typing()) { e.preventDefault(); } }, true);

    // The middle button starts the browser's autoscroll (and opens links in a new tab).
    document.addEventListener("mousedown", e => { if (e.button === 1 && !inField(e.target)) { e.preventDefault(); } }, true);
    document.addEventListener("auxclick", e => { if (e.button === 1 && !inField(e.target)) { e.preventDefault(); } }, true);

    // F5 and Ctrl/Cmd+R reload and Backspace goes back in some browsers: not while the player is not typing. Ctrl+Shift+R and the browser's own
    // reload button still work, so a stuck game can always be reloaded.
    window.addEventListener("keydown", e => {
        if (typing()) { return; }
        const reload = e.key === "F5" || ((e.ctrlKey || e.metaKey) && !e.shiftKey && (e.key === "r" || e.key === "R"));
        if (reload || e.key === "Backspace") { e.preventDefault(); }
    }, true);

    // Trackpad pinch and Ctrl+wheel zoom the page (touch pinch is off through touch-action and the viewport meta); Safari's gesture events likewise.
    window.addEventListener("wheel", e => { if (e.ctrlKey) { e.preventDefault(); } }, { passive: false, capture: true });
    for (const name of ["gesturestart", "gesturechange", "gestureend"]) {
        document.addEventListener(name, e => e.preventDefault(), true);
    }

    window.addEventListener("beforeunload", e => {
        if (window.nosumPage.unloadGuard && !window.__nosumSwReload) {
            e.preventDefault();
            e.returnValue = "";
        }
    });
})();
