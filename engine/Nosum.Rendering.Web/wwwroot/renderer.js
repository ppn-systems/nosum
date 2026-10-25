// Nosum browser render backend (JS side of Nosum.Rendering.Web.WebRenderBackend).
//
// submitFrame receives a MemoryView over .NET memory that is valid only during this call:
// decode and draw synchronously, never keep the view or a subarray of it.
// The command encoding and the Canvas2D drawing are not implemented yet; see docs/adr/ADR-002.

export function submitFrame(view) {
    if (view.length === 0) {
        return;
    }
}
