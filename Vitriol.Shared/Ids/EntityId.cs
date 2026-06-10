namespace Vitriol.Shared.Ids;

public readonly record struct EntityId(long Value)
{
    public override string ToString() => Value.ToString();
}
