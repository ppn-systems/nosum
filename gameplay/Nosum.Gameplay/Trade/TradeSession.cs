// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Nosum.Gameplay.Trade;

/// <summary>Result of a rule on a <see cref="TradeSession"/>. A refusal changes nothing.</summary>
public enum TradeVerdict
{
    /// <summary>Applied.</summary>
    Ok = 0,

    /// <summary>The offer is malformed (unknown or untradable item, bad quantity, duplicate line, negative coins).</summary>
    InvalidOffer = 1,

    /// <summary>The player is not a party of the session.</summary>
    NotParticipant = 2,

    /// <summary>Both sides offer nothing, so there is nothing to confirm.</summary>
    NothingOffered = 3,

    /// <summary>The session has finished.</summary>
    Closed = 4,
}

/// <summary>
/// The two-party confirm rules of a direct trade, pure and in memory: each side holds an offer and a confirmation; changing either
/// offer clears both confirmations; the swap is ready only when both sides confirmed the offers as they now stand. Moving the items
/// and coins is the server's atomic transfer, not this class (docs/adr/ADR-013).
/// </summary>
public sealed class TradeSession
{
    private TradeOffer _offerA = TradeOffer.Empty;
    private TradeOffer _offerB = TradeOffer.Empty;
    private bool _confirmedA;
    private bool _confirmedB;

    /// <summary>Initializes a session between two players.</summary>
    /// <param name="a">The player who asked for the trade.</param>
    /// <param name="b">The player who accepted.</param>
    public TradeSession(Guid a, Guid b)
    {
        if (a == b)
        {
            throw new ArgumentException("A trade needs two different players.", nameof(b));
        }

        this.A = a;
        this.B = b;
    }

    /// <summary>Gets the player who asked for the trade.</summary>
    public Guid A { get; }

    /// <summary>Gets the player who accepted.</summary>
    public Guid B { get; }

    /// <summary>Gets whether the session ended (swapped or cancelled); a closed session accepts no change.</summary>
    public bool IsClosed { get; private set; }

    /// <summary>Gets whether both sides confirmed the current offers.</summary>
    public bool IsReady => !this.IsClosed && _confirmedA && _confirmedB;

    /// <summary>Returns the partner of a player.</summary>
    /// <param name="player">A party.</param>
    /// <returns>The other party.</returns>
    public Guid OtherOf(Guid player) => player == this.A ? this.B : this.A;

    /// <summary>Returns whether a player is a party.</summary>
    /// <param name="player">The player.</param>
    /// <returns><see langword="true"/> for A and B.</returns>
    public bool Involves(Guid player) => player == this.A || player == this.B;

    /// <summary>Returns a player's offer.</summary>
    /// <param name="player">A party.</param>
    /// <returns>Their offer.</returns>
    public TradeOffer OfferOf(Guid player) => player == this.A ? _offerA : _offerB;

    /// <summary>Returns whether a player confirmed the current offers.</summary>
    /// <param name="player">A party.</param>
    /// <returns><see langword="true"/> when confirmed.</returns>
    public bool IsConfirmed(Guid player) => player == this.A ? _confirmedA : _confirmedB;

    /// <summary>Replaces a player's offer. A different offer clears both confirmations.</summary>
    /// <param name="player">The party who changes their offer.</param>
    /// <param name="offer">The new offer.</param>
    /// <returns><see cref="TradeVerdict.Ok"/> or the reason.</returns>
    public TradeVerdict SetOffer(Guid player, TradeOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);

        if (this.IsClosed)
        {
            return TradeVerdict.Closed;
        }

        if (!this.Involves(player))
        {
            return TradeVerdict.NotParticipant;
        }

        TradeVerdict shape = offer.Validate();
        if (shape != TradeVerdict.Ok)
        {
            return shape;
        }

        if (offer.Equals(this.OfferOf(player)))
        {
            return TradeVerdict.Ok;
        }

        if (player == this.A)
        {
            _offerA = offer;
        }
        else
        {
            _offerB = offer;
        }

        _confirmedA = false;
        _confirmedB = false;
        return TradeVerdict.Ok;
    }

    /// <summary>Confirms the offers as they now stand.</summary>
    /// <param name="player">The confirming party.</param>
    /// <returns><see cref="TradeVerdict.Ok"/> or the reason; check <see cref="IsReady"/> afterwards.</returns>
    public TradeVerdict Confirm(Guid player)
    {
        if (this.IsClosed)
        {
            return TradeVerdict.Closed;
        }

        if (!this.Involves(player))
        {
            return TradeVerdict.NotParticipant;
        }

        if (_offerA.IsEmpty && _offerB.IsEmpty)
        {
            return TradeVerdict.NothingOffered;
        }

        if (player == this.A)
        {
            _confirmedA = true;
        }
        else
        {
            _confirmedB = true;
        }

        return TradeVerdict.Ok;
    }

    /// <summary>Ends the session; nothing can change afterwards.</summary>
    public void Close() => this.IsClosed = true;
}
