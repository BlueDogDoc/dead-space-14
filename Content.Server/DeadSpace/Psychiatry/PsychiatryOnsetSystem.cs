// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Server.Popups;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Components;
using Content.Shared.DeadSpace.CCCCVars;
using Content.Shared.DeadSpace.Psychiatry;
using Content.Shared.Emp;
using Content.Shared.Drunk;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Slippery;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.DeadSpace.Psychiatry;

public sealed class PsychiatryOnsetSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly PsychiatrySystem _psychiatry = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    private static readonly ProtoId<PsychiatryHarmfulReagentsPrototype> HarmfulList = "PsychiatryHarmful";

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
        ScanMedicines();
        ScanIon();
    }

    private void OnSlip(Entity<SlipperyComponent> ent, ref SlipEvent args)
    {
        TrySlipRoll(args.Slipped, _random.NextFloat());
    }

    private void OnDefib(Entity<MobStateComponent> ent, ref TargetDefibrillatedEvent args)
    {
        var asphyx = GetDamage(ent, "Asphyxiation");
        var highAsphyx = _cfg.GetCVar(CCCCVars.PsychiatryDefibAsphyxiation);
        float chance;
        SchizophreniaStage stage;
        if (asphyx >= highAsphyx)
        {
            chance = _cfg.GetCVar(CCCCVars.PsychiatryDefibHighChance);
            stage = _random.Prob(_cfg.GetCVar(CCCCVars.PsychiatryDefibStageSplit))
                ? SchizophreniaStage.Simple
                : SchizophreniaStage.Acute;
        }
        else
        {
            chance = _cfg.GetCVar(CCCCVars.PsychiatryDefibLowChance);
            stage = _random.Prob(_cfg.GetCVar(CCCCVars.PsychiatryDefibStageSplit))
                ? SchizophreniaStage.Latent
                : SchizophreniaStage.Simple;
        }

        if (!_random.Prob(chance))
            return;

        Notify(ent, _psychiatry.TryOnsetOrEscalate(ent, stage, "defibrillation"));
    }

    private void OnDrunk(Entity<MobStateComponent> ent, ref SharedDrunkSystem.DrunkEvent args)
    {
        if (!_random.Prob(_cfg.GetCVar(CCCCVars.PsychiatryAlcoholChance)))
            return;
        Notify(ent, _psychiatry.TryOnsetOrEscalate(ent, SchizophreniaStage.Latent, "alcohol"));
    }

    private void OnEmp(Entity<MobStateComponent> ent, ref EmpPulseEvent args)
    {
        if (!_psychiatry.IsPositronic(ent) || !_random.Prob(_cfg.GetCVar(CCCCVars.PsychiatryEmpChance)))
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
            if (!damageable.Damage.DamageDict.TryGetValue("Shock", out var shock) || shock < _cfg.GetCVar(CCCCVars.PsychiatryIonShockMin))
                continue;
            if (!_random.Prob(_cfg.GetCVar(CCCCVars.PsychiatryIonChance)))
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
            if (!damageable.Damage.DamageDict.TryGetValue("Radiation", out var rad) || rad < _cfg.GetCVar(CCCCVars.PsychiatryRadiationMin))
                continue;
            if (!_random.Prob(_cfg.GetCVar(CCCCVars.PsychiatryRadiationChance)))
                continue;

            var stage = _random.Prob(_cfg.GetCVar(CCCCVars.PsychiatryRadiationStageSplit))
                ? SchizophreniaStage.Latent
                : SchizophreniaStage.Simple;
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

            foreach (var reagent in HarmfulReagents())
            {
                if (solution.GetTotalPrototypeQuantity(reagent) <= FixedPoint2.Zero)
                    continue;
                if (!_random.Prob(_cfg.GetCVar(CCCCVars.PsychiatryHarmfulReagentChance)))
                    continue;
                Notify(uid, _psychiatry.TryOnsetOrEscalate(uid, SchizophreniaStage.Latent, $"harmful-reagent:{reagent.Id}"));
                break;
            }
        }
    }

    public bool TrySlipRoll(EntityUid slipped, float roll)
    {
        if (roll >= _cfg.GetCVar(CCCCVars.PsychiatrySlipChance))
            return false;

        if (_psychiatry.IsPositronic(slipped))
            return NotifyCyber(slipped, _psychiatry.TryApplyCyber(slipped, SchizophreniaStage.Latent, "slip"));

        return Notify(slipped, _psychiatry.TryOnsetOrEscalate(slipped, SchizophreniaStage.Latent, "slip"));
    }

    public bool TryAsphyxiationRoll(EntityUid uid, float roll)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return false;
        if (!damageable.Damage.DamageDict.TryGetValue("Asphyxiation", out var asphyx) || asphyx < _cfg.GetCVar(CCCCVars.PsychiatryAsphyxiationMin))
            return false;
        if (roll >= _cfg.GetCVar(CCCCVars.PsychiatryAsphyxiationChance))
            return false;

        var stage = asphyx >= _cfg.GetCVar(CCCCVars.PsychiatryAsphyxiationSevere)
            ? SchizophreniaStage.Simple
            : SchizophreniaStage.Latent;
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

    private void ScanMedicines()
    {
        var scale = _cfg.GetCVar(CCCCVars.PsychiatryMedicineOnsetScale);
        if (scale <= 0f)
            return;

        var meds = new List<PsychiatryOnsetMedicinePrototype>();
        foreach (var med in _proto.EnumeratePrototypes<PsychiatryOnsetMedicinePrototype>())
        {
            if (med.Chance > 0f)
                meds.Add(med);
        }

        if (meds.Count == 0)
            return;

        var q = EntityQueryEnumerator<BloodstreamComponent, MobStateComponent>();
        while (q.MoveNext(out var uid, out var blood, out _))
        {
            foreach (var med in meds)
                TryMedicine(uid, blood, med.Reagent, med.Chance * scale);
        }
    }

    private void TryMedicine(EntityUid uid, BloodstreamComponent blood, ProtoId<ReagentPrototype> reagent, float chance)
    {
        Entity<SolutionComponent>? soln = null;
        if (!_solutions.ResolveSolution(uid, blood.BloodSolutionName, ref soln, out var solution))
            return;

        var present = solution.GetTotalPrototypeQuantity(reagent) > FixedPoint2.Zero;
        if (!present)
        {
            if (TryComp<SchizophreniaOnsetTrackerComponent>(uid, out var existing))
                existing.RolledMedicines.Remove(reagent.Id);
            return;
        }

        var tracker = EnsureComp<SchizophreniaOnsetTrackerComponent>(uid);

        if (!tracker.RolledMedicines.Add(reagent.Id))
            return;
        if (!_random.Prob(chance))
            return;

        Notify(uid, _psychiatry.TryOnsetOrEscalate(uid, SchizophreniaStage.Latent, $"medicine:{reagent.Id}"));
    }

    private IEnumerable<ProtoId<ReagentPrototype>> HarmfulReagents()
    {
        if (_proto.TryIndex(HarmfulList, out var list))
            return list.Reagents;
        return Array.Empty<ProtoId<ReagentPrototype>>();
    }

    private float GetDamage(EntityUid uid, string type)
    {
        if (!TryComp<DamageableComponent>(uid, out var damageable))
            return 0f;
        return damageable.Damage.DamageDict.TryGetValue(type, out var v) ? (float) v : 0f;
    }
}
