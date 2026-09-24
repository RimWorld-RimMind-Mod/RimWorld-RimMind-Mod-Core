namespace RimMind.Presentation.UI.Framework
{
    public sealed class CurrentAgentBinding<TAgent>
        where TAgent : class
    {
        private readonly System.Func<TAgent?> _resolveCurrent;

        public CurrentAgentBinding(System.Func<TAgent?> resolveCurrent)
        {
            _resolveCurrent = resolveCurrent ?? throw new System.ArgumentNullException(nameof(resolveCurrent));
        }

        public TAgent? Resolve()
        {
            return _resolveCurrent();
        }
    }

    public sealed class GenerationUiState
    {
        public long BoundGeneration { get; private set; } = -1;

        public bool HasDerivedState { get; private set; }

        public bool HasActiveInteraction { get; private set; }

        public bool Refresh(long generation)
        {
            if (generation == BoundGeneration)
                return false;

            BoundGeneration = generation;
            HasDerivedState = false;
            HasActiveInteraction = false;
            return true;
        }

        public void MarkDerivedState()
        {
            HasDerivedState = true;
        }

        public void MarkInteractionActive()
        {
            HasActiveInteraction = true;
        }

        public void ClearInteraction()
        {
            HasActiveInteraction = false;
        }
    }

    public sealed class GenerationSelectionState<T>
        where T : class
    {
        public long BoundGeneration { get; private set; } = -1;

        public T? Selection { get; private set; }

        public bool Refresh(long generation)
        {
            if (generation == BoundGeneration)
                return false;

            BoundGeneration = generation;
            Selection = null;
            return true;
        }

        public void Select(T? selection)
        {
            Selection = selection;
        }
    }
}
