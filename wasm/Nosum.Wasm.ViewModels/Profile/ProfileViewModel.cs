// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Account;
using Nosum.Gameplay.Players;
using Nosum.Wasm.ViewModels.Account;
using Nosum.Wasm.ViewModels.Player;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Profile;

/// <summary>
/// View model of the profile panel: shows the player's nickname and changes it (the server allows one change every 24 hours, validates the
/// rules and keeps names unique; this class only gives early feedback and shows the outcome). Only a registered player may rename: a guest keeps
/// the generated name, sees why, and chooses a nickname when registering (docs/adr/ADR-009 amendments).
/// </summary>
public sealed class ProfileViewModel : PanelViewModel
{
    private readonly IAccountClient _client;
    private readonly NicknameFormatter _names;
    private string _nickname = string.Empty;
    private bool _busy;
    private bool _isGuest;

    /// <summary>Initializes the view model.</summary>
    /// <param name="client">The network access.</param>
    /// <param name="names">Formats nicknames for display.</param>
    /// <param name="text">UI text.</param>
    public ProfileViewModel(IAccountClient client, NicknameFormatter names, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(names);
        _client = client;
        _names = names;
    }

    /// <summary>Gets or sets the nickname typed in the form.</summary>
    public string NewNickname { get; set; } = string.Empty;

    /// <summary>Gets the player's current nickname as the server sent it.</summary>
    public string Nickname => _nickname;

    /// <summary>Gets the "Current nickname: ..." line.</summary>
    public string CurrentText => this.Text("Profile_Current", _names.Display(_nickname));

    /// <summary>Gets the rules shown under the field.</summary>
    public string RuleText => string.Join(' ', this.Text("Account_NicknameHint", NicknameRules.MinLength, NicknameRules.MaxLength), this.Text("Profile_Rule"));

    /// <summary>Gets whether a request is in flight.</summary>
    public bool IsBusy => _busy;

    /// <summary>Gets whether the player is a guest and so cannot change the nickname.</summary>
    public bool IsGuest => _isGuest;

    /// <summary>Gets whether the rename form is offered (a registered player).</summary>
    public bool CanRename => !_isGuest;

    /// <summary>Gets the explanation shown to a guest instead of the rename form.</summary>
    public string GuestNoticeText => this.Text("Profile_GuestNotice");

    /// <summary>Records the nickname and kind of account of a fresh or updated session.</summary>
    /// <param name="nickname">The nickname from the account result.</param>
    /// <param name="isGuest">Whether the session is a guest's; registering turns this off.</param>
    public void Begin(string nickname, bool isGuest)
    {
        _nickname = nickname;
        _isGuest = isGuest;
        this.Raise();
    }

    /// <summary>Sends the typed nickname.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>A task that completes when the reply was shown.</returns>
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_busy)
        {
            return;
        }

        if (_isGuest)
        {
            this.SetStatus(this.Text("Account_Status_" + AccountStatus.NicknameRequiresAccount));
            return;
        }

        string typed = NicknameRules.Normalize(this.NewNickname);
        if (NicknameVerdictStatus.From(NicknameRules.Validate(typed)) is { } invalid)
        {
            this.SetStatus(this.Text("Account_Status_" + invalid));
            return;
        }

        _busy = true;
        try
        {
            NicknameReply reply = await _client.SetNicknameAsync(typed, cancellationToken).ConfigureAwait(false);
            _nickname = reply.Nickname.Length > 0 ? reply.Nickname : _nickname;
            if (reply.Status == AccountStatus.Ok)
            {
                this.NewNickname = string.Empty;
                this.SetStatus(this.Text("Profile_Changed", _names.Display(_nickname)));
            }
            else if (reply.Status == AccountStatus.NicknameTooSoon && reply.NextChangeAt is { } next)
            {
                this.SetStatus(string.Join(' ', this.Text("Account_Status_NicknameTooSoon"), this.Text("Profile_NextChange", next.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))));
            }
            else
            {
                this.SetStatus(this.Text("Account_Status_" + reply.Status));
            }
        }
        catch (AccountConnectionException)
        {
            this.SetStatus(this.Text("Account_Error_Connection"));
        }
        finally
        {
            _busy = false;
        }
    }
}
