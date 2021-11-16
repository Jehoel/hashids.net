using System;
using System.Buffers;
using System.Collections.Generic;

namespace HashidsNet
{
    internal static class ArrayExtensions
    {
        public static T[] SubArray<T>(this T[] array, int index)
        {
            return SubArray(array, index, array.Length - index);
        }

        public static T[] SubArray<T>(this T[] array, int index, int length)
        {
            if (index == 0 && length == array.Length) return array;
            if (length == 0) return Array.Empty<T>();

            var subarray = new T[length];
            Array.Copy(array, index, subarray, 0, length);
            return subarray;
        }

        public static T[] Append<T>(this T[] array, T[] appendArray, int index, int length)
        {
            if (length == 0) return array;

            int newLength = array.Length + length - index;
            if (newLength == 0) return Array.Empty<T>();

            var newArray = new T[newLength];
            Array.Copy(array, 0, newArray, 0, array.Length);
            Array.Copy(appendArray, index, newArray, array.Length, length - index);
            return newArray;
        }

        public static T[] CopyPooled<T>(this T[] array)
        {
            return SubArrayPooled(array, 0, array.Length);
        }

        public static T[] SubArrayPooled<T>(this T[] array, int index, int length)
        {
            var subarray = ArrayPool<T>.Shared.Rent(length);
            Array.Copy(array, index, subarray, 0, length);
            return subarray;
        }

        public static void ReturnToPool<T>(this T[] array)
        {
            if (array == null)
                return;

            ArrayPool<T>.Shared.Return(array);
        }

#if NETCOREAPP3_1_OR_GREATER
        /// <remarks>This method exists because <see cref="ReadOnlySpan{T}"/> does not implement <see cref="IEnumerable{T}"/> and using <c>.AsEnumerable()</c> will cause boxing.</remarks>
        public static bool Any<T>(this ReadOnlySpan<T> span, Func<T,bool> predicate)
        {
            for(int i = 0; i < span.Length; i++)
            {
                if(predicate(span[i])) return true;
            }

            return false;
        }
#endif

        public static System.Text.StringBuilder Insert(this System.Text.StringBuilder sb, int index, ArraySegment<char> charsSegment, int charsSegmentOffset, int charCount)
        {
            char[] array = charsSegment.Array!;
            int startIndex = charsSegment.Offset + charsSegmentOffset;
            return sb.Insert(index: index, value: array, startIndex: startIndex, charCount: charCount);
        }

        public static System.Text.StringBuilder Append(this System.Text.StringBuilder sb, ArraySegment<char> charsSegment, int charsSegmentOffset, int charCount)
        {
            char[] array = charsSegment.Array!;
            int startIndex = charsSegment.Offset + charsSegmentOffset;
            return sb.Append(value: array, startIndex: startIndex, charCount: charCount);
        }

#if !NETCOREAPP3_1_OR_GREATER
        public static void CopyTo<T>(this ArraySegment<T> source, ArraySegment<T> destination)
        {
            Array.Copy(sourceArray: source.Array!, sourceIndex: source.Offset, destinationArray: destination.Array, destinationIndex: destination.Offset, length: source.Count);
        }

        public static void CopyTo<T>(this ArraySegment<T> source, ArraySegment<T> destination, int count)
        {
            Array.Copy(sourceArray: source.Array!, sourceIndex: source.Offset, destinationArray: destination.Array, destinationIndex: destination.Offset, length: count);
        }

        public static ArraySegment<T> Slice<T>(this ArraySegment<T> source, int index, int count)
        {
            {
                int sourceLength   = source.Array.Length;
                int requiredLength = source.Offset + index + count;
                if (requiredLength > sourceLength) throw new ArgumentOutOfRangeException(paramName: nameof(count), actualValue: count, message: "Value exceeds underlying array size.");
            }

            return new ArraySegment<T>(array: source.Array, offset: source.Offset + index, count: count);
        }
#endif
    }

    internal static class RentedBuffer
    {
        /// <summary>Rents a new buffer from <see cref="ArrayPool{T}.Shared"/> and wraps it in a <see cref="RentedBuffer{T}"/>, wrap it in a <c>using</c> block for fire-and-forget safety so you don't need to forget to call <see cref="ArrayPool{T}.Return(T[], bool)"/>.</summary>
        public static RentedBuffer<T> Rent<T>(int length, out ArraySegment<T> segment)
        {
            RentedBuffer<T> rented = new RentedBuffer<T>(length);
            segment = rented.AsArraySegment();
            return rented;
        }

        /// <summary>Rents a new buffer from <see cref="ArrayPool{T}.Shared"/> with (at least) <paramref name="sourceArray"/>'s length, and then copies <paramref name="sourceArray"/> into the output <paramref name="array"/>.</summary>
        public static RentedBuffer<T> RentCopy<T>(T[] sourceArray, out ArraySegment<T> segment)
        {
            RentedBuffer<T> rented = new RentedBuffer<T>(sourceArray.Length);
            Array.Copy(sourceArray: sourceArray, sourceIndex: 0, destinationArray: rented.Array, destinationIndex: 0, length: sourceArray.Length);
            segment = rented.AsArraySegment();
            return rented;
        }

        /// <summary>Attempts to get the length of <paramref name="source"/> and uses that to rents a new buffer from <see cref="ArrayPool{T}.Shared"/> with (at least) <paramref name="source"/>'s length, and then copies <paramref name="source"/> into the output <paramref name="array"/>. If the length isn't known then <paramref name="source"/> is loaded into a <see cref="List{T}"/> as an intermediate step.</summary>
        public static RentedBuffer<T> RentCopy<T>(IEnumerable<T> source, out ArraySegment<T> segment)
        {
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            else if (source is T[] sourceArray)
            {
                return RentCopy<T>(sourceArray: sourceArray, out segment);
            }
            else if (source is IList<T> sourceMutableList) // Annoyingly, IList<T> does not implement `IReadOnlyCollection<T>`.
            {
                RentedBuffer<T> rented = new RentedBuffer<T>(length: sourceMutableList.Count);
                segment = rented.AsArraySegment();
                sourceMutableList.CopyTo(segment.Array!, arrayIndex: segment.Offset);
                return rented;
            }
            else if (source is IReadOnlyCollection<T> sourceRO)
            {
                RentedBuffer<T> rented = new RentedBuffer<T>(length: sourceRO.Count);
                segment = rented.AsArraySegment();

                {
                    T[] array = segment.Array!;
                    int i = segment.Offset;
                    foreach (T item in sourceRO )
                    {
                        array[i] = item;
                        i += 1;
                    }
                }
                
                return rented;
            }
            else
            {
                List<T> asList = System.Linq.Enumerable.ToList(source);

                RentedBuffer<T> rented = new RentedBuffer<T>(length: asList.Count);
                segment = rented.AsArraySegment();

                asList.CopyTo(segment.Array!, arrayIndex: segment.Offset);

                return rented;
            }
        }

        public static RentedBuffer<TOut> RentProjectedCopy<TIn,TOut>(IEnumerable<TIn> source, out ArraySegment<TOut> segment, Func<TIn,TOut> valueSelector)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (valueSelector is null) throw new ArgumentNullException(nameof(valueSelector));

            //

            if (TryGetNonEnumeratedCount(source, out int count))
            {
                RentedBuffer<TOut> rented = new RentedBuffer<TOut>(length: count);
                segment = rented.AsArraySegment();

                {
                    TOut[] array = segment.Array;
                    int i = segment.Offset;
                    foreach ( TIn item in source )
                    {
                        array[i] = valueSelector(item);
                        i += 1;
                    }
                }

                return rented;
            }
            else
            {
                List<TOut> asList = new List<TOut>();
                
                foreach ( TIn item in source )
                {
                    asList.Add(valueSelector(item));
                }

                return RentCopy<TOut>(source: asList, out segment);
            }
        }

        private static bool TryGetNonEnumeratedCount<T>(IEnumerable<T> source, out int count)
        {
            if (source is T[] array)
            {
                count = array.Length;
                return true;
            }
            else if (source is IList<T> sourceMutableList) // Includes TIn[], List<TIn>, ImmutableList<TIn> (surprisingly!), and more.
            {
                count = sourceMutableList.Count;
                return true;
            }
            else if (source is IReadOnlyCollection<T> sourceRO)
            {
                count = sourceRO.Count;
                return true;
            }
            else
            {
                count = -1;
                return false;
            }
        }
    }

    /// <summary>Is implicitly convertible to <see cref="ArraySegment{T}"/>.</summary>
    internal readonly ref struct RentedBuffer<T>// : IDisposable
    {
        public static implicit operator ArraySegment<T>(RentedBuffer<T> self)
        {
            return self.AsArraySegment();
        }

        private readonly int length;
        private readonly T[] array; // Careful, don't use `array.Length` as it can be larger than `this.length`!

        public RentedBuffer(int length)
        {
            this.length = length;
            this.array = ArrayPool<T>.Shared.Rent(length);
        }

        /// <summary>NOTE: Do not use <c><see cref="Array"/>.<see cref="Array.Length"/></c> as it will likely exceed <see cref="Length"/>.</summary>
        public T[] Array  => this.array;
        public int Length => this.length;
        public int Count  => this.length;

        public void Dispose()
        {
            if (this.array is not null) // Don't return `this.array` when `this == default(RentedBuffer<T>)`.
            {
                ArrayPool<T>.Shared.Return(this.array);
            }
        }

        /// <summary>WARNING: Ensure that the <see cref="ArraySegment{T}"/> does not outlive this <see cref="RentedBuffer{T}"/>.</summary>
        public ArraySegment<T> AsArraySegment()
        {
            return new ArraySegment<T>(array: this.array, offset: 0, count: this.length);
        }
    }
}