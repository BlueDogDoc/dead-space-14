// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Server.Popups;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Components;
using Content.Shared.DeadSpace.Psychiatry;
using Content.Shared.Emp;
using Content.Shared.Drunk;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Slippery;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace.Psychiatry;

public sealed class PsychiatryOnsetSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly PsychiatrySystem _psychiatry = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    private static readonly ProtoId<ReagentPrototype>[] HarmfulReagents =
    [
        "WeldingFuel",
        "Toxins",
        "Chlorine",
        "FluorosulfuricAcid",
        "SulphuricAcid",
        "Vomit",
        "Plasma",
    ];

    private const float SlipChance = 0.05f;
    private float _accum;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SlipperyComponent, SlipEvent>(OnSlip);
        SubscribeLocalEvent<MobStateComponent, TargetDefibrillatedEvent>(OnDefib);
        SubscribeLocalEvent<MobStateComponent, SharedDrunkSystem.DrunkEvent>(OnDrunk);
        SubscribeLocalEvent<MobStateComponent, EmpPulseEvent>(OnEmp);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accum += frameTime;
        if (_accum < 2f)
            return;
        _accum = 0f;

        ScanAsphyxiation();
        ScanRadiation();
        ScanHarmfulReagents();
        ScanIon();
    }

    private void OnSlip(Entity<SlipperyComponent> ent, ref SlipEvent args)
    {
        TrySlipRoll(args.Slipped, _random.NextFloat());
    }

    private void OnDefib(Entity<MobStateComponent> ent, ref TargetDefibrillatedEvent args)
    {
        var asphyx = GetDamage(ent, "Asphyxiation");
        float chance;
        SchizophreniaStage stage;
        if (asphyx >= 60)
        {
            chance = 0.20f;
            stage = _random.Prob(0.5f) ? SchizophreniaStage.Simple : SchizophreniaStage.Acute;
        }
        else
        {
            chance = 0.01f;
            stage = _random.Prob(0.5f) ? SchizophreniaStage.Latent : SchizophreniaStage.Simple;
        }

        if (!_random.Prob(chance))
            return;

        Notify(ent, _psychiatry.TryOnsetOrEscalate(ent, stage, "defibrillation"));
    }

    private void OnDrunk(Entity<MobStateComponent> ent, ref SharedDrunkSystem.DrunkEvent args)
    {
        if (!_random.Prob(0.02f))
            return;
        Notify(ent, _psychiatry.TryOnsetOrEscalate(ent, SchizophreniaStage.Latent, "alcohol"));
    }

    private void OnEmp(Entity<MobStateComponent> ent, ref EmpPulseEvent args)
    {
        if (!_psychiatry.IsPositronic(ent) || !_random.Prob(0.2f))
            return;

        NotifyCyber(ent, _psychiatry.TryApplyCyber(ent, SchizophreniaStage.Latent, "emp"));
    }

    private void ScanIon()
    {
        var q = EntityQueryEnumerator<DamageableComponent, MobStateComponent>();
        while (q.MoveNext(out var uid, out var damageable, out _))
        {
            if (!_psychiatry.IsPositronic(uid))
                continue;
            if (!damageable.Damage.DamageDict.TryGetValue("Shock", out var shock) || shock < 50)
                continue;
            if (!_random.Prob(0.02f))
                continue;

            NotifyCyber(uid, _psychiatry.TryApplyCyber(uid, SchizophreniaStage.Latent, "ion"));
        }
    }

    private void ScanAsphyxiation()
    {
        var q = EntityQueryEnumerator<DamageableComponent, MobStateComponent>();
        while (q.MoveNext(out var uid, out var damageable, out _))
        {
            TryAsphyxiationRoll(uid, _random.NextFloat());
        }
    }

    private void ScanRadiation()
    {
        var q = EntityQueryEnumerator<DamageableComponent, MobStateComponent>();
        while (q.MoveNext(out var uid, out var damageable, out _))
        {
            if (!damageable.Damage.DamageDict.TryGetValue("Radiation", out var rad) || rad < 50)
                continue;
            if (!_random.Prob(0.02f))
                continue;

            var stage = _random.Prob(0.5f) ? SchizophreniaStage.Latent : SchizophreniaStage.Simple;
            Notify(uid, _psychiatry.TryOnsetOrEscalate(uid, stage, "radiation"));
        }
    }

    private void ScanHarmfulReagents()
    {
        var q = EntityQueryEnumerator<BloodstreamComponent, MobStateComponent>();
        while (q.MoveNext(out var uid, out var blood, out _))
        {
            Entity<SolutionComponent>? soln = null;
            if (!_solutions.ResolveSolution(uid, blood.BloodSolutionName, ref soln, out var solution))
                continue;

            foreach (var reagent in HarmfulReagents)
            {
                if (solution.GetTotalPrototypeQuantity(reagent) <= FixedPoint2.Zero)
                    continue;
                if (!_random.Prob(0.005f))
                    continue;
                Notify(uid, _psychiatry.TryOnsetOrEscalate(uid, SchizophreniaStage.Latent, $"harmful-reagent:{reagent.Id}"));
                break;
            }
        }
    }

    public bool TrySlipRoll(EntityUid slipped, float roll)
    {
        if (roll >= SlipChance)
            return false;

        if (_psychiatry.IsPositronic(slipped))
            return NotifyCyber(slipped, _psychiatry.TryApplyCyber(slipped, SchizophreniaStage.Latent, "slip"));

        return Notify(slipped, _psychiatry.TryOnsetOrEscalate(slipped, SchizophreniaStage.Latent, "slip"));
    }

    public bool TryAsphyxiationRoll(EntityUid uid, float roll)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return false;
        if (!damageable.Damage.DamageDict.TryGetValue("Asphyxiation", out var asphyx) || asphyx < 40)
            return false;
        if (roll >= 0.01f)
            return false;

        var stage = asphyx >= 80 ? SchizophreniaStage.Simple : SchizophreniaStage.Latent;
        return Notify(uid, _psychiatry.TryOnsetOrEscalate(uid, stage, "asphyxiation"));
    }

    private bool Notify(EntityUid uid, bool applied)
    {
        if (applied)
            _popup.PopupEntity(Loc.GetString("psychiatry-onset-felt"), uid, uid, PopupType.MediumCaution);
        return applied;
    }

    private bool NotifyCyber(EntityUid uid, bool applied)
    {
        if (applied)
            _popup.PopupEntity(Loc.GetString("psychiatry-cyber-onset"), uid, uid, PopupType.MediumCaution);
        return applied;
    }

    private float GetDamage(EntityUid uid, string type)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return 0f;
        return damageable.Damage.DamageDict.TryGetValue(type, out var v) ? (float) v : 0f;
    }
}
