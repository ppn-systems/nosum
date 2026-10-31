// The room the shop list has in its window (ShopPanel, docs/design/ui-standard.md section 3): the component reads it to show as many whole rows
// as fit. It runs on a render and on a window resize, never per frame. The resize is debounced here, so a drag of the window asks once.

const RESIZE_DEBOUNCE_MS = 150;
const watchers = new Map();
let nextId = 1;

// [available, row, gap, columns] in CSS pixels (columns is a count), or null when the list is not laid out. Available is the height the list may take: the window's
// max-height (or the viewport, when the window has none) less everything else in the window (header, status line, and the page's other
// content: the quantity row, the pager, the detail strip).
export function measure(list) {
    if (!list || !list.isConnected) {
        return null;
    }

    const row = list.firstElementChild;
    const scroll = list.closest(".menu-page-body");
    const panel = list.closest(".game-panel");
    if (!row || !scroll || !panel) {
        return null;
    }

    const maxHeight = parseFloat(getComputedStyle(panel).maxHeight);
    const cap = Number.isFinite(maxHeight) ? maxHeight : window.innerHeight;
    const chrome = panel.getBoundingClientRect().height - scroll.getBoundingClientRect().height;
    const rest = scroll.scrollHeight - list.getBoundingClientRect().height;
    const style = getComputedStyle(list);
    const gap = parseFloat(style.rowGap) || 0;
    const columns = style.gridTemplateColumns.split(" ").filter((t) => t.length > 0).length || 1;
    return [cap - chrome - rest, row.getBoundingClientRect().height, gap, columns];
}

// Calls the component's OnViewportChanged once the window has stopped resizing for a moment.
export function watch(ref) {
    let timer = 0;
    const onResize = () => {
        clearTimeout(timer);
        timer = setTimeout(() => ref.invokeMethodAsync("OnViewportChanged"), RESIZE_DEBOUNCE_MS);
    };

    window.addEventListener("resize", onResize);
    const id = nextId++;
    watchers.set(id, () => {
        clearTimeout(timer);
        window.removeEventListener("resize", onResize);
    });
    return id;
}

export function unwatch(id) {
    const stop = watchers.get(id);
    if (stop) {
        stop();
        watchers.delete(id);
    }
}
