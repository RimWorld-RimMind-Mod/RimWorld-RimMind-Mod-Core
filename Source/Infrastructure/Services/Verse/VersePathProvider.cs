using RimMind.Application.Common.Interfaces.Abstractions;

namespace RimMind.Infrastructure.Services.Verse
{
    public sealed class VersePathProvider : IPathProvider
    {
        public string SaveDataFolderPath => global::Verse.GenFilePaths.SaveDataFolderPath;
    }
}
