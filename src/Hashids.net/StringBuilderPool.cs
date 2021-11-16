using System;
using System.Collections.Concurrent;
using System.Text;

namespace HashidsNet
{
    internal class StringBuilderPool
    {
        // TODO: use thread-local-storage for per-thread StringBuilders?
        private readonly ConcurrentBag<StringBuilder> _builders = new();

        public StringBuilder Get() => _builders.TryTake(out StringBuilder? sb) ? sb : new();

        public void Return(StringBuilder sb)
        {
            const int MAX_RETURNED_CAPACITY = 1024; // If a returned StringBuilder has an excessively large internal capacity then shrink it to avoid wasting memory.

            sb.Clear(); // <-- NOTE: This only resets the StringBuilder's internal buffer pointer, it doesn't free/deallocate any internal buffers.

            if (sb.Capacity > MAX_RETURNED_CAPACITY)
            {
                sb.Capacity = MAX_RETURNED_CAPACITY;
            }

            _builders.Add(sb);
        }

        public RentedStringBuilder Rent(out StringBuilder sb)
        {
            sb = this.Get();
            return new RentedStringBuilder(this, sb);
        }
    }

    internal ref struct RentedStringBuilder
    {
        public static implicit operator StringBuilder(RentedStringBuilder self)
        {
            return self.sb;
        }

        private readonly StringBuilder      sb;
        private readonly StringBuilderPool? pool;
        private          bool               isDisposed;

        /// <param name="pool">When <see langword="null"/> then the <paramref name="sb"/> is not considered rented and will not be returned.</param>
        public RentedStringBuilder(StringBuilderPool? pool, StringBuilder sb)
        {
            this.pool       = pool;
            this.sb         = sb ?? throw new ArgumentNullException(nameof(sb));
            this.isDisposed = false;
        }

        public bool IsRented => this.pool != null;

        /// <summary>Throws <see cref="ObjectDisposedException"/> if accessed after <see cref="Dispose"/> was called (and <see cref="IsRented"/> is <see langword="true"/>).</summary>
        public StringBuilder Instance
        {
            get
            {
                if (this.isDisposed)
                {
                    throw new ObjectDisposedException(objectName: null, message: "This rented " + nameof(RentedStringBuilder) + " has already been returned to the pool.");
                }
                else
                {
                    return this.sb;
                }
            }
        }

        public void Dispose()
        {
            if (this.pool != null && !this.isDisposed)
            {
                this.pool.Return(this.Instance);
                this.isDisposed = true;
            }
        }
    }
}