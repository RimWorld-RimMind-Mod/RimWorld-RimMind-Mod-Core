using RimMind.Application.Common.Interfaces;
using RimMind.Application.Common.Interfaces.Context;
using RimMind.Presentation.Runtime;
using RimMind.Presentation.Runtime.Services;
using Verse;

namespace RimMind.Presentation.Api
{
    public static partial class RimMindAPI
    {
        public static class ChatFlow
        {
            private static readonly RuntimeServiceRef<IGameContextBuilder> ContextBuilders =
                RuntimeServiceRef<IGameContextBuilder>.Required();

            public static string BuildMapContext(Map map, bool brief = false)
            {
                var builder = ContextBuilders.Value;
                return builder.BuildMapContextInstance(map, brief);
            }
        }

        public static class Chat
        {
            public static System.Func<int, int?>? ActiveDialogueRecipientResolver { get; set; }

            public static int? GetActiveDialogueRecipient(int pawnId)
            {
                return ActiveDialogueRecipientResolver?.Invoke(pawnId);
            }
        }
    }
}
