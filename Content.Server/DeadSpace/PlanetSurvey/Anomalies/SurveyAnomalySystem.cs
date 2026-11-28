// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using System.Linq;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Electrocution;
using Content.Server.Lightning;
using Content.Server.Popups;
using Content.Server.Temperature.Systems;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DeadSpace.PlanetSurvey;
using Content.Shared.Emp;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.StatusEffectNew;
using Content.Shared.Temperature.Components;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.PlanetSurvey;

public sealed partial class SurveyAnomalySystem : EntitySystem
{
    private const string Sensor = "anomaly";
    private static readonly TimeSpan RevealTime = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan BoltReveal = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DetectorReveal = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan FlashTime = TimeSpan.FromSeconds(0.9);
    private static readonly TimeSpan RiftImmunity = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ScanGap = TimeSpan.FromSeconds(0.4);
    private static readonly TimeSpan HintGap = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan WatchGap = TimeSpan.FromSeconds(1);
    private const float WatchRange = 24f;
    private static readonly EntProtoId Drunk = "StatusEffectDrunk";
    private static readonly EntProtoId Rainbow = "StatusEffectSeeingRainbow";

    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private ElectrocutionSystem _electrocution = default!;
    [Dependency] private SharedEmpSystem _emp = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private LightningSystem _lightning = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private SurveyDirectorSystem _director = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private ThrowingSystem _throwing = default!;

    private readonly HashSet<EntityUid> _awake = new();
    private readonly List<EntityUid> _asleep = new();
    private readonly List<EntityUid> _snapshot = new();
    private readonly HashSet<Entity<MobStateComponent>> _mobs = new();
    private readonly HashSet<Entity<SurveyAnomalyComponent>> _near = new();
    private readonly HashSet<Entity<SurveyArtifactComponent>> _artifacts = new();
    private readonly List<EntityUid> _held = new();
    private readonly List<EntityUid> _movables = new();
    private readonly HashSet<Entity<Content.Shared.Item.ItemComponent>> _items = new();
    private readonly List<(MapId Map, Vector2 Pos)> _watchers = new();
    private TimeSpan _nextWatch;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SurveyAnomalyComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SurveyAnomalyComponent, StartCollideEvent>(OnStartCollide);
        SubscribeLocalEvent<SurveyAnomalyComponent, EndCollideEvent>(OnEndCollide);
        SubscribeLocalEvent<SurveyAnomalyComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<SurveyAnomalyComponent> ent, ref MapInitEvent args)
    {
        SetShown(ent, ent.Comp.Visible ? SurveyAnomalyState.Revealed : SurveyAnomalyState.Hidden, true);
        if (ent.Comp.Cycle <= TimeSpan.Zero)
            return;

        ent.Comp.Ready = _timing.CurTime + ent.Comp.Cycle * _random.NextDouble();
        _awake.Add(ent);
    }

    private void OnShutdown(Entity<SurveyAnomalyComponent> ent, ref ComponentShutdown args)
    {
        _awake.Remove(ent);
    }

    private void OnStartCollide(Entity<SurveyAnomalyComponent> ent, ref StartCollideEvent args)
    {
        if (args.OurFixtureId != Sensor)
            return;

        var other = args.OtherEntity;
        if (HasComp<SurveyMemberComponent>(other))
            return;

        if (HasComp<MobStateComponent>(other))
        {
            ent.Comp.Inside.Add(other);
            _awake.Add(ent);
            if (ent.Comp.Cycle <= TimeSpan.Zero)
                TryFire(ent, other);
            return;
        }

        if (!HasComp<ThrownItemComponent>(other))
            return;

        Reveal(ent, BoltReveal);
        if (ent.Comp.Cycle <= TimeSpan.Zero)
            TryFire(ent, other);
    }

    private void OnEndCollide(Entity<SurveyAnomalyComponent> ent, ref EndCollideEvent args)
    {
        if (args.OurFixtureId == Sensor)
            ent.Comp.Inside.Remove(args.OtherEntity);
    }

    public void Reveal(Entity<SurveyAnomalyComponent> ent, TimeSpan time)
    {
        var until = _timing.CurTime + time;
        if (until > ent.Comp.RevealedUntil)
            ent.Comp.RevealedUntil = until;

        if (ent.Comp.Shown == SurveyAnomalyState.Hidden)
            SetShown(ent, SurveyAnomalyState.Revealed);

        _awake.Add(ent);
    }

    private void SetShown(Entity<SurveyAnomalyComponent> ent, SurveyAnomalyState state, bool force = false)
    {
        if (!force && ent.Comp.Shown == state)
            return;

        ent.Comp.Shown = state;
        _appearance.SetData(ent, SurveyAnomalyVisuals.State, state);
    }

    private void TryFire(Entity<SurveyAnomalyComponent> ent, EntityUid? victim)
    {
        var now = _timing.CurTime;
        if (now < ent.Comp.Ready || ent.Comp.StrikeAt != null)
            return;

        if (victim is { } rifted && ent.Comp.Kind == SurveyAnomalyKind.Rift &&
            TryComp<SurveyRiftedComponent>(rifted, out var immune) && now < immune.Until)
        {
            return;
        }

        if (ent.Comp.Windup > TimeSpan.Zero)
        {
            BeginWindup(ent);
            return;
        }

        Strike(ent, victim);
    }

    private void BeginWindup(Entity<SurveyAnomalyComponent> ent)
    {
        ent.Comp.StrikeAt = _timing.CurTime + ent.Comp.Windup;
        SetShown(ent, SurveyAnomalyState.Windup);
        if (ent.Comp.WindupSound != null)
            _audio.PlayPvs(ent.Comp.WindupSound, ent);

        if (ent.Comp.Kind == SurveyAnomalyKind.Vortex)
            Pull(ent);
    }

    private void Strike(Entity<SurveyAnomalyComponent> ent, EntityUid? victim)
    {
        var now = _timing.CurTime;
        var comp = ent.Comp;
        comp.StrikeAt = null;
        comp.Ready = now + (comp.Cycle > TimeSpan.Zero ? comp.Cycle : comp.Cooldown);
        comp.ActiveUntil = now + FlashTime;
        if (!comp.Visible)
            Reveal(ent, RevealTime);
        if (comp.Kind != SurveyAnomalyKind.Push)
            SetShown(ent, SurveyAnomalyState.Active);
        _awake.Add(ent);

        var coords = Transform(ent).Coordinates;
        if (comp.Sound != null)
            _audio.PlayPvs(comp.Sound, ent);
        if (comp.Effect is { } effect)
            SpawnAtPosition(effect, coords);

        switch (comp.Kind)
        {
            case SurveyAnomalyKind.Burner:
                foreach (var mob in Victims(ent))
                {
                    Hurt(ent, mob);
                    if (TryComp<FlammableComponent>(mob, out var flammable))
                        _flammable.AdjustFireStacks(mob, comp.Power, flammable, true);
                }
                break;

            case SurveyAnomalyKind.Electra:
                foreach (var mob in Victims(ent))
                {
                    _lightning.ShootLightning(ent.Owner, mob.Owner, "Lightning", false);
                    _electrocution.TryDoElectrocution(mob.Owner, ent.Owner, (int) comp.Power, TimeSpan.FromSeconds(2), true);
                }
                break;

            case SurveyAnomalyKind.Vortex:
                Fling(ent, comp.Power, true);
                break;

            case SurveyAnomalyKind.Frost:
                foreach (var mob in Victims(ent))
                {
                    Hurt(ent, mob);
                    if (TryComp<TemperatureComponent>(mob, out var temperature))
                        _temperature.ChangeHeat(mob, -comp.Power * _temperature.GetHeatCapacity(mob, temperature), false, temperature);
                }
                break;

            case SurveyAnomalyKind.Jelly:
                foreach (var mob in Victims(ent))
                {
                    Hurt(ent, mob);
                }
                break;

            case SurveyAnomalyKind.Rift:
                if (victim is { } traveller && !TerminatingOrDeleted(traveller))
                    Teleport(ent, traveller);
                break;

            case SurveyAnomalyKind.Psi:
                foreach (var mob in Victims(ent))
                {
                    var time = TimeSpan.FromSeconds(12 * comp.Power);
                    _status.TryAddStatusEffectDuration(mob, Drunk, time);
                    _status.TryAddStatusEffectDuration(mob, Rainbow, time);
                    Hurt(ent, mob);
                    _popup.PopupEntity(Loc.GetString($"survey-psi-whisper-{_random.Next(1, 5)}"), mob, mob, PopupType.MediumCaution);
                }
                break;

            case SurveyAnomalyKind.Press:
                foreach (var mob in Victims(ent))
                {
                    Hurt(ent, mob);
                }
                break;

            case SurveyAnomalyKind.Steam:
                foreach (var mob in Victims(ent))
                {
                    Hurt(ent, mob);
                }
                Fling(ent, comp.Power, false);
                break;

            case SurveyAnomalyKind.Laser:
                foreach (var mob in Victims(ent))
                {
                    Hurt(ent, mob);
                }
                RevealLine(ent);
                if (comp.Planet is { } planet)
                    _director.Tripwire(planet, _xform.GetWorldPosition(ent));
                break;

            case SurveyAnomalyKind.Magnet:
                Disarm(ent);
                break;

            case SurveyAnomalyKind.Springboard:
                foreach (var uid in Movables(ent))
                {
                    if (HasComp<MobStateComponent>(uid))
                        Hurt(ent, uid);
                    _throwing.TryThrow(uid, _random.NextVector2(comp.Power * 0.7f, comp.Power), 16f, ent.Owner, recoil: false);
                }
                break;

            case SurveyAnomalyKind.Emp:
                _emp.EmpPulse(_xform.GetMapCoordinates(ent), comp.Radius, comp.Power, TimeSpan.FromSeconds(8), ent.Owner);
                break;

            case SurveyAnomalyKind.Push:
                var heading = _xform.GetWorldRotation(ent).ToWorldVec() * comp.Power;
                foreach (var uid in Movables(ent))
                {
                    _throwing.TryThrow(uid, heading, 6f, ent.Owner, recoil: false);
                }
                break;
        }
    }

    private HashSet<Entity<MobStateComponent>> Victims(Entity<SurveyAnomalyComponent> ent)
    {
        _mobs.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Radius, _mobs, LookupFlags.Uncontained);
        _mobs.RemoveWhere(m => HasComp<SurveyMemberComponent>(m));
        return _mobs;
    }

    private void Hurt(Entity<SurveyAnomalyComponent> ent, EntityUid mob)
    {
        if (ent.Comp.Damage is { } damage)
            _damage.TryChangeDamage(mob, damage, origin: ent.Owner, interruptsDoAfters: true);
    }

    private List<EntityUid> Movables(Entity<SurveyAnomalyComponent> ent)
    {
        _movables.Clear();
        foreach (var mob in Victims(ent))
        {
            _movables.Add(mob.Owner);
        }

        _items.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, ent.Comp.Radius, _items, LookupFlags.Dynamic | LookupFlags.Sundries);
        foreach (var item in _items)
        {
            if (!Transform(item).Anchored)
                _movables.Add(item.Owner);
        }

        return _movables;
    }

    private void Pull(Entity<SurveyAnomalyComponent> ent)
    {
        var centre = _xform.GetWorldPosition(ent);
        foreach (var uid in Movables(ent))
        {
            var towards = centre - _xform.GetWorldPosition(uid);
            if (towards.LengthSquared() > 0.04f)
                _throwing.TryThrow(uid, towards, 4f, ent.Owner, recoil: false);
        }
    }

    private void Fling(Entity<SurveyAnomalyComponent> ent, float distance, bool hurt)
    {
        var centre = _xform.GetWorldPosition(ent);
        foreach (var uid in Movables(ent))
        {
            var away = _xform.GetWorldPosition(uid) - centre;
            if (away.LengthSquared() < 0.04f)
                away = _random.NextVector2(1f);

            if (hurt && HasComp<MobStateComponent>(uid))
                Hurt(ent, uid);
            _throwing.TryThrow(uid, Vector2.Normalize(away) * distance, 12f, ent.Owner, recoil: false);
        }
    }

    private void Disarm(Entity<SurveyAnomalyComponent> ent)
    {
        var centre = _xform.GetWorldPosition(ent);
        foreach (var mob in Victims(ent))
        {
            _held.Clear();
            foreach (var item in _hands.EnumerateHeld(mob.Owner))
            {
                if (HasComp<GunComponent>(item))
                    _held.Add(item);
            }

            foreach (var item in _held)
            {
                if (!_hands.TryDrop(mob.Owner, item, checkActionBlocker: false))
                    continue;

                var towards = centre - _xform.GetWorldPosition(item);
                _throwing.TryThrow(item, towards, 10f, ent.Owner, recoil: false);
            }

            if (_held.Count > 0)
                _popup.PopupEntity(Loc.GetString("survey-magnet-disarm"), mob, mob, PopupType.MediumCaution);
        }
    }

    private void RevealLine(Entity<SurveyAnomalyComponent> ent)
    {
        _near.Clear();
        _lookup.GetEntitiesInRange(Transform(ent).Coordinates, 6f, _near, LookupFlags.Static | LookupFlags.Sensors);
        foreach (var other in _near)
        {
            if (other.Comp.Kind == SurveyAnomalyKind.Laser)
                Reveal(other, RevealTime);
        }
    }

    private void Teleport(Entity<SurveyAnomalyComponent> ent, EntityUid traveller)
    {
        var xform = Transform(ent);
        Vector2 target;
        if (ent.Comp.Pair is { } pair && !TerminatingOrDeleted(pair))
        {
            target = _xform.GetWorldPosition(pair) + _random.NextVector2(1.2f, 1.6f);
            if (TryComp<SurveyAnomalyComponent>(pair, out var other))
                Reveal((pair, other), RevealTime);
        }
        else
        {
            target = _xform.GetWorldPosition(xform) + _random.NextVector2(8f, 14f);
        }

        EnsureComp<SurveyRiftedComponent>(traveller).Until = _timing.CurTime + RiftImmunity;
        if (xform.MapUid is not { } map)
            return;

        _xform.SetCoordinates(traveller, new EntityCoordinates(map, target));
        _xform.AttachToGridOrMap(traveller);
        if (ent.Comp.Sound != null)
            _audio.PlayPvs(ent.Comp.Sound, traveller);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        RefreshWatchers(now);

        _asleep.Clear();
        _snapshot.Clear();
        _snapshot.AddRange(_awake);
        foreach (var uid in _snapshot)
        {
            if (!TryComp<SurveyAnomalyComponent>(uid, out var comp) || TerminatingOrDeleted(uid))
            {
                _asleep.Add(uid);
                continue;
            }

            if (Paused(uid))
                continue;

            Entity<SurveyAnomalyComponent> ent = (uid, comp);
            comp.Inside.RemoveWhere(e => TerminatingOrDeleted(e));

            if (comp.StrikeAt is { } at)
            {
                if (now >= at)
                    Strike(ent, comp.Inside.FirstOrDefault());
            }
            else if (now >= comp.Ready)
            {
                if (comp.Cycle > TimeSpan.Zero && !Watched(uid))
                {
                    comp.Ready = now + comp.Cycle;
                }
                else if (comp.Cycle > TimeSpan.Zero)
                {
                    if (comp.Windup > TimeSpan.Zero)
                        BeginWindup(ent);
                    else
                        Strike(ent, null);
                }
                else if (comp.Inside.Count > 0 && comp.Kind != SurveyAnomalyKind.Rift)
                {
                    TryFire(ent, comp.Inside.First());
                }
            }

            if (comp.Shown == SurveyAnomalyState.Active && comp.StrikeAt == null && now >= comp.ActiveUntil)
                SetShown(ent, comp.Visible || now < comp.RevealedUntil ? SurveyAnomalyState.Revealed : SurveyAnomalyState.Hidden);
            else if (comp.Shown == SurveyAnomalyState.Revealed && !comp.Visible && now >= comp.RevealedUntil)
                SetShown(ent, SurveyAnomalyState.Hidden);

            var idle = comp.Cycle <= TimeSpan.Zero && comp.Inside.Count == 0 && comp.StrikeAt == null &&
                       (comp.Shown == SurveyAnomalyState.Hidden || comp.Visible && comp.Shown == SurveyAnomalyState.Revealed);
            if (idle)
                _asleep.Add(uid);
        }

        foreach (var uid in _asleep)
        {
            _awake.Remove(uid);
        }

        UpdateDetectors(now);
    }

    private void RefreshWatchers(TimeSpan now)
    {
        if (now < _nextWatch)
            return;

        _nextWatch = now + WatchGap;
        _watchers.Clear();
        var query = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapUid != null)
                _watchers.Add((xform.MapID, _xform.GetWorldPosition(xform)));
        }
    }

    private bool Watched(EntityUid uid)
    {
        var xform = Transform(uid);
        var pos = _xform.GetWorldPosition(xform);
        foreach (var (map, at) in _watchers)
        {
            if (map == xform.MapID && Vector2.DistanceSquared(at, pos) < WatchRange * WatchRange)
                return true;
        }

        return false;
    }

    private void UpdateDetectors(TimeSpan now)
    {
        var query = EntityQueryEnumerator<SurveyDetectorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var detector, out var xform))
        {
            if (now < detector.NextScan)
                continue;

            detector.NextScan = now + ScanGap;
            var holder = xform.ParentUid;
            if (!holder.IsValid() || !_hands.IsHolding(holder, uid))
                continue;

            var at = _xform.GetMapCoordinates(holder);
            var nearest = float.MaxValue;
            _near.Clear();
            _lookup.GetEntitiesInRange(at, detector.Range, _near, LookupFlags.Static | LookupFlags.Sensors);
            foreach (var anomaly in _near)
            {
                var d = Vector2.Distance(_xform.GetWorldPosition(anomaly), at.Position);
                nearest = MathF.Min(nearest, d);
                if (d <= detector.RevealRange)
                    Reveal(anomaly, DetectorReveal);
            }

            if (nearest < detector.Range && now >= detector.NextBeep)
            {
                var gap = 0.12f + 1.5f * Math.Clamp(nearest / detector.Range, 0f, 1f);
                detector.NextBeep = now + TimeSpan.FromSeconds(gap);
                _audio.PlayEntity(detector.Beep, Filter.Entities(holder), uid, false);
            }

            if (now < detector.NextHint)
                continue;

            detector.NextHint = now + HintGap;
            _artifacts.Clear();
            _lookup.GetEntitiesInRange(at, detector.ArtifactRange, _artifacts, LookupFlags.Uncontained);
            Vector2? best = null;
            foreach (var artifact in _artifacts)
            {
                if (artifact.Comp.Taken)
                    continue;

                var offset = _xform.GetWorldPosition(artifact) - at.Position;
                if (best == null || offset.LengthSquared() < best.Value.LengthSquared())
                    best = offset;
            }

            if (best is { } found)
            {
                _popup.PopupEntity(Loc.GetString("survey-detector-artifact",
                    ("distance", (int) MathF.Round(found.Length())),
                    ("direction", Loc.GetString($"survey-detector-dir-{Compass(found)}"))), holder, holder);
            }
        }
    }

    private static string Compass(Vector2 offset)
    {
        var angle = MathF.Atan2(offset.Y, offset.X);
        var sector = (int) MathF.Round(angle / (MathF.PI / 4f));
        return (((sector % 8) + 8) % 8) switch
        {
            0 => "e",
            1 => "ne",
            2 => "n",
            3 => "nw",
            4 => "w",
            5 => "sw",
            6 => "s",
            _ => "se",
        };
    }
}
