// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Account;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Account;

/// <summary>The step of the recovery form.</summary>
public enum RecoveryStep
{
    /// <summary>Asking for the account e-mail to send a code to.</summary>
    Request,

    /// <summary>Entering the code from the e-mail and a new password.</summary>
    Confirm,

    /// <summary>The password was changed; the player goes back to sign in.</summary>
    Done,
}

/// <summary>
/// View model of the "forgot password" form (docs/adr/ADR-032 D4): e-mail, then the e-mailed code with a new password. The server answers with
/// status codes only; every visible string comes from <c>Language.resx</c>.
/// </summary>
public sealed class PasswordRecoveryViewModel
{
    private const int CodeLength = 8;

    private readonly IRecoveryClient _client;
    private readonly IStringLocalizer<Language> _text;

    /// <summary>Initializes the view model.</summary>
    /// <param name="client">The network access.</param>
    /// <param name="text">The localizer for <c>Language.resx</c>.</param>
    public PasswordRecoveryViewModel(IRecoveryClient client, IStringLocalizer<Language> text)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(text);
        _client = client;
        _text = text;
    }

    /// <summary>Raised when something the components show has changed; they re-render.</summary>
    public event Action? Changed;

    /// <summary>Raised when the form should close and the sign-in form return (cancelled, or done and acknowledged).</summary>
    public event Action? Closed;

    /// <summary>Gets the current step.</summary>
    public RecoveryStep Step { get; private set; }

    /// <summary>Gets or sets the typed account e-mail.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Gets or sets the typed code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the typed new password. Cleared after every submit.</summary>
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>Gets or sets the repeated new password. Cleared after every submit.</summary>
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>Gets whether a request is in flight.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Gets the localized error to show, or null.</summary>
    public string? ErrorText { get; private set; }

    /// <summary>Gets the localized note to show (the code was sent, the password changed), or null.</summary>
    public string? InfoText { get; private set; }

    /// <summary>Gets the form title.</summary>
    public string Title => _text[this.Step switch
    {
        RecoveryStep.Confirm => "Recovery_Title_Confirm",
        RecoveryStep.Done => "Recovery_Title_Done",
        _ => "Recovery_Title_Request",
    }];

    /// <summary>Gets the text of the link that opens this form.</summary>
    public string LinkText => _text["Recovery_Link"];

    /// <summary>Gets the e-mail field label.</summary>
    public string EmailLabel => _text["Account_Email"];

    /// <summary>Gets the explanation under the e-mail field.</summary>
    public string RequestHint => _text["Recovery_RequestHint"];

    /// <summary>Gets the code field label.</summary>
    public string CodeLabel => _text["Recovery_Code"];

    /// <summary>Gets the hint under the code field.</summary>
    public string CodeHint => _text["Recovery_CodeHint"];

    /// <summary>Gets the new-password field label.</summary>
    public string NewPasswordLabel => _text["Recovery_NewPassword"];

    /// <summary>Gets the repeat-password field label.</summary>
    public string ConfirmPasswordLabel => _text["Account_ConfirmPassword"];

    /// <summary>Gets the submit button text; the busy text while a request is in flight.</summary>
    public string SubmitText => this.IsBusy
        ? _text["Account_Busy"]
        : _text[this.Step switch
        {
            RecoveryStep.Confirm => "Recovery_Submit_Confirm",
            RecoveryStep.Done => "Recovery_Back",
            _ => "Recovery_Submit_Request",
        }];

    /// <summary>Gets the text of the button that leaves the form.</summary>
    public string CancelText => _text["Account_Cancel"];

    /// <summary>Opens the form on its first step, with no leftovers of an earlier visit.</summary>
    /// <param name="email">An e-mail already typed on the sign-in form, or empty.</param>
    public void Begin(string email)
    {
        this.Step = RecoveryStep.Request;
        this.Email = email ?? string.Empty;
        this.Code = string.Empty;
        this.ClearPasswords();
        this.ErrorText = null;
        this.InfoText = null;
        this.Changed?.Invoke();
    }

    /// <summary>Leaves the form without changing anything.</summary>
    public void Cancel()
    {
        if (!this.IsBusy)
        {
            this.Closed?.Invoke();
        }
    }

    /// <summary>Validates the step, sends the request and moves on or shows the refusal. Does nothing while a request is in flight.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the reply has been handled.</returns>
    public async Task SubmitAsync(CancellationToken cancellationToken)
    {
        if (this.IsBusy)
        {
            return;
        }

        if (this.Step == RecoveryStep.Done)
        {
            this.Closed?.Invoke();
            return;
        }

        string? local = this.Validate();
        if (local is not null)
        {
            this.ErrorText = local;
            this.Changed?.Invoke();
            return;
        }

        this.IsBusy = true;
        this.ErrorText = null;
        this.Changed?.Invoke();

        string email = this.Email.Trim();
        AccountStatus? status = null;
        try
        {
            status = this.Step == RecoveryStep.Request
                ? await _client.RequestResetAsync(email, cancellationToken).ConfigureAwait(false)
                : await _client.ConfirmResetAsync(email, this.Code.Trim(), this.NewPassword, cancellationToken).ConfigureAwait(false);
        }
        catch (AccountConnectionException)
        {
            this.ErrorText = _text["Account_Error_Connection"];
        }
        finally
        {
            this.IsBusy = false;
            this.ClearPasswords();
        }

        if (status == AccountStatus.Ok)
        {
            if (this.Step == RecoveryStep.Request)
            {
                this.Step = RecoveryStep.Confirm;
                this.InfoText = _text["Recovery_CodeSent"];
            }
            else
            {
                this.Step = RecoveryStep.Done;
                this.Code = string.Empty;
                this.InfoText = _text["Recovery_Done"];
            }
        }
        else if (status is { } refused)
        {
            this.ErrorText = _text["Account_Status_" + refused];
        }

        this.Changed?.Invoke();
    }

    private string? Validate()
    {
        if (!AccountFormViewModel.IsPlausibleEmail(this.Email.Trim()))
        {
            return _text["Account_Status_" + AccountStatus.InvalidEmail];
        }

        if (this.Step == RecoveryStep.Request)
        {
            return null;
        }

        string code = this.Code.Trim();
        if (code.Length != CodeLength || !System.Linq.Enumerable.All(code, char.IsAsciiDigit))
        {
            return _text["Recovery_Error_Code"];
        }

        if (this.NewPassword.Length < AccountLimits.PasswordMinLength || this.NewPassword.Length > AccountLimits.PasswordMaxLength)
        {
            return _text["Account_Status_" + AccountStatus.InvalidPassword];
        }

        if (!string.Equals(this.NewPassword, this.ConfirmPassword, StringComparison.Ordinal))
        {
            return _text["Account_Error_PasswordMismatch"];
        }

        return null;
    }

    private void ClearPasswords()
    {
        this.NewPassword = string.Empty;
        this.ConfirmPassword = string.Empty;
    }
}
