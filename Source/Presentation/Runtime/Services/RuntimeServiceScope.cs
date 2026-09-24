namespace RimMind.Presentation.Runtime.Services
{
    public sealed class RuntimeServiceScope
    {
        internal RuntimeServiceScope(RuntimeServiceSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public RuntimeServiceSnapshot Snapshot { get; }

        public long Generation => Snapshot.Generation;

        public RuntimeGenerationToken Token => new RuntimeGenerationToken(Snapshot.RuntimeId, Snapshot.Generation);

        public T GetRequired<T>()
            where T : class
        {
            return Snapshot.GetRequired<T>();
        }

        public T? GetOptional<T>()
            where T : class
        {
            return Snapshot.GetOptional<T>();
        }
    }
}
