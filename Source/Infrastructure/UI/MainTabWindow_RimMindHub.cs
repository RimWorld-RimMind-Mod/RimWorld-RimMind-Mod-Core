using System;
using System.Collections.Generic;
using System.Linq;
using RimMind.Infrastructure.UI.DebugCenter;
using RimMind.Infrastructure.UI.Framework;
using RimMind.Presentation.UI.Framework;
using RimMind.Presentation.UI.Layout;
using RimMind.Presentation.Runtime.Services;
using UnityEngine;
using Verse;

namespace RimMind.Infrastructure.UI
{
    public class Window_RimMindHub : RimMindWindowBase
    {
        private string _pageId;
        private readonly DebugCenterPageContext _context;
        private readonly DebugCenterNavigation _navigation = new();
        private readonly IReadOnlyList<DebugCenterPageRegistration> _pages;
        private readonly Dictionary<string, IDebugCenterPageDrawer> _drawerCache = new();
        private readonly RimMindTabbedPageHostDrawer _tabDrawer = new();
        private readonly RuntimeBinding _runtimeBinding = new();

        public override Vector2 InitialSize => new Vector2(780f, 580f);

        public Window_RimMindHub()
            : this(DebugCenterPageRegistry.DefaultPageId, selectedPawn: null)
        {
        }

        internal Window_RimMindHub(string initialPageId, Pawn? selectedPawn)
        {
            _pages = DebugCenterPageRegistry.CreateAllRegistrations();
            foreach (DebugCenterPageRegistration page in _pages)
                _drawerCache[page.Descriptor.Id] = page.CreateDrawer();
            _pageId = ResolvePageId(initialPageId);
            _context = new DebugCenterPageContext(selectedPawn, _navigation);
            forcePause = false;
            closeOnClickedOutside = true;
            absorbInputAroundWindow = false;
            doCloseX = true;
        }

        public static Window_RimMindHub OpenAgentsForPawn(Pawn selectedPawn)
            => new Window_RimMindHub("agents", selectedPawn);

        public static Window_RimMindHub OpenAIRequests()
            => new Window_RimMindHub("ai_requests", selectedPawn: null);

        internal string CurrentPageId => _pageId;
        internal IDebugCenterPageDrawer CurrentDrawer => _drawerCache[_pageId];
        internal void SelectPage(string pageId) => _pageId = ResolvePageId(pageId);

        protected override void DrawContents(Rect inRect, RimMindLayoutScope scope)
        {
            _runtimeBinding.Refresh(BindDrawers);
            Rect body = inRect.InsetSafe(RimMindUiMetrics.WindowInset);
            Rect header = new Rect(body.x, body.y, body.width, RimMindUiMetrics.HeaderHeight);
            Rect tabRoot = new Rect(
                body.x,
                header.yMax + RimMindUiMetrics.Padding,
                body.width,
                Mathf.Max(1f, body.yMax - header.yMax - RimMindUiMetrics.Padding));
            var tabs = BuildTabModels();
            var tabLayout = TabbedPageLayout.Calculate(tabRoot, tabs);

            scope.Record(header, "Hub:Header");
            RimMindUI.DrawWindowHeader(header, "RimMind.UI.Hub.Title".Translate());
            _pageId = _tabDrawer.DrawTabs(tabRoot, tabs, _pageId, scope);
            DebugCenterPageRegistration? selectedPage = ResolveSelectedPage();
            if (selectedPage != null)
            {
                GetDrawer(selectedPage).Draw(tabLayout.Content, _context, scope);
            }

            string? requestedPageId = _navigation.ConsumeRequestedPageId();
            if (!string.IsNullOrEmpty(requestedPageId))
                _pageId = ResolvePageId(requestedPageId);
        }

        private IReadOnlyList<TabbedPageTabModel> BuildTabModels()
            => _pages
                .Select(page => new TabbedPageTabModel(
                    page.Descriptor.Id,
                    page.Descriptor.LabelKey.Translate(),
                    page.Descriptor.LabelKey,
                    _pageId == page.Descriptor.Id,
                    enabled: true,
                    tooltipKey: null))
                .ToList();

        private DebugCenterPageRegistration? ResolveSelectedPage()
        {
            var selectedPage = _pages.FirstOrDefault(page => page.Descriptor.Id == _pageId)
                ?? _pages.FirstOrDefault(page => page.Descriptor.IsDefault)
                ?? _pages.FirstOrDefault();

            if (selectedPage != null)
                _pageId = selectedPage.Descriptor.Id;

            return selectedPage;
        }

        private IDebugCenterPageDrawer GetDrawer(DebugCenterPageRegistration registration)
        {
            return _drawerCache[registration.Descriptor.Id];
        }

        private IDisposable? BindDrawers(RuntimeServiceScope scope)
        {
            var leases = new List<IDisposable>();
            foreach (IDebugCenterPageDrawer drawer in _drawerCache.Values)
            {
                if (!(drawer is IRuntimeBoundDebugCenterPageDrawer runtimeBoundDrawer))
                    continue;
                IDisposable? lease = runtimeBoundDrawer.Bind(scope);
                if (lease != null)
                    leases.Add(lease);
            }

            return leases.Count == 0 ? null : new DrawerLease(leases);
        }

        public override void PreClose()
        {
            _runtimeBinding.Dispose();
            base.PreClose();
        }

        private sealed class DrawerLease : IDisposable
        {
            private List<IDisposable>? _leases;

            public DrawerLease(List<IDisposable> leases)
            {
                _leases = leases;
            }

            public void Dispose()
            {
                List<IDisposable>? leases = _leases;
                _leases = null;
                if (leases == null)
                    return;
                foreach (IDisposable lease in leases)
                    lease.Dispose();
            }
        }

        private static string ResolvePageId(string pageId)
        {
            return DebugCenterPageRegistry.Find(pageId)?.Id
                ?? DebugCenterPageRegistry.DefaultPageId;
        }
    }

    public class MainTabWindow_RimMindHub : Window_RimMindHub
    {
    }
}
