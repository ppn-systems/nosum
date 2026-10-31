// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Pet;
using Nosum.Gameplay.Pets;
using Nosum.Wasm.Socket.Pet;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Pets;

/// <summary>
/// The pet panel (docs/adr/ADR-075 D3-D4 and its amendment): with a pet, its name, its mood, the petting button (once a game day) and the give-back
/// button, which asks first; with none, where a cat or a dog is bought (the carpenter's counter, for a price). Pets are never adopted for free. The
/// server decides every outcome; the status line shows the answer.
/// </summary>
public sealed class PetViewModel : PanelViewModel, IDisposable
{
    private readonly PetChannel _pets;
    private bool _rehomeAsked;

    /// <summary>Initializes the view model and follows the pet state.</summary>
    /// <param name="pets">The pet connection.</param>
    /// <param name="text">UI text.</param>
    public PetViewModel(PetChannel pets, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(pets);
        _pets = pets;
        pets.Changed += this.OnPetChanged;
        pets.Completed += this.OnCompleted;
    }

    /// <summary>Raised after the server answered an adoption, a petting or a rehoming, with the answer. May run on any thread.</summary>
    public event Action<PetStatus>? Completed;

    /// <summary>Gets the owner's pet as the server last answered.</summary>
    public PetInfo Pet => _pets.Current;

    /// <summary>Gets whether the server has answered a pet request yet (before that the panel shows nothing about the pet).</summary>
    public bool IsLoaded => _pets.IsLoaded;

    /// <summary>Gets whether the owner has a pet.</summary>
    public bool HasPet => this.Pet.HasPet;

    /// <summary>Gets whether the pet can be petted now: it has a pet and it was not petted today.</summary>
    public bool CanPet => this.HasPet && !this.Pet.PettedToday;

    /// <summary>Gets the mood as a percentage for the bar, 0 to 100.</summary>
    public int MoodPercent => Math.Clamp(this.Pet.Mood, 0, PetRules.MaxMood);

    /// <summary>Gets the maximum length of a pet name, for the text boxes.</summary>
    public static int MaxNameLength => PetRules.MaxNameLength;

    /// <summary>Gets the localized name of the pet's kind.</summary>
    public string KindName => this.HasPet ? this.KindNameOf(this.Pet.Kind) : string.Empty;

    /// <summary>Gets the line that says where a pet is bought, with both prices (the carpenter's counter).</summary>
    public string BuyHintText => this.Text("Pet_BuyHint", Number(PetRules.CatPrice), Number(PetRules.DogPrice));

    /// <summary>Gets whether the give-back question is open, waiting for the player's answer.</summary>
    public bool RehomeAsked => _rehomeAsked;

    /// <summary>Gets the coins the carpenter gives back for the pet.</summary>
    public long RefundCoins => PetRules.RehomeRefundOf((byte)this.Pet.Kind);

    /// <summary>Gets the title of the give-back confirm box.</summary>
    public string RehomeTitle => this.Text("Pet_RehomeTitle");

    /// <summary>Gets the question of the give-back confirm box: the pet's name and the refund.</summary>
    public string RehomeMessage => this.Text("Pet_RehomeMessage", this.Pet.Name, Number(this.RefundCoins));

    /// <summary>Gets the localized name of a kind.</summary>
    /// <param name="kind">The kind: 1 cat, 2 dog.</param>
    /// <returns>The name.</returns>
    public string KindNameOf(PetKind kind) => kind == PetKind.Dog ? this.Text("Pet_Dog") : this.Text("Pet_Cat");

    /// <summary>Reads the pet. Call whenever the connection is signed in.</summary>
    public void Load() => _pets.Load();

    /// <summary>Buys (adopts) a cat or a dog under a name, at the carpenter's counter. The server charges the price and refuses a second pet.</summary>
    /// <param name="kind">The kind: 1 cat, 2 dog.</param>
    /// <param name="name">The name as typed; trimmed here, cleaned by the server.</param>
    public void Adopt(byte kind, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        _pets.Adopt(kind, name.Trim());
    }

    /// <summary>Pets the pet, once a game day.</summary>
    public void PetIt() => _pets.Pet();

    /// <summary>Opens the give-back question. Nothing is sent until the player confirms it.</summary>
    public void AskRehome()
    {
        if (this.HasPet && !_rehomeAsked)
        {
            _rehomeAsked = true;
            this.Raise();
        }
    }

    /// <summary>Closes the give-back question without giving the pet back.</summary>
    public void CancelRehome()
    {
        if (_rehomeAsked)
        {
            _rehomeAsked = false;
            this.Raise();
        }
    }

    /// <summary>Gives the pet back to the carpenter for half its price, once the player confirmed it.</summary>
    public void ConfirmRehome()
    {
        _rehomeAsked = false;
        this.Raise();
        if (this.HasPet)
        {
            _pets.Rehome();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _pets.Changed -= this.OnPetChanged;
        _pets.Completed -= this.OnCompleted;
    }

    private void OnPetChanged()
    {
        if (!this.HasPet)
        {
            _rehomeAsked = false;
        }

        this.Raise();
    }

    private void OnCompleted(PetStatus status)
    {
        this.SetStatus(this.StatusText(status), status == PetStatus.Ok);
        this.Completed?.Invoke(status);
    }

    private string StatusText(PetStatus status) => status switch
    {
        PetStatus.Ok => this.Text("Pet_Status_Ok"),
        PetStatus.NoPet => this.Text("Pet_Status_NoPet"),
        PetStatus.AlreadyHasPet => this.Text("Pet_Status_AlreadyHasPet"),
        PetStatus.InvalidKind => this.Text("Pet_Status_InvalidKind"),
        PetStatus.InvalidName => this.Text("Pet_Status_InvalidName"),
        PetStatus.AlreadyPetted => this.Text("Pet_Status_AlreadyPetted"),
        PetStatus.NotAtShop => this.Text("Pet_Status_NotAtShop"),
        PetStatus.NotEnoughCoins => this.Text("Pet_Status_NotEnoughCoins"),
        _ => this.Text("Pet_Status_Unavailable"),
    };
}
