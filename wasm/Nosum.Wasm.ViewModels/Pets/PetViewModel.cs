// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Pet;
using Nosum.Gameplay.Pets;
using Nosum.Wasm.Socket.Pet;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Pets;

/// <summary>
/// The pet panel (docs/adr/ADR-075 D3-D4): with no pet, the adoption form (a cat or a dog and a name) on the player's own farm; with a pet, its name, its
/// mood and the petting button, disabled once it was petted today. The server decides every outcome; the status line shows the answer.
/// </summary>
public sealed class PetViewModel : PanelViewModel, IDisposable
{
    private readonly PetChannel _pets;
    private bool _onOwnFarm;
    private byte _chosenKind = (byte)PetKind.Cat;
    private string _name = string.Empty;

    /// <summary>Initializes the view model and follows the pet state.</summary>
    /// <param name="pets">The pet connection.</param>
    /// <param name="text">UI text.</param>
    public PetViewModel(PetChannel pets, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(pets);
        _pets = pets;
        pets.Changed += this.Raise;
        pets.Completed += this.OnCompleted;
    }

    /// <summary>Gets the owner's pet as the server last answered.</summary>
    public PetInfo Pet => _pets.Current;

    /// <summary>Gets whether the server has answered a pet request yet (before that the panel shows nothing about the pet).</summary>
    public bool IsLoaded => _pets.IsLoaded;

    /// <summary>Gets whether the owner has a pet.</summary>
    public bool HasPet => this.Pet.HasPet;

    /// <summary>Gets whether the adoption form is offered: the state is known, there is no pet, and the player is on their own farm.</summary>
    public bool CanAdopt => this.IsLoaded && !this.HasPet && _onOwnFarm;

    /// <summary>Gets whether the pet can be petted now: it has a pet and it was not petted today.</summary>
    public bool CanPet => this.HasPet && !this.Pet.PettedToday;

    /// <summary>Gets the mood as a percentage for the bar, 0 to 100.</summary>
    public int MoodPercent => Math.Clamp(this.Pet.Mood, 0, PetRules.MaxMood);

    /// <summary>Gets the maximum length of a pet name, for the text box.</summary>
    public static int MaxNameLength => PetRules.MaxNameLength;

    /// <summary>Gets or sets the kind chosen in the adoption form: 1 cat, 2 dog.</summary>
    public byte ChosenKind
    {
        get => _chosenKind;
        set
        {
            if (_chosenKind != value)
            {
                _chosenKind = value;
                this.Raise();
            }
        }
    }

    /// <summary>Gets or sets the name typed in the adoption form. The server cleans it and refuses an empty one.</summary>
    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    /// <summary>Gets the localized name of the pet's kind.</summary>
    public string KindName => this.HasPet ? this.KindNameOf(this.Pet.Kind) : string.Empty;

    /// <summary>Gets the localized name of a kind for the adoption buttons.</summary>
    /// <param name="kind">The kind: 1 cat, 2 dog.</param>
    /// <returns>The name.</returns>
    public string KindNameOf(PetKind kind) => kind == PetKind.Dog ? this.Text("Pet_Dog") : this.Text("Pet_Cat");

    /// <summary>Tells whether the player is on their own farm; the adoption form is offered only there.</summary>
    /// <param name="onOwnFarm">Whether the player is on their own farm.</param>
    public void SetOnOwnFarm(bool onOwnFarm)
    {
        if (_onOwnFarm != onOwnFarm)
        {
            _onOwnFarm = onOwnFarm;
            this.Raise();
        }
    }

    /// <summary>Reads the pet. Call whenever the connection is signed in.</summary>
    public void Load() => _pets.Load();

    /// <summary>Adopts the chosen kind under the typed name.</summary>
    public void Adopt() => _pets.Adopt(_chosenKind, _name.Trim());

    /// <summary>Pets the pet, once a game day.</summary>
    public void PetIt() => _pets.Pet();

    /// <inheritdoc/>
    public void Dispose()
    {
        _pets.Changed -= this.Raise;
        _pets.Completed -= this.OnCompleted;
    }

    private void OnCompleted(PetStatus status)
    {
        if (status == PetStatus.Ok)
        {
            this.Name = string.Empty;
        }

        this.SetStatus(this.StatusText(status), status == PetStatus.Ok);
    }

    private string StatusText(PetStatus status) => status switch
    {
        PetStatus.Ok => this.Text("Pet_Status_Ok"),
        PetStatus.NoPet => this.Text("Pet_Status_NoPet"),
        PetStatus.AlreadyHasPet => this.Text("Pet_Status_AlreadyHasPet"),
        PetStatus.InvalidKind => this.Text("Pet_Status_InvalidKind"),
        PetStatus.InvalidName => this.Text("Pet_Status_InvalidName"),
        PetStatus.AlreadyPetted => this.Text("Pet_Status_AlreadyPetted"),
        _ => this.Text("Pet_Status_Unavailable"),
    };
}
