using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Audio;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Audio
{
    /// <summary>
    /// Orthogonal speech-to-text extension port registered via <c>RimMindAPI.Ext.Get&lt;ISpeechRecognizer&gt;()</c>.
    /// Implementations must perform network/transcription work on background threads only.
    /// </summary>
    public interface ISpeechRecognizer : IExtension
    {
        string DisplayLabel { get; }
        bool IsConfigured();

        [ThreadAffinity(ThreadAffinityKind.BackgroundOnly)]
        Task<Result<SpeechRecognitionResult, RimMindError>> TranscribeAsync(SpeechRecognitionRequest request);
    }
}
