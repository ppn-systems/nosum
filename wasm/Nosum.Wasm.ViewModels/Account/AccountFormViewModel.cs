using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Account;
using Nosum.Contracts.Common;
using Nosum.Gameplay.Players;
using Nosum.Wasm.ViewModels.Localization;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>Which form the account component shows.</summary>
public enum AccountFormMode
{
    /// <summary>Sign in with Google or Microsoft, or play as a guest.</summary>
    Login,

    /// <summary>The signed-in guest links a Google or Microsoft identity and chooses a nickname.</summary>
    Upgrade,
}

/// <summary>
/// View model of the account UI: sign in with Google or Microsoft, play as guest, link a guest to Google or Microsoft, sign out (docs/adr/ADR-069).
/// Reusable: any page binds <c>AccountForm</c> and <c>SessionBar</c> to one instance and listens to
/// <see cref="SignedIn"/>, <see cref="SessionUpdated"/> and <see cref="SignedOut"/>. Every visible
/// string comes from <c>Language.resx</c>; the server only ever sends status codes.
/// </summary>
public sealed class AccountFormViewModel : IObservableViewModel
{
    private readonly IAccountClient _client;
    private readonly IExternalSignIn _externalSignIn;
    private readonly ExternalSignInOptions _options;
    private readonly IStringLocalizer<Language> _text;
    private readonly RememberedSessionViewModel _remembered;

    /// <summary>Initializes the view model.</summary>
    /// <param name="client">The network access.</param>
    /// <param name="externalSignIn">Runs the Google or Microsoft sign-in in the browser.</param>
    /// <param name="options">Which providers have a client id on this client.</param>
    /// <param name="text">The localizer for <c>Language.resx</c>.</param>
    /// <param name="remembered">The remembered session this browser keeps for the continue card (docs/adr/ADR-081).</param>
    public AccountFormViewModel(
        IAccountClient client,
        IExternalSignIn externalSignIn,
        ExternalSignInOptions options,
        IStringLocalizer<Language> text,
        RememberedSessionViewModel remembered)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(externalSignIn);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(remembered);
        _client = client;
        _externalSignIn = externalSignIn;
        _options = options;
        _text = text;
        _remembered = remembered;
    }

    /// <summary>Gets the remembered session the continue card reads (docs/adr/ADR-081).</summary>
    public RememberedSessionViewModel Remembered => _remembered;

    /// <summary>Gets whether the continue card replaces the sign-in buttons: a session is remembered and nobody is signed in on this page.</summary>
    public bool ShowsContinue => _remembered.HasRemembered && this.CurrentSession is null && this.Mode == AccountFormMode.Login;

    /// <summary>Raised when something the components show has changed; they re-render.</summary>
    public event Action? Changed;

    /// <summary>Raised after a fresh sign-in: login, register or a new guest. Apply the language and start the game here.</summary>
    public event Action<AccountSession>? SignedIn;

    /// <summary>Raised after a guest upgraded: same player, new session token. Store the new session; do not restart the game.</summary>
    public event Action<AccountSession>? SessionUpdated;

    /// <summary>Raised after the player signed out. Return to the account form.</summary>
    public event Action? SignedOut;

    /// <summary>Gets the form being shown.</summary>
    public AccountFormMode Mode { get; private set; }

    /// <summary>Gets the current session, or null while signed out.</summary>
    public AccountSession? CurrentSession { get; private set; }

    /// <summary>Gets or sets the typed nickname (link only).</summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>Gets whether a request is in flight.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Gets the localized error to show, or null.</summary>
    public string? ErrorText { get; private set; }

    /// <summary>Gets whether the Google or Microsoft sign-in window is open and the form waits for the player to finish in it.</summary>
    public bool IsAwaitingProvider { get; private set; }

    /// <summary>Gets whether the guest-upgrade form is currently shown.</summary>
    public bool IsUpgradeMode => this.Mode == AccountFormMode.Upgrade;

    /// <summary>Gets the maximum allowed length for a nickname input.</summary>
    public static int NicknameMaxLength => NicknameRules.MaxLength;

    /// <summary>Gets whether the account form must be shown: signed out, or upgrading.</summary>
    public bool ShowsForm => this.CurrentSession is null || this.Mode == AccountFormMode.Upgrade;

    /// <summary>Gets whether the "play as guest" option is offered.</summary>
    public bool CanPlayAsGuest => this.CurrentSession is null;

    /// <summary>Gets whether the session bar offers the upgrade: a guest whose invitation to register is unlocked.</summary>
    public bool CanUpgrade => this.CurrentSession is { IsGuest: true, RegistrationPromptUnlocked: true } && this.Mode != AccountFormMode.Upgrade;

    /// <summary>Gets whether the Google button can be used on this client.</summary>
    public bool GoogleEnabled => _options.IsConfigured(ExternalProvider.Google);

    /// <summary>Gets whether the Microsoft button can be used on this client.</summary>
    public bool MicrosoftEnabled => _options.IsConfigured(ExternalProvider.Microsoft);

    /// <summary>Gets the one line that says what to do when a provider is off on this server (issue #207): play as a guest. Null when both providers are on.</summary>
    public string? ProviderHint => this.GoogleEnabled && this.MicrosoftEnabled ? null : _text["Account_Provider_Guidance"].Value;

    /// <summary>Gets the name of the game, shown as the logo of the start screen.</summary>
    public string GameTitle => _text["Game_Title"];

    /// <summary>Gets the one-line tagline under the logo.</summary>
    public string Tagline => _text["Game_Tagline"];

    /// <summary>Gets the form title.</summary>
    public string Title => _text[this.Mode == AccountFormMode.Upgrade ? "Account_Title_Upgrade" : "Account_Title_Login"];

    /// <summary>Gets the nickname field label.</summary>
    public string NicknameLabel => _text["Account_Nickname"];

    /// <summary>Gets the nickname rules, shown under the field when linking.</summary>
    public string NicknameHint => string.Format(CultureInfo.CurrentCulture, _text["Account_NicknameHint"].Value, NicknameRules.MinLength, NicknameRules.MaxLength);

    /// <summary>Gets the locale Google's button is drawn in now ("en" or "vi"); the button is drawn again when it changes.</summary>
    public static string GoogleButtonLocale => UiCulture.Current == GameLanguage.English ? "en" : "vi";

    /// <summary>Gets the text of the Google button on the sign-in form.</summary>
    public string SignInGoogleText => _text["Account_SignIn_Google"];

    /// <summary>Gets the text of the Microsoft button on the sign-in form.</summary>
    public string SignInMicrosoftText => _text["Account_SignIn_Microsoft"];

    /// <summary>Gets the text of the Google button on the link form.</summary>
    public string LinkGoogleText => _text["Account_Link_Google"];

    /// <summary>Gets the text of the Microsoft button on the link form.</summary>
    public string LinkMicrosoftText => _text["Account_Link_Microsoft"];

    /// <summary>Gets the text shown while the provider's sign-in window is open.</summary>
    public string AwaitingProviderText => _text["Account_Busy"];

    /// <summary>Gets the text of the "play as guest" button.</summary>
    public string PlayAsGuestText => _text["Account_PlayAsGuest"];

    /// <summary>Gets the text of the button that leaves the upgrade form.</summary>
    public string CancelText => _text["Account_Cancel"];

    /// <summary>Gets the signed-in line of the session bar: the nickname, the provider's e-mail, or "Guest".</summary>
    public string SignedInText => this.CurrentSession is null
        ? string.Empty
        : this.CurrentSession.IsGuest
            ? _text["Account_Guest"]
            : string.Format(CultureInfo.CurrentCulture, _text["Account_SignedInAs"].Value, string.IsNullOrEmpty(this.CurrentSession.Email) ? this.CurrentSession.Nickname : this.CurrentSession.Email);

    /// <summary>Gets the invitation shown to a guest whose registration prompt is unlocked.</summary>
    public string UpgradePromptText => _text["Account_Upgrade_Prompt"];

    /// <summary>Gets the text of the upgrade button.</summary>
    public string UpgradeText => _text["Account_Upgrade_Action"];

    /// <summary>Gets the text of the sign-out button.</summary>
    public string SignOutText => _text["Account_SignOut"];

    /// <summary>Shows why the player is back at the form after the server ended their session.</summary>
    /// <param name="reason">The server's reason code.</param>
    public void ShowSessionEnded(AccountStatus reason)
    {
        this.ErrorText = StatusText(reason);
        this.Changed?.Invoke();
    }

    /// <summary>
    /// Records that the server unlocked the invitation to register for the signed-in guest, so the session bar
    /// offers it without signing in again. Does nothing for a registered player or when nobody is signed in.
    /// </summary>
    public void UnlockRegistrationPrompt()
    {
        if (this.CurrentSession is { IsGuest: true } session && !session.RegistrationPromptUnlocked)
        {
            this.CurrentSession = session with { RegistrationPromptUnlocked = true };
            this.Changed?.Invoke();
        }
    }

    /// <summary>Shows the upgrade form to the signed-in guest. Does nothing for a registered player.</summary>
    public void BeginUpgrade()
    {
        if (this.CurrentSession is { IsGuest: true })
        {
            this.ChangeMode(AccountFormMode.Upgrade);
        }
    }

    /// <summary>Leaves the upgrade form and goes back to the game.</summary>
    public void CancelUpgrade()
    {
        if (this.Mode == AccountFormMode.Upgrade)
        {
            this.ChangeMode(AccountFormMode.Login);
        }
    }

    /// <summary>Starts playing as a guest in the current UI language.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the reply has been handled.</returns>
    public Task PlayAsGuestAsync(CancellationToken cancellationToken = default)
        => this.RunAsync(() => _client.StartGuestAsync(UiCulture.Current, cancellationToken), isUpgrade: false, RememberedAccountKind.Guest);

    /// <summary>Loads the remembered session so the continue card knows whether to show. Call once, when the start screen appears.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the card's state is known.</returns>
    public Task LoadRememberedAsync(CancellationToken cancellationToken = default) => _remembered.LoadAsync(cancellationToken);

    /// <summary>
    /// Resumes the remembered session without another sign-in (the continue card). A refusal falls back to the sign-in buttons with the reason shown.
    /// Does nothing while a request is in flight.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the reply has been handled.</returns>
    public Task ContinueAsync(CancellationToken cancellationToken = default)
        => this.RunAsync(() => _remembered.ContinueAsync(cancellationToken), isUpgrade: false, kind: null);

    /// <summary>Signs out the remembered session and shows the sign-in buttons (the "sign out / use another account" action).</summary>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>A task that completes when the sign-in buttons are shown.</returns>
    public async Task ForgetRememberedAsync(CancellationToken cancellationToken = default)
    {
        if (this.IsBusy)
        {
            return;
        }

        this.IsBusy = true;
        this.ErrorText = null;
        this.Changed?.Invoke();
        try
        {
            await _remembered.ForgetAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this.IsBusy = false;
            this.Changed?.Invoke();
        }
    }

    /// <summary>
    /// Signs in with Google or Microsoft: the provider's sign-in runs, then the server is asked to sign the account in (creating it on first use).
    /// Does nothing while a request is in flight. A provider without a client id is refused here, before any window opens.
    /// </summary>
    /// <param name="provider">The provider the player picked.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the reply has been handled.</returns>
    public Task SignInWithAsync(ExternalProvider provider, CancellationToken cancellationToken = default)
        => this.RunExternalAsync(
            provider,
            token => _client.SignInWithExternalAsync(provider, token, UiCulture.Current, cancellationToken),
            isUpgrade: false,
            cancellationToken);

    /// <summary>Signs in with Microsoft (the Microsoft button).</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the attempt is over.</returns>
    public Task SignInWithMicrosoftAsync(CancellationToken cancellationToken = default) => this.SignInWithAsync(ExternalProvider.Microsoft, cancellationToken);

    /// <summary>
    /// Draws Google's own sign-in button into the element with the given id (docs/adr/ADR-069). A click opens Google's popup; the ID token it
    /// returns comes to <see cref="CompleteGoogleAsync"/>, and a failure to <see cref="ReportGoogleFailure"/>.
    /// </summary>
    /// <param name="elementId">The id of the element the button is drawn into.</param>
    /// <returns>A task that completes when the button is drawn.</returns>
    public Task AttachGoogleButtonAsync(string elementId)
        => _externalSignIn.RenderGoogleButtonAsync(elementId, UiCulture.Current, token => this.CompleteGoogleAsync(token), failure => this.ReportGoogleFailure(failure));

    /// <summary>
    /// Finishes the Google button's sign-in, or the link to the guest in upgrade mode, with the token Google issued. The nickname is checked
    /// here, after the popup, for the link. Does nothing while a request is in flight.
    /// </summary>
    /// <param name="idToken">The Google ID token. The token is a bearer credential: never log it.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the reply has been handled.</returns>
    public Task CompleteGoogleAsync(string idToken, CancellationToken cancellationToken = default)
    {
        if (this.IsBusy)
        {
            return Task.CompletedTask;
        }

        if (!this.IsUpgradeMode)
        {
            return this.RunAsync(() => _client.SignInWithExternalAsync(ExternalProvider.Google, idToken, UiCulture.Current, cancellationToken), isUpgrade: false, RememberedAccountKind.Google);
        }

        string nickname = NicknameRules.Normalize(this.Nickname);
        if (NicknameVerdictStatus.From(NicknameRules.Validate(nickname)) is { } invalid)
        {
            this.ErrorText = StatusText(invalid);
            this.Changed?.Invoke();
            return Task.CompletedTask;
        }

        return this.RunAsync(() => _client.LinkExternalIdentityAsync(ExternalProvider.Google, idToken, nickname, cancellationToken), isUpgrade: true, RememberedAccountKind.Google);
    }

    /// <summary>Shows why Google's button could not sign the player in (the script did not load, or the button was not configured).</summary>
    /// <param name="failure">The failure the browser reported.</param>
    /// <returns>A task that completes at once; the text is shown under the buttons.</returns>
    public Task ReportGoogleFailure(ExternalSignInFailure failure)
    {
        this.ErrorText = ProviderFailureText(failure);
        this.Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Links a Microsoft identity to the guest (the Microsoft button in upgrade mode).</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the attempt is over.</returns>
    public Task LinkMicrosoftAsync(CancellationToken cancellationToken = default) => this.LinkIdentityAsync(ExternalProvider.Microsoft, cancellationToken);

    /// <summary>
    /// Links a Google or Microsoft identity to the signed-in guest, with the nickname typed in the form. The nickname is checked first, so
    /// nothing opens when it is not valid. Does nothing while a request is in flight.
    /// </summary>
    /// <param name="provider">The provider the player picked.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the reply has been handled.</returns>
    public Task LinkIdentityAsync(ExternalProvider provider, CancellationToken cancellationToken = default)
    {
        if (this.IsBusy)
        {
            return Task.CompletedTask;
        }

        string nickname = NicknameRules.Normalize(this.Nickname);
        if (NicknameVerdictStatus.From(NicknameRules.Validate(nickname)) is { } invalid)
        {
            this.ErrorText = StatusText(invalid);
            this.Changed?.Invoke();
            return Task.CompletedTask;
        }

        return this.RunExternalAsync(
            provider,
            token => _client.LinkExternalIdentityAsync(provider, token, nickname, cancellationToken),
            isUpgrade: true,
            cancellationToken);
    }

    /// <summary>Gets the language the UI is in now (the signed-in session's, which every switch updates; before sign-in the UI culture's).</summary>
    public GameLanguage Language => GameLanguages.Resolve(this.CurrentSession?.Language ?? UiCulture.Current);

    /// <summary>Gets the preferred UI language (alias for <see cref="Language"/>).</summary>
    public GameLanguage PreferredLanguage => this.Language;

    /// <summary>Gets whether the active UI language is Vietnamese.</summary>
    public bool IsVietnamese => this.Language == GameLanguage.Vietnamese;

    /// <summary>Gets whether the active UI language is English.</summary>
    public bool IsEnglish => this.Language == GameLanguage.English;

    /// <summary>Gets the localized note under the language buttons when the server could not store the choice, or <see langword="null"/>.</summary>
    public string? LanguageNote { get; private set; }

    /// <summary>Switches the UI language to Vietnamese.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the server has answered.</returns>
    public Task SetVietnameseAsync(CancellationToken cancellationToken = default)
        => this.SetLanguageAsync(GameLanguage.Vietnamese, cancellationToken);

    /// <summary>Switches the UI language to English.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the server has answered.</returns>
    public Task SetEnglishAsync(CancellationToken cancellationToken = default)
        => this.SetLanguageAsync(GameLanguage.English, cancellationToken);

    /// <summary>
    /// Switches the UI language now, then stores it on the account so it applies at every later sign-in. The UI does not wait for the
    /// server: if the request fails the language stays changed for this visit and <see cref="LanguageNote"/> says it was not saved.
    /// </summary>
    /// <param name="language">The wanted language.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the server has answered.</returns>
    public async Task SetLanguageAsync(GameLanguage language, CancellationToken cancellationToken = default)
    {
        language = GameLanguages.Resolve(language);
        if (language == this.Language)
        {
            return;
        }

        UiCulture.Apply(language);
        this.LanguageNote = null;
        if (this.CurrentSession is { } session)
        {
            this.CurrentSession = session with { Language = language };
        }

        this.Changed?.Invoke();
        if (this.CurrentSession is null)
        {
            return;
        }

        bool saved;
        try
        {
            saved = await _client.SetLanguageAsync(language, cancellationToken).ConfigureAwait(false) == AccountStatus.Ok;
        }
        catch (AccountConnectionException)
        {
            saved = false;
        }

        if (!saved)
        {
            this.LanguageNote = _text["Settings_LanguageNotSaved"];
            this.Changed?.Invoke();
        }
    }

    /// <summary>Signs out: the server revokes the session, then <see cref="SignedOut"/> is raised.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the player is signed out (or the request failed).</returns>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (this.IsBusy || this.CurrentSession is null)
        {
            return;
        }

        this.IsBusy = true;
        this.ErrorText = null;
        this.Changed?.Invoke();
        try
        {
            await _client.LogoutAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (AccountConnectionException)
        {
            // The connection is gone, so the server cannot honour the request; the local sign-out still happens
            // and the token lapses by itself. Nothing useful to show: the player asked to leave.
        }
        finally
        {
            this.IsBusy = false;
        }

        this.CurrentSession = null;
        await _remembered.ClearAsync(CancellationToken.None).ConfigureAwait(false);
        this.ChangeMode(AccountFormMode.Login);
        this.SignedOut?.Invoke();
    }

    private async Task RunAsync(Func<Task<AccountReply>> request, bool isUpgrade, RememberedAccountKind? kind)
    {
        if (this.IsBusy)
        {
            return;
        }

        this.IsBusy = true;
        this.ErrorText = null;
        this.Changed?.Invoke();

        AccountReply? reply = null;
        try
        {
            reply = await request().ConfigureAwait(false);
        }
        catch (AccountConnectionException)
        {
            this.ErrorText = _text["Account_Error_Connection"];
        }
        finally
        {
            this.IsBusy = false;
        }

        // A sign-in or link is remembered for the continue card; a resume (kind null) renews its own record in the view model.
        if (kind is { } remember && reply is { Status: AccountStatus.Ok, Session: { } issued })
        {
            await _remembered.RememberAsync(issued, remember, CancellationToken.None).ConfigureAwait(false);
        }

        this.ApplyReply(reply, isUpgrade);
    }

    private async Task RunExternalAsync(ExternalProvider provider, Func<string, Task<AccountReply>> send, bool isUpgrade, CancellationToken cancellationToken)
    {
        if (this.IsBusy)
        {
            return;
        }

        if (!_options.IsConfigured(provider))
        {
            this.ErrorText = _text["Account_Provider_NotConfigured"];
            this.Changed?.Invoke();
            return;
        }

        this.IsBusy = true;
        this.ErrorText = null;
        this.Changed?.Invoke();

        AccountReply? reply = null;
        try
        {
            this.IsAwaitingProvider = true;
            this.Changed?.Invoke();
            string idToken = await _externalSignIn.GetIdTokenAsync(provider, cancellationToken).ConfigureAwait(false);
            this.IsAwaitingProvider = false;
            reply = await send(idToken).ConfigureAwait(false);
        }
        catch (ExternalSignInException ex)
        {
            this.ErrorText = ProviderFailureText(ex.Failure);
        }
        catch (AccountConnectionException)
        {
            this.ErrorText = _text["Account_Error_Connection"];
        }
        finally
        {
            this.IsAwaitingProvider = false;
            this.IsBusy = false;
        }

        if (reply is { Status: AccountStatus.Ok, Session: { } issued })
        {
            RememberedAccountKind kind = provider == ExternalProvider.Google ? RememberedAccountKind.Google : RememberedAccountKind.Microsoft;
            await _remembered.RememberAsync(issued, kind, CancellationToken.None).ConfigureAwait(false);
        }

        this.ApplyReply(reply, isUpgrade);
    }

    private void ApplyReply(AccountReply? reply, bool isUpgrade)
    {
        if (reply is { Status: AccountStatus.Ok, Session: { } session })
        {
            this.CurrentSession = session;
            this.Mode = AccountFormMode.Login;
            this.Nickname = string.Empty;
            this.Changed?.Invoke();
            if (isUpgrade)
            {
                this.SessionUpdated?.Invoke(session);
            }
            else
            {
                this.SignedIn?.Invoke(session);
            }

            return;
        }

        if (reply is not null)
        {
            this.ErrorText = StatusText(reply.Status);
        }

        this.Changed?.Invoke();
    }

    private void ChangeMode(AccountFormMode mode)
    {
        this.Mode = mode;
        this.ErrorText = null;
        this.Changed?.Invoke();
    }

    private string ProviderFailureText(ExternalSignInFailure failure) => failure switch
    {
        ExternalSignInFailure.Cancelled => _text["Account_Error_Cancelled"],
        ExternalSignInFailure.PopupBlocked => _text["Account_Error_PopupBlocked"],
        ExternalSignInFailure.NotDisplayed => _text["Account_Error_NotDisplayed"],
        ExternalSignInFailure.NotConfigured => _text["Account_Provider_NotConfigured"],
        _ => _text["Account_Error_ProviderFailed"],
    };

    private string StatusText(AccountStatus status) => _text["Account_Status_" + status.ToString()];
}
