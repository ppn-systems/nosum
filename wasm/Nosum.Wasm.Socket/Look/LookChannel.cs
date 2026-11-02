// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Nalix.Codec.Pooling;
using Nosum.Contracts.Look;
using Nosum.Contracts.Look.C2S;
using Nosum.Contracts.Look.S2C;
using Nosum.Contracts.Player.S2C;

namespace Nosum.Wasm.Socket.Look;

/// <summary>One player's look as the server sent it: ids only. The client turns them into sheets and tints through its catalogue (docs/adr/ADR-006 D6).</summary>
/// <param name="SkinId">Skin tone id.</param>
/// <param name="HairStyleId">Hair style id.</param>
/// <param name="HairDyeId">Hair dye id.</param>
/// <param name="ShirtItemId">Worn shirt item id, or zero.</param>
/// <param name="PantsItemId">Worn trousers item id, or zero.</param>
/// <param name="HatItemId">Worn hat item id, or zero.</param>
/// <param name="FeetItemId">Worn boots item id, or zero (docs/adr/ADR-084).</param>
/// <param name="Nickname">The player's nickname as the server sent it with the look (empty until known; a guest's neutral name is localized by the view layer).</param>
/// <param name="PetKind">The kind of the player's pet: 0 none, 1 cat, 2 dog (docs/adr/ADR-075 D3).</param>
/// <param name="PetName">The pet's cleaned name; empty without a pet.</param>
public readonly record struct LookInfo(int SkinId, int HairStyleId, int HairDyeId, int ShirtItemId, int PantsItemId, int HatItemId, string Nickname = "", byte PetKind = 0, string PetName = "", int FeetItemId = 0)
{
    /// <summary>Gets the look shown for a player whose look has not arrived yet (the server's default).</summary>
    public static LookInfo Default => new(1, 1, 1, 0, 0, 0, string.Empty);
}

/// <summary>
/// The client's mirror of the looks of the players in sight. The server sends a player's look when they come into the interest set and on every change, and
/// a <see cref="PlayerLeft"/> when they go (docs/adr/ADR-022 D3); nothing is derived from a player id here. The frame loop reads with <see cref="TryGet"/>, which never allocates or awaits.
/// </summary>
public sealed class LookChannel : IDisposable
{
    private readonly IGameSocket _socket;
    private readonly Func<uint> _localPlayerId;
    private readonly IDisposable[] _subscriptions;
    private readonly Lock _gate = new();
    private readonly Dictionary<uint, LookInfo> _looks = [];

    // Every look change the player chose, in the order chosen, until the server answers it. The first one is in flight; the rest wait. Look writes go
    // one at a time: the server applies each under a lock per player, but not necessarily in the order they were sent, so a take-off sent after a wear
    // could run first and find nothing worn (docs/adr/ADR-120).
    private readonly List<PendingLook> _queue = [];
    private bool _sending;

    /// <summary>Initializes the channel and starts listening for pushed looks.</summary>
    /// <param name="socket">The game connection.</param>
    /// <param name="localPlayerId">Returns the local player's id on the map (zero before joining).</param>
    public LookChannel(IGameSocket socket, Func<uint> localPlayerId)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(localPlayerId);

        _socket = socket;
        _localPlayerId = localPlayerId;
        _subscriptions = [socket.Subscribe<LookChanged>(p => this.Store(p.Look)), socket.Subscribe<PlayerLeft>(this.OnPlayerLeft)];
    }

    /// <summary>Raised when any look changed, or a look change was queued or answered. May run on any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised with the server's answer to a wardrobe request. May run on any thread.</summary>
    public event Action<LookStatus>? Completed;

    /// <summary>
    /// Gets the look changes the player chose that the server has not answered for yet, oldest first. The first one is in flight. The wardrobe shows them
    /// over the server's look, so a choice shows at once and a refused one disappears when its answer arrives.
    /// </summary>
    public IReadOnlyList<PendingLook> Pending
    {
        get
        {
            lock (_gate)
            {
                return _queue.ToArray();
            }
        }
    }

    /// <summary>Looks up a player's look.</summary>
    /// <param name="playerId">The player's id on the map.</param>
    /// <param name="look">The look when known.</param>
    /// <returns><see langword="true"/> when the server sent this player's look.</returns>
    public bool TryGet(uint playerId, out LookInfo look)
    {
        lock (_gate)
        {
            return _looks.TryGetValue(playerId, out look);
        }
    }

    /// <summary>Looks up a player's nickname (cached with the look the server sends on join, map change and every change).</summary>
    /// <param name="playerId">The player's id on the map.</param>
    /// <param name="nickname">The nickname when known and not empty.</param>
    /// <returns><see langword="true"/> when the server sent this player's nickname.</returns>
    public bool TryGetNickname(uint playerId, [NotNullWhen(true)] out string? nickname)
    {
        lock (_gate)
        {
            if (_looks.TryGetValue(playerId, out LookInfo look) && !string.IsNullOrEmpty(look.Nickname))
            {
                nickname = look.Nickname;
                return true;
            }
        }

        nickname = null;
        return false;
    }

    /// <summary>Copies the nicknames of everyone whose look is known (a copy, for the label cache; not for the frame).</summary>
    /// <returns>The distinct nicknames.</returns>
    public string[] KnownNicknames()
    {
        lock (_gate)
        {
            HashSet<string> names = [];
            foreach (LookInfo look in _looks.Values)
            {
                if (!string.IsNullOrEmpty(look.Nickname))
                {
                    _ = names.Add(look.Nickname);
                }
            }

            return [.. names];
        }
    }

    /// <summary>Looks up the local player's look.</summary>
    /// <param name="look">The look when known.</param>
    /// <returns><see langword="true"/> when the server sent it.</returns>
    public bool TryGetLocal(out LookInfo look) => this.TryGet(_localPlayerId(), out look);

    /// <summary>Asks for the looks of everyone on the map; the server also announces the player's own look to the others. Call after joining a map or changing map.</summary>
    public void Load() => _ = this.LoadAsync();

    /// <summary>
    /// Chooses avatar parts. The choice joins the queue; while an earlier change waits for its answer, only the newest of the appearance choices that
    /// wait is sent (an appearance request carries all three parts, so older ones would only be overwritten).
    /// </summary>
    /// <param name="skinId">Skin tone id.</param>
    /// <param name="hairStyleId">Hair style id.</param>
    /// <param name="hairDyeId">Hair dye id.</param>
    public void SetAppearance(int skinId, int hairStyleId, int hairDyeId) => this.Enqueue(new PendingAppearance(skinId, hairStyleId, hairDyeId));

    /// <summary>Wears the clothing item in an inventory slot. Queued behind the earlier look changes.</summary>
    /// <param name="slot">Inventory slot.</param>
    /// <param name="itemId">The item expected in the slot.</param>
    /// <param name="wearSlot">The wear slot the item goes to (1 shirt, 2 trousers, 3 hat, 4 boots).</param>
    public void Equip(int slot, int itemId, int wearSlot) => this.Enqueue(new PendingWear(slot, wearSlot, itemId));

    /// <summary>Takes the item worn in a slot off. Queued behind the earlier look changes.</summary>
    /// <param name="wearSlot">1 shirt, 2 trousers, 3 hat, 4 boots.</param>
    public void Unequip(int wearSlot) => this.Enqueue(new PendingTakeOff(wearSlot));

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    // A player who left the interest set (or the map) takes their look with them; they get a fresh one when they come back into sight.
    private void OnPlayerLeft(PlayerLeft left)
    {
        bool removed;
        lock (_gate)
        {
            removed = _looks.Remove(left.PlayerId);
        }

        if (removed)
        {
            this.Changed?.Invoke();
        }
    }

    private void Store(LookRow row)
    {
        lock (_gate)
        {
            _looks[row.PlayerId] = new LookInfo(row.SkinId, row.HairStyleId, row.HairDyeId, row.ShirtItemId, row.PantsItemId, row.HatItemId, row.Nickname, row.PetKind, row.PetName, row.FeetItemId);
        }

        this.Changed?.Invoke();
    }

    [SuppressMessage("Design", "CA1031", Justification = "A failed load is retried at the next join; there is no caller to report to.")]
    private async Task LoadAsync()
    {
        try
        {
            // After a map change the server may not have put the player on the new map yet: ask again a moment later when it answers with nobody.
            LooksSnapshot snapshot;
            int attempt = 0;
            do
            {
                using PacketScope<GetLooks> request = PacketFactory<GetLooks>.Acquire();
                snapshot = await _socket.RequestAsync<LooksSnapshot>(request.Value).ConfigureAwait(false);
                if (snapshot.Looks.Count == 0)
                {
                    await Task.Delay(300).ConfigureAwait(false);
                }
            }
            while (snapshot.Looks.Count == 0 && ++attempt < 4);

            lock (_gate)
            {
                _looks.Clear();
                foreach (LookRow row in snapshot.Looks)
                {
                    _looks[row.PlayerId] = new LookInfo(row.SkinId, row.HairStyleId, row.HairDyeId, row.ShirtItemId, row.PantsItemId, row.HatItemId, row.Nickname, row.PetKind, row.PetName, row.FeetItemId);
                }
            }

            this.Changed?.Invoke();
        }
        catch (Exception)
        {
            // Connection dropped mid-load.
        }
    }

    // Queues a change. An appearance choice that waits behind another appearance choice replaces it: the newest one carries everything the older one
    // would have sent. The change in flight (index 0) is never replaced.
    private void Enqueue(PendingLook change)
    {
        bool start;
        lock (_gate)
        {
            if (change is PendingAppearance && _queue.Count > 1 && _queue[^1] is PendingAppearance)
            {
                _queue[^1] = change;
            }
            else
            {
                _queue.Add(change);
            }

            start = !_sending;
            _sending = true;
        }

        this.Changed?.Invoke();
        if (start)
        {
            _ = this.SendQueueAsync();
        }
    }

    // Sends the queue one change at a time, oldest first, until it is empty. A lost connection drops the changes not yet sent; the pending changes
    // then clear and the server's look is shown again.
    [SuppressMessage("Design", "CA1031", Justification = "A lost request changes nothing; the player tries again.")]
    private async Task SendQueueAsync()
    {
        while (true)
        {
            PendingLook head;
            lock (_gate)
            {
                if (_queue.Count == 0)
                {
                    _sending = false;
                    break;
                }

                head = _queue[0];
            }

            LookStatus status;
            try
            {
                status = await this.SendAsync(head).ConfigureAwait(false);
            }
            catch (Exception)
            {
                lock (_gate)
                {
                    _queue.Clear();
                    _sending = false;
                }

                this.Changed?.Invoke();
                return;
            }

            lock (_gate)
            {
                _queue.RemoveAt(0);
            }

            this.Changed?.Invoke();
            this.Completed?.Invoke(status);
        }

        this.Changed?.Invoke();
    }

    private async Task<LookStatus> SendAsync(PendingLook change)
    {
        switch (change)
        {
            case PendingAppearance appearance:
            {
                using PacketScope<SetAppearance> request = PacketFactory<SetAppearance>.Acquire();
                request.Value.SkinId = (byte)appearance.SkinId;
                request.Value.HairStyleId = (byte)appearance.HairStyleId;
                request.Value.HairDyeId = (byte)appearance.HairDyeId;
                LookResult reply = await _socket.RequestAsync<LookResult>(request.Value).ConfigureAwait(false);
                return reply.Status;
            }

            case PendingWear wear:
            {
                using PacketScope<EquipItem> request = PacketFactory<EquipItem>.Acquire();
                request.Value.Slot = (byte)wear.InventorySlot;
                request.Value.ItemId = wear.ItemId;
                LookResult reply = await _socket.RequestAsync<LookResult>(request.Value).ConfigureAwait(false);
                return reply.Status;
            }

            case PendingTakeOff takeOff:
            {
                using PacketScope<UnequipItem> request = PacketFactory<UnequipItem>.Acquire();
                request.Value.WearSlot = (byte)takeOff.WearSlot;
                LookResult reply = await _socket.RequestAsync<LookResult>(request.Value).ConfigureAwait(false);
                return reply.Status;
            }

            default:
                throw new InvalidOperationException("Unknown look change: " + change.GetType().Name);
        }
    }
}
