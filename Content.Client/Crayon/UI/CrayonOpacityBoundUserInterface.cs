//DS-14 Start
using Content.Shared.Crayon;
using Robust.Client.UserInterface;

namespace Content.Client.Crayon.UI;

public sealed class CrayonOpacityBoundUserInterface : BoundUserInterface
{
    private CrayonOpacityWindow? _window;

    public CrayonOpacityBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<CrayonOpacityWindow>();
        if (EntMan.TryGetComponent<CrayonComponent>(Owner, out var crayon))
            _window.SetOpacity(crayon);

        _window.OnOpacity += opacity => SendMessage(new CrayonOpacityMessage(opacity));
    }
}
//DS-14 End
