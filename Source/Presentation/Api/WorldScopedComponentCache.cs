using RimWorld.Planet;
using Verse;

namespace RimMind.Presentation.Api
{
    /// <summary>
    /// Thread-safe, world-scoped cache for a RimWorld <see cref="WorldComponent"/>.
    /// Automatically re-resolves when the active <see cref="World"/> changes across save reloads or new games.
    /// </summary>
    public sealed class WorldScopedComponentCache<T> where T : WorldComponent
    {
        private T? _cached;

        /// <summary>
        /// Gets the active <typeparamref name="T"/> for <c>Current.Game.World</c>, or null if no world is loaded.
        /// </summary>
        public T? Get()
        {
            var world = Current.Game?.World;
            if (world == null)
            {
                _cached = null;
                return null;
            }

            var current = _cached;
            if (current != null && ReferenceEquals(current.world, world))
            {
                return current;
            }

            current = world.GetComponent<T>();
            _cached = current;
            return current;
        }

        /// <summary>
        /// Clears the cached component reference (e.g., on explicit reset or shutdown).
        /// </summary>
        public void Reset()
        {
            _cached = null;
        }
    }
}
