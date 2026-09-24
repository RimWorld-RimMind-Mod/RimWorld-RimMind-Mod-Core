namespace RimMind.Infrastructure.UI.DebugCenter
{
    public sealed class DebugCenterNavigation
    {
        public string? RequestedPageId { get; private set; }

        public void GoTo(string pageId)
        {
            RequestedPageId = pageId;
        }

        public string? ConsumeRequestedPageId()
        {
            string? requestedPageId = RequestedPageId;
            RequestedPageId = null;
            return requestedPageId;
        }
    }
}
