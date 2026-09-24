namespace RimMind.Application.Common.Interfaces.Extension;

public interface IToggleBehavior : IExtension
{
    bool IsActive { get; }
    void Toggle();
}
