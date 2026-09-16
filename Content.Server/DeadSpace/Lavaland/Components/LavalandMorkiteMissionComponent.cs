// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using Robust.Shared.Prototypes;

namespace Content.Server.DeadSpace.Lavaland.Components;

[RegisterComponent]
public sealed partial class LavalandMorkiteMissionComponent : Component
{
    [DataField] public EntProtoId ExtractorPrototype = "LavalandMorkiteExtractor";
    [DataField] public int ExtractorCount = 4;
    [DataField] public float SpawnDistance = 40f;
    [DataField] public float MorkiteChance = 0.5f;
    [DataField] public EntProtoId OreObjective = "LavalandObjectivePaperOre";
    [DataField] public EntProtoId MorkiteObjective = "LavalandObjectivePaperMorkite";
    public bool IsMorkite;
    public bool Spawned;
}

[RegisterComponent]
public sealed partial class LavalandMorkiteExtractorComponent : Component
{
    [DataField] public TimeSpan RequiredExtractionTime = TimeSpan.FromMinutes(8);
    [DataField] public float OutputPerSecond = 100f / 480f;
}
