namespace RimMind.Presentation.Runtime.Services
{
    public sealed class GameServiceScope
    {
        internal GameServiceScope(GameServiceSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public GameServiceSnapshot Snapshot { get; }

        public long Generation => Snapshot.Generation;

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
