// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using System.Numerics;
using Content.Server.DeadSpace.Lavaland.Components;
using Content.Server.Weapons.Ranged.Systems;
using Content.Shared.Chasm;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.DeadSpace.Lavaland.Bosses;
using Content.Shared.DeadSpace.Lavaland.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.MouseRotator;
using Content.Shared.Physics;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Lavaland;

public sealed class LavalandMadMinerSystem : EntitySystem
{
    private static readonly float[] ShotgunSpread = [-18f, -9f, 0f, 9f, 18f];

    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly GunSystem _gun = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private int _nextHudId = 100000;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LavalandMadMinerComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<LavalandMadMinerComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<ChasmFallingAttemptEvent>(OnChasmFalling);
    }

    private void OnMapInit(Entity<LavalandMadMinerComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.HudId = _nextHudId++;
        ent.Comp.SpawnCoordinates = _transform.GetMapCoordinates(ent.Owner);
        ent.Comp.SelectedMusic = ent.Comp.Music.Count == 0 ? null : _random.Pick(ent.Comp.Music);
        BuildPlatform(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<LavalandMadMinerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var miner, out var xform))
        {
            if (miner.NextThink > now || IsDead(uid))
                continue;
            miner.NextThink = now + TimeSpan.FromSeconds(0.1);

            if (!TryPickTarget(uid, miner, xform, out var target))
            {
                if (miner.Active)
                    EndFight(miner);
                continue;
            }

            miner.Target = target;
            if (!miner.Active)
                StartFight(uid, miner, now);

            ProcessHook(uid, miner, target, now);
            var from = _transform.GetWorldPosition(xform);
            var to = _transform.GetWorldPosition(target);
            var distance = Vector2.Distance(from, to);

            if (distance > miner.BlinkDistance && now >= miner.NextBlink)
                BlinkBehind(uid, miner, target, now);
            else if (distance <= 9f && now >= miner.NextFeint)
                Feint(uid, miner, target, now);

            if (now >= miner.NextHook && distance <= 14f)
                BeginHook(uid, miner, target, now);
            else if (now >= miner.NextShot && distance <= 16f)
                FirePattern(uid, miner, target, now);

            SendHud(uid, miner);
        }
    }

    private bool TryPickTarget(EntityUid uid, LavalandMadMinerComponent miner, TransformComponent xform, out EntityUid target)
    {
        target = default;
        var best = float.MaxValue;
        var origin = _transform.GetWorldPosition(xform);
        var range = miner.Active ? miner.ChaseRange : miner.NoticeRange;
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } player || IsDead(player) ||
                Transform(player).MapID != xform.MapID)
                continue;
            var distance = Vector2.DistanceSquared(origin, _transform.GetWorldPosition(player));
            if (distance > range * range || distance >= best)
                continue;
            best = distance;
            target = player;
        }
        return target.IsValid();
    }

    private void StartFight(EntityUid uid, LavalandMadMinerComponent miner, TimeSpan now)
    {
        miner.Active = true;
        miner.NextBlink = now + TimeSpan.FromSeconds(0.5);
        miner.NextShot = now + TimeSpan.FromSeconds(1);
        miner.NextHook = now + TimeSpan.FromSeconds(2);
        foreach (var session in GetNearbySessions(uid, miner.ChaseRange))
        {
            if (miner.SelectedMusic != null)
                RaiseNetworkEvent(new LavalandBossMusicStartEvent(miner.HudId,
                    _audio.ResolveSound(miner.SelectedMusic), miner.SelectedMusic.Params.WithLoop(true)), session.Channel);
        }
    }

    private void EndFight(LavalandMadMinerComponent miner)
    {
        miner.Active = false;
        miner.Target = null;
        miner.HookTarget = null;
        foreach (var session in _players.Sessions)
        {
            RaiseNetworkEvent(new LavalandBossHudHideEvent(miner.HudId), session.Channel);
            RaiseNetworkEvent(new LavalandBossMusicStopEvent(miner.HudId), session.Channel);
        }
    }

    private void BlinkBehind(EntityUid uid, LavalandMadMinerComponent miner, EntityUid target, TimeSpan now)
    {
        var facing = TryComp<MouseRotatorComponent>(target, out var mouse) && mouse.GoalRotation is { } goal
            ? goal.ToVec()
            : _transform.GetWorldRotation(target).ToVec();
        var destination = _transform.GetMapCoordinates(target).Offset(-facing * 1.3f);
        _transform.SetMapCoordinates(uid, destination);
        if (miner.BlinkSound != null)
            _audio.PlayPvs(miner.BlinkSound, uid);
        miner.NextBlink = now + miner.BlinkCooldown;
    }

    private void Feint(EntityUid uid, LavalandMadMinerComponent miner, EntityUid target, TimeSpan now)
    {
        var angle = _random.NextAngle();
        var destination = _transform.GetMapCoordinates(target).Offset(angle.ToVec() * _random.NextFloat(1.4f, 3.2f));
        _transform.SetMapCoordinates(uid, destination);
        miner.NextFeint = now + miner.FeintCooldown;
    }

    private void FirePattern(EntityUid uid, LavalandMadMinerComponent miner, EntityUid target, TimeSpan now)
    {
        var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(uid);
        if (direction.LengthSquared() < 0.01f)
            return;
        var baseAngle = direction.ToWorldAngle();
        switch (miner.AttackPattern++ % 3)
        {
            case 0:
                Fire(uid, miner, baseAngle);
                break;
            case 1:
                foreach (var offset in ShotgunSpread)
                    Fire(uid, miner, baseAngle + Angle.FromDegrees(offset));
                break;
            default:
                Fire(uid, miner, baseAngle - Angle.FromDegrees(5));
                Fire(uid, miner, baseAngle);
                Fire(uid, miner, baseAngle + Angle.FromDegrees(5));
                break;
        }
        miner.NextShot = now + miner.ShootCooldown;
    }

    private void Fire(EntityUid uid, LavalandMadMinerComponent miner, Angle angle)
    {
        var projectile = Spawn(miner.Projectile, Transform(uid).Coordinates);
        _gun.ShootProjectile(projectile, angle.ToVec(), Vector2.Zero, uid, uid, miner.ProjectileSpeed);
    }

    private void BeginHook(EntityUid uid, LavalandMadMinerComponent miner, EntityUid target, TimeSpan now)
    {
        miner.HookTarget = target;
        miner.HookEnds = now + miner.HookDuration;
        miner.NextHook = now + miner.HookCooldown;
        if (miner.HookSound != null)
            _audio.PlayPvs(miner.HookSound, uid);
        var direction = _transform.GetWorldPosition(target) - _transform.GetWorldPosition(uid);
        if (direction.LengthSquared() > 0.01f)
        {
            var visual = Spawn(miner.HookProjectile, Transform(uid).Coordinates);
            _gun.ShootProjectile(visual, Vector2.Normalize(direction), Vector2.Zero, uid, uid, 28f);
        }
    }

    private void ProcessHook(EntityUid uid, LavalandMadMinerComponent miner, EntityUid fallbackTarget, TimeSpan now)
    {
        if (miner.HookTarget is not { Valid: true } target || now >= miner.HookEnds || !Exists(target))
        {
            miner.HookTarget = null;
            return;
        }
        var direction = _transform.GetWorldPosition(uid) - _transform.GetWorldPosition(target);
        var distance = direction.Length();
        if (distance <= miner.HookImpactRange)
        {
            _damage.TryChangeDamage(target, miner.HookImpactDamage, ignoreResistances: true, origin: uid);
            if (TryComp<PhysicsComponent>(target, out var body) && distance > 0.01f)
                _physics.SetLinearVelocity(target, -Vector2.Normalize(direction) * miner.HookKnockback, body: body);
            miner.HookTarget = null;
            return;
        }
        if (TryComp<PhysicsComponent>(target, out var physics) && distance > 0.01f)
            _physics.SetLinearVelocity(target, Vector2.Normalize(direction) * miner.HookForce, body: physics);
    }

    public bool TryInterceptJaunter(EntityUid user, EntityUid jaunter)
    {
        var query = EntityQueryEnumerator<LavalandMadMinerComponent>();
        while (query.MoveNext(out var uid, out var miner))
        {
            if (!miner.Active || Transform(uid).MapID != Transform(user).MapID ||
                Vector2.DistanceSquared(_transform.GetWorldPosition(uid), _transform.GetWorldPosition(user)) > miner.ChaseRange * miner.ChaseRange)
                continue;
            _containers.TryRemoveFromContainer(jaunter, force: true);
            _transform.SetMapCoordinates(jaunter, _transform.GetMapCoordinates(uid).Offset(new Vector2(0.5f, 0)));
            BeginHook(uid, miner, user, _timing.CurTime);
            return true;
        }
        return false;
    }

    private void OnChasmFalling(ChasmFallingAttemptEvent args)
    {
        TryRescueFromChasm(args.Tripper, args);
    }

    public bool TryRescueFromChasm(EntityUid tripper, ChasmFallingAttemptEvent? args = null)
    {
        var query = EntityQueryEnumerator<LavalandMadMinerComponent>();
        while (query.MoveNext(out var uid, out var miner))
        {
            if (!miner.Active || Transform(uid).MapID != Transform(tripper).MapID)
                continue;
            args?.Cancel();
            _transform.SetMapCoordinates(tripper, _transform.GetMapCoordinates(uid).Offset(new Vector2(2, 0)));
            BeginHook(uid, miner, tripper, _timing.CurTime);
            return true;
        }
        return false;
    }

    private void OnMobStateChanged(Entity<LavalandMadMinerComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead || ent.Comp.RewardsSpawned)
            return;
        ent.Comp.RewardsSpawned = true;
        SpawnReward(ent.Owner, ent.Comp.ChainHandReward, ent.Comp.ChainHandChance);
        SpawnReward(ent.Owner, ent.Comp.ExplosivePkaReward, ent.Comp.ExplosivePkaChance);
        EndFight(ent.Comp);
    }

    private void SpawnReward(EntityUid uid, EntProtoId? reward, float chance)
    {
        if (reward is not { } proto || !_random.Prob(Math.Clamp(chance, 0f, 1f)) || !_prototypes.HasIndex<EntityPrototype>(proto.Id))
            return;
        Spawn(proto.Id, Transform(uid).Coordinates);
    }

    private void SendHud(EntityUid uid, LavalandMadMinerComponent miner)
    {
        var health = miner.MaxHealth;
        if (TryComp<DamageableComponent>(uid, out var damageable))
            health = Math.Max(0, miner.MaxHealth - damageable.TotalDamage.Float());
        foreach (var session in GetNearbySessions(uid, miner.ChaseRange))
            RaiseNetworkEvent(new LavalandBossHudUpdateEvent(miner.HudId, "Безумный шахтёр", health, miner.MaxHealth, 1), session.Channel);
    }

    private IEnumerable<ICommonSession> GetNearbySessions(EntityUid uid, float range)
    {
        var origin = _transform.GetWorldPosition(uid);
        var map = Transform(uid).MapID;
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is { Valid: true } player && Transform(player).MapID == map &&
                Vector2.DistanceSquared(origin, _transform.GetWorldPosition(player)) <= range * range)
                yield return session;
        }
    }

    private void BuildPlatform(EntityUid uid)
    {
        if (Transform(uid).GridUid is not { } gridUid || !TryComp<MapGridComponent>(gridUid, out var grid))
            return;
        var center = _map.LocalToTile(gridUid, grid, Transform(uid).Coordinates);
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        {
            // Keep the existing generated floor; the 3x3 footprint is cleared of blocking terrain by the boss's spawn layer.
            var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, center + new Vector2i(x, y));
            while (anchored.MoveNext(out var entity))
            {
                if (entity is { } obstacle && MetaData(obstacle).EntityPrototype?.ID.StartsWith("WallRockBasalt") == true)
                    QueueDel(obstacle);
            }
        }
    }

    private bool IsDead(EntityUid uid) => TryComp<MobStateComponent>(uid, out var mob) && mob.CurrentState == MobState.Dead;
}
