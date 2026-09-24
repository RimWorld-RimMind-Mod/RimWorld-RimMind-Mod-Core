using Verse;

namespace RimMind.Infrastructure.UI.DebugCenter
{
    public sealed class DebugCenterPageContext
    {
        public DebugCenterPageContext(Pawn? selectedPawn)
            : this(selectedPawn, new DebugCenterNavigation())
        {
        }

        public DebugCenterPageContext(Pawn? selectedPawn, DebugCenterNavigation navigation)
        {
            SelectedPawn = selectedPawn;
            Navigation = navigation;
        }

        public Pawn? SelectedPawn { get; set; }

        public DebugCenterNavigation Navigation { get; }
    }
}
