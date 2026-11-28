using Content.Server.Interaction;
using Content.Server.NPC.Systems; //DS-14
using Content.Shared.Physics;

namespace Content.Server.NPC.HTN.Preconditions;

public sealed partial class TargetInLOSPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    private InteractionSystem _interaction = default!;
    private NPCCombatSystem _combat = default!; //DS-14

    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("rangeKey")]
    public string RangeKey = "RangeKey";

    [DataField("opaqueKey")]
    public bool UseOpaqueForLOSChecksKey = true;

    [DataField("ignoreLoose")]
    public bool IgnoreLoose; //DS-14

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _interaction = sysManager.GetEntitySystem<InteractionSystem>();
        _combat = sysManager.GetEntitySystem<NPCCombatSystem>(); //DS-14
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return false;

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);
        var collisionGroup = UseOpaqueForLOSChecksKey ? CollisionGroup.Opaque : (CollisionGroup.Impassable | CollisionGroup.InteractImpassable);

        if (IgnoreLoose) //DS-14
            return _interaction.InRangeUnobstructed(owner, target, range, collisionGroup, _combat.IsLoose); //DS-14

        return _interaction.InRangeUnobstructed(owner, target, range, collisionGroup);
    }
}
