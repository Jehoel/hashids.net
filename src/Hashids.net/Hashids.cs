﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HashidsNet
{
    /// <summary>
    /// Generates YouTube-like hashes from one or many numbers. Use hashids when you do not want to expose your database ids to the user.
    /// </summary>
    public partial class Hashids : IHashids
    {
        public const string DEFAULT_ALPHABET = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
        public const string DEFAULT_SEPS = "cfhistuCFHISTU";
        public const int MIN_ALPHABET_LENGTH = 16;
        public const int MAX_HASH_LENGTH = 1024; // To prevent consumers with silly bugs nuking their memory.

        private const double SEP_DIV = 3.5;
        private const double GUARD_DIV = 12.0;

        private const int MaxNumberHashLength = 12; // Length of long.MaxValue;

        private readonly char[] _alphabet;
        private readonly char[] _seps;
        private readonly char[] _guards;
        private readonly char[] _salt;
        private readonly int _minHashLength;

        private readonly StringBuilderPool _sbPool = new();

        // Using Lazy<T> means the Regex won't be init until it's actually first-used, which speeds up first use of non-hex methods

        /// <summary><c>[\w\W]{1,12}</c> matches any sequence of word char and non-word chars between 1 and 12 (inclusive) in length.</summary>
        private static readonly Lazy<Regex> hexSplitter = new(() => new Regex(@"[\w\W]{1,12}", RegexOptions.Compiled));

        /// <summary>
        /// Instantiates a new Hashids encoder/decoder with defaults.
        /// </summary>
        public Hashids() : this(salt: string.Empty, minHashLength: 0, alphabet: DEFAULT_ALPHABET, seps: DEFAULT_SEPS)
        {
            // empty constructor with defaults needed to allow mocking of public methods
        }

        /// <summary>
        /// Instantiates a new Hashids encoder/decoder.
        /// All parameters are optional and will use defaults unless otherwise specified.
        /// </summary>
        /// <param name="salt">Must not be <see langword="null"/> but can be empty (<see cref="string.Empty"/>). Default value is <see cref="string.Empty"/>.</param>
        /// <param name="minHashLength">Must be in the range <c>0-1024</c> (<see cref="MAX_HASH_LENGTH"/>). Can be zero. Default is zero.</param>
        /// <param name="alphabet">String of characters to use when generating output strings.<br />Must contain at least <see cref="MIN_ALPHABET_LENGTH"/> distinct characters.<br />Must not be <see langword="null"/>, empty, or whitespace.<br />Default value is <see cref="DEFAULT_ALPHABET"/>.</param>
        /// <param name="seps">String of possible characters to use as separators between values in generated hashid strings. Every character in <paramref name="seps"/> must also appear in <paramref name="alphabet"/>. Must not be <see langword="null"/>, empty, or whitespace.<br />Default value is <see cref="DEFAULT_SEPS"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="salt"/>, <paramref name="seps"/>, or <paramref name="alphabet"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minHashLength"/> is outside the range <c>0-<see cref="MAX_HASH_LENGTH"/></c>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="salt"/>, <paramref name="seps"/>, or <paramref name="alphabet"/> are non-<see langword="null"/> but are otherwise invalid.</exception>
        public Hashids(
            string salt = "",
            int minHashLength = 0,
            string alphabet = DEFAULT_ALPHABET,
            string seps = DEFAULT_SEPS)
        {
            if (salt     is null) throw new ArgumentNullException(nameof(salt));
            if (alphabet is null) throw new ArgumentNullException(nameof(alphabet));
            if (seps     is null) throw new ArgumentNullException(nameof(seps));
                
            if (string.IsNullOrWhiteSpace(alphabet)) throw new ArgumentException(message: "Value cannot be null, empty nor whitespace.", paramName: nameof(alphabet));
            if (string.IsNullOrWhiteSpace(seps)    ) throw new ArgumentException(message: "Value cannot be null, empty nor whitespace.", paramName: nameof(seps)    );
                
            if (minHashLength < 0 || minHashLength > MAX_HASH_LENGTH) throw new ArgumentOutOfRangeException(paramName: nameof(minHashLength), actualValue: minHashLength, message: "Value must be in the range 0 to 1024.");

            //

            _salt = salt.Trim().ToCharArray();
            _minHashLength = minHashLength;

            InitCharArrays(alphabet: alphabet, seps: seps, salt: _salt, alphabetChars: out _alphabet, sepChars: out _seps, guardChars: out _guards);
        }

        /// <remarks>This method uses <c>out</c> params instead of returning a <c>ValueTuple</c> so it works with .NET 4.6.1.</remarks>
        private static void InitCharArrays(string alphabet, string seps, ReadOnlySpan<char> salt, out char[] alphabetChars, out char[] sepChars, out char[] guardChars)
        {
            alphabetChars = alphabet.ToCharArray().Distinct().ToArray();
            sepChars      = seps.ToCharArray();

            if (alphabetChars.Length < MIN_ALPHABET_LENGTH)
            {
                throw new ArgumentException($"Alphabet must contain at least {MIN_ALPHABET_LENGTH:N0} unique characters.", paramName: nameof(alphabet));
            }

            // SetupGuards():

            // seps should contain only characters present in alphabet:
            if (sepChars.Length > 0)
            {
                sepChars = sepChars.Intersect(alphabetChars).ToArray();
            }
            
            // alphabet should not contain seps // TODO: This comment contradicts the above, it needs rephrasing.
            if (sepChars.Length > 0 )
            {
                alphabetChars = alphabetChars.Except(sepChars).ToArray();
            }

            if (alphabetChars.Length < (MIN_ALPHABET_LENGTH - 6))
            {
                #warning TODO: What should the minimum length be after removing chars in `sep`?
                throw new ArgumentException($"Alphabet must contain at least {MIN_ALPHABET_LENGTH:N0} unique characters that are also not present in the separators list.", paramName: nameof(alphabet));
            }

            ConsistentShuffle(alphabet: sepChars, alphabetLength: sepChars.Length, salt: salt, saltLength: salt.Length);

            if (sepChars.Length == 0 || ((float)alphabetChars.Length / sepChars.Length) > SEP_DIV)
            {
                var sepsLength = (int)Math.Ceiling((float)alphabetChars.Length / SEP_DIV);

                if (sepsLength == 1)
                {
                    sepsLength = 2;
                }

                if (sepsLength > sepChars.Length)
                {
                    var diff = sepsLength - sepChars.Length;
                    sepChars = sepChars.Append(alphabetChars, 0, diff);
                    alphabetChars = alphabetChars.SubArray(diff);
                }
                else
                {
                    sepChars = sepChars.SubArray(0, sepsLength);
                }
            }

            ConsistentShuffle(alphabet: alphabetChars, alphabetChars.Length, salt: salt, salt.Length);
            
            // SetupGuards():
           
            var guardCount = (int)Math.Ceiling(alphabetChars.Length / GUARD_DIV);

            if (alphabetChars.Length < 3)
            {
                guardChars = sepChars.SubArray(index: 0, length: guardCount);
                sepChars   = sepChars.SubArray(index: guardCount);
            }

            else
            {
                guardChars    = alphabetChars.SubArray(index: 0, length: guardCount);
                alphabetChars = alphabetChars.SubArray(index: guardCount);
            }
        }

        #region Public entrypoints

        /// <summary>
        /// Encodes the provided numbers into a hash string.
        /// </summary>
        /// <param name="numbers">List of integers.</param>
        /// <returns>Encoded hash string.</returns>
        public virtual string Encode(params int[] numbers) => EncodeInt32ValuesImpl(numbers);

        /// <summary>
        /// Encodes the provided numbers into a hash string.
        /// </summary>
        /// <param name="numbers">Enumerable list of integers.</param>
        /// <returns>Encoded hash string.</returns>
        public virtual string Encode(IEnumerable<int> numbers) => EncodeInt32ValuesImpl(numbers);

        /// <summary>
        /// Encodes the provided numbers into a hash string.
        /// </summary>
        /// <param name="numbers">List of 64-bit integers.</param>
        /// <returns>Encoded hash string.</returns>
        public string EncodeLong(params long[] numbers) => EncodeInt64ValuesImpl(numbers);

        /// <summary>
        /// Encodes the provided numbers into a hash string.
        /// </summary>
        /// <param name="numbers">Enumerable list of 64-bit integers.</param>
        /// <returns>Encoded hash string.</returns>
        public string EncodeLong(IEnumerable<long> numbers) => EncodeInt64ValuesImpl(numbers);

        /// <summary>
        /// Decodes the provided hash into <see cref="Int32"/> numbers.
        /// </summary>
        /// <param name="hash">Hash string to decode.</param>
        /// <returns>Array of integers.</returns>
        /// <exception cref="T:System.OverflowException">If the decoded number overflows integer.</exception>
        public virtual int[] Decode(string hash) => Array.ConvertAll(GetNumbersFrom(hash), n => (int)n);

        /// <summary>
        /// Decodes the provided hash into <see cref="Int64"/> numbers.
        /// </summary>
        /// <param name="hash">Hash string to decode.</param>
        /// <returns>Array of 64-bit integers.</returns>
        public long[] DecodeLong(string hash) => GetNumbersFrom(hash);

        /// <summary>
        /// Encodes the provided hex-string into a hash string. Returns <see cref="string.Empty"/> when <paramref name="hex"/> is null, empty or otherwise invalid.
        /// </summary>
        /// <param name="hex">Hex string to encode.</param>
        /// <returns>Encoded hash string.</returns>
        public virtual string EncodeHex(string? hex)
        {
            if (!IsNonemptyHexString(hex)) return string.Empty;

            var matches = hexSplitter.Value.Matches(hex);
            if (matches.Count == 0) return string.Empty;

            var numbers = new long[matches.Count];
            for (int i = 0; i < numbers.Length; i++)
            {
                Match match = matches[i];
                string concat = string.Concat("1", match.Value); // TODO: Why append '1'?
                var number = Convert.ToInt64(concat, fromBase: 16);
                numbers[i] = number;
            }

            return EncodeLong(numbers);
        }

        #endregion

        private string EncodeInt64ValuesImpl(IEnumerable<long> numbers)
        {
            using (RentedBuffer.RentCopy(source: numbers, out ArraySegment<long> i64Array))
            {
                return GenerateHashFrom(i64Array);
            }
        }

        private string EncodeInt32ValuesImpl(IEnumerable<int> numbers)
        {
            using (RentedBuffer.RentProjectedCopy(source: numbers, array: out ArraySegment<long> i64Array, valueSelector: i32 => (Int64)i32))
            {
                return GenerateHashFrom(i64Array);
            }
        }

        /// <summary>Indicates if <paramref name="value"/> is a non-null, non-empty string containing only an even number of hexadecimal (base 16) digit characters (<c>0, 1, 2, 3, 4, 5, 6, 7, 8, 9, A, B, C, D, E, F</c>).</summary>
        protected static bool IsNonemptyHexString( [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isHexDigit = (
                    (c >= '0' && c <= '9')
                    ||
                    (c >= 'A' && c <= 'F')
                    ||
                    (c >= 'a' && c <= 'f')
                );

                if (!isHexDigit) return false;
            }

            return true;
        }

        /// <summary>
        /// Decodes the provided hash into a hex-string.
        /// </summary>
        /// <param name="hash">Hash string to decode.</param>
        /// <returns>Decoded hex string.</returns>
        public virtual string DecodeHex(string hash)
        {
            var numbers = DecodeLong(hash);

            var builder = _sbPool.Get();

            foreach (var number in numbers)
            {
                var s = number.ToString("X");

                for (var i = 1; i < s.Length; i++)
                {
                    builder.Append(s[i]);
                }
            }

            var result = builder.ToString();
            _sbPool.Return(builder);
            return result;
        }

        private string GenerateHashFrom(ReadOnlySpan<long> numbers)
        {
            if (numbers.Length == 0 || numbers.Any(n => n < 0))
                return string.Empty;

            long numbersHashInt = 0;
            for (var i = 0; i < numbers.Length; i++)
            {
                numbersHashInt += numbers[i] % (i + 100);
            }

            var builder = _sbPool.Get();

            using(RentedBuffer.Rent    (_alphabet.Length   , out ArraySegment<char> shuffleBuffer))
            using(RentedBuffer.RentCopy(_alphabet          , out ArraySegment<char> alphabet))
            using(RentedBuffer.Rent    (MaxNumberHashLength, out ArraySegment<char> hashBuffer))
            {
                System.Diagnostics.Debug.Assert(alphabet.Count == _alphabet.Length); // Just a reminder.

                int lotteryIdx = (int)(numbersHashInt % _alphabet.Length);
                var lottery = alphabet[lotteryIdx];
                builder.Append(lottery);
                InitShuffleBuffer(shuffleBuffer, lottery, _salt);

                var startIndex = 1 + _salt.Length;
                var length = _alphabet.Length - startIndex;

                for (var i = 0; i < numbers.Length; i++)
                {
                    var number = numbers[i];

                    if (length > 0)
                    {
                        Array.Copy(sourceArray: alphabet.Array!, sourceIndex: 0, shuffleBuffer.Array!, destinationIndex: shuffleBuffer.Offset + startIndex, length: length);
                    }
                    
                    ConsistentShuffle(alphabet, salt: shuffleBuffer, saltLength: _alphabet.Length); // TODO: Is `saltLength` really correct here? Why isn't it `shuffleBuffer.Count` instead?
                    var hashLength = BuildReversedHash(number, alphabet, hashBuffer);

                    for (var j = hashLength - 1; j > -1; j--)
                    {
                        builder.Append(hashBuffer[j]);
                    }

                    if (i + 1 < numbers.Length)
                    {
                        number %= hashBuffer[hashLength - 1] + i;
                        var sepsIndex = number % _seps.Length;

                        builder.Append(_seps[sepsIndex]);
                    }
                }

                if (builder.Length < _minHashLength)
                {
                    var guardIndex = (numbersHashInt + builder[0]) % _guards.Length;
                    var guard = _guards[guardIndex];

                    builder.Insert(0, guard);

                    if (builder.Length < _minHashLength)
                    {
                        guardIndex = (numbersHashInt + builder[2]) % _guards.Length;
                        guard = _guards[guardIndex];

                        builder.Append(guard);
                    }
                }

                var halfLength = _alphabet.Length / 2;

                while (builder.Length < _minHashLength)
                {
                    alphabet.CopyTo(shuffleBuffer);

                    ConsistentShuffle(alphabet, salt: shuffleBuffer, saltLength: _alphabet.Length);
                    builder.Insert(index: 0, charsSegment: alphabet, charsSegmentOffset: halfLength, charCount: _alphabet.Length - halfLength);
                    builder.Append(charsSegment: alphabet, charsSegmentOffset: 0, charCount: halfLength);

                    var excess = builder.Length - _minHashLength;
                    if (excess > 0)
                    {
                        builder.Remove(0, excess / 2);
                        builder.Remove(_minHashLength, builder.Length - _minHashLength);
                    }
                }
            }

            var result = builder.ToString();
            _sbPool.Return(builder);
            return result;
        }

        private int BuildReversedHash(long input, ReadOnlySpan<char> alphabet, ArraySegment<char> hashBuffer)
        {
            var length = 0;
            do
            {
                int idx = (int)(input % _alphabet.Length);
                hashBuffer[length] = alphabet[idx];
                length += 1;
                input /= _alphabet.Length;
            }
            while (input > 0);

            return length;
        }

        private long Unhash(string input, ReadOnlySpan<char> alphabet)
        {
            long number = 0;

            for (var i = 0; i < input.Length; i++)
            {
                var pos = alphabet.IndexOf(input[i]); // TODO: Use a dictionary for O(1) lookup... provided alphabet is longer than... 10 chars?
                number = (number * _alphabet.Length) + pos;
            }

            return number;
        }

        private long[] GetNumbersFrom(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
                return Array.Empty<long>();

            var hashArray = hash.Split(_guards, StringSplitOptions.RemoveEmptyEntries);
            if (hashArray.Length == 0)
                return Array.Empty<long>();

            var hashBreakdownIdx = (hashArray.Length is 3 or 2 ) ? 1 : 0;

            var hashBreakdown = hashArray[hashBreakdownIdx];
            var lottery = hashBreakdown[0];

            if (lottery == '\0') /* default(char) == '\0' */
                return Array.Empty<long>();
                
            hashBreakdown = hashBreakdown.Substring(1);

            hashArray = hashBreakdown.Split(_seps, StringSplitOptions.RemoveEmptyEntries);

            var result = new long[hashArray.Length];

            using(RentedBuffer.RentCopy(_alphabet       , out ArraySegment<char> alphabet))
            using(RentedBuffer.Rent    (_alphabet.Length, out ArraySegment<char> buffer))
            {
                InitShuffleBuffer(buffer, lottery, _salt);

                var startIndex = 1 + _salt.Length;
                var length = _alphabet.Length - startIndex; // TODO: What is this the length of, exactly?

                for (var j = 0; j < hashArray.Length; j++)
                {
                    var subHash = hashArray[j];

                    if (length > 0)
                    {
                        alphabet.CopyTo(buffer.Slice(index: startIndex, count: length));
                    }

                    ConsistentShuffle(alphabetSegment: alphabet, salt: buffer, saltLength: _alphabet.Length); // TODO: Hold on, why is `saltLength` *not* `buffer.Length`?
                    result[j] = Unhash(subHash, alphabet);
                }
            }

            if (EncodeLong(result) == hash)
            {
                return result;
            }

            return Array.Empty<long>();
        }

        /// <summary>Sets <c><paramref name="shuffleBuffer"/>[0] = <paramref name="lottery"/></c>, and then copies chars from <c>offset: 0</c> in <paramref name="salt"/> into <paramref name="shuffleBuffer"/> (from <c>offset: 1</c>).</summary>
        private static void InitShuffleBuffer(ArraySegment<char> shuffleBuffer, char lottery, ReadOnlySpan<char> salt)
        {
            shuffleBuffer[0] = lottery;

            int copyCount = Math.Min(salt.Length, shuffleBuffer.Count - 1);

            for (int i = 0; i < copyCount; i++)
            {
                shuffleBuffer[i + 1] = salt[i];
            }
        }

        /// <summary>NOTE: This method mutates the <paramref name="alphabetSegment"/> argument in-place.</summary>
        private static void ConsistentShuffle(ArraySegment<char> alphabetSegment, ReadOnlySpan<char> salt, int saltLength)
        {
            ConsistentShuffle(alphabetSegment.Array!, alphabetSegment.Count, salt, saltLength);
        }

        /// <summary>NOTE: This method mutates the <paramref name="alphabet"/> argument in-place.</summary>
        private static void ConsistentShuffle(char[] alphabet, int alphabetLength, ReadOnlySpan<char> salt, int saltLength) // TODO: Why is `saltLength` a parameter at all? Why not use `salt.Length` instead?
        {
            if (salt.Length == 0)
                return;

            // TODO: Document or rename these cryptically-named variables: i, v, p, n.
            int n;
            for (int i = alphabetLength - 1, v = 0, p = 0; i > 0; i--, v++)
            {
                v %= saltLength;
                n = salt[v];
                p += n;
                var j = (n + v + p) % i;

                // swap characters at positions i and j:
                var temp = alphabet[j];
                alphabet[j] = alphabet[i];
                alphabet[i] = temp;
            }
        }
    }
}