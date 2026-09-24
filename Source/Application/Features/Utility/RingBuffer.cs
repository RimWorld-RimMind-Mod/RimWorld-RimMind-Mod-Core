using System.Collections.Generic;

namespace RimMind.Application.Features.Utility
{
    /// <summary>
    /// Fixed-size circular buffer. Oldest entries are overwritten when full.
    /// </summary>
    internal sealed class RingBuffer<T>
    {
        private readonly T[] _buf;
        private int _idx;

        public int Count { get; private set; }

        public RingBuffer(int size)
        {
            _buf = new T[size];
        }

        public void Add(T item)
        {
            _buf[_idx] = item;
            _idx = (_idx + 1) % _buf.Length;
            if (Count < _buf.Length) Count++;
        }

        public IEnumerable<T> AsEnumerable()
        {
            for (int i = 0; i < Count; i++)
            {
                int actualIdx = (Count < _buf.Length) ? i : ((_idx + i) % _buf.Length);
                yield return _buf[actualIdx];
            }
        }
    }
}
