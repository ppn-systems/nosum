// Copyright (c) 2026 PPN Systems. All rights reserved.

// xUnit tests await on the test context; CA2007 is not useful here.
#pragma warning disable CA2007

using System.Threading;
using System.Threading.Tasks;
using Nosum.Contracts.Combat.C2S;
using Nosum.Contracts.Combat.S2C;

namespace Nosum.IntegrationTests.Server;

/// <summary>The combat requests of the wire tests (docs/adr/ADR-056).</summary>
internal sealed partial class SocialClient
{
    /// <summary>Casts a combat skill and waits for its reply.</summary>
    public async Task<CastSkillResult> CastSkillAsync(byte skillId, int targetMonsterId, CancellationToken ct)
    {
        using CastSkill request = new() { SkillId = skillId, TargetMonsterId = targetMonsterId };
        return await this.RequestAsync<CastSkillResult>(request, ct);
    }

    /// <summary>
    /// Casts a combat skill without the wire pacing of <see cref="CastSkillAsync"/>, so two casts sent together reach the server together, the way two
    /// presses in one frame would.
    /// </summary>
    public async Task<CastSkillResult> CastSkillUnpacedAsync(byte skillId, int targetMonsterId, CancellationToken ct)
    {
        using CastSkill request = new() { SkillId = skillId, TargetMonsterId = targetMonsterId };
        return await _session.RequestAsync<CastSkillResult>(request, ct: ct);
    }

    /// <summary>Reads the monsters of the player's current map.</summary>
    public async Task<MonstersSnapshot> GetMonstersAsync(CancellationToken ct)
    {
        using Nosum.Contracts.Combat.C2S.GetMonsters request = new();
        return await this.RequestAsync<MonstersSnapshot>(request, ct);
    }

    /// <summary>Reads the player's HP and MP.</summary>
    public async Task<VitalsState> GetVitalsAsync(CancellationToken ct)
    {
        using Nosum.Contracts.Combat.C2S.GetVitals request = new();
        return await this.RequestAsync<VitalsState>(request, ct);
    }
}
