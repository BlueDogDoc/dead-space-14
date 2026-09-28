// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using System.Numerics;
using Content.Shared.DeadSpace.Psychiatry;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.DeadSpace.Psychiatry;

public enum PsychiatryScareKind : byte
{
    FleetingGhost,
    Walker,
    Laser,
    Fireball,
}

public sealed class PsychiatryScareOverlay : Overlay
{
    private readonly IEntityManager _ent;
    private readonly SharedTransformSystem _xform;
    private readonly IGameTiming _timing;
    private readonly IResourceCache _resources;
    private readonly IRobustRandom _random;
    private readonly List<ScareFx> _fx = new();

    private Texture? _ghostTex;
    private Texture? _carpTex;
    private Texture? _dragonTex;
    private Texture? _goliathTex;
    private Texture? _laserTex;
    private Texture? _fireballTex;
    private bool _texTried;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public SchizophreniaStage Stage;
    public int Seed;
    public EntityUid? Subject;

    public float ScareMinSec = 30f;
    public float ScareMaxSec = 120f;

    public Action<PsychiatryScareKind>? PlaySound;

    private float _nextScareAt;

    public PsychiatryScareOverlay(IEntityManager ent, SharedTransformSystem xform, IGameTiming timing, IRobustRandom random)
    {
        _ent = ent;
        _xform = xform;
        _timing = timing;
        _random = random;
        _resources = IoCManager.Resolve<IResourceCache>();
        ZIndex = 210;
        _nextScareAt = (float) timing.CurTime.TotalSeconds + random.NextFloat(ScareMinSec, ScareMaxSec);
    }

    public void Configure(EntityUid subject, SchizophreniaComponent schizo)
    {
        Subject = subject;
        Seed = schizo.Seed;
        Stage = schizo.Stage;
        EnsureTex();
    }

    public void Clear()
    {
        _fx.Clear();
        Subject = null;
    }

    public void Tick(float dt)
    {
        if (Subject is not { } subject || !_ent.EntityExists(subject) || Stage < SchizophreniaStage.Acute)
        {
            _fx.Clear();
            return;
        }

        var t = (float) _timing.CurTime.TotalSeconds;
        if (t >= _nextScareAt)
        {
            _nextScareAt = t + _random.NextFloat(ScareMinSec, ScareMaxSec);
            SpawnScare(subject);
        }

        for (var i = _fx.Count - 1; i >= 0; i--)
        {
            var fx = _fx[i];
            fx.Age += dt;
            if (fx.Age >= fx.MaxAge)
                _fx.RemoveAt(i);
            else
                _fx[i] = fx;
        }
    }

    private void SpawnScare(EntityUid subject)
    {
        var origin = _xform.GetWorldPosition(subject);
        var roll = HashCode.Combine(Seed, (int) (_timing.CurTime.TotalSeconds), _fx.Count) & 255;

        PsychiatryScareKind kind;
        if (roll < 40)
            kind = PsychiatryScareKind.Fireball;
        else if (roll < 90)
            kind = PsychiatryScareKind.Laser;
        else if (roll < 200)
            kind = PsychiatryScareKind.Walker;
        else
            kind = PsychiatryScareKind.FleetingGhost;

        var axis = (roll >> 2) & 3;
        var dir = Cardinal(axis);
        var lateral = Perpendicular(dir) * (_random.NextFloat(1.5f, 2.5f) * (_random.Prob(0.5f) ? 1f : -1f));
        var from = origin - dir * 8f + lateral;

        var speed = kind switch
        {
            PsychiatryScareKind.Laser => 11f,
            PsychiatryScareKind.Fireball => 7f,
            PsychiatryScareKind.Walker => 3.5f,
            _ => 1.6f,
        };

        var maxAge = kind switch
        {
            PsychiatryScareKind.Walker => 3.5f,
            PsychiatryScareKind.Fireball => 1.7f,
            PsychiatryScareKind.Laser => 1.2f,
            _ => 1.4f,
        };

        _fx.Add(new ScareFx
        {
            Kind = kind,
            Origin = from,
            Velocity = dir * speed,
            MaxAge = maxAge,
            Phase = (roll & 31) / 10f,
            Size = kind == PsychiatryScareKind.Fireball ? 1.25f : 1f,
            WalkerVariant = (byte) ((roll >> 4) & 3),
            Angle = MathF.Abs(dir.Y) > MathF.Abs(dir.X) ? MathF.PI / 2f : 0f,
        });

        PlaySound?.Invoke(kind);
    }

    private static Vector2 Cardinal(int axis) => axis switch
    {
        0 => new Vector2(1f, 0f),
        1 => new Vector2(-1f, 0f),
        2 => new Vector2(0f, 1f),
        _ => new Vector2(0f, -1f),
    };

    private static Vector2 Perpendicular(Vector2 dir) => new(-dir.Y, dir.X);

    private void EnsureTex()
    {
        if (_texTried)
            return;
        _texTried = true;

        _ghostTex = TryFrame("/Textures/Mobs/Ghosts/ghost_human.rsi", "icon")
                    ?? TryFrame("/Textures/Mobs/Ghosts/ghost_human.rsi", "animated");
        _carpTex = TryFrame("/Textures/Mobs/Aliens/Carps/space.rsi", "alive");
        _dragonTex = TryFrame("/Textures/_DeadSpace/Mobs/Aliens/MiniDragon.rsi", "alive")
                     ?? TryFrame("/Textures/Mobs/Aliens/Carps/dragon.rsi", "alive");
        _goliathTex = TryFrame("/Textures/_DeadSpace/Lavaland/Mobs/lavaland_monsters.rsi", "goliath")
                      ?? TryFrame("/Textures/Mobs/Aliens/Asteroid/goliath.rsi", "goliath");
        _laserTex = TryFrame("/Textures/Objects/Weapons/Guns/Projectiles/projectiles.rsi", "u_laser")
                    ?? TryFrame("/Textures/Objects/Weapons/Guns/Projectiles/projectiles_tg.rsi", "omnilaser");
        _fireballTex = TryFrame("/Textures/_DeadSpace/Lavaland/Effects/AshDrakeFireball.rsi", "fireball")
                       ?? TryFrame("/Textures/Objects/Weapons/Guns/Projectiles/magic.rsi", "fireball");
    }

    private Texture? TryFrame(string path, string state)
    {
        if (!_resources.TryGetResource<RSIResource>(path, out var rsi))
            return null;
        if (!rsi.RSI.TryGetState(state, out var st))
            return null;
        var frames = st.GetFrames(RsiDirection.South);
        return frames.Length > 0 ? frames[0] : null;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (Stage < SchizophreniaStage.Acute || Subject is not { } subject || !_ent.EntityExists(subject))
            return;

        var handle = args.WorldHandle;
        var t = (float) _timing.CurTime.TotalSeconds;

        foreach (var fx in _fx)
        {
            var progress = fx.Age / Math.Max(fx.MaxAge, 0.01f);
            var pos = fx.Origin + fx.Velocity * fx.Age;
            var fade = progress < 0.12f
                ? progress / 0.12f
                : progress > 0.8f
                    ? (1f - progress) / 0.2f
                    : 1f;
            fade = Math.Clamp(fade, 0f, 1f);

            Texture? tex = fx.Kind switch
            {
                PsychiatryScareKind.FleetingGhost => _ghostTex,
                PsychiatryScareKind.Walker => fx.WalkerVariant switch
                {
                    0 => _carpTex,
                    1 => _dragonTex,
                    2 => _goliathTex,
                    _ => _dragonTex,
                },
                PsychiatryScareKind.Laser => _laserTex,
                PsychiatryScareKind.Fireball => _fireballTex,
                _ => null,
            };

            var size = new Vector2(fx.Size, fx.Size);
            if (fx.Kind == PsychiatryScareKind.Fireball)
                size = new Vector2(1.35f, 1.35f);
            if (fx.Kind == PsychiatryScareKind.Laser)
                size = new Vector2(0.95f, 0.28f);

            if (fx.Kind == PsychiatryScareKind.Walker && fx.WalkerVariant == 2)
                size *= 1.35f;

            var wobble = fx.Kind == PsychiatryScareKind.FleetingGhost
                ? new Vector2(0f, MathF.Sin(t * 3f + fx.Phase) * 0.15f)
                : Vector2.Zero;

            var tint = Color.White.WithAlpha(0.5f + 0.45f * fade);

            if (tex != null)
            {
                var angle = fx.Kind == PsychiatryScareKind.Laser ? fx.Angle : 0f;
                handle.DrawTextureRect(tex, new Box2Rotated(Box2.CenteredAround(pos + wobble, size), angle, pos + wobble), tint);
            }
            else
            {
                handle.DrawRect(Box2.CenteredAround(pos + wobble, size), new Color(0.9f, 0.95f, 1f, tint.A));
            }
        }
    }

    private struct ScareFx
    {
        public PsychiatryScareKind Kind;
        public Vector2 Origin;
        public Vector2 Velocity;
        public float Age;
        public float MaxAge;
        public float Phase;
        public float Size;
        public byte WalkerVariant;
        public float Angle;
    }
}
