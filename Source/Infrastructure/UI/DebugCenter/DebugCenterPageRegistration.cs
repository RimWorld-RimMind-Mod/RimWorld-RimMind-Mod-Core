using System;
using RimMind.Application.Common.Models.UI;

namespace RimMind.Infrastructure.UI.DebugCenter
{
    public sealed class DebugCenterPageRegistration
    {
        private readonly Func<IDebugCenterPageDrawer> _factory;

        public DebugCenterPageRegistration(
            DebugCenterPageDescriptor descriptor,
            Func<IDebugCenterPageDrawer> factory)
        {
            Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public DebugCenterPageDescriptor Descriptor { get; }

        public IDebugCenterPageDrawer CreateDrawer()
            => _factory();
    }
}
