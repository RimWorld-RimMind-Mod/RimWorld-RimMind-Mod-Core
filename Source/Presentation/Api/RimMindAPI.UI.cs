using System;
using System.Collections.Generic;
using RimMind.Application.Common.Models.UI;
using RimMind.Infrastructure.UI.DebugCenter;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        /// <summary>
        /// Public registration surface for Debug Center (Hub) pages.
        /// Submodules can register custom diagnostic/overview pages and unregister by OwnerModId.
        /// </summary>
        public static class DebugPages
        {
            public static string DefaultPageId => DebugCenterPageRegistry.DefaultPageId;

            public static void Register(DebugCenterPageDescriptor descriptor, Func<IDebugCenterPageDrawer> factory)
                => DebugCenterPageRegistry.Register(descriptor, factory);

            public static bool Unregister(string id)
                => DebugCenterPageRegistry.Unregister(id);

            public static int UnregisterByOwner(string ownerModId)
                => DebugCenterPageRegistry.UnregisterByOwner(ownerModId);

            public static void ResetToDefaults()
                => DebugCenterPageRegistry.ResetToDefaults();

            public static DebugCenterPageDescriptor? Find(string id)
                => DebugCenterPageRegistry.Find(id);

            public static IReadOnlyList<DebugCenterPageDescriptor> GetAll()
                => DebugCenterPageRegistry.GetAll();
        }

        /// <summary>
        /// Public UI extension helpers for Hub debug pages.
        /// </summary>
        public static class UI
        {
            public static void RegisterDebugPage(DebugCenterPageDescriptor descriptor, Func<IDebugCenterPageDrawer> factory)
                => DebugCenterPageRegistry.Register(descriptor, factory);

            public static bool UnregisterDebugPage(string id)
                => DebugCenterPageRegistry.Unregister(id);

            public static int UnregisterDebugPagesByOwner(string ownerModId)
                => DebugCenterPageRegistry.UnregisterByOwner(ownerModId);

            public static void ResetDebugPagesToDefaults()
                => DebugCenterPageRegistry.ResetToDefaults();

            public static IReadOnlyList<DebugCenterPageDescriptor> GetDebugPages()
                => DebugCenterPageRegistry.GetAll();
        }
    }
}
