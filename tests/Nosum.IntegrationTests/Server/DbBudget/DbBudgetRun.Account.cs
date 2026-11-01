// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Nosum.Contracts.Account;
using Nosum.Contracts.Account.C2S;
using Nosum.Contracts.Account.S2C;
using Nosum.Contracts.Common;
using Nosum.Contracts.Player;
using Nosum.Contracts.Player.S2C;
using Nosum.Host;

namespace Nosum.IntegrationTests.Server.DbBudget;

public sealed partial class DbBudgetRun
{
    // Connecting, signing up or in, entering the world and leaving it: the cost of one player arriving and going.
    private async Task AccountAsync()
    {
        // The socket and the transport handshake touch no table.
        BudgetPlayer anonymous = await _rig.MeasureAsync("Connection", null, "Connect", "WebSocket + handshake", async () => await _rig.ConnectAsync());

        using (StartGuest guest = new() { PreferredLanguage = GameLanguage.English })
        {
            AccountResult started = await _rig.MeasureAsync("Account", AccountOpCode.StartGuest, "StartGuest", "new guest", () => anonymous.RequestAsync<AccountResult>(guest));
            await anonymous.ResumeAsync(started.SessionToken);
        }

        await anonymous.DisposeAsync();

        // Registered players: created through the service, signed in over the wire with their Google identity.
        string nickname = this.NextNickname();
        string subject = Guid.NewGuid().ToString("N");
        string token = await _rig.RegisteredAccountAsync(nickname, subject);
        BudgetPlayer loginSocket = await _rig.ConnectAsync();
        using (ExternalSignIn signIn = NewSignIn(subject))
        {
            AccountResult result = await _rig.MeasureAsync("Account", AccountOpCode.ExternalSignIn, "ExternalSignIn", "Google identity, registered player", () => loginSocket.RequestAsync<AccountResult>(signIn));
            if (result.Status != AccountStatus.Ok)
            {
                throw new System.InvalidOperationException("Sign-in refused: " + result.Status);
            }
        }

        // The same player on a second socket: the first is replaced (one active session per player).
        BudgetPlayer replacing = await _rig.ConnectAsync();
        using (ResumeSession resume = new() { SessionToken = token })
        {
            _ = await _rig.MeasureAsync("Account", AccountOpCode.ResumeSession, "ResumeSession", "token, replacing an open session", () => replacing.RequestAsync<AccountResult>(resume));
        }

        await loginSocket.DisposeAsync();

        // A fresh socket resuming a token nobody holds open: the plain reconnect.
        BudgetPlayer fresh = await _rig.ConnectAsync();
        using (ResumeSession resume = new() { SessionToken = token })
        {
            _ = await _rig.MeasureAsync("Account", AccountOpCode.ResumeSession, "ResumeSession", "token, no other session", () => fresh.RequestAsync<AccountResult>(resume));
        }

        await replacing.DisposeAsync();

        using (SetLanguage language = new() { Language = GameLanguage.Vietnamese })
        {
            _ = await _rig.MeasureAsync("Account", AccountOpCode.SetLanguage, "SetLanguage", "registered player", () => fresh.RequestAsync<LanguageResult>(language));
        }

        using (SetNickname rename = new() { Nickname = this.NextNickname() })
        {
            _ = await _rig.MeasureAsync("Account", AccountOpCode.SetNickname, "SetNickname", "first change is free", () => fresh.RequestAsync<NicknameResult>(rename));
        }

        // Entering the world.
        await _rig.MeasureAsync("Player", PlayerOpCode.JoinMap, "JoinMap", "first join, no saved place", async () => await fresh.JoinAsync(Program.FarmMapId));
        await _rig.Counter.SettleAsync(System.TimeSpan.FromMilliseconds(150), _rig.Timeout);

        // Leaving: the connection drops and the place is saved. The save starts from the copy of the player's state row that the server holds; whether that
        // copy is still held when the socket closes depends on timing (a run that found it gone read the row first: one extra SELECT). A player who has been
        // playing holds it, so the scenario loads it first and measures the leave itself.
        _ = await BudgetRig.Service<Nosum.Abstractions.Accounts.IPlayerStateService>().LoadAsync(fresh.AccountId, _rig.Timeout);
        await _rig.MeasureAsync("Connection", null, "Disconnect", "joined player, socket closed", async () =>
        {
            await fresh.DisposeAsync();
            return true;
        });

        // A joined player coming back to a saved place.
        BudgetPlayer again = await _rig.ConnectAsync();
        _ = await again.ResumeAsync(token);
        _ = await _rig.MeasureAsync("Player", PlayerOpCode.JoinMap, "JoinMap", "returning player, saved place", () => again.JoinAsync(Program.FarmMapId));

        using (Logout logout = new())
        {
            _ = await _rig.MeasureAsync("Account", AccountOpCode.Logout, "Logout", "joined player", () => again.RequestAsync<AccountResult>(logout));
        }

        await again.DisposeAsync();

        // Sign-up on a bare socket: the first sign-in of a Google identity creates the account.
        BudgetPlayer signingUp = await _rig.ConnectAsync();
        using (ExternalSignIn register = NewSignIn(Guid.NewGuid().ToString("N")))
        {
            AccountResult result = await _rig.MeasureAsync("Account", AccountOpCode.ExternalSignIn, "ExternalSignIn", "Google identity, new account", () => signingUp.RequestAsync<AccountResult>(register));
            if (result.Status != AccountStatus.Ok)
            {
                throw new System.InvalidOperationException("Sign-up refused: " + result.Status);
            }
        }

        await signingUp.DisposeAsync();

        // A guest turning into a registered player by linking a Google identity.
        BudgetPlayer guestPlayer = await _rig.SignedInGuestAsync();
        // The guest's friends and requests are held as a playing guest holds them. Whether they still are when the link arrives depends on timing (a session-end
        // handler runs off the closing path, and a run that found them gone read the friendships first: one extra SELECT), so they are loaded here first.
        _ = await BudgetRig.Service<Nosum.Infrastructure.Friends.FriendFacts>().GetAsync(guestPlayer.AccountId, _rig.Timeout);
        string upNick = this.NextNickname();
        using (LinkExternalIdentity link = new() { Provider = ExternalProvider.Google, IdToken = FakeExternalTokenVerifier.Token(ExternalProvider.Google, Guid.NewGuid().ToString("N")), Nickname = upNick })
        {
            AccountResult result = await _rig.MeasureAsync("Account", AccountOpCode.LinkExternalIdentity, "LinkExternalIdentity", "guest to registered", () => guestPlayer.RequestAsync<AccountResult>(link));
            if (result.Status != AccountStatus.Ok)
            {
                throw new System.InvalidOperationException("Link refused: " + result.Status);
            }
        }

        await guestPlayer.DisposeAsync();
    }

    // The Google sign-in of one subject, as the start screen sends it.
    private static ExternalSignIn NewSignIn(string subject) => new()
    {
        Provider = ExternalProvider.Google,
        IdToken = FakeExternalTokenVerifier.Token(ExternalProvider.Google, subject),
        PreferredLanguage = GameLanguage.English,
    };
}
