namespace RimMind.Application.Common.Interfaces.Extension;

/// <summary>
/// Contract for bridge modules that register/unregister with external mods.
/// Implementations track their own registration state via <see cref="IsRegistered"/>.
/// Coordinators iterate a list of IBridgeModule rather than hardcoding static calls.
/// </summary>
public interface IBridgeModule : IExtension
{
    /// <summary>True if Register() has been called and Unregister() has not.</summary>
    bool IsRegistered { get; }

    /// <summary>Register hooks/variables/providers with the external mod. Idempotent.</summary>
    void Register();

    /// <summary>Unregister hooks/variables/providers. Idempotent; no-op if not registered.</summary>
    void Unregister();
}
