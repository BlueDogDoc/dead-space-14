// Мёртвый Космос, Licensed under custom terms with restrictions on public hosting and commercial use, full text: https://raw.githubusercontent.com/dead-space-server/space-station-14-fobos/master/LICENSE.TXT

namespace Content.Shared.DeadSpace.Body.Limbs;

[ByRefEvent]
public record struct LimbSeverAttemptEvent(LimbZone Zone, bool Blast)
{
    public bool Cancelled;
}
