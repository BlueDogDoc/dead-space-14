// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Server.DeadSpace.Skill;
using Content.Server.Popups;
using Content.Server.Traits.Assorted;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.DeadSpace.CCCCVars;
using Content.Shared.DeadSpace.Psychiatry;
using Content.Shared.DeadSpace.Skills.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.DeadSpace.Psychiatry;

public sealed class PsychiatrySystem : SharedPsychiatrySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SkillSystem _skills = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly ParacusiaSystem _paracusia = default!;
    [Dependency] private readonly InternalsSystem _internals = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    private static readonly TimeSpan GasOnsetCooldown = TimeSpan.FromSeconds(30);

    private static readonly SoundSpecifier HallucinationSounds = new SoundCollectionSpecifier("Paracusia");

    private static readonly ProtoId<PsychiatryPhrasesPrototype> DefaultPhrases = "PsychiatryDefault";
    private static readonly ProtoId<ReagentPrototype> Schizotoxin = SpecialPillReagentId;
    private static readonly ProtoId<ReagentPrototype> PsychogenLatent = "PsychogenLatent";
    private static readonly ProtoId<ReagentPrototype> PsychiatryRemedy = "PsychiatryRemedy";
    private static readonly ProtoId<SkillPrototype> AdvancedTreatment = "AdvancedTreatment";

    private float _accum;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SchizophreniaComponent, ComponentStartup>(OnIllnessStartup);
        SubscribeLocalEvent<SchizophreniaComponent, ComponentShutdown>(OnIllnessShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_cfg.GetCVar(CCCCVars.PsychiatryEnabled))
            return;

        _accum += frameTime;
        if (_accum < 1f)
            return;
        _accum = 0f;

        ScanReagents();
        TickAutoEscalateAndWhispers();
    }

    private void ScanReagents()
    {
        var query = EntityQueryEnumerator<BloodstreamComponent, MobStateComponent>();
        while (query.MoveNext(out var uid, out var blood, out _))
        {
            if (IsPositronic(uid))
            {
                if (GetReagentUnits(uid, blood, Schizotoxin) > FixedPoint2.Zero
                    || GetReagentUnits(uid, blood, PsychogenLatent) > FixedPoint2.Zero
                    || GetReagentUnits(uid, blood, PsychiatryRemedy) > FixedPoint2.Zero)
                {
                    RemoveReagent(uid, blood, Schizotoxin);
                    RemoveReagent(uid, blood, PsychogenLatent);
                    RemoveReagent(uid, blood, PsychiatryRemedy);
                    _popup.PopupEntity(Loc.GetString("psychiatry-pill-positronic"), uid, uid);
                }

                continue;
            }

            if (GetReagentUnits(uid, blood, PsychiatryRemedy) > FixedPoint2.Zero)
            {
                ClearIllness(uid, "PsychiatryRemedy");
                RemoveReagent(uid, blood, PsychiatryRemedy);
                continue;
            }

            if (GetReagentUnits(uid, blood, Schizotoxin) > FixedPoint2.Zero)
            {
                TryApplyOrEscalate(uid, SchizophreniaStage.Acute, pillForced: true, ignoreCooldown: true, reason: "Schizotoxin");
                RemoveReagent(uid, blood, Schizotoxin);
            }
        }
    }

    private void TickAutoEscalateAndWhispers()
    {
        var q = EntityQueryEnumerator<SchizophreniaComponent>();
        while (q.MoveNext(out var uid, out var schizo))
        {
            if (Timing.CurTime >= schizo.NextAutoEscalate && schizo.Stage < SchizophreniaStage.Acute)
            {
                if (!IsAntagImmune(uid, schizo.PillForced))
                    AdjustStage(uid, +1, "auto-escalate");
                else
                    ScheduleAutoEscalate(schizo, uid);
                continue;
            }

            if (schizo.Stage >= SchizophreniaStage.Simple)
                TryWhisper(uid, schizo);
        }
    }

    public bool IsPsychogenBlocked(EntityUid uid)
    {
        if (_internals.AreInternalsWorking(uid))
            return true;

        if (!_inventory.TryGetContainerSlotEnumerator(uid, out var slots, SlotFlags.HEAD | SlotFlags.MASK))
            return false;

        while (slots.NextItem(out var item))
        {
            if (!HasComp<PsychogenFilterComponent>(item))
                continue;

            if (TryComp<BreathToolComponent>(item, out var breath) && breath.IsFunctional)
                return true;
        }

        return false;
    }

    public bool TryInhalePsychogen(EntityUid uid)
    {
        if (!_cfg.GetCVar(CCCCVars.PsychiatryEnabled))
            return false;

        if (IsPositronic(uid) || IsAntagImmune(uid, pillForced: false))
            return false;

        if (IsPsychogenBlocked(uid))
            return false;

        var tracker = EnsureComp<SchizophreniaOnsetTrackerComponent>(uid);
        if (Timing.CurTime < tracker.NextAllowedGasOnset)
            return false;

        if (TryComp<SchizophreniaComponent>(uid, out var existing))
        {
            if (existing.Kind != PsychiatryIllnessKind.Schizophrenia || existing.Stage >= SchizophreniaStage.Acute)
            {
                tracker.NextAllowedGasOnset = Timing.CurTime + GasOnsetCooldown;
                return false;
            }

            AdjustStage(uid, +1, "psychogen-gas");
            tracker.NextAllowedGasOnset = Timing.CurTime + GasOnsetCooldown;
            return true;
        }

        ApplyNew(uid, SchizophreniaStage.Latent, pillForced: false, "psychogen-gas");
        tracker.NextAllowedGasOnset = Timing.CurTime + GasOnsetCooldown;
        return true;
    }

    public bool TryOnsetOrEscalate(EntityUid uid, SchizophreniaStage suggested, string reason, bool ignoreCooldown = false)
    {
        return TryApplyOrEscalate(uid, suggested, pillForced: false, ignoreCooldown, reason);
    }

    public bool TryApplyOrEscalate(EntityUid uid, SchizophreniaStage suggested, bool pillForced, bool ignoreCooldown, string reason)
    {
        if (!_cfg.GetCVar(CCCCVars.PsychiatryEnabled))
            return false;

        if (IsPositronic(uid))
            return false;

        if (IsAntagImmune(uid, pillForced))
            return false;

        var tracker = EnsureComp<SchizophreniaOnsetTrackerComponent>(uid);
        var cd = TimeSpan.FromSeconds(_cfg.GetCVar(CCCCVars.PsychiatryOnsetCooldownSec));
        if (!ignoreCooldown && Timing.CurTime < tracker.NextAllowedOnset)
            return false;

        if (TryComp<SchizophreniaComponent>(uid, out var existing))
        {
            if (existing.Stage >= SchizophreniaStage.Acute)
            {
                if (!ignoreCooldown)
                    tracker.NextAllowedOnset = Timing.CurTime + cd;
                return false;
            }

            if (pillForced)
                existing.PillForced = true;

            AdjustStage(uid, +1, reason);
            if (!ignoreCooldown)
                tracker.NextAllowedOnset = Timing.CurTime + cd;
            return true;
        }

        ApplyNew(uid, suggested, pillForced, reason);
        if (!ignoreCooldown)
            tracker.NextAllowedOnset = Timing.CurTime + cd;
        return true;
    }

    public bool TryApplyCyber(EntityUid uid, SchizophreniaStage suggested, string reason, bool ignoreCooldown = false)
    {
        if (!_cfg.GetCVar(CCCCVars.PsychiatryEnabled) || !IsPositronic(uid))
            return false;

        if (IsAntagImmune(uid, pillForced: false))
            return false;

        var tracker = EnsureComp<SchizophreniaOnsetTrackerComponent>(uid);
        var cd = TimeSpan.FromSeconds(_cfg.GetCVar(CCCCVars.PsychiatryOnsetCooldownSec));
        if (!ignoreCooldown && Timing.CurTime < tracker.NextAllowedOnset)
            return false;

        if (TryComp<SchizophreniaComponent>(uid, out var existing))
        {
            if (existing.Kind != PsychiatryIllnessKind.Cyberpsychosis || existing.Stage >= SchizophreniaStage.Acute)
            {
                if (!ignoreCooldown)
                    tracker.NextAllowedOnset = Timing.CurTime + cd;
                return false;
            }

            AdjustStage(uid, +1, reason);
            if (!ignoreCooldown)
                tracker.NextAllowedOnset = Timing.CurTime + cd;
            return true;
        }

        ApplyNew(uid, suggested, pillForced: false, reason, PsychiatryIllnessKind.Cyberpsychosis);
        if (!ignoreCooldown)
            tracker.NextAllowedOnset = Timing.CurTime + cd;
        return true;
    }

    public void ApplyNew(EntityUid uid, SchizophreniaStage stage, bool pillForced, string reason, PsychiatryIllnessKind kind = PsychiatryIllnessKind.Schizophrenia)
    {
        if (stage <= SchizophreniaStage.None)
            return;

        if (kind == PsychiatryIllnessKind.Schizophrenia && IsPositronic(uid))
            return;

        if (kind == PsychiatryIllnessKind.Cyberpsychosis && !IsPositronic(uid))
            return;

        var comp = EnsureComp<SchizophreniaComponent>(uid);
        comp.Kind = kind;
        comp.Stage = ClampStage((int) stage);
        comp.StageHealth = 1f;
        comp.PillForced = pillForced || comp.PillForced;
        if (comp.Seed == 0)
            comp.Seed = _random.Next();
        ScheduleAutoEscalate(comp, uid);
        ScheduleWhisper(comp, uid);
        SyncHallucinations(uid, comp.Stage);
        Dirty(uid, comp);

        _adminLog.Add(LogType.Damaged, LogImpact.Medium,
            $"{ToPrettyString(uid):player} developed {comp.Kind} stage {comp.Stage} ({reason}, pillForced={comp.PillForced})");
    }

    public void ApplyClarityDose(EntityUid uid, float units)
    {
        if (units <= 0f || IsPositronic(uid))
            return;
        if (!TryComp<SchizophreniaComponent>(uid, out var comp))
            return;
        if (comp.Kind != PsychiatryIllnessKind.Schizophrenia || comp.Stage != SchizophreniaStage.Latent)
            return;

        var damage = units * 0.1f;
        while (damage > 0.0001f)
        {
            if (!TryComp<SchizophreniaComponent>(uid, out comp) || comp.Stage != SchizophreniaStage.Latent)
                return;

            if (damage + 0.0001f < comp.StageHealth)
            {
                comp.StageHealth -= damage;
                Dirty(uid, comp);
                return;
            }

            damage -= comp.StageHealth;
            comp.StageHealth = 0f;
            AdjustStage(uid, -1, "NeuroClarity");
        }
    }

    public void ApplyPsychogenDose(EntityUid uid, float units)
    {
        if (units <= 0f || IsPositronic(uid))
            return;

        if (TryComp<SchizophreniaComponent>(uid, out var existing) && existing.Stage >= SchizophreniaStage.Acute)
            return;

        var dose = EnsureComp<PsychogenDoseComponent>(uid);
        dose.Units += units;
        while (dose.Units >= 5f)
        {
            if (!TryApplyOrEscalate(uid, SchizophreniaStage.Latent, pillForced: false, ignoreCooldown: true, reason: "PsychogenLatent"))
            {
                dose.Units = Math.Min(dose.Units, 4.99f);
                break;
            }

            dose.Units -= 5f;
        }
    }

    public void ClearIllness(EntityUid uid, string reason)
    {
        if (!TryComp<SchizophreniaComponent>(uid, out var comp))
            return;

        RemComp<SchizophreniaComponent>(uid);
        _adminLog.Add(LogType.Healed, LogImpact.High,
            $"{ToPrettyString(uid):player} cleared {comp.Kind} ({reason})");
    }

    public void AdjustStage(EntityUid uid, int delta, string reason)
    {
        if (!TryComp<SchizophreniaComponent>(uid, out var comp))
            return;

        var next = delta < 0
            ? LowerStage(comp.Stage, -delta)
            : ClampStage((int) comp.Stage + delta);
        if (next == SchizophreniaStage.None)
        {
            RemComp<SchizophreniaComponent>(uid);
            _adminLog.Add(LogType.Healed, LogImpact.Medium,
                $"{ToPrettyString(uid):player} cleared schizophrenia ({reason})");
            return;
        }
        if (next == comp.Stage && delta > 0)
            return;

        comp.Stage = next;
        comp.StageHealth = 1f;
        ScheduleAutoEscalate(comp, uid);
        SyncHallucinations(uid, comp.Stage);
        Dirty(uid, comp);
        _adminLog.Add(LogType.Damaged, LogImpact.Medium,
            $"{ToPrettyString(uid):player} schizophrenia stage → {comp.Stage} ({reason})");
    }

    private void ScheduleAutoEscalate(SchizophreniaComponent comp, EntityUid uid)
    {
        var min = _cfg.GetCVar(CCCCVars.PsychiatryAutoEscalateMinSec);
        var max = _cfg.GetCVar(CCCCVars.PsychiatryAutoEscalateMaxSec);
        comp.NextAutoEscalate = Timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(min, max));
        Dirty(uid, comp);
    }

    private void ScheduleWhisper(SchizophreniaComponent comp, EntityUid uid)
    {
        var min = _cfg.GetCVar(CCCCVars.PsychiatryWhisperMinSec);
        var max = _cfg.GetCVar(CCCCVars.PsychiatryWhisperMaxSec);
        if (comp.Stage >= SchizophreniaStage.Acute)
        {
            min *= 0.35f;
            max *= 0.45f;
        }

        comp.NextWhisper = Timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(min, max));
        Dirty(uid, comp);
    }

    private void TryWhisper(EntityUid uid, SchizophreniaComponent schizo)
    {
        if (Timing.CurTime < schizo.NextWhisper)
            return;

        ScheduleWhisper(schizo, uid);

        if (!TryComp<ActorComponent>(uid, out var actor))
            return;
        if (!_proto.TryIndex(DefaultPhrases, out PsychiatryPhrasesPrototype? phrases))
            return;

        List<string> pool;
        if (schizo.Stage >= SchizophreniaStage.Acute && phrases.Crime.Count > 0)
            pool = phrases.Crime;
        else if (_random.Prob(0.5f) && phrases.Crime.Count > 0)
            pool = phrases.Crime;
        else if (phrases.Mockery.Count > 0)
            pool = phrases.Mockery;
        else
            pool = phrases.Neutral;

        var victim = Identity.Name(uid, EntityManager);
        var asRadio = schizo.Stage >= SchizophreniaStage.Simple
                      && phrases.Radio.Count > 0
                      && _random.Prob(schizo.Stage >= SchizophreniaStage.Acute ? 0.7f : 0.5f);

        string speaker;
        string message;
        var job = "";
        if (asRadio)
        {
            pool = phrases.Radio;
            speaker = phrases.RadioNames.Count > 0
                ? Loc.GetString(_random.Pick(phrases.RadioNames))
                : Loc.GetString("psychiatry-radio-name-default");
            var jobName = phrases.RadioJobs.Count > 0
                ? Loc.GetString(_random.Pick(phrases.RadioJobs))
                : Loc.GetString("psychiatry-radio-job-default");
            job = $"\\[{jobName}\\] ";
            message = Loc.GetString(_random.Pick(pool), ("name", victim));
        }
        else
        {
            pool = new List<string>(pool);
            if (phrases.Addressed.Count > 0)
                pool.AddRange(phrases.Addressed);
            if (pool.Count == 0)
                return;

            message = Loc.GetString(_random.Pick(pool), ("name", victim));
            speaker = phrases.FakeNames.Count > 0
                ? Loc.GetString(_random.Pick(phrases.FakeNames))
                : Loc.GetString("psychiatry-fake-name-default");
        }

        var whisper = new PsychiatryWhisperEvent(speaker, message, null)
        {
            AsRadio = asRadio,
            Job = job,
        };
        RaiseNetworkEvent(whisper, Filter.SinglePlayer(actor.PlayerSession));
        var heard = new PsychiatryBrainActivityEvent(PsychiatryBrainRegion.Hearing, 0.9f);
        RaiseLocalEvent(uid, ref heard);
        var fear = new PsychiatryBrainActivityEvent(PsychiatryBrainRegion.Fear, 0.75f);
        RaiseLocalEvent(uid, ref fear);
    }

    public bool HasAdvancedTreatment(EntityUid user) =>
        _skills.CnowThisSkill(user, AdvancedTreatment);

    private FixedPoint2 GetReagentUnits(EntityUid uid, BloodstreamComponent blood, ProtoId<ReagentPrototype> reagent)
    {
        Entity<SolutionComponent>? soln = null;
        if (!_solutions.ResolveSolution(uid, blood.BloodSolutionName, ref soln, out var solution))
            return FixedPoint2.Zero;
        return solution.GetTotalPrototypeQuantity(reagent);
    }

    private void OnIllnessStartup(Entity<SchizophreniaComponent> ent, ref ComponentStartup args)
    {
        SyncHallucinations(ent.Owner, ent.Comp.Stage);
    }

    private void OnIllnessShutdown(Entity<SchizophreniaComponent> ent, ref ComponentShutdown args)
    {
        if (!HasComp<PsychiatryParacusiaComponent>(ent.Owner))
            return;

        RemComp<ParacusiaComponent>(ent.Owner);
        RemComp<PsychiatryParacusiaComponent>(ent.Owner);
    }

    private void SyncHallucinations(EntityUid uid, SchizophreniaStage stage)
    {
        if (stage <= SchizophreniaStage.None)
            return;

        if (!HasComp<ParacusiaComponent>(uid))
            EnsureComp<PsychiatryParacusiaComponent>(uid);

        if (!HasComp<PsychiatryParacusiaComponent>(uid))
            return;

        var paracusia = EnsureComp<ParacusiaComponent>(uid);
        _paracusia.SetSounds(uid, HallucinationSounds, paracusia);
        _paracusia.SetDistance(uid, 7f, paracusia);
        var (min, max) = stage switch
        {
            SchizophreniaStage.Acute => (8f, 18f),
            SchizophreniaStage.Simple => (18f, 40f),
            _ => (35f, 70f),
        };
        _paracusia.SetTime(uid, min, max, paracusia);
    }

    private void RemoveReagent(EntityUid uid, BloodstreamComponent blood, ProtoId<ReagentPrototype> reagent)
    {
        Entity<SolutionComponent>? soln = null;
        if (!_solutions.ResolveSolution(uid, blood.BloodSolutionName, ref soln, out _))
            return;
        _solutions.RemoveReagent(soln.Value, reagent, FixedPoint2.New(1000));
    }
}
