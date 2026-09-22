// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.DeadSpace.Pickles.Components;

namespace Content.IntegrationTests.Tests.DeadSpace.Pickles;

public sealed class PickleConstructionTest : InteractionTest
{
    [Test]
    public async Task WoodenBarrelCraftsFromThreePlanks()
    {
        await StartConstruction("PickleBarrelWood");
        await InteractUsing("WoodPlank", 3);
        AssertPrototype("PickleBarrelWood");
        AssertComp<FermentationBarrelComponent>();
    }

    [Test]
    public async Task PlasticBarrelCraftsFromTwoBucketsAndAWelder()
    {
        await StartConstruction("PickleBarrelPlastic");
        await InteractUsing("Bucket");
        AssertPrototype("PickleBarrelPlasticFrame");
        await InteractUsing("Bucket");
        await InteractUsing(Weld);
        AssertPrototype("PickleBarrelPlastic");
        AssertComp<FermentationBarrelComponent>();
    }

    [Test]
    public async Task WoodenBarrelPriesBackIntoPlanks()
    {
        await StartDeconstruction("PickleBarrelWood");
        await InteractUsing(Pry);
        AssertDeleted();
        await AssertEntityLookup(("WoodPlank", 3));
    }

    [Test]
    public async Task PlasticBarrelPriesBackIntoSheets()
    {
        await StartDeconstruction("PickleBarrelPlastic");
        await InteractUsing(Pry);
        AssertDeleted();
        await AssertEntityLookup((Plastic, 2));
    }
}
