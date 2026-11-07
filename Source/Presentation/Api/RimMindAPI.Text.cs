namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class Text
        {
            public static string TrOr(string key, string fallback, params object[] args)
                => RimMindText.TrOr(key, fallback, args);

            public static string Tr(string key, params object[] args)
                => RimMindText.Tr(key, args);
        }
    }
}
