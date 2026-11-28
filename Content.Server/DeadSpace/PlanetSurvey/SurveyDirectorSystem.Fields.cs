// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.Parallax.Biomes;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.DeadSpace.PlanetSurvey;

public sealed partial class SurveyDirectorSystem
{
    private const float FieldClearance = 14f;
    private const float WallChance = 0.85f;
    private const float PathHazardChance = 0.2f;

    private static readonly Vector2i[] FieldSteps = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    private readonly HashSet<Vector2i> _safe = new();
    private readonly List<Vector2i> _path = new();
    private readonly List<EntityUid> _rifts = new();

    private void SpawnFields(Entity<SurveyDirectorComponent> ent, SurveyWorldPrototype world, System.Random random)
    {
        var director = ent.Comp;
        if (world.FieldCount <= 0 || world.Anomalies.Count == 0)
            return;

        _players.Clear();

        var half = world.FieldSize * 0.5f;
        var inner = director.BaseRadius + half + 10f;
        var outer = Math.Max(inner + 4f, director.ArenaRadius - half - 4f);

        for (var i = 0; i < world.FieldCount; i++)
        {
            var dir = Around(random, i, world.FieldCount);
            var heading = MathF.Atan2(dir.Y, dir.X);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var angle = heading + (attempt % 2 == 0 ? 1 : -1) * attempt * 0.3f;
                var distance = inner + (outer - inner) * (float) random.NextDouble();
                var pos = director.BaseCentre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
                if (!FieldClear(director, pos, world.FieldSize))
                    continue;

                pos = Grounded(ent.Owner, pos);
                director.Fields.Add(new SurveyField { Pos = pos, Size = world.FieldSize });
                AddPoi(ent, pos, SurveyPoiKind.Anomaly);
                break;
            }
        }

        var ordered = director.Fields.OrderByDescending(f => Vector2.DistanceSquared(f.Pos, director.Landing)).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Rare = i < ordered.Count / 2;
        }

        foreach (var field in director.Fields)
        {
            Carve(ent, field, FieldOrigin(field), random);
            field.Artifact = SpawnFieldArtifact(ent, world, field, random);
        }

        director.ShiftAt = _timing.CurTime + world.ShiftGap;
        if (HasComp<BiomeComponent>(ent))
            director.FieldsPending = true;
        else
            FillFields(ent, world, random);
    }

    private static Vector2i FieldOrigin(SurveyField field)
    {
        var half = field.Size / 2;
        return new Vector2i((int) MathF.Floor(field.Pos.X) - half, (int) MathF.Floor(field.Pos.Y) - half);
    }

    private void FieldsBaked(Entity<SurveyDirectorComponent> ent)
    {
        if (!ent.Comp.FieldsPending || !_proto.TryIndex(ent.Comp.World, out var world))
            return;

        ent.Comp.FieldsPending = false;
        FillFields(ent, world, new System.Random(_random.Next()));
    }

    private bool FieldClear(SurveyDirectorComponent director, Vector2 pos, int size)
    {
        var room = size * 0.75f + FieldClearance;
        if (NearPad(director, pos, room) ||
            Vector2.Distance(pos, director.BaseCentre) < director.BaseRadius + size ||
            Vector2.Distance(pos, director.BaseCentre) > director.ArenaRadius - size * 0.5f)
        {
            return false;
        }

        foreach (var outpost in director.Outposts)
        {
            if (Vector2.Distance(outpost.Pos, pos) < room)
                return false;
        }

        foreach (var field in director.Fields)
        {
            if (Vector2.Distance(field.Pos, pos) < field.Size + size)
                return false;
        }

        return true;
    }

    private void FillFields(Entity<SurveyDirectorComponent> ent, SurveyWorldPrototype world, System.Random random)
    {
        _rifts.Clear();
        foreach (var field in ent.Comp.Fields)
        {
            foreach (var uid in field.Anomalies)
            {
                if (!TerminatingOrDeleted(uid))
                    Del(uid);
            }

            field.Anomalies.Clear();
            Lay(ent, world, field, random);

            if (field.Artifact is not { } artifact || TerminatingOrDeleted(artifact) ||
                TryComp<SurveyArtifactComponent>(artifact, out var old) && old.Taken)
            {
                field.Artifact = SpawnFieldArtifact(ent, world, field, random);
            }
        }

        for (var i = 0; i < _rifts.Count; i++)
        {
            var partner = _rifts[(i + 1 + random.Next(Math.Max(1, _rifts.Count - 1))) % _rifts.Count];
            if (partner != _rifts[i] && TryComp<SurveyAnomalyComponent>(_rifts[i], out var rift))
                rift.Pair = partner;
        }
    }

    private void Lay(Entity<SurveyDirectorComponent> ent, SurveyWorldPrototype world, SurveyField field, System.Random random)
    {
        var map = ent.Owner;
        if (!TryComp<MapGridComponent>(map, out var grid))
            return;

        var size = field.Size;
        var origin = FieldOrigin(field);
        if (field.Safe.Count == 0)
            Carve(ent, field, origin, random);

        for (var x = 0; x < size; x++)
        {
            for (var y = 0; y < size; y++)
            {
                var local = new Vector2i(x, y);
                if (field.Safe.Contains(local))
                    continue;

                var wall = false;
                foreach (var step in FieldSteps)
                {
                    if (field.Safe.Contains(local + step))
                    {
                        wall = true;
                        break;
                    }
                }

                if (random.NextDouble() >= (wall ? WallChance : world.FieldDensity))
                    continue;

                PlaceHazard(ent, grid, field, origin + local, world.Anomalies[random.Next(world.Anomalies.Count)], random);
            }
        }

        if (world.PathHazards.Count == 0)
            return;

        for (var i = 2; i < field.Path.Count - 2; i++)
        {
            if (random.NextDouble() < PathHazardChance)
                PlaceHazard(ent, grid, field, origin + field.Path[i], world.PathHazards[random.Next(world.PathHazards.Count)], random);
        }
    }

    private void Carve(Entity<SurveyDirectorComponent> ent, SurveyField field, Vector2i origin, System.Random random)
    {
        var size = field.Size;
        var half = size / 2;
        var goal = new Vector2i(half, half);
        _safe.Clear();
        _path.Clear();
        var cell = Entry(ent.Comp.Landing - field.Pos, size, half);
        _safe.Add(cell);
        _path.Add(cell);
        for (var steps = 0; cell != goal && steps < size * 8; steps++)
        {
            var towards = goal - cell;
            Vector2i step;
            if (random.NextDouble() < 0.62)
            {
                step = Math.Abs(towards.X) >= Math.Abs(towards.Y)
                    ? new Vector2i(Math.Sign(towards.X), 0)
                    : new Vector2i(0, Math.Sign(towards.Y));
            }
            else
            {
                step = FieldSteps[random.Next(FieldSteps.Length)];
            }

            var next = cell + step;
            if (next.X < 0 || next.Y < 0 || next.X >= size || next.Y >= size)
                continue;

            cell = next;
            if (_safe.Add(cell))
                _path.Add(cell);
        }

        _safe.Add(goal);
        for (var branch = 0; branch < 2 && _path.Count > 3; branch++)
        {
            var spur = _path[random.Next(1, _path.Count - 1)];
            for (var i = 0; i < 3 + random.Next(3); i++)
            {
                var next = spur + FieldSteps[random.Next(FieldSteps.Length)];
                if (next.X < 0 || next.Y < 0 || next.X >= size || next.Y >= size)
                    break;

                spur = next;
                _safe.Add(spur);
            }
        }

        field.Safe.UnionWith(_safe);
        field.Path.AddRange(_path);
        foreach (var local in field.Safe)
        {
            var tile = origin + local;
            _reserved.Clear();
            _biome.ReserveTiles(ent.Owner, new Box2(tile.X + 0.1f, tile.Y + 0.1f, tile.X + 0.9f, tile.Y + 0.9f), _reserved);
        }

        _reserved.Clear();
    }

    private static Vector2i Entry(Vector2 towards, int size, int half)
    {
        if (towards.LengthSquared() < 0.01f)
            return new Vector2i(half, 0);

        var dir = Vector2.Normalize(towards);
        if (MathF.Abs(dir.X) >= MathF.Abs(dir.Y))
        {
            var y = Math.Clamp(half + (int) MathF.Round(dir.Y / MathF.Abs(dir.X) * half), 0, size - 1);
            return new Vector2i(dir.X > 0 ? size - 1 : 0, y);
        }

        var x = Math.Clamp(half + (int) MathF.Round(dir.X / MathF.Abs(dir.Y) * half), 0, size - 1);
        return new Vector2i(x, dir.Y > 0 ? size - 1 : 0);
    }

    private void PlaceHazard(Entity<SurveyDirectorComponent> ent, MapGridComponent grid, SurveyField field, Vector2i tile, Robust.Shared.Prototypes.EntProtoId proto, System.Random random)
    {
        var map = ent.Owner;
        if (!_map.TryGetTileRef(map, grid, tile, out var tileRef) || tileRef.Tile.IsEmpty ||
            !_anchorable.TileFree((map, grid), tile, Blocking, Blocking) || Blocked(map, tile))
        {
            return;
        }

        var centre = tile + new Vector2(0.5f, 0.5f);
        foreach (var (_, pos) in _players)
        {
            if (Vector2.DistanceSquared(pos, centre) < 2.25f)
                return;
        }

        var uid = SpawnAtPosition(proto, new EntityCoordinates(map, centre));
        if (!TryComp<SurveyAnomalyComponent>(uid, out var anomaly))
            return;

        anomaly.Planet = map;
        field.Anomalies.Add(uid);
        if (anomaly.Kind == SurveyAnomalyKind.Rift)
            _rifts.Add(uid);
        else if (anomaly.Kind == SurveyAnomalyKind.Push)
            _xform.SetLocalRotation(uid, Angle.FromDegrees(90 * random.Next(4)));
    }

    private EntityUid? SpawnFieldArtifact(Entity<SurveyDirectorComponent> ent, SurveyWorldPrototype world, SurveyField field, System.Random random)
    {
        var pool = field.Rare && world.RareArtifacts.Count > 0 ? world.RareArtifacts : world.Artifacts;
        if (pool.Count == 0)
            return null;

        var centre = new Vector2(MathF.Floor(field.Pos.X) + 0.5f, MathF.Floor(field.Pos.Y) + 0.5f);
        var uid = SpawnAtPosition(pool[random.Next(pool.Count)], new EntityCoordinates(ent.Owner, centre));
        if (TryComp<SurveyArtifactComponent>(uid, out var artifact))
            artifact.Planet = ent.Owner;
        return uid;
    }

    public void ShiftFields(Entity<SurveyDirectorComponent> ent, SurveyWorldPrototype world)
    {
        if (ent.Comp.Fields.Count == 0 || ent.Comp.FieldsPending)
            return;

        FillFields(ent, world, new System.Random(_random.Next()));
        ent.Comp.ShiftAt = _timing.CurTime + world.ShiftGap;
        Warn(ent.Owner, Loc.GetString(world.FactoryHalls ? "survey-fields-shift-factory" : "survey-fields-shift"));
    }

    private void TickFields(Entity<SurveyDirectorComponent> ent)
    {
        if (ent.Comp.Fields.Count == 0 || _timing.CurTime < ent.Comp.ShiftAt ||
            !_proto.TryIndex(ent.Comp.World, out var world) || world.Storm != null)
        {
            return;
        }

        ShiftFields(ent, world);
    }

    public void ArtifactTaken(EntityUid planet, EntityUid artifact)
    {
        if (!TryComp<SurveyDirectorComponent>(planet, out var director))
            return;

        foreach (var field in director.Fields)
        {
            if (field.Artifact == artifact)
                field.Artifact = null;
        }

        Progress(planet, "artifact");
    }

    public void Tripwire(EntityUid planet, Vector2 pos)
    {
        if (!TryComp<SurveyDirectorComponent>(planet, out var director))
            return;

        SurveySquad? best = null;
        var bestDist = float.MaxValue;
        foreach (var squad in director.Squads)
        {
            if (squad.Alive == 0)
                continue;

            var d = Vector2.DistanceSquared(squad.Home.Position, pos);
            if (d >= bestDist)
                continue;

            bestDist = d;
            best = squad;
        }

        if (best != null)
            RaiseAlarm((planet, director), best);
    }
}
