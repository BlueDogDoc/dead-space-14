// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

using Robust.Shared.Configuration;

namespace Content.Shared.DeadSpace.SlimeForm;

[CVarDefs]
public sealed class SlimeFormCVars
{
    public static readonly CVarDef<float> Duration =
        CVarDef.Create("slime_form.duration", 4f, CVar.SERVERONLY);

    public static readonly CVarDef<float> Cooldown =
        CVarDef.Create("slime_form.cooldown", 300f, CVar.SERVERONLY);
}
