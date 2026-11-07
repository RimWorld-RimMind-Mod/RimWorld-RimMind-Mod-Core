using System.Collections.Generic;
using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Application.Common.Interfaces.Agent.Modes
{
    public interface IThinkContributor : IExtension
    {
        string ContributorId { get; }
        int Order { get; }
        void Contribute(string npcId, int pawnId, IDictionary<string, string> sections);
    }
}
