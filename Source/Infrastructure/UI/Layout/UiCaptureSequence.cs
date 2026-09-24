using System;
using System.Collections.Generic;
using System.Linq;

namespace RimMind.Infrastructure.UI.Layout
{
    internal sealed class UiCaptureSequence
    {
        private readonly string[] _sceneIds;
        private int _index;
        private int _lastFrame = -1;
        private int _repaints;
        private bool _awaitingImage;
        private double _deadline;

        public UiCaptureSequence(IEnumerable<string> sceneIds, double now)
        {
            _sceneIds = sceneIds?.ToArray() ?? throw new ArgumentNullException(nameof(sceneIds));
            if (_sceneIds.Length == 0 || _sceneIds.Distinct(StringComparer.Ordinal).Count() != _sceneIds.Length
                || _sceneIds.Any(id => string.IsNullOrEmpty(id) || id.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_'))))
                throw new ArgumentException("Scene IDs must be unique nonempty filename-safe identifiers.", nameof(sceneIds));
            _deadline = now + 10;
        }

        public string? CurrentSceneId => _index < _sceneIds.Length ? _sceneIds[_index] : null;
        public string? Error { get; private set; }
        public bool IsComplete => _index == _sceneIds.Length && Error == null && !IsCancelled;
        public bool IsCancelled { get; private set; }
        public bool IsTerminal => IsComplete || IsCancelled || Error != null;

        public void ObserveRepaint(string sceneId, int frame, double now)
        {
            CheckTimeout(now);
            if (IsTerminal || _awaitingImage) return;
            if (sceneId != CurrentSceneId) { Fail("wrong-page"); return; }
            if (frame <= _lastFrame) return;
            _lastFrame = frame;
            _repaints++;
        }

        public bool TryBeginCapture(double now)
        {
            CheckTimeout(now);
            if (IsTerminal || _awaitingImage || _repaints < 2) return false;
            _awaitingImage = true;
            _deadline = now + 10;
            return true;
        }

        public void ConfirmCapture(string sceneId, bool valid, double now)
        {
            CheckTimeout(now);
            if (IsTerminal) return;
            if (!_awaitingImage || sceneId != CurrentSceneId) { Fail("unexpected-capture"); return; }
            if (!valid) { Fail("invalid-image"); return; }
            _index++;
            _repaints = 0;
            _awaitingImage = false;
            // Retain the last observed frame: a receipt cannot reuse old render evidence.
            _deadline = now + 10;
        }

        public void CheckTimeout(double now)
        {
            if (!IsTerminal && now >= _deadline) Fail("timeout");
        }

        public void Fail(string reason)
        {
            if (!IsTerminal) Error = reason;
        }

        public void Cancel()
        {
            if (!IsTerminal) IsCancelled = true;
        }
    }
}
