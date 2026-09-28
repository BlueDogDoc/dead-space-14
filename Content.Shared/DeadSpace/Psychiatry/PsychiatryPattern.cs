// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Robust.Shared.Maths;

namespace Content.Shared.DeadSpace.Psychiatry;

public enum PsychiatryRemapPool : byte
{
    Animal,
    Monster,
}

public enum PsychiatryMobKind : byte
{
    Cow,
    Monkey,
    Mouse,
    Corgi,
    Spider,
    Snake,
    Carp,
    FleshGolem,
    Goliath,
    Basilisk,
    Legion,
    Watcher,
    SpaceDragon,
    MiniDragonFire,
    MiniDragonIce,
    MiniDragonToxic,
}

public static class PsychiatryPattern
{
    public static bool IsMeatWall(Vector2i idx, int seed, SchizophreniaStage stage)
    {
        if (stage < SchizophreniaStage.Simple)
            return false;
        var n = Fbm(idx.X, idx.Y, seed + 17, 0.09f);
        var intensity = (int) stage / 3f;
        var threshold = MathHelper.Lerp(0.58f, 0.36f, intensity);
        return n > threshold;
    }

    public static bool IsLavaFloor(Vector2i idx, int seed)
    {
        var n = Fbm(idx.X, idx.Y, seed, 0.11f);
        return n > 0.55f;
    }

    public static bool IsWaterFloor(Vector2i idx, int seed)
    {
        var n = Fbm(idx.X, idx.Y, seed + 41, 0.11f);
        return n > 0.55f && !IsLavaFloor(idx, seed);
    }

    public static bool ShouldRemapMob(int entityHash, int seed, SchizophreniaStage stage)
    {
        var chance = stage switch
        {
            SchizophreniaStage.Latent => 0.45f,
            SchizophreniaStage.Simple => 0.65f,
            SchizophreniaStage.Acute => 0.9f,
            _ => 0f,
        };
        var v = (HashCode.Combine(entityHash, seed) & 255) / 255f;
        return v <= chance;
    }

    public static bool ShouldRemapItem(int entityHash, int seed, SchizophreniaStage stage)
    {
        if (stage < SchizophreniaStage.Latent)
            return false;
        var chance = stage switch
        {
            SchizophreniaStage.Latent => 0.35f,
            SchizophreniaStage.Simple => 0.45f,
            SchizophreniaStage.Acute => 0.55f,
            _ => 0f,
        };
        var v = (HashCode.Combine(entityHash, seed, 23) & 255) / 255f;
        return v <= chance;
    }

    public static PsychiatryRemapPool PickPool(SchizophreniaStage stage, int entityHash, int seed)
    {
        if (stage >= SchizophreniaStage.Acute)
        {
            return (HashCode.Combine(entityHash, seed, 9) & 255) < 200
                ? PsychiatryRemapPool.Monster
                : PsychiatryRemapPool.Animal;
        }

        return PsychiatryRemapPool.Animal;
    }

    public static PsychiatryMobKind PickMobKind(int entityHash, int seed, PsychiatryRemapPool pool, bool preferCow)
    {
        if (preferCow && pool == PsychiatryRemapPool.Animal)
            return PsychiatryMobKind.Cow;

        var roll = HashCode.Combine(entityHash, seed, 11) & 255;

        if (pool == PsychiatryRemapPool.Animal)
        {
            return (roll % 7) switch
            {
                0 => PsychiatryMobKind.Cow,
                1 => PsychiatryMobKind.Monkey,
                2 => PsychiatryMobKind.Mouse,
                3 => PsychiatryMobKind.Corgi,
                4 => PsychiatryMobKind.Spider,
                5 => PsychiatryMobKind.Snake,
                _ => PsychiatryMobKind.Carp,
            };
        }

        if (roll < 40)
            return PsychiatryMobKind.FleshGolem;
        if (roll < 70)
            return PsychiatryMobKind.Goliath;
        if (roll < 95)
            return PsychiatryMobKind.SpaceDragon;
        if (roll < 115)
            return PsychiatryMobKind.MiniDragonFire;
        if (roll < 135)
            return PsychiatryMobKind.MiniDragonToxic;
        if (roll < 150)
            return PsychiatryMobKind.MiniDragonIce;
        if (roll < 175)
            return PsychiatryMobKind.Basilisk;
        if (roll < 200)
            return PsychiatryMobKind.Legion;
        if (roll < 225)
            return PsychiatryMobKind.Watcher;
        return PsychiatryMobKind.FleshGolem;
    }

    public static PsychiatryMobKind PickItemKind(int entityHash, int seed)
    {
        var roll = HashCode.Combine(entityHash, seed, 29) % 4;
        return roll switch
        {
            0 => PsychiatryMobKind.Mouse,
            1 => PsychiatryMobKind.Snake,
            2 => PsychiatryMobKind.Corgi,
            _ => PsychiatryMobKind.Monkey,
        };
    }

    public static bool PreferCow(int entityHash, int seed) =>
        (HashCode.Combine(entityHash, seed, 3) & 1) == 0;

    public static string MeatWallState() => "full";

    private static float Fbm(int x, int y, int seed, float scale)
    {
        var n = 0f;
        var amp = 1f;
        var freq = scale;
        var norm = 0f;
        for (var o = 0; o < 4; o++)
        {
            n += ValueNoise(x * freq, y * freq, seed + o * 131) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }

        return n / norm;
    }

    private static float ValueNoise(float x, float y, int seed)
    {
        var x0 = (int) MathF.Floor(x);
        var y0 = (int) MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        var a = Hash01(x0, y0, seed);
        var b = Hash01(x0 + 1, y0, seed);
        var c = Hash01(x0, y0 + 1, seed);
        var d = Hash01(x0 + 1, y0 + 1, seed);
        return MathHelper.Lerp(MathHelper.Lerp(a, b, fx), MathHelper.Lerp(c, d, fx), fy);
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            var h = x * 374761393 + y * 668265263 + seed * 982451653;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float) 0xFFFFFF;
        }
    }
}
