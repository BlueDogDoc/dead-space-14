// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Shared.DeadSpace.PlanetSurvey;
using Content.Shared.DoAfter;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace.PlanetSurvey;

public sealed partial class SurveyDirectorSystem
{
    private static readonly TimeSpan PatrolCheck = TimeSpan.FromSeconds(5);
    private const float PatrolReach = 6f;
    private const int PatrolGiveUp = 24;
    private const int Sectors = 8;

    private void InitializeSurveying()
    {
        SubscribeLocalEvent<SurveyBeaconComponent, UseInHandEvent>(OnBeaconUse);
        SubscribeLocalEvent<SurveyBeaconComponent, SurveyBeaconDoAfterEvent>(OnBeaconDeployed);
    }

    private void SetupPatrols(Entity<SurveyDirectorComponent> ent, System.Random random)
    {
        var director = ent.Comp;
        if (director.Kind is not (SurveyKind.Raiders or SurveyKind.Pirates or SurveyKind.Beasts))
            return;

        var stops = new List<Vector2>();
        foreach (var field in director.Fields)
        {
            var outward = field.Pos - director.BaseCentre;
            if (outward.LengthSquared() < 0.01f)
                continue;

            stops.Add(field.Pos - Vector2.Normalize(outward) * (field.Size * 0.5f + 4f));
        }

        foreach (var outpost in director.Outposts)
        {
            stops.Add(outpost.Pos);
        }

        var ring = director.BaseRadius + 16f;
        while (stops.Count < 3)
        {
            stops.Add(director.BaseCentre + Around(random, stops.Count, 3) * ring);
        }

        var candidates = director.Squads
            .Where(s => !s.Ambush && s.Size >= 2 && s.Members.Count > 0 &&
                        !(TryComp<SurveyMemberComponent>(s.Members[0], out var member) && member.Prey))
            .OrderBy(_ => random.Next())
            .Take(Math.Clamp(director.Squads.Count / 3, 1, 3))
            .ToList();

        foreach (var squad in candidates)
        {
            var patrol = new SurveyPatrol { Squad = squad };
            patrol.Route.AddRange(stops.OrderBy(_ => random.Next()).Take(3));
            patrol.Route.Add(squad.Home.Position);
            director.Patrols.Add(patrol);
        }
    }

    private void TickPatrols(Entity<SurveyDirectorComponent> ent)
    {
        var now = _timing.CurTime;
        foreach (var patrol in ent.Comp.Patrols)
        {
            var squad = patrol.Squad;
            if (squad.Alive == 0 || squad.Withdrawn || squad.Engaged || patrol.Route.Count == 0 || now < patrol.Next)
                continue;

            patrol.Next = now + PatrolCheck;
            var centre = Centroid(squad, out var alive);
            if (alive == 0)
                continue;

            var target = patrol.Route[patrol.Leg];
            if (patrol.Issued && Vector2.Distance(centre, target) > PatrolReach && ++patrol.Checks < PatrolGiveUp)
                continue;

            if (patrol.Issued)
            {
                patrol.Leg = (patrol.Leg + 1) % patrol.Route.Count;
                target = patrol.Route[patrol.Leg];
                if (_random.Prob(0.35f))
                    Radio(ent, Speaker(squad), "patrol", ("where", Where(ent.Comp, centre)));
            }

            patrol.Issued = true;
            patrol.Checks = 0;
            var index = 0;
            foreach (var member in squad.Members)
            {
                if (!Alive(member) || Board(member) is not { } board)
                    continue;

                var offset = new Vector2(index % 2 == 0 ? 1.2f : -1.2f, (index / 2) * 1.2f);
                board.SetValue(SurveyKeys.Post, At(ent.Owner, Walkable(ent.Owner, target + offset, 2)));
                Replan(member);
                index++;
            }
        }
    }

    private int SectorOf(SurveyDirectorComponent director, Vector2 pos)
    {
        var offset = pos - director.BaseCentre;
        var angle = MathF.Atan2(offset.Y, offset.X);
        var slice = ((int) MathF.Floor((angle + MathF.PI) / MathF.Tau * Sectors) % Sectors + Sectors) % Sectors;
        var outer = offset.Length() > director.ArenaRadius * 0.55f ? 1 : 0;
        return outer * Sectors + slice;
    }

    private void OnBeaconUse(Entity<SurveyBeaconComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = args.User;
        if (Transform(user).MapUid is not { } map || !TryComp<SurveyDirectorComponent>(map, out var director))
        {
            _popup.PopupEntity(Loc.GetString("survey-beacon-offworld"), user, user);
            return;
        }

        if (director.Surveyed.Contains(SectorOf(director, Pos(user))))
        {
            _popup.PopupEntity(Loc.GetString("survey-beacon-taken"), user, user);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(ent.Comp.Time), new SurveyBeaconDoAfterEvent(), ent, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnBeaconDeployed(Entity<SurveyBeaconComponent> ent, ref SurveyBeaconDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        var user = args.User;
        if (Transform(user).MapUid is not { } map || !TryComp<SurveyDirectorComponent>(map, out var director))
            return;

        var pos = Pos(user);
        var sector = SectorOf(director, pos);
        if (director.Surveyed.Contains(sector))
        {
            _popup.PopupEntity(Loc.GetString("survey-beacon-taken"), user, user);
            return;
        }

        args.Handled = true;
        var beacon = SpawnAtPosition(ent.Comp.Deployed, new EntityCoordinates(map, At(map, pos).Position));
        if (TryComp<SurveyBeaconDeployedComponent>(beacon, out var deployed))
        {
            deployed.Deployer = user;
            deployed.Sector = sector;
            deployed.DoneAt = _timing.CurTime + deployed.Duration;
        }

        QueueDel(ent);
        _popup.PopupEntity(Loc.GetString("survey-beacon-start", ("seconds", (int) (deployed?.Duration.TotalSeconds ?? 45))), user, user, PopupType.Medium);
        Tripwire(map, pos);
        Radio((map, director), null, "beacon", ("where", Where(director, pos)));
    }

    private void TickBeacons(Entity<SurveyDirectorComponent> ent)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SurveyBeaconDeployedComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var beacon, out var xform))
        {
            if (beacon.Done || xform.MapUid != ent.Owner || now < beacon.DoneAt)
                continue;

            beacon.Done = true;
            if (beacon.Sector < 0 || !ent.Comp.Surveyed.Add(beacon.Sector))
                continue;

            Pay(ent.Comp.Station, beacon.StationReward);
            if (beacon.Deployer is { } owner && !TerminatingOrDeleted(owner))
            {
                GiveCash(owner, beacon.Reward);
                _popup.PopupEntity(Loc.GetString("survey-beacon-done", ("money", beacon.Reward)), owner, owner, PopupType.Medium);
            }

            _popup.PopupEntity(Loc.GetString("survey-beacon-done-local"), uid);
            Progress(ent.Owner, "sectors");
        }
    }
}
