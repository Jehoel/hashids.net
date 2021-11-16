using System;
using System.Collections;
using System.Collections.Generic;

namespace HashidsNet
{
#if !NETCOREAPP3_1_OR_GREATER
    internal struct Span<T> : IList<T>
    {
        public static implicit operator ReadOnlySpan<T>(Span<T> self)
        {
            return new ReadOnlySpan<T>(array: self.array, startIndex: self.startIndex, count: self.count);
        }

        public static implicit operator ArraySegment<T>(Span<T> self)
        {
            return self.AsArraySegment();
        }

        public static implicit operator Span<T>(T[]? array)
        {
            return new Span<T>(array);
        }

        public static implicit operator Span<T>(ArraySegment<T> segment)
        {
            return new Span<T>(segment.Array, startIndex: segment.Offset, count: segment.Count);
        }

        private readonly T[] array;
        private readonly int startIndex;
        private readonly int count;

        public Span(T[]? array)
            : this(array, startIndex: 0, count: array?.Length ?? 0)
        {
        }

        public Span(T[]? array, int startIndex, int count)
        {
            array ??= Array.Empty<T>();

            this.array      = array;
            this.startIndex = startIndex;
            this.count      = count;

            if (this.array.Length == 0)
            {
                if (startIndex is not (0 or -1)       ) throw new ArgumentOutOfRangeException(paramName: nameof(startIndex), actualValue: startIndex, message: "Value must be zero or -1 for empty arrays.");
                if (count             != 0            ) throw new ArgumentOutOfRangeException(paramName: nameof(count)     , actualValue: count     , message: "Value must be zero for empty arrays.");
            }
            else
            {
                if (count              < 0            ) throw new ArgumentOutOfRangeException(paramName: nameof(count)     , actualValue: count     , message: "Value must be non-negative.");
                if (startIndex         < 0            ) throw new ArgumentOutOfRangeException(paramName: nameof(startIndex), actualValue: startIndex, message: "Value must be non-negative.");

                if (startIndex         >= array.Length) throw new ArgumentOutOfRangeException(paramName: nameof(startIndex), actualValue: startIndex, message: "Value exceeds the length of the provided array.");
                if (startIndex + count >  array.Length) throw new ArgumentOutOfRangeException(paramName: nameof(count)     , actualValue: count     , message: "Value (plus " + nameof(startIndex) + ") exceeds the length of the provided array.");
            }
        }

        #region Translate

        private void Translate(ref int index, bool allowNegative)
        {
            index = this.Translate(index, allowNegative);
        }

        private int Translate(int index, bool allowNegative)
        {
            if ((!allowNegative && index < 0) || index >= this.count)
            {
                throw new ArgumentOutOfRangeException(paramName: nameof(index), actualValue: index, message: "Value must be between 0 and " + nameof(this.Length) + " (exclusive).");
            }
            
            return this.startIndex + index;
        }

        #endregion

        public Span<T> Slice(int offset)
        {
            if (offset == 0)
            {
                return this;
            }
            else if (offset < 0) // or throw?
            {
                int absOffset = this.Translate(offset, allowNegative: true);
//                int newCount = this.count
            }

            
            if (absOffset )
            return new Span<T>(array: this.array, startIndex: absOffset, count: );
        }

        public T this[int index]
        {
            get
            {
                if(index < 0 || index >= this.count)
                {
                    throw new ArgumentOutOfRangeException(paramName: nameof(index), actualValue: index, message: "Value must be between 0 and " + nameof(this.Length) + " (exclusive).");
                }
                else
                {
                    int sourceIndex = this.startIndex + index;
                    return this.array[sourceIndex];
                }
            }
            set
            {

            }
        }

        public int Length => this.count;
        public int Count  => this.count; // Specifying both Length and Count for source-level compatibility with both IList<T>.Count and Array<T>.Length (and of course, Span<T>.Length)

        private int EndIndex => ( this.startIndex + this.count ) - 1;

        public int IndexOf(T value)
        {
            int sourceIndex = Array.IndexOf(this.array, value, startIndex: this.startIndex, count: this.count);
            if (sourceIndex < 0) return -1;
            return this.startIndex + sourceIndex;
        }

        public IEnumerator<T> GetEnumerator()
        {
            if( this.count == 0 )
            {
                return ((IEnumerable<T>)Array.Empty<T>()).GetEnumerator();
            }
            else
            {
                return this.AsEnumerable().GetEnumerator();
            }
        }

        private IEnumerable<T> AsEnumerable()
        {
            int endIdx = this.EndIndex;
            for (int i = this.startIndex; i <= endIdx; i++)
            {
                yield return this.array[i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        public ArraySegment<T> AsArraySegment() => new ArraySegment<T>(this.array, offset: this.startIndex, count: this.count);

        /// <remarks>This method exists because using Linq's extensions over <see cref="IEnumerable{T}"/> or <see cref="IList{T}"/> are a lot slower than doing it directly.</remarks>
        public bool Any(Func<T,bool> predicate)
        {
            int endIdx = this.EndIndex;
            for (int i = this.startIndex; i <= endIdx; i++)
            {
               if(predicate(this.array[i])) return true;
            }

            return false;
        }
    }
#endif
}