// Google and Microsoft sign-in for the account form (docs/adr/ADR-071). Loaded by C# on the first click, through the JS import.
//
// signIn(provider, clientId, tenant) resolves with the provider's ID token. It rejects with an Error whose message is one of:
//   "cancelled"       the player closed or dismissed the sign-in
//   "popup_blocked"   the browser blocked the Microsoft sign-in window
//   "not_displayed"   Google did not show its sign-in prompt
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
let googlePending = null;        // the sign-in waiting for Google's credential callback

export function signIn(provider, clientId, tenant) {
    if (!clientId) {
        return Promise.reject(new Error("not_configured"));
    }
    if (provider === "google") {
        return googleIdToken(clientId);
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

// Google Identity Services: the One Tap prompt returns an ID token ("credential") to the callback given to initialize().
async function googleIdToken(clientId) {
    await loadScript(GOOGLE_SCRIPT_URL);
    const id = window.google && window.google.accounts && window.google.accounts.id;
    if (!id) {
        throw new Error("failed");
    }
    if (googleClientId !== clientId) {
        id.initialize({
            client_id: clientId,
            callback: onGoogleCredential,
            auto_select: false,
            cancel_on_tap_outside: true,
        });
        googleClientId = clientId;
    }

    return new Promise((resolve, reject) => {
        const pending = { resolve: resolve, reject: reject };
        if (googlePending) {
            googlePending.reject(new Error("cancelled"));
        }
        googlePending = pending;
        id.prompt((notification) => {
            if (googlePending !== pending) {
                return; // already settled by the credential callback
            }
            const dismissedByPlayer = notification.isDismissedMoment() && notification.getDismissedReason() !== "credential_returned";
            if (notification.isNotDisplayed()) {
                googlePending = null;
                reject(new Error("not_displayed"));
            } else if (notification.isSkippedMoment() || dismissedByPlayer) {
                googlePending = null;
                reject(new Error("cancelled"));
            }
        });
    });
}

function onGoogleCredential(response) {
    const pending = googlePending;
    googlePending = null;
    if (!pending) {
        return;
    }
    if (response && response.credential) {
        pending.resolve(response.credential);
    } else {
        pending.reject(new Error("failed"));
    }
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
