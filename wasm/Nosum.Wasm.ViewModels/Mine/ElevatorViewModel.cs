// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Localization;
using Nosum.Contracts.Mine;
using Nosum.Gameplay.Mines;
using Nosum.Wasm.Socket.Mine;
using Nosum.Wasm.ViewModels.Resources;

namespace Nosum.Wasm.ViewModels.Mine;

/// <summary>One stop of the elevator, already localized.</summary>
/// <param name="Floor">0 for the entrance cave, or the floor.</param>
/// <param name="Label">"Mine entrance" or "Floor 15".</param>
/// <param name="IsHere">Whether the player is on that stop now.</param>
public sealed record ElevatorStopDisplay(int Floor, string Label, bool IsHere);

/// <summary>
/// View model of the elevator picker (docs/adr/ADR-026 D3): a small dialog, opened with the action key next to an elevator, that lists the entrance and
/// every fifth floor down to the deepest the player reached. The server checks the player stands at an elevator and has been that deep.
/// </summary>
public sealed class ElevatorViewModel : PanelViewModel, IDisposable
{
    private readonly MineChannel _channel;
    private MineInfo _info;
    private bool _riding;

    /// <summary>Initializes the view model.</summary>
    /// <param name="channel">Talks to the elevator.</param>
    /// <param name="text">UI text.</param>
    public ElevatorViewModel(MineChannel channel, IStringLocalizer<Language> text)
        : base(text)
    {
        ArgumentNullException.ThrowIfNull(channel);
        _channel = channel;
        channel.Updated += this.OnUpdated;
    }

    /// <summary>Gets whether the picker is showing.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Gets the deepest floor the player reached, as the server last said.</summary>
    public int Deepest => _info.Deepest;

    /// <summary>Gets the line that tells how deep the player has been.</summary>
    public string DeepestText => this.Text("Elevator_Deepest", _info.Deepest);

    /// <summary>Gets the stops, the entrance first.</summary>
    public IReadOnlyList<ElevatorStopDisplay> Stops
    {
        get
        {
            List<ElevatorStopDisplay> stops = [];
            foreach (int floor in MineRules.ElevatorStops(_info.Deepest))
            {
                stops.Add(new ElevatorStopDisplay(floor, floor == MineRules.EntranceFloor ? this.Text("Elevator_Entrance") : this.Text("Elevator_Floor", floor), floor == _info.Floor));
            }

            return stops;
        }
    }

    /// <summary>Opens the picker and reads how deep the player has been.</summary>
    public void Open()
    {
        this.IsOpen = true;
        this.ClearStatus();
        _channel.Load();
        this.Raise();
    }

    /// <summary>Closes the picker.</summary>
    public void Close()
    {
        this.IsOpen = false;
        this.ClearStatus();
        this.Raise();
    }

    /// <summary>Rides to a stop.</summary>
    /// <param name="floor">The stop.</param>
    public void Ride(int floor)
    {
        this.ClearStatus();
        _riding = true;
        _channel.Ride(floor);
    }

    /// <inheritdoc/>
    public void Dispose() => _channel.Updated -= this.OnUpdated;

    private void OnUpdated(MineInfo info)
    {
        _info = info;
        bool rode = _riding;
        _riding = false;
        if (info.Status != MineStatus.Ok)
        {
            this.SetStatus(this.Text("Elevator_Status_" + info.Status));
        }
        else if (rode)
        {
            // The server moved the player; the map change follows as its own push.
            this.IsOpen = false;
            this.Raise();
        }
        else
        {
            this.Raise();
        }
    }
}
