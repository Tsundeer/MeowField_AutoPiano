using MeowField.Domain;

namespace MeowField.App;

public sealed record InstrumentChoice(InstrumentKind Kind, GameProfile? Profile)
{
    public bool IsGeneric => Profile is null;
    public string Id => Profile?.Id ?? $"generic:{Kind}";
}
