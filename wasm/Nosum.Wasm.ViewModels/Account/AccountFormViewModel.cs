// Copyright (c) 2026 PPN Systems. All rights reserved.

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
    /// <summary>E-mail and password of an existing account.</summary>
    Login,

    /// <summary>A new account.</summary>
    Register,

    /// <summary>Registration for the signed-in guest: the same player gets an e-mail and password.</summary>
    Upgrade,
}

/// <summary>
/// View model of the account UI: login, register, play as guest, upgrade a guest, sign out. Reusable:
/// any page binds <c>AccountForm</c> and <c>SessionBar</c> to one instance and listens to
/// <see cref="SignedIn"/>, <see cref="SessionUpdated"/> and <see cref="SignedOut"/>. Every visible
/// string comes from <c>Language.resx</c>; the server only ever sends status codes.
/// </summary>
public sealed class AccountFormViewModel
{
    private const string EmailSuffix = "@" + AccountLimits.EmailDomain;

    private readonly IAccountClient _client;
    private readonly IStringLocalizer<Language> _text;

    /// <summary>Initializes the view model.</summary>
    /// <param name="client">The network access.</param>
    /// <param name="text">The localizer for <c>Language.resx</c>.</param>
    public AccountFormViewModel(IAccountClient client, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(text);
        _client = client;
        _text = text;
    }

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

    /// <summary>Gets or sets the typed e-mail.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the typed nickname (register and upgrade).</summary>
    public string Nickname { get; set; } = string.Empty;

    /// <summary>Gets or sets the typed password. Cleared after every submit.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the repeated password (register and upgrade).</summary>
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Gets whether a request is in flight.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Gets the localized error to show, or null.</summary>
    public string? ErrorText { get; private set; }

    /// <summary>Gets whether the form asks for a password confirmation (register and upgrade).</summary>
    public bool IsRegister => this.Mode != AccountFormMode.Login;

    /// <summary>Gets whether the account form must be shown: signed out, or upgrading.</summary>
    public bool ShowsForm => this.CurrentSession is null || this.Mode == AccountFormMode.Upgrade;

    /// <summary>Gets whether the "play as guest" option is offered.</summary>
    public bool CanPlayAsGuest => this.CurrentSession is null;

    /// <summary>Gets whether the session bar offers the upgrade: a guest whose invitation to register is unlocked.</summary>
    public bool CanUpgrade => this.CurrentSession is { IsGuest: true, RegistrationPromptUnlocked: true } && this.Mode != AccountFormMode.Upgrade;

    /// <summary>Gets the name of the game, shown as the logo of the start screen.</summary>
    public string GameTitle => _text["Game_Title"];

    /// <summary>Gets the one-line tagline under the logo.</summary>
    public string Tagline => _text["Game_Tagline"];

    /// <summary>Gets the form title.</summary>
    public string Title => _text[this.Mode switch
    {
        AccountFormMode.Register => "Account_Title_Register",
        AccountFormMode.Upgrade => "Account_Title_Upgrade",
        _ => "Account_Title_Login",
    }];

    /// <summary>Gets the e-mail field label.</summary>
    public string EmailLabel => _text["Account_Email"];

    /// <summary>Gets the nickname field label.</summary>
    public string NicknameLabel => _text["Account_Nickname"];

    /// <summary>Gets the nickname rules, shown under the field when registering.</summary>
    public string NicknameHint => string.Format(CultureInfo.CurrentCulture, _text["Account_NicknameHint"].Value, NicknameRules.MinLength, NicknameRules.MaxLength);

    /// <summary>Gets the password field label.</summary>
    public string PasswordLabel => _text["Account_Password"];

    /// <summary>Gets the repeat-password field label.</summary>
    public string ConfirmPasswordLabel => _text["Account_ConfirmPassword"];

    /// <summary>Gets the e-mail rules, shown under the field when registering.</summary>
    public string EmailHint => string.Format(CultureInfo.CurrentCulture, _text["Account_EmailHint"].Value, AccountLimits.EmailDomain);

    /// <summary>Gets the password rules, shown under the field when registering.</summary>
    public string PasswordHint => string.Format(
        CultureInfo.CurrentCulture, _text["Account_PasswordHint"].Value, AccountLimits.PasswordMinLength, AccountLimits.PasswordMaxLength);

    /// <summary>Gets the submit button text; the busy text while a request is in flight.</summary>
    public string SubmitText => this.IsBusy
        ? _text["Account_Busy"]
        : _text[this.IsRegister ? "Account_Submit_Register" : "Account_Submit_Login"];

    /// <summary>Gets the text of the link that switches between login and register.</summary>
    public string SwitchModeText => _text[this.Mode == AccountFormMode.Register ? "Account_Switch_ToLogin" : "Account_Switch_ToRegister"];

    /// <summary>Gets the text of the "play as guest" button.</summary>
    public string PlayAsGuestText => _text["Account_PlayAsGuest"];

    /// <summary>Gets the text of the button that leaves the upgrade form.</summary>
    public string CancelText => _text["Account_Cancel"];

    /// <summary>Gets the signed-in line of the session bar: the e-mail, or "Guest".</summary>
    public string SignedInText => this.CurrentSession is null
        ? string.Empty
        : this.CurrentSession.IsGuest
            ? _text["Account_Guest"]
            : string.Format(CultureInfo.CurrentCulture, _text["Account_SignedInAs"].Value, this.CurrentSession.Email);

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

    /// <summary>Switches between the login and register forms and clears the error and the typed passwords.</summary>
    public void SwitchMode()
    {
        if (this.Mode == AccountFormMode.Upgrade)
        {
            return;
        }

        this.ChangeMode(this.Mode == AccountFormMode.Register ? AccountFormMode.Login : AccountFormMode.Register);
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
    public Task PlayAsGuestAsync(CancellationToken cancellationToken)
        => this.RunAsync(() => _client.StartGuestAsync(UiCulture.Current, cancellationToken), isUpgrade: false);

    /// <summary>
    /// Checks the form for the current mode, sends the request and either raises the matching event or
    /// sets <see cref="ErrorText"/>. Does nothing while a request is in flight.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the reply has been handled.</returns>
    public async Task SubmitAsync(CancellationToken cancellationToken)
    {
        if (this.IsBusy)
        {
            return;
        }

        string? localError = this.Validate();
        if (localError is not null)
        {
            this.ErrorText = localError;
            this.Changed?.Invoke();
            return;
        }

        string email = this.Email.Trim();
        string password = this.Password;
        string nickname = NicknameRules.Normalize(this.Nickname);
        await this.RunAsync(
            () => this.Mode switch
            {
                AccountFormMode.Register => _client.RegisterAsync(email, password, nickname, UiCulture.Current, cancellationToken),
                AccountFormMode.Upgrade => _client.UpgradeGuestAsync(email, password, nickname, cancellationToken),
                _ => _client.LoginAsync(email, password, cancellationToken),
            },
            isUpgrade: this.Mode == AccountFormMode.Upgrade).ConfigureAwait(false);
    }

    /// <summary>Gets the language the UI is in now (the signed-in session's, which every switch updates; before sign-in the UI culture's).</summary>
    public GameLanguage Language => GameLanguages.Resolve(this.CurrentSession?.Language ?? UiCulture.Current);

    /// <summary>Gets the localized note under the language buttons when the server could not store the choice, or <see langword="null"/>.</summary>
    public string? LanguageNote { get; private set; }

    /// <summary>
    /// Switches the UI language now, then stores it on the account so it applies at every later sign-in. The UI does not wait for the
    /// server: if the request fails the language stays changed for this visit and <see cref="LanguageNote"/> says it was not saved.
    /// </summary>
    /// <param name="language">The wanted language.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the server has answered.</returns>
    public async Task SetLanguageAsync(GameLanguage language, CancellationToken cancellationToken)
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
    public async Task LogoutAsync(CancellationToken cancellationToken)
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
        this.ChangeMode(AccountFormMode.Login);
        this.SignedOut?.Invoke();
    }

    private async Task RunAsync(Func<Task<AccountReply>> request, bool isUpgrade)
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
            this.Password = string.Empty;
            this.ConfirmPassword = string.Empty;
        }

        if (reply is { Status: AccountStatus.Ok, Session: { } session })
        {
            this.CurrentSession = session;
            this.Mode = AccountFormMode.Login;
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
        this.Password = string.Empty;
        this.ConfirmPassword = string.Empty;
        this.Changed?.Invoke();
    }

    private string? Validate()
    {
        string email = this.Email.Trim();
        if (!IsPlausibleEmail(email))
        {
            // On login any unknown address is simply a failed sign-in.
            return this.IsRegister ? StatusText(AccountStatus.InvalidEmail) : StatusText(AccountStatus.InvalidCredentials);
        }

        if (this.Password.Length < AccountLimits.PasswordMinLength || this.Password.Length > AccountLimits.PasswordMaxLength)
        {
            // On login any wrong password is simply a failed sign-in; the length rules are a register concern.
            return this.IsRegister ? StatusText(AccountStatus.InvalidPassword) : StatusText(AccountStatus.InvalidCredentials);
        }

        if (this.IsRegister && !string.Equals(this.Password, this.ConfirmPassword, StringComparison.Ordinal))
        {
            return _text["Account_Error_PasswordMismatch"];
        }

        if (this.IsRegister)
        {
            // Early feedback only; the server applies the same rules and checks that the name is free.
            return NicknameVerdictStatus.From(NicknameRules.Validate(this.Nickname)) is { } status ? StatusText(status) : null;
        }

        return null;
    }

    // Early feedback only; the server applies the full rule (AccountRules.IsValidEmail).
    private static bool IsPlausibleEmail(string email)
    {
        if (email.Length > AccountLimits.EmailMaxLength || !email.EndsWith(EmailSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int local = email.Length - EmailSuffix.Length;
        return local >= AccountLimits.EmailLocalMinLength && local <= AccountLimits.EmailLocalMaxLength;
    }

    private string StatusText(AccountStatus status) => _text["Account_Status_" + status.ToString()];
}
