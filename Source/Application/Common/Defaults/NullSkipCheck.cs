using RimMind.Application.Common.Constants;
using RimMind.Application.Common.Interfaces.Extension;

namespace RimMind.Application.Common.Defaults
{
    public sealed class NullSkipCheck : ISkipCheck
    {
        public static readonly NullSkipCheck Instance = new NullSkipCheck();

        public string Id => "null.skip-check";
        public string OwnerModId => RimMindOwnerConsts.CoreModId;
        public SkipCheckKind Kind => (SkipCheckKind)(-1);
        public bool ShouldSkip(in SkipCheckArgs args) => false;
    }
}
