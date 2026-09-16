// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.DeadSpace.Lavaland.Components;

[RegisterComponent]
public sealed partial class LavalandMadMinerComponent : Component
{
    [DataField] public float MaxHealth = 1250f;
    [DataField] public float NoticeRange = 12f;
    [DataField] public float ChaseRange = 160f;
    [DataField] public float BlinkDistance = 4.5f;
    [DataField] public TimeSpan BlinkCooldown = TimeSpan.FromSeconds(1.25);
    [DataField] public TimeSpan FeintCooldown = TimeSpan.FromSeconds(3.5);
    [DataField] public TimeSpan ShootCooldown = TimeSpan.FromSeconds(1.4);
    [DataField] public TimeSpan HookCooldown = TimeSpan.FromSeconds(7);
    [DataField] public TimeSpan HookDuration = TimeSpan.FromSeconds(5);
    [DataField] public float HookForce = 18f;
    [DataField] public float HookImpactRange = 1.35f;
    [DataField] public float HookKnockback = 14f;
    [DataField] public DamageSpecifier HookImpactDamage = new();
    [DataField] public EntProtoId Projectile = "LavalandMadMinerKineticBolt";
    [DataField] public EntProtoId HookProjectile = "LavalandMadMinerHookVisual";
    [DataField] public float ProjectileSpeed = 24f;
    [DataField] public List<SoundSpecifier> Music = new();
    [DataField] public SoundSpecifier? BlinkSound;
    [DataField] public SoundSpecifier? HookSound;
    [DataField] public EntProtoId? ChainHandReward = "UnitologyWeaponHandHookImplanter";
    [DataField] public float ChainHandChance = 0.5f;
    [DataField] public EntProtoId? ExplosivePkaReward = "PKAUpgradeExplosiveLavaland";
    [DataField] public float ExplosivePkaChance = 0.85f;

    [ViewVariables] public EntityUid? Target;
    [ViewVariables] public EntityUid? HookTarget;
    [ViewVariables] public bool Active;
    [ViewVariables] public bool RewardsSpawned;
    [ViewVariables] public TimeSpan NextThink;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan NextBlink;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan NextFeint;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan NextShot;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan NextHook;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan HookEnds;
    public int AttackPattern;
    public int HudId;
    public SoundSpecifier? SelectedMusic;
    public MapCoordinates SpawnCoordinates;
}
