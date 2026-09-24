using System;

namespace RimMind.Presentation.Runtime.Services
{
    public readonly struct RuntimeGenerationToken : IEquatable<RuntimeGenerationToken>
    {
        public RuntimeGenerationToken(Guid runtimeId, long generation)
        {
            RuntimeId = runtimeId;
            Generation = generation;
        }

        public Guid RuntimeId { get; }

        public long Generation { get; }

        public bool Equals(RuntimeGenerationToken other)
        {
            return RuntimeId.Equals(other.RuntimeId) && Generation == other.Generation;
        }

        public override bool Equals(object? obj)
        {
            return obj is RuntimeGenerationToken other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (RuntimeId.GetHashCode() * 397) ^ Generation.GetHashCode();
            }
        }

        public static bool operator ==(RuntimeGenerationToken left, RuntimeGenerationToken right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(RuntimeGenerationToken left, RuntimeGenerationToken right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"{RuntimeId:N}@{Generation}";
        }
    }
}
