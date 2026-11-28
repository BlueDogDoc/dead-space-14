// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using System.Linq;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Cargo;
using Content.Shared.Clothing.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.DeadSpace.Body.Limbs;
using Content.Shared.DeadSpace.PlanetSurvey;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Humanoid;
using Content.Shared.Projectiles;
using Content.Shared.Radiation.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Zombies;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.PlanetSurvey;

public sealed partial class SurveyArtifactSystem : EntitySystem
{
    private static readonly TimeSpan TickRate = TimeSpan.FromSeconds(2);
    private static readonly ProtoId<DamageGroupPrototype> Brute = "Brute";
    private static readonly ProtoId<DamageGroupPrototype> Burn = "Burn";
    private const int MaxCarried = 3;
    private const float InfuseTime = 3f;

    private static readonly string[] PlatingTypes = { "Blunt", "Slash", "Piercing" };
    private static readonly string[] InsulationTypes = { "Heat", "Cold", "Shock" };
    private static readonly string[] PurityTypes = { "Poison", "Radiation", "Caustic", "Cellular" };

    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedGunSystem _gun = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private SurveyDirectorSystem _director = default!;

    private TimeSpan _nextTick;
    private readonly Dictionary<EntityUid, Dictionary<SurveyBoon, float>> _totals = new();
    private readonly Dictionary<EntityUid, int> _carried = new();
    private readonly List<EntityUid> _stale = new();
    private readonly Stack<Dictionary<SurveyBoon, float>> _pool = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SurveyArtifactComponent, ExaminedEvent>(OnArtifactExamined);
        SubscribeLocalEvent<SurveyArtifactComponent, PriceCalculationEvent>(OnPrice);
        SubscribeLocalEvent<SurveyArtifactComponent, GotEquippedHandEvent>(OnPickedUp);
        SubscribeLocalEvent<SurveyArtifactComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SurveyArtifactComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SurveyArtifactComponent, SurveyAnalyzeDoAfterEvent>(OnAnalyzed);
        SubscribeLocalEvent<SurveyArtifactComponent, SurveyStabilizeDoAfterEvent>(OnStabilized);

        SubscribeLocalEvent<SurveyAnalyzerComponent, AfterInteractEvent>(OnAnalyzerUse);
        SubscribeLocalEvent<SurveyStabilizerComponent, AfterInteractEvent>(OnStabilizerUse);

        SubscribeLocalEvent<SurveyPlateComponent, AfterInteractEvent>(OnPlateUse);
        SubscribeLocalEvent<SurveyPlateComponent, SurveyInfuseDoAfterEvent>(OnInfused);
        SubscribeLocalEvent<SurveyPlateComponent, ExaminedEvent>(OnPlateExamined);

        SubscribeLocalEvent<SurveyInfusionComponent, ExaminedEvent>(OnInfusionExamined);
        SubscribeLocalEvent<SurveyInfusionComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnInfusedDamage);
        SubscribeLocalEvent<SurveyInfusionComponent, InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent>>(OnInfusedSpeed);
        SubscribeLocalEvent<SurveyInfusionComponent, InventoryRelayedEvent<BeforeStaminaDamageEvent>>(OnInfusedStamina);
        SubscribeLocalEvent<SurveyInfusionComponent, GotEquippedEvent>(OnInfusionEquipped);
        SubscribeLocalEvent<SurveyInfusionComponent, GotUnequippedEvent>(OnInfusionUnequipped);

        SubscribeLocalEvent<SurveyBoonHolderComponent, DamageModifyEvent>(OnHolderDamage);
        SubscribeLocalEvent<SurveyBoonHolderComponent, RefreshMovementSpeedModifiersEvent>(OnHolderSpeed);
        SubscribeLocalEvent<SurveyBoonHolderComponent, BeforeStaminaDamageEvent>(OnHolderStamina);
        SubscribeLocalEvent<LimbBodyComponent, LimbSeverAttemptEvent>(OnSeverAttempt);
        SubscribeLocalEvent<SurveyInfusionComponent, GunRefreshModifiersEvent>(OnInfusedGun);
        SubscribeLocalEvent<SurveyInfusionComponent, AmmoShotEvent>(OnInfusedShot);
        SubscribeLocalEvent<SurveyBaneComponent, BeforeProjectileHitEvent>(OnBaneHit);
    }

    private void OnSeverAttempt(Entity<LimbBodyComponent> ent, ref LimbSeverAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var chance = 0f;
        if (TryComp<SurveyBoonHolderComponent>(ent, out var holder))
            chance += holder.Totals.GetValueOrDefault(SurveyBoon.Sinew);

        var slots = _inventory.GetSlotEnumerator(ent.Owner, SlotFlags.WITHOUT_POCKET);
        while (slots.NextItem(out var item))
        {
            if (TryComp<SurveyInfusionComponent>(item, out var infusion) && infusion.Boon == SurveyBoon.Sinew)
                chance += infusion.Value;
        }

        if (chance > 0f && _random.Prob(Math.Clamp(chance, 0f, 0.9f)))
            args.Cancelled = true;
    }

    private void OnArtifactExamined(Entity<SurveyArtifactComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (!ent.Comp.Identified)
        {
            args.PushMarkup(Loc.GetString("survey-artifact-unknown"));
            return;
        }

        using (args.PushGroup(nameof(SurveyArtifactComponent)))
        {
            args.PushMarkup(Loc.GetString("survey-artifact-tier", ("tier", ent.Comp.Tier)));
            foreach (var boon in ent.Comp.Boons)
            {
                args.PushMarkup(BoonLine(boon.Boon, boon.Value));
            }
        }
    }

    private string BoonLine(SurveyBoon boon, float value)
    {
        return Loc.GetString(value >= 0 ? "survey-boon-line-good" : "survey-boon-line-bad",
            ("boon", Loc.GetString($"survey-boon-{boon.ToString().ToLowerInvariant()}")),
            ("value", BoonValue(boon, value)));
    }

    private static string BoonValue(SurveyBoon boon, float value)
    {
        return boon == SurveyBoon.Mending
            ? $"{Math.Abs(value):0.#}"
            : $"{Math.Abs(value) * 100f:0}%";
    }

    private void OnPrice(Entity<SurveyArtifactComponent> ent, ref PriceCalculationEvent args)
    {
        if (ent.Comp.Identified)
            args.Price += ent.Comp.IdentifiedBonus;
    }

    private void OnPickedUp(Entity<SurveyArtifactComponent> ent, ref GotEquippedHandEvent args)
    {
        if (ent.Comp.Taken)
            return;

        ent.Comp.Taken = true;
        if (ent.Comp.Planet is { } planet)
            _director.ArtifactTaken(planet, ent);
    }

    private bool InCase(EntityUid uid)
    {
        return _container.TryGetContainingContainer(uid, out var container) && HasComp<SurveyLeadCaseComponent>(container.Owner);
    }

    private void OnInserted(Entity<SurveyArtifactComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (HasComp<SurveyLeadCaseComponent>(args.Container.Owner) && TryComp<RadiationSourceComponent>(ent, out var radiation))
            radiation.Enabled = false;
    }

    private void OnRemoved(Entity<SurveyArtifactComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (HasComp<SurveyLeadCaseComponent>(args.Container.Owner) && TryComp<RadiationSourceComponent>(ent, out var radiation))
            radiation.Enabled = true;
    }

    private bool Allowed(EntityUid user, ProtoId<Content.Shared.Access.AccessLevelPrototype> access)
    {
        return _access.FindAccessTags(user).Contains(access);
    }

    private void OnAnalyzerUse(Entity<SurveyAnalyzerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (TryComp<SurveyPlateComponent>(target, out var plate))
        {
            args.Handled = true;
            _popup.PopupEntity(Loc.GetString("survey-analyzer-plate"), target, args.User);
            return;
        }

        if (!TryComp<SurveyArtifactComponent>(target, out var artifact))
            return;

        args.Handled = true;
        if (!Allowed(args.User, ent.Comp.Access))
        {
            _popup.PopupEntity(Loc.GetString("survey-analyzer-noaccess"), args.User, args.User);
            return;
        }

        if (artifact.Identified)
        {
            _popup.PopupEntity(Loc.GetString("survey-analyzer-known"), target, args.User);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.Time), new SurveyAnalyzeDoAfterEvent(), target, target: target, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnAnalyzed(Entity<SurveyArtifactComponent> ent, ref SurveyAnalyzeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || ent.Comp.Identified)
            return;

        args.Handled = true;
        ent.Comp.Identified = true;
        var station = _station.GetOwningStation(args.User);
        if (station == null && ent.Comp.Planet is { } planet && TryComp<SurveyDirectorComponent>(planet, out var director))
            station = director.Station;

        _director.GivePoints(station, ent.Comp.Points);
        _popup.PopupEntity(Loc.GetString("survey-analyzer-done"), ent, args.User, PopupType.Medium);
    }

    private void OnStabilizerUse(Entity<SurveyStabilizerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !TryComp<SurveyArtifactComponent>(target, out var artifact))
            return;

        args.Handled = true;
        if (!Allowed(args.User, ent.Comp.Access))
        {
            _popup.PopupEntity(Loc.GetString("survey-analyzer-noaccess"), args.User, args.User);
            return;
        }

        if (!artifact.Identified)
        {
            _popup.PopupEntity(Loc.GetString("survey-stabilizer-unknown"), target, args.User);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(ent.Comp.Time), new SurveyStabilizeDoAfterEvent(), target, target: target, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnStabilized(Entity<SurveyArtifactComponent> ent, ref SurveyStabilizeDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } used || !TryComp<SurveyStabilizerComponent>(used, out var stabilizer))
            return;

        var best = ent.Comp.Boons.Where(b => b.Value > 0).OrderByDescending(b => b.Value).FirstOrDefault();
        if (best == null)
        {
            _popup.PopupEntity(Loc.GetString("survey-stabilizer-empty"), ent, args.User);
            return;
        }

        args.Handled = true;
        var coords = Transform(ent).Coordinates;
        var count = 1 + Math.Clamp(ent.Comp.Tier, 1, 3);
        if (stabilizer.Products.TryGetValue(best.Boon, out var product))
        {
            for (var i = 0; i < count; i++)
            {
                SpawnAtPosition(product, coords);
            }

            _popup.PopupCoordinates(Loc.GetString("survey-stabilizer-done", ("count", count)), coords, args.User, PopupType.Medium);
            QueueDel(ent);
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var uid = SpawnAtPosition(stabilizer.Plate, coords);
            var plate = EnsureComp<SurveyPlateComponent>(uid);
            plate.Boon = best.Boon;
            plate.Value = best.Value * stabilizer.Share;
            _meta.SetEntityName(uid, Loc.GetString("survey-plate-name", ("boon", Loc.GetString($"survey-boon-{best.Boon.ToString().ToLowerInvariant()}"))));
        }

        _popup.PopupCoordinates(Loc.GetString("survey-stabilizer-done", ("count", count)), coords, args.User, PopupType.Medium);
        QueueDel(ent);
    }

    private static SlotFlags SlotsFor(SurveyBoon boon)
    {
        return boon switch
        {
            SurveyBoon.Plating or SurveyBoon.Insulation or SurveyBoon.Purity => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING | SlotFlags.HEAD,
            SurveyBoon.Swift => SlotFlags.FEET,
            SurveyBoon.Stamina => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
            SurveyBoon.Mending => SlotFlags.INNERCLOTHING,
            SurveyBoon.Sinew => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
            _ => SlotFlags.NONE,
        };
    }

    private static bool WeaponBoon(SurveyBoon boon)
    {
        return boon is SurveyBoon.Rapid or SurveyBoon.Bane;
    }

    private void OnPlateUse(Entity<SurveyPlateComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        var gun = HasComp<GunComponent>(target);
        if (!gun && !HasComp<ClothingComponent>(target))
            return;

        args.Handled = true;
        var fits = WeaponBoon(ent.Comp.Boon)
            ? gun
            : TryComp<ClothingComponent>(target, out var clothing) && (clothing.Slots & SlotsFor(ent.Comp.Boon)) != SlotFlags.NONE;
        if (!fits)
        {
            _popup.PopupEntity(Loc.GetString($"survey-plate-wrong-{ent.Comp.Boon.ToString().ToLowerInvariant()}"), target, args.User);
            return;
        }

        if (HasComp<SurveyInfusionComponent>(target))
        {
            _popup.PopupEntity(Loc.GetString("survey-plate-taken"), target, args.User);
            return;
        }

        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, TimeSpan.FromSeconds(InfuseTime), new SurveyInfuseDoAfterEvent(), ent, target: target, used: ent)
        {
            BreakOnMove = true,
            NeedHand = true,
        });
    }

    private void OnInfused(Entity<SurveyPlateComponent> ent, ref SurveyInfuseDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || HasComp<SurveyInfusionComponent>(target))
            return;

        args.Handled = true;
        var infusion = AddComp<SurveyInfusionComponent>(target);
        infusion.Boon = ent.Comp.Boon;
        infusion.Value = ent.Comp.Value;
        _popup.PopupEntity(Loc.GetString("survey-plate-done"), target, args.User, PopupType.Medium);
        if (_container.TryGetContainingContainer(target, out var container))
            _movement.RefreshMovementSpeedModifiers(container.Owner);
        if (HasComp<GunComponent>(target))
            _gun.RefreshModifiers(target);
        QueueDel(ent);
    }

    private void OnPlateExamined(Entity<SurveyPlateComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(BoonLine(ent.Comp.Boon, ent.Comp.Value));
    }

    private void OnInfusionExamined(Entity<SurveyInfusionComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushMarkup(Loc.GetString("survey-infusion-examine", ("line", BoonLine(ent.Comp.Boon, ent.Comp.Value))));
    }

    private void OnInfusionEquipped(Entity<SurveyInfusionComponent> ent, ref GotEquippedEvent args)
    {
        if (ent.Comp.Boon == SurveyBoon.Swift)
            _movement.RefreshMovementSpeedModifiers(args.Equipee);
    }

    private void OnInfusionUnequipped(Entity<SurveyInfusionComponent> ent, ref GotUnequippedEvent args)
    {
        if (ent.Comp.Boon == SurveyBoon.Swift)
            _movement.RefreshMovementSpeedModifiers(args.Equipee);
    }

    private void OnInfusedDamage(Entity<SurveyInfusionComponent> ent, ref InventoryRelayedEvent<DamageModifyEvent> args)
    {
        if (Modifier(ent.Comp.Boon, ent.Comp.Value) is { } set)
            args.Args.Damage = DamageSpecifier.ApplyModifierSet(args.Args.Damage, set);
    }

    private void OnInfusedSpeed(Entity<SurveyInfusionComponent> ent, ref InventoryRelayedEvent<RefreshMovementSpeedModifiersEvent> args)
    {
        if (ent.Comp.Boon == SurveyBoon.Swift)
            args.Args.ModifySpeed(1f + ent.Comp.Value);
    }

    private void OnInfusedStamina(Entity<SurveyInfusionComponent> ent, ref InventoryRelayedEvent<BeforeStaminaDamageEvent> args)
    {
        if (ent.Comp.Boon == SurveyBoon.Stamina && args.Args.Value > 0)
            args.Args.Value *= Math.Clamp(1f - ent.Comp.Value, 0.2f, 2f);
    }

    private void OnInfusedGun(Entity<SurveyInfusionComponent> ent, ref GunRefreshModifiersEvent args)
    {
        if (ent.Comp.Boon == SurveyBoon.Rapid)
            args.FireRate *= 1f + ent.Comp.Value;
    }

    private void OnInfusedShot(Entity<SurveyInfusionComponent> ent, ref AmmoShotEvent args)
    {
        if (ent.Comp.Boon != SurveyBoon.Bane)
            return;

        foreach (var projectile in args.FiredProjectiles)
        {
            EnsureComp<SurveyBaneComponent>(projectile).Bonus = ent.Comp.Value;
        }
    }

    private void OnBaneHit(Entity<SurveyBaneComponent> ent, ref BeforeProjectileHitEvent args)
    {
        if (HasComp<HumanoidAppearanceComponent>(args.Target) && !HasComp<ZombieComponent>(args.Target))
            return;

        args.Damage = args.Damage * (1f + ent.Comp.Bonus);
    }

    private void OnHolderDamage(Entity<SurveyBoonHolderComponent> ent, ref DamageModifyEvent args)
    {
        foreach (var (boon, value) in ent.Comp.Totals)
        {
            if (Modifier(boon, value) is { } set)
                args.Damage = DamageSpecifier.ApplyModifierSet(args.Damage, set);
        }
    }

    private void OnHolderSpeed(Entity<SurveyBoonHolderComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Totals.TryGetValue(SurveyBoon.Swift, out var swift))
            args.ModifySpeed(Math.Clamp(1f + swift, 0.5f, 1.6f));
    }

    private void OnHolderStamina(Entity<SurveyBoonHolderComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        if (args.Value > 0 && ent.Comp.Totals.TryGetValue(SurveyBoon.Stamina, out var stamina))
            args.Value *= Math.Clamp(1f - stamina, 0.2f, 2f);
    }

    private static DamageModifierSet? Modifier(SurveyBoon boon, float value)
    {
        var types = boon switch
        {
            SurveyBoon.Plating => PlatingTypes,
            SurveyBoon.Insulation => InsulationTypes,
            SurveyBoon.Purity => PurityTypes,
            _ => null,
        };

        if (types == null || MathF.Abs(value) < 0.001f)
            return null;

        var coefficient = Math.Clamp(1f - value, 0.2f, 2f);
        var set = new DamageModifierSet();
        foreach (var type in types)
        {
            set.Coefficients[type] = coefficient;
        }

        return set;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + TickRate;
        CollectBoons();
        ApplyBoons();
        MendInfused();
    }

    private void CollectBoons()
    {
        foreach (var totals in _totals.Values)
        {
            totals.Clear();
            _pool.Push(totals);
        }

        _totals.Clear();
        _carried.Clear();

        var query = EntityQueryEnumerator<SurveyArtifactComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var artifact, out var xform))
        {
            if (xform.ParentUid == xform.GridUid || xform.ParentUid == xform.MapUid || InCase(uid) || Carrier(xform) is not { } carrier)
                continue;

            var count = _carried.GetValueOrDefault(carrier);
            if (count >= MaxCarried)
                continue;

            _carried[carrier] = count + 1;
            if (!_totals.TryGetValue(carrier, out var totals))
            {
                totals = _pool.Count > 0 ? _pool.Pop() : new Dictionary<SurveyBoon, float>();
                _totals[carrier] = totals;
            }

            foreach (var boon in artifact.Boons)
            {
                totals[boon.Boon] = totals.GetValueOrDefault(boon.Boon) + boon.Value;
            }
        }
    }

    private EntityUid? Carrier(TransformComponent xform)
    {
        while (xform.ParentUid.IsValid() && xform.ParentUid != xform.MapUid && xform.ParentUid != xform.GridUid)
        {
            var parent = xform.ParentUid;
            if (HasComp<MobStateComponent>(parent))
                return parent;

            xform = Transform(parent);
        }

        return null;
    }

    private void ApplyBoons()
    {
        _stale.Clear();
        var query = EntityQueryEnumerator<SurveyBoonHolderComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_totals.ContainsKey(uid))
                _stale.Add(uid);
        }

        foreach (var uid in _stale)
        {
            var hadSwift = Comp<SurveyBoonHolderComponent>(uid).Totals.ContainsKey(SurveyBoon.Swift);
            RemComp<SurveyBoonHolderComponent>(uid);
            if (hadSwift)
                _movement.RefreshMovementSpeedModifiers(uid);
        }

        foreach (var (uid, totals) in _totals)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            var holder = EnsureComp<SurveyBoonHolderComponent>(uid);
            var oldSwift = holder.Totals.GetValueOrDefault(SurveyBoon.Swift);
            holder.Totals.Clear();
            foreach (var (boon, value) in totals)
            {
                holder.Totals[boon] = value;
            }

            if (MathF.Abs(oldSwift - totals.GetValueOrDefault(SurveyBoon.Swift)) > 0.001f)
                _movement.RefreshMovementSpeedModifiers(uid);

            if (totals.TryGetValue(SurveyBoon.Mending, out var mending) && mending > 0f)
                Mend(uid, mending);
        }
    }

    private void MendInfused()
    {
        var query = EntityQueryEnumerator<SurveyInfusionComponent>();
        while (query.MoveNext(out var uid, out var infusion))
        {
            if (infusion.Boon != SurveyBoon.Mending ||
                !_container.TryGetContainingContainer(uid, out var container) ||
                !_inventory.TryGetContainingSlot(uid, out _) ||
                !HasComp<MobStateComponent>(container.Owner))
            {
                continue;
            }

            Mend(container.Owner, infusion.Value);
        }
    }

    private void Mend(EntityUid uid, float amount)
    {
        var heal = new DamageSpecifier(_proto.Index(Brute), -amount);
        heal += new DamageSpecifier(_proto.Index(Burn), -amount * 0.5f);
        _damage.TryChangeDamage(uid, heal, true, interruptsDoAfters: false);
    }
}
