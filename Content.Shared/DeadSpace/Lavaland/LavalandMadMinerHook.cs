// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Content.Shared.DoAfter;

namespace Content.Shared.DeadSpace.Lavaland;

[RegisterComponent, NetworkedComponent]
public sealed partial class LavalandMadMinerHookedComponent : Component
{
    [ViewVariables] public EntityUid Miner;
    [ViewVariables] public bool Removing;
}

[RegisterComponent]
public sealed partial class LavalandMadMinerHookVisualComponent : Component
{
    [ViewVariables] public EntityUid Target;
}

[Serializable, NetSerializable]
public sealed partial class LavalandRemoveMadMinerHookDoAfterEvent : SimpleDoAfterEvent;
