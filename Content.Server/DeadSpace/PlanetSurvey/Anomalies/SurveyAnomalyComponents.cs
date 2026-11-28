// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using System.Numerics;
using Content.Shared.Access;
using Content.Shared.Damage;
using Content.Shared.DeadSpace.PlanetSurvey;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace.PlanetSurvey;

public enum SurveyAnomalyKind : byte
{
    Burner,
    Electra,
    Vortex,
    Frost,
    Jelly,
    Rift,
    Psi,
    Press,
    Steam,
    Laser,
    Magnet,
    Springboard,
    Emp,
    Push,
}

public enum SurveyBoon : byte
{
    Plating,
    Insulation,
    Purity,
    Swift,
    Stamina,
    Mending,
    Sinew,
    Rapid,
    Bane,
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class SurveyAnomalyComponent : Component
{
    [DataField(required: true)]
    public SurveyAnomalyKind Kind;

    [DataField]
    public float Radius = 1.2f;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan Windup = TimeSpan.Zero;

    [DataField]
    public TimeSpan Cycle = TimeSpan.Zero;

    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public float Power = 1f;

    [DataField]
    public bool Visible;

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public SoundSpecifier? WindupSound;

    [DataField]
    public EntProtoId? Effect;

    [ViewVariables]
    public readonly HashSet<EntityUid> Inside = new();

    [ViewVariables]
    public EntityUid? Pair;

    [ViewVariables]
    public EntityUid? Planet;

    [ViewVariables]
    public SurveyAnomalyState Shown = SurveyAnomalyState.Hidden;

    [ViewVariables, AutoPausedField]
    public TimeSpan Ready;

    [ViewVariables, AutoPausedField]
    public TimeSpan? StrikeAt;

    [ViewVariables, AutoPausedField]
    public TimeSpan RevealedUntil;

    [ViewVariables, AutoPausedField]
    public TimeSpan ActiveUntil;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class SurveyRiftedComponent : Component
{
    [ViewVariables, AutoPausedField]
    public TimeSpan Until;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class SurveyDetectorComponent : Component
{
    [DataField]
    public float Range = 10f;

    [DataField]
    public float RevealRange = 2.6f;

    [DataField]
    public float ArtifactRange = 20f;

    [DataField]
    public SoundSpecifier Beep = new SoundPathSpecifier("/Audio/Items/locator_beep.ogg");

    [ViewVariables, AutoPausedField]
    public TimeSpan NextScan;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextBeep;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextHint;
}

[DataDefinition]
public sealed partial class SurveyBoonEntry
{
    [DataField(required: true)]
    public SurveyBoon Boon;

    [DataField(required: true)]
    public float Value;
}

[RegisterComponent]
public sealed partial class SurveyArtifactComponent : Component
{
    [DataField]
    public int Tier = 1;

    [DataField]
    public List<SurveyBoonEntry> Boons = new();

    [DataField]
    public int Points = 4000;

    [DataField]
    public int IdentifiedBonus = 2000;

    [ViewVariables]
    public bool Identified;

    [ViewVariables]
    public bool Taken;

    [ViewVariables]
    public EntityUid? Planet;
}

[RegisterComponent]
public sealed partial class SurveyPlateComponent : Component
{
    [DataField]
    public SurveyBoon Boon;

    [DataField]
    public float Value;
}

[RegisterComponent]
public sealed partial class SurveyInfusionComponent : Component
{
    [ViewVariables]
    public SurveyBoon Boon;

    [ViewVariables]
    public float Value;
}

[RegisterComponent]
public sealed partial class SurveyBoonHolderComponent : Component
{
    [ViewVariables]
    public readonly Dictionary<SurveyBoon, float> Totals = new();
}

[RegisterComponent]
public sealed partial class SurveyAnalyzerComponent : Component
{
    [DataField]
    public float Time = 6f;

    [DataField]
    public ProtoId<AccessLevelPrototype> Access = "Research";
}

[RegisterComponent]
public sealed partial class SurveyStabilizerComponent : Component
{
    [DataField]
    public float Time = 8f;

    [DataField]
    public float Share = 0.4f;

    [DataField]
    public EntProtoId Plate = "SurveyInfusionPlate";

    [DataField]
    public Dictionary<SurveyBoon, EntProtoId> Products = new();

    [DataField]
    public ProtoId<AccessLevelPrototype> Access = "Research";
}

[RegisterComponent]
public sealed partial class SurveyLeadCaseComponent : Component;

[RegisterComponent]
public sealed partial class SurveyBaneComponent : Component
{
    [ViewVariables]
    public float Bonus;
}

[RegisterComponent]
public sealed partial class SurveyBeaconComponent : Component
{
    [DataField]
    public EntProtoId Deployed = "SurveyBeaconDeployed";

    [DataField]
    public float Time = 3f;
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class SurveyBeaconDeployedComponent : Component
{
    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(45);

    [DataField]
    public int Reward = 250;

    [DataField]
    public int StationReward = 800;

    [ViewVariables, AutoPausedField]
    public TimeSpan DoneAt;

    [ViewVariables]
    public EntityUid? Deployer;

    [ViewVariables]
    public int Sector = -1;

    [ViewVariables]
    public bool Done;
}

public sealed class SurveyPatrol
{
    public SurveySquad Squad = default!;
    public readonly List<Vector2> Route = new();
    public int Leg;
    public int Checks;
    public bool Issued;
    public TimeSpan Next;
}

public sealed class SurveyField
{
    public Vector2 Pos;
    public int Size;
    public bool Rare;
    public readonly HashSet<Vector2i> Safe = new();
    public readonly List<Vector2i> Path = new();
    public readonly List<EntityUid> Anomalies = new();
    public EntityUid? Artifact;
}
