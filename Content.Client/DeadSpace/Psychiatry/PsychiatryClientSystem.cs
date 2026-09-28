// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Content.Client.Examine;
using Content.Client.Humanoid;
using Content.Client.UserInterface.Systems.Chat;
using Content.Shared.Chat;
using Content.Shared.DeadSpace.CCCCVars;
using Content.Shared.DeadSpace.Psychiatry;
using Content.Shared.Examine;
using Content.Shared.Follower.Components;
using Content.Shared.Humanoid;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.StatusIcon.Components;
using Content.Shared.Traits.Assorted;
using Robust.Client.Audio;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.DeadSpace.Psychiatry;

public sealed class PsychiatryClientSystem : SharedPsychiatrySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;

    private static readonly ResPath CowRsi = new("Mobs/Animals/cow.rsi");
    private static readonly ResPath MonkeyRsi = new("Mobs/Animals/monkey.rsi");
    private static readonly ResPath MouseRsi = new("Mobs/Animals/mouse.rsi");
    private static readonly ResPath CorgiRsi = new("Mobs/Pets/corgi.rsi");
    private static readonly ResPath SpiderRsi = new("Mobs/Animals/spider.rsi");
    private static readonly ResPath SnakeRsi = new("Mobs/Animals/snake.rsi");
    private static readonly ResPath CarpRsi = new("Mobs/Aliens/Carps/space.rsi");
    private static readonly ResPath SpaceDragonRsi = new("Mobs/Aliens/Carps/dragon.rsi");
    private static readonly ResPath MiniDragonRsi = new("_DeadSpace/Mobs/Aliens/MiniDragon.rsi");
    private static readonly ResPath MeatWallRsi = new("Structures/Walls/meat.rsi");
    private static readonly ResPath FleshRsi = new("Mobs/Aliens/flesh.rsi");
    private static readonly ResPath GoliathRsi = new("_DeadSpace/Lavaland/Mobs/lavaland_monsters.rsi");
    private static readonly ResPath BasiliskRsi = new("Mobs/Aliens/Asteroid/basilisk.rsi");
    private static readonly ResPath WatcherRsi = new("Mobs/Aliens/Lavaland/watcher.rsi");

    private static readonly SoundPathSpecifier SfxGhost = new("/Audio/_DeadSpace/Lavaland/SpectralBlade/ghost2.ogg");
    private static readonly SoundPathSpecifier SfxWalker = new("/Audio/_DeadSpace/Lavaland/AshDrake/demon_attack1.ogg");
    private static readonly SoundPathSpecifier SfxLaser = new("/Audio/Weapons/Guns/Gunshots/laser_cannon.ogg");
    private static readonly SoundPathSpecifier SfxFireball = new("/Audio/_DeadSpace/Lavaland/AshDrake/fireball.ogg");
    private static readonly SoundPathSpecifier SfxFallback = new("/Audio/Magic/fireball.ogg");

    private PsychiatryFloorOverlay? _floorOverlay;
    private PsychiatryScareOverlay? _scareOverlay;

    private enum RemapLayer
    {
        Fake,
    }
    private EntityUid? _activeSubject;
    private float _discoverAccum;
    private bool _fxEnabled = true;
    private bool _typingSent;
    private TimeSpan _nextTypingSend;
    private string _chatSnapshot = "";
    private bool _chatReady;
    private TimeSpan _lastEdit;
    private TimeSpan _lastParacusia;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(HumanoidAppearanceSystem));
        SubscribeNetworkEvent<PsychiatryWhisperEvent>(OnWhisper);
        SubscribeLocalEvent<PsychiatryRemapComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PsychiatryRemapComponent, ClientExaminedEvent>(OnClientExamined);
        SubscribeLocalEvent<PsychiatryRemapComponent, GetStatusIconsEvent>(OnGetStatusIcons);
        Subs.CVar(_cfg, CCCCVars.PsychiatryClientFx, v =>
        {
            _fxEnabled = v;
            if (!v)
            {
                ClearVisuals();
                RemoveOverlays();
            }
        }, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_timing.IsFirstTimePredicted)
            return;

        var text = "";
        foreach (var chat in _ui.GetUIController<ChatUIController>().Chats)
            text += chat.ChatInput.Input.Text;

        if (!_chatReady)
        {
            _chatSnapshot = text;
            _chatReady = true;
        }
        else if (!string.Equals(text, _chatSnapshot, StringComparison.Ordinal))
        {
            _chatSnapshot = text;
            _lastEdit = _timing.CurTime;
        }

        var typing = _lastEdit != TimeSpan.Zero && _timing.CurTime - _lastEdit < TimeSpan.FromSeconds(0.5);

        if (typing == _typingSent && (!typing || _timing.CurTime < _nextTypingSend))
            return;

        _typingSent = typing;
        _nextTypingSend = _timing.CurTime + TimeSpan.FromSeconds(0.25);
        RaiseNetworkEvent(new PsychiatryTypingRequestEvent(typing));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        ClearVisuals();
        RemoveOverlays();
    }

    public bool TryGetSubject(out EntityUid subject, out SchizophreniaComponent schizo)
    {
        subject = default!;
        schizo = null!;

        if (!_cfg.GetCVar(CCCCVars.PsychiatryEnabled) || !_fxEnabled)
            return false;

        var local = _player.LocalEntity;
        if (local == null)
            return false;

        if (TryComp(local.Value, out SchizophreniaComponent? self) && IsIll(self.Stage))
        {
            subject = local.Value;
            schizo = self;
            return true;
        }

        if (TryComp(local.Value, out FollowerComponent? follower) &&
            TryComp(follower.Following, out SchizophreniaComponent? followed) &&
            IsIll(followed.Stage))
        {
            subject = follower.Following;
            schizo = followed;
            return true;
        }

        return false;
    }

    private static bool IsIll(SchizophreniaStage stage)
    {
        return stage is >= SchizophreniaStage.Latent and <= SchizophreniaStage.Acute;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (!_timing.IsFirstTimePredicted)
            return;

        if (!TryGetSubject(out var subject, out var schizo))
        {
            if (_activeSubject != null)
            {
                ClearVisuals();
                RemoveOverlays();
                _activeSubject = null;
                _lastParacusia = TimeSpan.Zero;
            }
            return;
        }

        _activeSubject = subject;
        EnsureOverlays(schizo);
        _floorOverlay?.UpdateClusters(subject, schizo);
        NoteParacusia(subject);
        if (_scareOverlay != null)
        {
            _scareOverlay.ScareMinSec = _cfg.GetCVar(CCCCVars.PsychiatryScareMinSec);
            _scareOverlay.ScareMaxSec = _cfg.GetCVar(CCCCVars.PsychiatryScareMaxSec);
            _scareOverlay.Configure(subject, schizo);
            _scareOverlay.Tick(frameTime);
        }

        RefreshActiveRemaps(schizo);

        _discoverAccum += frameTime;
        if (_discoverAccum >= 0.2f)
        {
            _discoverAccum = 0f;
            RemapNearby(subject, schizo);
        }
    }

    private void EnsureOverlays(SchizophreniaComponent schizo)
    {
        if (_floorOverlay == null)
        {
            _floorOverlay = new PsychiatryFloorOverlay(EntityManager, _map);
            _overlays.AddOverlay(_floorOverlay);
        }

        if (schizo.Stage >= SchizophreniaStage.Acute && _scareOverlay == null)
        {
            _scareOverlay = new PsychiatryScareOverlay(EntityManager, _xform, _timing, _random);
            _scareOverlay.PlaySound = PlayScareSound;
            _overlays.AddOverlay(_scareOverlay);
        }
        else if (schizo.Stage < SchizophreniaStage.Acute && _scareOverlay != null)
        {
            _scareOverlay.PlaySound = null;
            _scareOverlay.Clear();
            _overlays.RemoveOverlay(_scareOverlay);
            _scareOverlay = null;
        }
    }

    private void PlayScareSound(PsychiatryScareKind kind)
    {
        var path = kind switch
        {
            PsychiatryScareKind.FleetingGhost => SfxGhost,
            PsychiatryScareKind.Walker => SfxWalker,
            PsychiatryScareKind.Laser => SfxLaser,
            PsychiatryScareKind.Fireball => SfxFireball,
            _ => SfxGhost,
        };

        try
        {
            _audio.PlayGlobal(path, Filter.Local(), false, AudioParams.Default.WithVolume(-3f));
        }
        catch
        {
            try
            {
                _audio.PlayGlobal(SfxFallback, Filter.Local(), false, AudioParams.Default.WithVolume(-3f));
            }
            catch
            {
            }
        }

        RaiseNetworkEvent(new PsychiatryUnrealSoundEvent(0.9f, 0.9f));
    }

    private void NoteParacusia(EntityUid subject)
    {
        if (!TryComp<ParacusiaComponent>(subject, out var paracusia))
        {
            _lastParacusia = TimeSpan.Zero;
            return;
        }

        if (_lastParacusia != TimeSpan.Zero && paracusia.NextIncidentTime > _lastParacusia)
            RaiseNetworkEvent(new PsychiatryUnrealSoundEvent(0.9f, 0.85f));

        _lastParacusia = paracusia.NextIncidentTime;
    }

    private void RemoveOverlays()
    {
        if (_floorOverlay != null)
        {
            _floorOverlay.Clear();
            _overlays.RemoveOverlay(_floorOverlay);
            _floorOverlay = null;
        }

        if (_scareOverlay != null)
        {
            _scareOverlay.PlaySound = null;
            _scareOverlay.Clear();
            _overlays.RemoveOverlay(_scareOverlay);
            _scareOverlay = null;
        }
    }

    private void RemapNearby(EntityUid subject, SchizophreniaComponent schizo)
    {
        var keep = new HashSet<EntityUid>();
        var origin = _xform.GetMapCoordinates(subject);
        var rangeSq = 12f * 12f;
        var gridUid = Transform(subject).GridUid;
        MapGridComponent? grid = null;
        if (gridUid != null)
            TryComp(gridUid.Value, out grid);

        var query = EntityQueryEnumerator<SpriteComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var sprite, out var xform, out var meta))
        {
            if (uid == subject || xform.MapID != origin.MapId)
                continue;
            if ((_xform.GetWorldPosition(xform) - origin.Position).LengthSquared() > rangeSq)
                continue;

            Vector2i? tileIdx = null;
            if (gridUid != null && grid != null && xform.GridUid == gridUid)
                tileIdx = _map.TileIndicesFor(gridUid.Value, grid, xform.Coordinates);

            if (!TryPickRemap(uid, meta, schizo, tileIdx, out var rsi, out var state, out var fakeName, out var kind, out var isWall))
                continue;

            keep.Add(uid);
            ApplyRemap(uid, sprite, rsi, state, fakeName, isWall, tileIdx, kind);
        }

        var stale = new List<EntityUid>();
        var remapQuery = EntityQueryEnumerator<PsychiatryRemapComponent>();
        while (remapQuery.MoveNext(out var uid, out _))
        {
            if (!keep.Contains(uid))
                stale.Add(uid);
        }

        foreach (var uid in stale)
            Restore(uid);
    }

    private void RefreshActiveRemaps(SchizophreniaComponent schizo)
    {
        var t = (float) _timing.CurTime.TotalSeconds;
        var q = EntityQueryEnumerator<PsychiatryRemapComponent, SpriteComponent>();
        while (q.MoveNext(out var uid, out var remap, out var sprite))
        {
            if (remap.IsWall)
            {
                var pulse = 0.65f + 0.35f * MathF.Sin(t * 2.8f + uid.GetHashCode() * 0.01f);
                remap.DrawColor = new Color(1f, pulse, pulse * 0.85f);
            }

            SyncRemapLayer(uid, sprite, remap);
        }
    }

    private bool TryPickRemap(EntityUid uid, MetaDataComponent meta, SchizophreniaComponent schizo,
        Vector2i? tileIdx,
        out ResPath rsi, out string state, out string fakeName, out PsychiatryMobKind? kind, out bool isWall)
    {
        rsi = default;
        state = string.Empty;
        fakeName = meta.EntityName;
        kind = null;
        isWall = false;

        var protoId = meta.EntityPrototype?.ID ?? string.Empty;

        if (schizo.Stage >= SchizophreniaStage.Simple && IsWallPrototype(protoId))
        {
            if (tileIdx is { } t && !PsychiatryPattern.IsMeatWall(t, schizo.Seed, schizo.Stage))
                return false;

            rsi = MeatWallRsi;
            state = PsychiatryPattern.MeatWallState();
            fakeName = Loc.GetString("psychiatry-name-meat-wall");
            isWall = true;
            return true;
        }

        if (HasComp<HumanoidAppearanceComponent>(uid) || HasComp<MobStateComponent>(uid))
        {
            if (!PsychiatryPattern.ShouldRemapMob(uid.GetHashCode(), schizo.Seed, schizo.Stage))
                return false;

            var pool = PsychiatryPattern.PickPool(schizo.Stage, uid.GetHashCode(), schizo.Seed);
            var chefLike = meta.EntityName.Contains("Chef", StringComparison.OrdinalIgnoreCase)
                           || meta.EntityName.Contains("Cook", StringComparison.OrdinalIgnoreCase)
                           || meta.EntityName.Contains("Повар", StringComparison.OrdinalIgnoreCase);
            var pick = PsychiatryPattern.PickMobKind(
                uid.GetHashCode(),
                schizo.Seed,
                pool,
                preferCow: chefLike || PsychiatryPattern.PreferCow(uid.GetHashCode(), schizo.Seed));

            kind = pick;
            GetMobVisual(pick, out rsi, out state, out fakeName);
            return true;
        }

        if (HasComp<ItemComponent>(uid) &&
            Transform(uid).GridUid != null &&
            !_containers.IsEntityInContainer(uid) &&
            PsychiatryPattern.ShouldRemapItem(uid.GetHashCode(), schizo.Seed, schizo.Stage))
        {
            var pick = PsychiatryPattern.PickItemKind(uid.GetHashCode(), schizo.Seed);
            kind = pick;
            GetMobVisual(pick, out rsi, out state, out fakeName);
            return true;
        }

        return false;
    }

    private void GetMobVisual(PsychiatryMobKind kind, out ResPath rsi, out string state, out string fakeName)
    {
        switch (kind)
        {
            case PsychiatryMobKind.Cow:
                rsi = CowRsi; state = "cow";
                fakeName = Loc.GetString("psychiatry-name-cow");
                break;
            case PsychiatryMobKind.Monkey:
                rsi = MonkeyRsi; state = "monkey";
                fakeName = Loc.GetString("psychiatry-name-monkey");
                break;
            case PsychiatryMobKind.Mouse:
                rsi = MouseRsi; state = "mouse-0";
                fakeName = Loc.GetString("psychiatry-name-mouse");
                break;
            case PsychiatryMobKind.Corgi:
                rsi = CorgiRsi; state = "corgi";
                fakeName = Loc.GetString("psychiatry-name-corgi");
                break;
            case PsychiatryMobKind.Spider:
                rsi = SpiderRsi; state = "hunter";
                fakeName = Loc.GetString("psychiatry-name-spider");
                break;
            case PsychiatryMobKind.Snake:
                rsi = SnakeRsi; state = "snake";
                fakeName = Loc.GetString("psychiatry-name-snake");
                break;
            case PsychiatryMobKind.Carp:
                rsi = CarpRsi; state = "alive";
                fakeName = Loc.GetString("psychiatry-name-carp");
                break;
            case PsychiatryMobKind.SpaceDragon:
                rsi = SpaceDragonRsi; state = "alive";
                fakeName = Loc.GetString("psychiatry-name-dragon");
                break;
            case PsychiatryMobKind.MiniDragonFire:
                rsi = MiniDragonRsi; state = "alive";
                fakeName = Loc.GetString("psychiatry-name-mini-dragon-fire");
                break;
            case PsychiatryMobKind.MiniDragonIce:
                rsi = MiniDragonRsi; state = "alive";
                fakeName = Loc.GetString("psychiatry-name-mini-dragon-ice");
                break;
            case PsychiatryMobKind.MiniDragonToxic:
                rsi = MiniDragonRsi; state = "alive";
                fakeName = Loc.GetString("psychiatry-name-mini-dragon-toxic");
                break;
            case PsychiatryMobKind.Goliath:
                rsi = GoliathRsi; state = "goliath";
                fakeName = Loc.GetString("psychiatry-name-goliath");
                break;
            case PsychiatryMobKind.Basilisk:
                rsi = BasiliskRsi; state = "basilisk";
                fakeName = Loc.GetString("psychiatry-name-basilisk");
                break;
            case PsychiatryMobKind.Legion:
                rsi = GoliathRsi; state = "legion";
                fakeName = Loc.GetString("psychiatry-name-legion");
                break;
            case PsychiatryMobKind.Watcher:
                rsi = WatcherRsi; state = "base";
                fakeName = Loc.GetString("psychiatry-name-watcher");
                break;
            default:
                rsi = FleshRsi; state = "golem";
                fakeName = Loc.GetString("psychiatry-name-flesh");
                break;
        }
    }

    private static bool IsWallPrototype(string protoId)
    {
        if (string.IsNullOrEmpty(protoId))
            return false;
        if (protoId.Contains("Wallmount", StringComparison.Ordinal) ||
            protoId.Contains("Window", StringComparison.Ordinal))
            return false;
        return protoId.StartsWith("Wall", StringComparison.Ordinal);
    }

    private void ApplyRemap(EntityUid uid, SpriteComponent sprite, ResPath rsi, string state, string fakeName,
        bool isWall, Vector2i? tileIdx, PsychiatryMobKind? kind)
    {
        var remap = EnsureComp<PsychiatryRemapComponent>(uid);
        remap.FakeName = fakeName;
        remap.IsWall = isWall;
        remap.TileIndex = tileIdx;
        remap.MobKind = kind;
        remap.DrawRsi = rsi;
        remap.DrawState = state;
        remap.DrawColor = Color.White;
        SyncRemapLayer(uid, sprite, remap);
    }

    private void SyncRemapLayer(EntityUid uid, SpriteComponent sprite, PsychiatryRemapComponent remap)
    {
        if (remap.DrawState.Length == 0)
            return;

        var ent = (uid, sprite);
        var hasFake = _sprites.LayerMapTryGet(ent, RemapLayer.Fake, out var fake, false);
        if (remap.OriginalLayerVisible.Count == 0)
        {
            var seen = 0;
            foreach (ISpriteLayer layer in sprite.AllLayers)
            {
                if (!hasFake || seen != fake)
                    remap.OriginalLayerVisible.Add(layer.Visible);
                seen++;
            }
        }

        if (!hasFake)
        {
            fake = _sprites.AddLayer(ent, new SpriteSpecifier.Rsi(remap.DrawRsi, remap.DrawState));
            _sprites.LayerMapSet(ent, RemapLayer.Fake, fake);
        }
        else
        {
            _sprites.LayerSetRsi(ent, RemapLayer.Fake, remap.DrawRsi, remap.DrawState);
        }

        _sprites.LayerSetColor(ent, RemapLayer.Fake, remap.DrawColor);
        _sprites.LayerSetVisible(ent, RemapLayer.Fake, true);
        if (!_sprites.LayerMapTryGet(ent, RemapLayer.Fake, out fake, false))
            return;

        var i = 0;
        foreach (ISpriteLayer layer in sprite.AllLayers)
        {
            if (i != fake && layer.Visible)
                _sprites.LayerSetVisible(ent, i, false);
            i++;
        }
    }

    private void Restore(EntityUid uid)
    {
        if (!TryComp(uid, out PsychiatryRemapComponent? remap))
            return;

        if (TryComp(uid, out SpriteComponent? sprite))
        {
            var ent = (uid, sprite);
            if (!_sprites.RemoveLayer(ent, RemapLayer.Fake, false))
                RemoveUnmappedReplacement(ent, sprite, remap);

            var i = 0;
            foreach (ISpriteLayer _ in sprite.AllLayers)
            {
                var visible = i < remap.OriginalLayerVisible.Count
                    ? remap.OriginalLayerVisible[i]
                    : true;
                _sprites.LayerSetVisible(ent, i, visible);
                i++;
            }
        }

        RemCompDeferred<PsychiatryRemapComponent>(uid);
    }

    private void RemoveUnmappedReplacement(Entity<SpriteComponent> ent, SpriteComponent sprite, PsychiatryRemapComponent remap)
    {
        var guard = 4;
        while (guard-- > 0 && LayerCount(sprite) > remap.OriginalLayerVisible.Count)
        {
            var last = LayerCount(sprite) - 1;
            if (last < 0 || !LayerIsReplacement(sprite, last, remap))
                break;
            SpriteComponent? layerSprite = sprite;
            _sprites.RemoveLayer((ent.Owner, layerSprite), last);
        }
    }

    private static int LayerCount(SpriteComponent sprite)
    {
        var n = 0;
        foreach (var _ in sprite.AllLayers)
            n++;
        return n;
    }

    private static bool LayerIsReplacement(SpriteComponent sprite, int index, PsychiatryRemapComponent remap)
    {
        var i = 0;
        foreach (ISpriteLayer layer in sprite.AllLayers)
        {
            if (i == index)
            {
                var path = layer.Rsi?.Path.ToString() ?? string.Empty;
                return path.EndsWith(remap.DrawRsi.ToString(), StringComparison.Ordinal)
                       && layer.RsiState.ToString() == remap.DrawState;
            }

            i++;
        }

        return false;
    }

    private void ClearVisuals()
    {
        var toClear = new List<EntityUid>();
        var q = EntityQueryEnumerator<PsychiatryRemapComponent>();
        while (q.MoveNext(out var uid, out _))
            toClear.Add(uid);
        foreach (var uid in toClear)
            Restore(uid);
    }

    private void OnGetStatusIcons(Entity<PsychiatryRemapComponent> ent, ref GetStatusIconsEvent args)
    {
        if (!TryGetSubject(out _, out _))
            return;
        args.StatusIcons.Clear();
    }

    private void OnExamined(Entity<PsychiatryRemapComponent> ent, ref ExaminedEvent args)
    {
        if (!TryGetSubject(out _, out _))
            return;
        args.PushMarkup(Loc.GetString("psychiatry-examine-appears", ("name", ent.Comp.FakeName)));
    }

    private void OnClientExamined(Entity<PsychiatryRemapComponent> ent, ref ClientExaminedEvent ev)
    {
        var fake = ent.Comp.FakeName;
        if (string.IsNullOrEmpty(fake))
            return;

        foreach (var child in _ui.ModalRoot.Children)
        {
            if (child is not Popup popup)
                continue;
            ReplaceExamineTitle(popup, fake);
        }
    }

    private static void ReplaceExamineTitle(Control root, string fakeName)
    {
        foreach (var child in root.Children)
        {
            if (child is RichTextLabel label)
            {
                label.SetMessage(FormattedMessage.FromMarkupPermissive($"[bold]{FormattedMessage.EscapeText(fakeName)}[/bold]"));
                return;
            }

            ReplaceExamineTitle(child, fakeName);
        }
    }

    private void OnWhisper(PsychiatryWhisperEvent ev)
    {
        if (!TryGetSubject(out _, out var schizo) || schizo.Stage < SchizophreniaStage.Simple)
            return;

        var source = EntityUid.Invalid;
        if (ev.Source is { Valid: true } net)
            source = GetEntity(net);

        var channel = ev.AsRadio ? ChatChannel.Radio : ChatChannel.Local;
        var wrapped = ev.AsRadio
            ? Loc.GetString(
                "chat-radio-message-wrap-lang",
                ("channel-color", "#32cd32"),
                ("fontType", "Default"),
                ("fontSize", 12),
                ("verb", Loc.GetString("psychiatry-radio-verb")),
                ("language", Loc.GetString("psychiatry-radio-language")),
                ("channel", $"\\[{Loc.GetString("chat-radio-common")}\\]"),
                ("name", FormattedMessage.EscapeText(ev.SpeakerName)),
                ("message", FormattedMessage.EscapeText(ev.Message)),
                ("headset-color", "#32cd32"),
                ("job", ev.Job))
            : Loc.GetString(
                "chat-manager-entity-whisper-wrap-message",
                ("entityName", FormattedMessage.EscapeText(ev.SpeakerName)),
                ("message", FormattedMessage.EscapeText(ev.Message)));

        if (!ev.AsRadio && wrapped.StartsWith("chat-manager", StringComparison.Ordinal))
        {
            wrapped = $"[color=#AAAAAA][italic]{FormattedMessage.EscapeText(ev.SpeakerName)}[/italic] whispers, \"{FormattedMessage.EscapeText(ev.Message)}\"[/color]";
        }

        var msg = new ChatMessage(
            channel,
            ev.Message,
            wrapped,
            GetNetEntity(source),
            null);

        _ui.GetUIController<ChatUIController>().ProcessChatMessage(msg, speechBubble: false);

        var brainEv = new PsychiatryBrainActivityEvent(PsychiatryBrainRegion.Hearing, 0.8f);
        RaiseLocalEvent(ref brainEv);
        var voiceEv = new PsychiatryBrainActivityEvent(PsychiatryBrainRegion.Voice, 0.6f);
        RaiseLocalEvent(ref voiceEv);
        var arousalEv = new PsychiatryBrainActivityEvent(PsychiatryBrainRegion.Arousal, 0.5f);
        RaiseLocalEvent(ref arousalEv);
    }
}
