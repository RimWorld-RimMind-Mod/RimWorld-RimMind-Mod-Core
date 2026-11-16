using System.Threading.Tasks;
using RimMind.Application.Common.Interfaces.Extension;
using RimMind.Domain.Audio;
using RimMind.Domain.Common;
using RimMind.Domain.ValueObjects;

namespace RimMind.Application.Common.Interfaces.Audio
{
    /// <summary>
    /// Orthogonal text-to-speech extension port registered via <c>RimMindAPI.Ext.Get&lt;ISpeechSynthesizer&gt;()</c>.
    /// Implementations must perform network/synthesis work on background threads only.
    /// </summary>
    public interface ISpeechSynthesizer : IExtension
    {
        string DisplayLabel { get; }
        bool IsConfigured();

        [ThreadAffinity(ThreadAffinityKind.BackgroundOnly)]
        Task<Result<SpeechSynthesisResult, RimMindError>> SynthesizeAsync(SpeechSynthesisRequest request);
    }
}
