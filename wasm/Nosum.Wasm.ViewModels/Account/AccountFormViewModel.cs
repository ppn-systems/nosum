// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Account;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>Which form the account component shows.</summary>
public enum AccountFormMode
{
    /// <summary>Username and password of an existing account.</summary>
    Login,

    /// <summary>A new account.</summary>
    Register,
}

/// <summary>
/// View model of the login / register form. Reusable: any page binds the component to one instance
/// and listens to <see cref="SignedIn"/>. Every visible string comes from <c>Language.resx</c>;
/// the server only ever sends status codes.
/// </summary>
public sealed class AccountFormViewModel
{
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

    /// <summary>Raised when something the component shows has changed; the component re-renders.</summary>
    public event Action? Changed;

    /// <summary>Raised once after a successful register or login. Apply <see cref="AccountSession.PreferredLanguage"/> here.</summary>
    public event Action<AccountSession>? SignedIn;

    /// <summary>Gets the form being shown.</summary>
    public AccountFormMode Mode { get; private set; }

    /// <summary>Gets or sets the typed username.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Gets or sets the typed password. Cleared after every submit.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the repeated password (register only).</summary>
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Gets whether a request is in flight.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Gets the localized error to show, or null.</summary>
    public string? ErrorText { get; private set; }

    /// <summary>Gets whether the register form is shown.</summary>
    public bool IsRegister => this.Mode == AccountFormMode.Register;

    /// <summary>Gets the form title.</summary>
    public string Title => _text[this.IsRegister ? "Account_Title_Register" : "Account_Title_Login"];

    /// <summary>Gets the username field label.</summary>
    public string UsernameLabel => _text["Account_Username"];

    /// <summary>Gets the password field label.</summary>
    public string PasswordLabel => _text["Account_Password"];

    /// <summary>Gets the repeat-password field label.</summary>
    public string ConfirmPasswordLabel => _text["Account_ConfirmPassword"];

    /// <summary>Gets the username rules, shown under the field on the register form.</summary>
    public string UsernameHint => string.Format(
        CultureInfo.CurrentCulture, _text["Account_UsernameHint"].Value, AccountLimits.UsernameMinLength, AccountLimits.UsernameMaxLength);

    /// <summary>Gets the password rules, shown under the field on the register form.</summary>
    public string PasswordHint => string.Format(
        CultureInfo.CurrentCulture, _text["Account_PasswordHint"].Value, AccountLimits.PasswordMinLength, AccountLimits.PasswordMaxLength);

    /// <summary>Gets the submit button text; the busy text while a request is in flight.</summary>
    public string SubmitText => this.IsBusy
        ? _text["Account_Busy"]
        : _text[this.IsRegister ? "Account_Submit_Register" : "Account_Submit_Login"];

    /// <summary>Gets the text of the link that switches between the two forms.</summary>
    public string SwitchModeText => _text[this.IsRegister ? "Account_Switch_ToLogin" : "Account_Switch_ToRegister"];

    /// <summary>Shows the other form and clears the error and the typed passwords.</summary>
    public void SwitchMode()
    {
        this.Mode = this.IsRegister ? AccountFormMode.Login : AccountFormMode.Register;
        this.ErrorText = null;
        this.Password = string.Empty;
        this.ConfirmPassword = string.Empty;
        this.Changed?.Invoke();
    }

    /// <summary>
    /// Checks the form, sends the request and either raises <see cref="SignedIn"/> or sets
    /// <see cref="ErrorText"/>. Does nothing while a request is in flight.
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

        this.IsBusy = true;
        this.ErrorText = null;
        this.Changed?.Invoke();

        AccountReply? reply = null;
        try
        {
            reply = this.IsRegister
                ? await _client.RegisterAsync(this.Username, this.Password, CurrentLanguage(), cancellationToken).ConfigureAwait(false)
                : await _client.LoginAsync(this.Username, this.Password, cancellationToken).ConfigureAwait(false);
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
            this.Changed?.Invoke();
            SignedIn?.Invoke(session);
            return;
        }

        if (reply is not null)
        {
            this.ErrorText = StatusText(reply.Status);
        }

        this.Changed?.Invoke();
    }

    private string? Validate()
    {
        if (this.Username.Length < AccountLimits.UsernameMinLength || this.Username.Length > AccountLimits.UsernameMaxLength)
        {
            return StatusText(AccountStatus.InvalidUsername);
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

        return null;
    }

    private string StatusText(AccountStatus status) => _text["Account_Status_" + status.ToString()];

    private static string CurrentLanguage()
    {
        string tag = CultureInfo.CurrentUICulture.Name;
        return tag.Length == 0 ? AccountLimits.DefaultLanguage : tag;
    }
}
