using System;
using System.Collections.Generic;

namespace RimMind.Domain.ValueObjects
{
    public readonly struct Result<TValue, TError> : IEquatable<Result<TValue, TError>>
    {
        private readonly TValue? _value;
        private readonly TError? _error;
        public bool IsOk { get; }
        public bool IsErr => !IsOk;

        private Result(TValue value) { _value = value; _error = default; IsOk = true; }
        private Result(TError error) { _value = default; _error = error; IsOk = false; }

        public static Result<TValue, TError> Ok(TValue value) => new(value);
        public static Result<TValue, TError> Err(TError error) => new(error);

        public TValue Value => IsOk ? _value!
            : throw new InvalidOperationException("Result is Err; cannot access Value");
        public TError Error => IsErr ? _error!
            : throw new InvalidOperationException("Result is Ok; cannot access Error");

        public TResult Match<TResult>(Func<TValue, TResult> onOk, Func<TError, TResult> onErr)
            => IsOk ? onOk(_value!) : onErr(_error!);

        public Result<TNew, TError> Map<TNew>(Func<TValue, TNew> mapper)
            => IsOk ? Result<TNew, TError>.Ok(mapper(_value!)) : Result<TNew, TError>.Err(_error!);

        public bool TryGetValue(out TValue? value)
        {
            value = _value;
            return IsOk;
        }

        public bool TryGetError(out TError? error)
        {
            error = _error;
            return IsErr;
        }

        public bool Equals(Result<TValue, TError> other)
        {
            if (IsOk != other.IsOk) return false;
            if (IsOk)
                return EqualityComparer<TValue>.Default.Equals(_value!, other._value!);
            return EqualityComparer<TError>.Default.Equals(_error!, other._error!);
        }

        public override bool Equals(object? obj) => obj is Result<TValue, TError> other && Equals(other);

        public override int GetHashCode()
        {
            if (IsOk)
                return _value == null ? 0 : _value.GetHashCode();
            return _error == null ? 1 : _error.GetHashCode();
        }

        public static bool operator ==(Result<TValue, TError> left, Result<TValue, TError> right) => left.Equals(right);
        public static bool operator !=(Result<TValue, TError> left, Result<TValue, TError> right) => !left.Equals(right);
    }
}
