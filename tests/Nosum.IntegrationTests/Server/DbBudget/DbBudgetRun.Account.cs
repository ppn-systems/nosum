// Copyright (c) 2026 PPN Systems. All rights reserved.

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

        // Registered players: created through the service, signed in over the wire.
        string email = this.NextEmail();
        string nickname = this.NextNickname();
        email = "budget.player.login" + nickname + "@gmail.com";
        string token = await _rig.RegisteredAccountAsync(nickname, email);
        BudgetPlayer loginSocket = await _rig.ConnectAsync();
        using (Login login = new() { Email = email, Password = "correct horse battery" })
        {
            AccountResult result = await _rig.MeasureAsync("Account", AccountOpCode.Login, "Login", "e-mail and password", () => loginSocket.RequestAsync<AccountResult>(login));
            if (result.Status != AccountStatus.Ok)
            {
                throw new System.InvalidOperationException("Login refused: " + result.Status);
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

        // Leaving: the connection drops and the place is saved.
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

        // Sign-up on a bare socket.
        BudgetPlayer signingUp = await _rig.ConnectAsync();
        string newNick = this.NextNickname();
        using (Register register = new() { Email = "budget.register." + newNick + "@gmail.com", Password = "correct horse battery", PreferredLanguage = GameLanguage.English, Nickname = newNick })
        {
            AccountResult result = await _rig.MeasureAsync("Account", AccountOpCode.Register, "Register", "new account", () => signingUp.RequestAsync<AccountResult>(register));
            if (result.Status != AccountStatus.Ok)
            {
                throw new System.InvalidOperationException("Register refused: " + result.Status);
            }
        }

        await signingUp.DisposeAsync();

        // A guest turning into a registered player.
        BudgetPlayer guestPlayer = await _rig.SignedInGuestAsync();
        string upNick = this.NextNickname();
        using (UpgradeGuest upgrade = new() { Email = "budget.upgrade." + upNick + "@gmail.com", Password = "correct horse battery", Nickname = upNick })
        {
            AccountResult result = await _rig.MeasureAsync("Account", AccountOpCode.UpgradeGuest, "UpgradeGuest", "guest to registered", () => guestPlayer.RequestAsync<AccountResult>(upgrade));
            if (result.Status != AccountStatus.Ok)
            {
                throw new System.InvalidOperationException("Upgrade refused: " + result.Status);
            }
        }

        await guestPlayer.DisposeAsync();
    }
}
