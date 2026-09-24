using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Application.Common.Defaults
{
    public sealed class NullIncidentExecutedListener : IIncidentExecutedListener
    {
        public static readonly NullIncidentExecutedListener Instance = new NullIncidentExecutedListener();

        public string Id => "null.incident-executed-listener";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public void OnIncidentExecuted() { }
    }
}
