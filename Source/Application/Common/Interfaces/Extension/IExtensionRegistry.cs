using System.Collections.Generic;

namespace RimMind.Application.Common.Interfaces.Extension;

public interface IExtensionRegistry<T> where T : class, IExtension
{
    void Register(T extension);
    bool Unregister(string id);
    int UnregisterByOwner(string ownerModId);
    IReadOnlyList<T> All { get; }
    T? FindById(string id);
}
