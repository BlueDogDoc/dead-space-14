// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.PlanetSurvey;

[Serializable, NetSerializable]
public enum SurveyAnomalyVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum SurveyAnomalyState : byte
{
    Hidden,
    Revealed,
    Active,
    Windup,
}

[Serializable, NetSerializable]
public sealed partial class SurveyAnalyzeDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class SurveyStabilizeDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class SurveyInfuseDoAfterEvent : SimpleDoAfterEvent;

[Serializable, NetSerializable]
public sealed partial class SurveyBeaconDoAfterEvent : SimpleDoAfterEvent;
