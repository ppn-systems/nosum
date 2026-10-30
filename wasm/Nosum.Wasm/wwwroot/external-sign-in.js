// Google and Microsoft sign-in for the account form (docs/adr/ADR-069). Loaded by C# on the first use, through the JS import.
//
// renderGoogleButton(elementId, clientId, locale, callbacks) draws Google's own sign-in button into the element. A click opens Google's popup;
//   the ID token comes back as callbacks.invokeMethodAsync("OnGoogleCredential", token), and a failure as ("OnGoogleFailure", code).
// signIn("microsoft", clientId, tenant) resolves with the Microsoft ID token. It rejects with an Error whose message is one of:
//   "cancelled"       the player closed or dismissed the Microsoft sign-in
//   "popup_blocked"   the browser blocked the Microsoft sign-in window
//   "not_configured"  no client id was given
//   "failed"          anything else (a script did not load, the provider returned an error)
// The C# side maps these codes to localized text. Nothing secret lives here: the client ids are public.
//
// The MSAL version is pinned and must match the one in msal-redirect.html.

const GOOGLE_SCRIPT_URL = "https://accounts.google.com/gsi/client";
const MSAL_VERSION = "3.30.0";
const MSAL_SCRIPT_URL = "https://cdn.jsdelivr.net/npm/@azure/msal-browser@" + MSAL_VERSION + "/lib/msal-browser.min.js";
const MSAL_SCOPES = ["openid", "profile", "email"];

const scriptLoads = new Map();   // script URL -> Promise, so each script is requested once
const msalApps = new Map();      // "clientId|tenant" -> Promise of an initialized PublicClientApplication
let googleClientId = null;       // the client id Google Identity Services was initialized with
let googleCallbacks = null;      // the .NET object that receives the Google credential or failure

export function signIn(provider, clientId, tenant) {
    if (!clientId) {
        return Promise.reject(new Error("not_configured"));
    }
    if (provider === "microsoft") {
        return microsoftIdToken(clientId, tenant || "common");
    }
    return Promise.reject(new Error("failed"));
}

function loadScript(src) {
    if (!scriptLoads.has(src)) {
        const load = new Promise((resolve, reject) => {
            const script = document.createElement("script");
            script.src = src;
            script.async = true;
            script.onload = () => resolve();
            script.onerror = () => {
                scriptLoads.delete(src);
                script.remove();
                reject(new Error("failed"));
            };
            document.head.appendChild(script);
        });
        scriptLoads.set(src, load);
    }
    return scriptLoads.get(src);
}

// Google Identity Services: Google's rendered button opens its sign-in popup on click (ux_mode "popup"), so the player always gets a window.
// The credential callback is set once per client id; the .NET callbacks object is replaced on each render.
export function renderGoogleButton(elementId, clientId, locale, callbacks) {
    const element = document.getElementById(elementId);
    if (!element || !clientId) {
        return reportGoogleFailure(callbacks, "not_configured");
    }
    return loadScript(GOOGLE_SCRIPT_URL).then(() => {
        const id = window.google && window.google.accounts && window.google.accounts.id;
        if (!id) {
            throw new Error("failed");
        }
        googleCallbacks = callbacks;
        if (googleClientId !== clientId) {
            id.initialize({
                client_id: clientId,
                callback: onGoogleCredential,
                ux_mode: "popup",
                auto_select: false,
                cancel_on_tap_outside: true,
            });
            googleClientId = clientId;
        }
        element.replaceChildren();
        id.renderButton(element, {
            type: "standard",
            theme: "outline",
            size: "large",
            text: "signin_with",
            shape: "rectangular",
            logo_alignment: "left",
            width: googleButtonWidth(element),
            locale: locale,
        });
    }).catch(() => reportGoogleFailure(callbacks, "failed"));
}

// Google's button is a fixed pixel width (200 to 400) and cannot stretch, so it follows the width of the slot it is drawn in.
function googleButtonWidth(element) {
    return Math.min(400, Math.max(200, Math.round(element.clientWidth)));
}

function onGoogleCredential(response) {
    const callbacks = googleCallbacks;
    if (!callbacks) {
        return;
    }
    if (response && response.credential) {
        callbacks.invokeMethodAsync("OnGoogleCredential", response.credential);
    } else {
        reportGoogleFailure(callbacks, "failed");
    }
}

function reportGoogleFailure(callbacks, code) {
    if (callbacks) {
        callbacks.invokeMethodAsync("OnGoogleFailure", code);
    }
    return Promise.resolve();
}

// Microsoft: MSAL in a popup window, which lands on msal-redirect.html and returns the ID token to this page.
function authorityOf(tenant) {
    return "https://login.microsoftonline.com/" + encodeURIComponent(tenant);
}

function redirectUri() {
    return new URL("msal-redirect.html", document.baseURI).href;
}

function msalApp(clientId, tenant) {
    const key = clientId + "|" + tenant;
    if (!msalApps.has(key)) {
        const app = loadScript(MSAL_SCRIPT_URL).then(async () => {
            const msal = window.msal;
            if (!msal || !msal.PublicClientApplication) {
                throw new Error("failed");
            }
            const instance = new msal.PublicClientApplication({
                auth: { clientId: clientId, authority: authorityOf(tenant), redirectUri: redirectUri() },
                cache: { cacheLocation: "sessionStorage" },
            });
            await instance.initialize();
            return instance;
        });
        msalApps.set(key, app);
        app.catch(() => msalApps.delete(key));
    }
    return msalApps.get(key);
}

async function microsoftIdToken(clientId, tenant) {
    const app = await msalApp(clientId, tenant);
    let result;
    try {
        result = await app.loginPopup({ scopes: MSAL_SCOPES, redirectUri: redirectUri() });
    } catch (error) {
        const code = error && error.errorCode;
        if (code === "user_cancelled" || code === "monitor_window_timeout") {
            throw new Error("cancelled");
        }
        if (code === "popup_window_error" || code === "empty_window_error") {
            throw new Error("popup_blocked");
        }
        throw new Error("failed");
    }
    if (!result || !result.idToken) {
        throw new Error("failed");
    }
    return result.idToken;
}
