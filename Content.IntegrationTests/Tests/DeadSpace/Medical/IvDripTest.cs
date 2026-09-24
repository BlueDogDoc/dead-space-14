#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DeadSpace.Medical.IvDrip;
using Content.Shared.FixedPoint;
using Content.Shared.Foldable;
using Content.Shared.Item;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.IntegrationTests.Tests.DeadSpace.Medical;

[TestOf(typeof(IvDripComponent))]
public sealed class IvDripTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private static readonly EntProtoId DripFolded = "IvDripFolded";
    private static readonly EntProtoId Drip = "IvDrip";
    private static readonly EntProtoId TargetProto = "MobHuman";
    private static readonly ProtoId<VendingMachineInventoryPrototype> NanoMedPlus = "NanoMedPlusInventory";
    private static readonly ProtoId<VendingMachineInventoryPrototype> NanoMed = "NanoMedInventory";
    private static readonly ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype> Blood = "Blood";

    [Test]
    public async Task UnfoldsOnceAndCannotRefold()
    {
        await SpawnTarget(DripFolded);
        Assert.That(Comp<FoldableComponent>().IsFolded, Is.True);

        await Server.WaitAssertion(() =>
        {
            var foldable = SEntMan.System<FoldableSystem>();
            Assert.That(foldable.TrySetFolded(STarget!.Value, Comp<FoldableComponent>(), false), Is.True);
            Assert.That(Comp<FoldableComponent>().IsFolded, Is.False);
            Assert.That(foldable.TrySetFolded(STarget.Value, Comp<FoldableComponent>(), true), Is.False);
        });
    }

    [Test]
    public async Task UnfoldedCannotBePickedUp()
    {
        await SpawnTarget(DripFolded);

        await Server.WaitAssertion(() =>
        {
            var foldable = SEntMan.System<FoldableSystem>();
            Assert.That(foldable.TrySetFolded(STarget!.Value, Comp<FoldableComponent>(), false), Is.True);

            var ev = new GettingPickedUpAttemptEvent(SPlayer, STarget.Value, showPopup: false);
            SEntMan.EventBus.RaiseLocalEvent(STarget.Value, ev);
            Assert.That(ev.Cancelled, Is.True);
        });
    }

    [Test]
    public async Task InjectsFromTankWhenAttached()
    {
        await AddAtmosphere();

        EntityUid drip = default;
        EntityUid patient = default;
        FixedPoint2 startVol = default;

        await Server.WaitPost(() =>
        {
            var coords = SEntMan.GetComponent<TransformComponent>(SPlayer).Coordinates;
            patient = SEntMan.SpawnEntity(TargetProto, coords);
            drip = SEntMan.SpawnEntity(Drip, coords.Offset(new Vector2(1.0f, 0f)));

            SEntMan.System<FoldableSystem>()
                .SetFolded(drip, SEntMan.GetComponent<FoldableComponent>(drip), false);

            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(drip, IvDripComponent.TankSolutionId, out var soln, out _), Is.True);
            Assert.That(solutions.TryAddReagent(soln!.Value, Blood, FixedPoint2.New(50)), Is.True);
            Assert.That(solutions.TryGetSolution(drip, IvDripComponent.TankSolutionId, out _, out var tank), Is.True);
            startVol = tank!.Volume;

            var dripComp = SEntMan.GetComponent<IvDripComponent>(drip);
            dripComp.AttachedPatient = patient;
            dripComp.Speed = IvDripSpeed.Fast;
            dripComp.NextTransfer = TimeSpan.Zero;
            SEntMan.Dirty(drip, dripComp);

            var connected = SEntMan.EnsureComponent<IvDripConnectedComponent>(patient);
            connected.Drip = drip;
            SEntMan.Dirty(patient, connected);
        });

        await RunSeconds(1.2f);

        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(drip, IvDripComponent.TankSolutionId, out _, out var tank), Is.True);
            Assert.That(tank!.Volume, Is.LessThan(startVol));
        });
    }

    [Test]
    public async Task NanoMedStocksFoldedDrip()
    {
        await using var pair = await PoolManager.GetServerClient();
        var proto = pair.Server.ResolveDependency<IPrototypeManager>();
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(proto.HasIndex(Drip), Is.True);
            Assert.That(proto.HasIndex(DripFolded), Is.True);
            Assert.That(proto.TryIndex(NanoMedPlus, out var plus), Is.True);
            Assert.That(plus!.StartingInventory.ContainsKey(DripFolded.Id), Is.True);
            Assert.That(proto.TryIndex(NanoMed, out var nano), Is.True);
            Assert.That(nano!.StartingInventory.ContainsKey(DripFolded.Id), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
