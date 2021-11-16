using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.RegularExpressions;

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
    }

    internal static class RentedBuffer
    {
        /// <summary>Rents a new buffer from <see cref="ArrayPool{T}.Shared"/> and wraps it in a <see cref="RentedBuffer{T}"/>, wrap it in a <c>using</c> block for fire-and-forget safety so you don't need to forget to call <see cref="ArrayPool{T}.Return(T[], bool)"/>.</summary>
        public static RentedBuffer<T> Rent<T>(int length, out T[] array)
        {
            RentedBuffer<T> rented = new RentedBuffer<T>(length);
            array = rented.Array;
            return rented;
        }

        /// <summary>Rents a new buffer from <see cref="ArrayPool{T}.Shared"/> with (at least) <paramref name="source"/>'s length, and then copies <paramref name="source"/> into the output <paramref name="array"/>.</summary>
        public static RentedBuffer<T> RentCopy<T>(T[] source, out T[] array)
        {
            RentedBuffer<T> rented = new RentedBuffer<T>(source.Length);
            array = rented.Array;
            Array.Copy(sourceArray: source, sourceIndex: 0, destinationArray: array, destinationIndex: 0, length: source.Length);
            return rented;
        }
    }

    internal ref struct RentedBuffer<T>// : IDisposable
    {
        private readonly int length;
        private readonly T[] array; // Careful, don't use `array.Length` as it can be larger than `this.length`!

        public RentedBuffer(int length)
        {
            this.length = length;
            this.array = ArrayPool<T>.Shared.Rent(length);
        }

        public T[] Array  => this.array;
        public int Length => this.length;
        public int Count  => this.length;

        public void Dispose()
        {
            ArrayPool<T>.Shared.Return(this.array);
        }
    }
}