// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT
using Content.Server.DeadSpace.Lavaland.Components;
using Robust.Shared.Map;
using System.Numerics;
using Content.Server.GameTicking.Events;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace.Lavaland;

public sealed class LavalandMorkiteMissionSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    private float _objectiveRoll;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartingEvent>(_ => _objectiveRoll = _random.NextFloat());
        SubscribeLocalEvent<LavalandMorkiteMissionComponent, MapInitEvent>(OnMissionMapInit);
    }

    private void OnMissionMapInit(Entity<LavalandMorkiteMissionComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.IsMorkite = _objectiveRoll < ent.Comp.MorkiteChance;
        var paper = Spawn(ent.Comp.IsMorkite ? ent.Comp.MorkiteObjective : ent.Comp.OreObjective,
            Transform(ent).Coordinates);
        var container = _containers.EnsureContainer<Container>(ent, "storagebase");
        _containers.Insert(paper, container);
    }

    public override void Update(float frameTime)
    {
        var missions = EntityQueryEnumerator<LavalandMorkiteMissionComponent>();
        while (missions.MoveNext(out _, out var mission))
        {
            if (!mission.IsMorkite || mission.Spawned || !TryFindLavalandOutpost(out var map, out var outpost))
                continue;
            mission.Spawned = true;
            var center = _transform.GetMapCoordinates(outpost).Position;
            var baseRadius = 0f;
            if (TryComp<MapGridComponent>(outpost, out var baseGrid))
            {
                var min = new Vector2(float.MaxValue);
                var max = new Vector2(float.MinValue);
                foreach (var tile in _map.GetAllTiles(outpost, baseGrid))
                {
                    if (tile.Tile.IsEmpty)
                        continue;
                    var position = _transform.ToMapCoordinates(_map.ToCenterCoordinates(tile)).Position;
                    min = Vector2.Min(min, position);
                    max = Vector2.Max(max, position);
                }
                if (min.X != float.MaxValue)
                {
                    center = (min + max) / 2;
                    baseRadius = Vector2.Distance(min, max) / 2 + 1f;
                }
            }
            var count = Math.Max(1, mission.ExtractorCount);
            for (var i = 0; i < count; i++)
            {
                var angle = Angle.FromDegrees(360f * i / count + _random.NextFloat(0f, 360f / count));
                var distance = baseRadius + Math.Max(40f, mission.SpawnDistance) + _random.NextFloat(0f, 20f);
                var coordinates = new EntityCoordinates(map, center + angle.ToWorldVec() * distance);
                var extractor = Spawn(mission.ExtractorPrototype, coordinates);
                var extractorTransform = Transform(extractor);
                if (!extractorTransform.Anchored)
                    _transform.AnchorEntity(extractor, extractorTransform);
            }
        }
    }

    private bool TryFindLavalandOutpost(out EntityUid map, out EntityUid outpost)
    {
        var query = EntityQueryEnumerator<LavalandOutpostComponent>();
        while (query.MoveNext(out var uid, out var marker))
        {
            if (!Exists(marker.Map))
                continue;
            map = marker.Map;
            outpost = uid;
            return true;
        }
        map = default;
        outpost = default;
        return false;
    }
}
