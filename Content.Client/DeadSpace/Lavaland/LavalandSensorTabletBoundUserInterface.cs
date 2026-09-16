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

    public LavalandSensorTabletWindow()
    {
        Title = "Планшет разведки лаваленда";
        MinSize = SetSize = new Vector2(720, 680);

        var legend = new BoxContainer
        {
            HorizontalAlignment = Control.HAlignment.Center,
        };
        AddLegend(legend, "● шахтёр", Color.White);
        AddLegend(legend, "● погибший", Color.Black);
        AddLegend(legend, "● фауна", Color.Red);
        AddLegend(legend, "■ руда", Color.Gold);
        AddLegend(legend, "■ лава", Color.OrangeRed);
        AddLegend(legend, "■ база", new Color(115, 210, 230));
        var recenter = new Button { Text = "Центрировать карту" };
        recenter.OnPressed += _ => _radar.Recenter();
        var hint = new Label { Text = "Колесо — масштаб · ЛКМ с перетаскиванием — перемещение", HorizontalAlignment = Control.HAlignment.Center };
        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children = { legend, hint, recenter, _status, _radar },
        };
        _radar.VerticalExpand = true;
        _radar.HorizontalExpand = true;
        Contents.AddChild(content);
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
    protected override bool Draggable => true;

    public LavalandRadarControl() : base(4f, 600f, 48f)
    {
        MinSize = new Vector2(640, 560);
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
            min = Vector2.Min(min, point.Position);
            max = Vector2.Max(max, point.Position);
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
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _dragging = false;
    }

    private float GetScale() => MathF.Min(PixelSize.X, PixelSize.Y) / (MathF.Max(WorldRange, 0.1f) * 2);

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var bounds = new UIBox2(0, 0, PixelSize.X, PixelSize.Y);
        handle.DrawRect(bounds, Color.FromHex("#090604"));
        if (_points.Count == 0)
            return;

        var scale = GetScale();

        foreach (var point in _points)
        {
            var relative = (point.Position - Offset) * scale;
            var position = PixelSize / 2 + new Vector2(relative.X, -relative.Y);
            var radius = MathF.Max(1.5f, scale * point.Size);
            if (point.Kind is not (LavalandRadarPointKind.LivingMiner or LavalandRadarPointKind.DeadMiner or LavalandRadarPointKind.Fauna))
            {
                handle.DrawRect(new UIBox2(position.X - radius, position.Y - radius,
                    position.X + radius, position.Y + radius), point.Color);
            }
            else
            {
                if (point.Kind == LavalandRadarPointKind.DeadMiner)
                    handle.DrawCircle(position, MathF.Max(3f, radius) + 1.5f, Color.LightGray);
                handle.DrawCircle(position, MathF.Max(3f, radius), point.Color);
            }
        }
    }
}
