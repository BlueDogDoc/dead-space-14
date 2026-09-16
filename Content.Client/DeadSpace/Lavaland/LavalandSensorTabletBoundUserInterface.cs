// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared.DeadSpace.Lavaland;
using JetBrains.Annotations;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client.DeadSpace.Lavaland;

[UsedImplicitly]
public sealed class LavalandSensorTabletBoundUserInterface(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    private LavalandSensorTabletWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<LavalandSensorTabletWindow>();
        _window.OnClose += Close;
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is LavalandSensorTabletState radar)
            _window?.SetState(radar);
    }
}

public sealed class LavalandSensorTabletWindow : DefaultWindow
{
    private readonly LavalandRadarControl _radar = new();
    private readonly Label _status = new()
    {
        HorizontalAlignment = Control.HAlignment.Center,
        Text = "Нет активных координатных датчиков на лаваленде",
    };
    private readonly Label _coordinates = new() { Text = "X: ---  Y: ---" };

    public LavalandSensorTabletWindow()
    {
        Title = "Планшет разведки лаваленда";
        MinSize = new Vector2(720, 610);
        SetSize = new Vector2(720, 610);

        var legendTop = new BoxContainer
        {
            HorizontalAlignment = Control.HAlignment.Center,
        };
        var legendBottom = new BoxContainer { HorizontalAlignment = Control.HAlignment.Center };
        AddLegend(legendTop, "● шахтёр", Color.White);
        AddLegend(legendTop, "● погибший", Color.Black);
        AddLegend(legendTop, "● фауна", Color.Red);
        AddLegend(legendBottom, "■ руда", Color.Gold);
        AddLegend(legendBottom, "■ лава", Color.OrangeRed);
        AddLegend(legendBottom, "■ база", new Color(115, 210, 230));
        AddLegend(legendBottom, "◯ моркит", new Color(40, 220, 190));
        var recenter = new Button { Text = "Центрировать карту" };
        recenter.OnPressed += _ => _radar.Recenter();
        _radar.CoordinatesChanged += coordinates =>
            _coordinates.Text = $"X: {coordinates.X,7:0.0}  Y: {coordinates.Y,7:0.0}";
        var hint = new Label { Text = "Колесо — масштаб · ЛКМ с перетаскиванием — перемещение", HorizontalAlignment = Control.HAlignment.Center };
        var footer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Children =
            {
                _coordinates,
                new Control { HorizontalExpand = true },
                recenter,
            },
        };
        var frameStyle = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#07100e"),
            BorderColor = Color.FromHex("#b96b2d"),
            BorderThickness = new Thickness(2),
            ContentMarginLeftOverride = 5,
            ContentMarginRightOverride = 5,
            ContentMarginTopOverride = 5,
            ContentMarginBottomOverride = 5,
        };
        var radarFrame = new PanelContainer
        {
            PanelOverride = frameStyle,
            HorizontalExpand = true,
            VerticalExpand = true,
            Children = { _radar },
        };
        var footerFrame = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#17110c"),
                BorderColor = Color.FromHex("#75431f"),
                BorderThickness = new Thickness(1),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 3,
                ContentMarginBottomOverride = 3,
            },
            Children = { footer },
        };
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Children = { legendTop, legendBottom, hint, _status, radarFrame, footerFrame },
        };
        _radar.VerticalExpand = true;
        _radar.HorizontalExpand = true;
        var tabletBody = new PanelContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#57240f"),
                BorderColor = Color.FromHex("#d8792f"),
                BorderThickness = new Thickness(4),
                ContentMarginLeftOverride = 10,
                ContentMarginRightOverride = 10,
                ContentMarginTopOverride = 10,
                ContentMarginBottomOverride = 10,
            },
            Children = { content },
        };
        Contents.AddChild(tabletBody);
    }

    private static void AddLegend(BoxContainer legend, string text, Color color)
    {
        legend.AddChild(new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = new Color(65, 65, 65) },
            Margin = new Thickness(3),
            Children = { new Label { Text = text, Modulate = color, Margin = new Thickness(4) } },
        });
    }

    public void SetState(LavalandSensorTabletState state)
    {
        _status.Visible = !state.Points.Exists(p => p.Kind is LavalandRadarPointKind.LivingMiner or LavalandRadarPointKind.DeadMiner);
        _radar.SetPoints(state.Points);
    }
}

public sealed class LavalandRadarControl : MapGridControl
{
    private List<LavalandRadarPoint> _points = new();
    private bool _initialized;
    private bool _dragging;
    private float _interferenceTime;
    public event Action<Vector2>? CoordinatesChanged;
    protected override bool Draggable => true;

    public LavalandRadarControl() : base(4f, 600f, 48f)
    {
        MinSize = new Vector2(650, 360);
        SetSize = new Vector2(650, 360);
        RectClipContent = true;
    }

    public void SetPoints(List<LavalandRadarPoint> points)
    {
        _points = points;
        if (!_initialized && points.Count > 0)
        {
            Recenter();
            _initialized = true;
        }
    }

    public void Recenter()
    {
        if (_points.Count == 0)
            return;
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var point in _points)
        {
            min = Vector2.Min(min, point.Position - new Vector2(point.Size));
            max = Vector2.Max(max, point.Position + new Vector2(point.Size));
        }
        Offset = (min + max) / 2;
        WorldRange = ActualRadarRange = Math.Clamp(MathF.Max(max.X - min.X, max.Y - min.Y) * 0.6f, 12f, 600f);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIClick || args.Function == EngineKeyFunctions.Use)
        {
            _dragging = true;
            args.Handle();
        }
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIClick || args.Function == EngineKeyFunctions.Use)
            _dragging = false;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        if (_dragging)
            Offset -= new Vector2(args.Relative.X, -args.Relative.Y) / GetScale();
        var relative = args.RelativePosition - PixelSize / 2;
        CoordinatesChanged?.Invoke(Offset + new Vector2(relative.X, -relative.Y) / GetScale());
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _dragging = false;
    }

    private float GetScale() => MathF.Min(PixelSize.X, PixelSize.Y) / (MathF.Max(WorldRange, 0.1f) * 2);

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _interferenceTime += args.DeltaSeconds;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var bounds = new UIBox2(0, 0, PixelSize.X, PixelSize.Y);
        handle.DrawRect(bounds, Color.FromHex("#090604"));
        var scale = GetScale();
        var glitch = _interferenceTime % 7f > 6.55f;

        foreach (var point in _points)
        {
            var relative = (point.Position - Offset) * scale;
            var position = PixelSize / 2 + new Vector2(relative.X, -relative.Y);
            if (glitch)
                position.X += MathF.Round(MathF.Sin(MathF.Floor(position.Y / 16f) * 2.1f + _interferenceTime * 35f) * 5f);
            var radius = MathF.Max(1.5f, scale * point.Size);
            if (point.Kind == LavalandRadarPointKind.MorkiteSearchArea)
            {
                DrawMorkiteHint(handle, position, MathF.Max(12f, scale * point.Size), point.Color);
            }
            else if (point.Kind is not (LavalandRadarPointKind.LivingMiner or LavalandRadarPointKind.DeadMiner or LavalandRadarPointKind.Fauna))
            {
                handle.DrawRect(new UIBox2(position.X - radius, position.Y - radius,
                    position.X + radius, position.Y + radius), point.Color);
            }
            else
            {
                if (glitch)
                {
                    handle.DrawCircle(position + new Vector2(-3, 0), MathF.Max(3f, radius),
                        new Color(235, 65, 65, 90));
                    handle.DrawCircle(position + new Vector2(3, 0), MathF.Max(3f, radius),
                        new Color(65, 210, 235, 90));
                }
                if (point.Kind == LavalandRadarPointKind.DeadMiner)
                    handle.DrawCircle(position, MathF.Max(3f, radius) + 1.5f, Color.LightGray);
                handle.DrawCircle(position, MathF.Max(3f, radius), point.Color);
            }
        }
        DrawInterference(handle);
    }

    private void DrawMorkiteHint(DrawingHandleScreen handle, Vector2 center, float radius, Color color)
    {
        const int segments = 18;
        var previous = center + new Vector2(radius, 0);
        for (var i = 1; i <= segments; i++)
        {
            var angle = MathF.Tau * i / segments;
            var wobble = 1f + 0.12f * MathF.Sin(i * 2.73f + _interferenceTime * 1.7f);
            var current = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * wobble;
            handle.DrawLine(previous, current, color);
            previous = current;
        }
    }

    private void DrawInterference(DrawingHandleScreen handle)
    {
        // Quantized static and brief block dropouts imitate an unstable low-resolution display.
        var phase = (int) (_interferenceTime * 12f);
        var burst = _interferenceTime % 7f > 6.55f;
        for (var i = 0; i < (burst ? 180 : 35); i++)
        {
            var seed = unchecked(phase * 1103515245 + i * 12345);
            seed ^= seed >> 13;
            seed = unchecked(seed * 1274126177);
            var positive = seed & int.MaxValue;
            var y = positive % Math.Max(1, (int) PixelSize.Y) / 4 * 4;
            var x = (positive >> 10) % Math.Max(1, (int) PixelSize.X) / 4 * 4;
            var size = burst && i % 17 == 0 ? 16 : 2;
            var color = i % 3 == 0
                ? new Color(0, 0, 0, burst ? (byte) 85 : (byte) 20)
                : new Color(100, 195, 155, burst ? (byte) 65 : (byte) 18);
            handle.DrawRect(new UIBox2(x, y, Math.Min(PixelSize.X, x + size),
                Math.Min(PixelSize.Y, y + size)), color);
        }
    }
}
