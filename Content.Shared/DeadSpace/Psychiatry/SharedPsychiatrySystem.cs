// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Roles;
using Robust.Shared.Timing;

namespace Content.Shared.DeadSpace.Psychiatry;

public abstract class SharedPsychiatrySystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedRoleSystem _roles = default!;
    [Dependency] protected readonly IGameTiming Timing = default!;

    public const string SpecialPillReagentId = "Schizotoxin";
    public const string ClarityReagentId = "NeuroClarity";
    public const string PositronicSpecies = "IPC";

    public bool IsPositronic(EntityUid uid)
    {
        return TryComp<HumanoidAppearanceComponent>(uid, out var humanoid)
               && humanoid.Species == PositronicSpecies;
    }

    public bool IsAntagImmune(EntityUid uid, bool pillForced)
    {
        if (pillForced)
            return false;
        if (!_mind.TryGetMind(uid, out var mindId, out _))
            return false;
        return _roles.MindIsAntagonist(mindId);
    }

    public static SchizophreniaStage ClampStage(int stage) =>
        (SchizophreniaStage) Math.Clamp(stage, 0, (int) SchizophreniaStage.Acute);

    public static SchizophreniaStage LowerStage(SchizophreniaStage current, int by)
    {
        var next = (int) current - by;
        return next <= 0 ? SchizophreniaStage.None : ClampStage(next);
    }
}
