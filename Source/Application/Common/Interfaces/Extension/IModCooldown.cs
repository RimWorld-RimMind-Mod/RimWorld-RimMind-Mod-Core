namespace RimMind.Application.Common.Interfaces.Extension;

public interface IModCooldown : IExtension
{
    int CooldownTicks { get; }
}
