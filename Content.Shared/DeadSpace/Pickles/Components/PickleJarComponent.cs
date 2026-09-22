// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.DeadSpace.Pickles.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PickleJarComponent : Component
{
    public const string SolutionName = "drink";

    [DataField, AutoNetworkedField]
    public EntProtoId? PiecePrototype;

    [DataField, AutoNetworkedField]
    public int RemainingPieces;

    [DataField, AutoNetworkedField]
    public int MaxPieces = 4;

    [DataField, AutoNetworkedField]
    public Color? PieceTint;

    [DataField, AutoNetworkedField]
    public PickleMethod Method = PickleMethod.Vinegar;

    /// <summary>Wine jars use liquid fill only — no produce overlay.</summary>
    [DataField, AutoNetworkedField]
    public bool IsWine;

    /// <summary>RSI style key for jar contents (cucumber, tomato, …).</summary>
    [DataField, AutoNetworkedField]
    public string ContentsStyle = "cucumber";

    [DataField]
    public float SlipChance = 0.25f;

    /// <summary>Chance per wine sip to apply permanent welder-style eye damage.</summary>
    [DataField]
    public float WineBlindChance = 0.1f;

    [DataField]
    public LocId PieceName = "pickle-piece-pickled";
}

[Serializable, NetSerializable]
public enum PickleJarVisuals : byte
{
    ProduceCount,
    HasProduce,
    ContentsStyle,
}
