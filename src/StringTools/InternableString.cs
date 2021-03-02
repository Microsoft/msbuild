// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Microsoft.NET.StringTools
{
    /// <summary>
    /// Represents a string that can be converted to System.String with interning, i.e. by returning an existing string if it has been seen before
    /// and is still tracked in the intern table.
    /// </summary>
    internal ref struct InternableString
    {
        /// <summary>
        /// Enumerator for the top-level struct. Enumerates characters of the string.
        /// </summary>
        public ref struct Enumerator
        {
            /// <summary>
            /// The InternableString being enumerated.
            /// </summary>
            private InternableString _string;

            /// <summary>
            /// Index of the current span, -1 represents the inline span.
            /// </summary>
            private int _spanIndex;

            /// <summary>
            /// Index of the current character in the current span, -1 if MoveNext has not been called yet.
            /// </summary>
            private int _charIndex;

            internal Enumerator(ref InternableString str)
            {
                _string = str;
                _spanIndex = -1;
                _charIndex = -1;
            }

            /// <summary>
            /// Returns the current character.
            /// </summary>
            public ref readonly char Current
            {
                get
                {
                    if (_spanIndex == -1)
                    {
                        return ref _string._inlineSpan[_charIndex];
                    }
                    ReadOnlyMemory<char> span = _string._spans![_spanIndex];
                    return ref span.Span[_charIndex];
                }
            }

            /// <summary>
            /// Moves to the next character.
            /// </summary>
            /// <returns>True if there is another character, false if the enumerator reached the end.</returns>
            public bool MoveNext()
            {
                int newCharIndex = _charIndex + 1;
                if (_spanIndex == -1)
                {
                    if (newCharIndex < _string._inlineSpan.Length)
                    {
                        _charIndex = newCharIndex;
                        return true;
                    }
                    _spanIndex = 0;
                    newCharIndex = 0;
                }

                if (_string._spans != null)
                {
                    while (_spanIndex < _string._spans.Count)
                    {
                        if (newCharIndex < _string._spans[_spanIndex].Length)
                        {
                            _charIndex = newCharIndex;
                            return true;
                        }
                        _spanIndex++;
                        newCharIndex = 0;
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// The span held by this struct, inline to be able to represent <see cref="ReadOnlySpan{char}"/>. May be empty.
        /// </summary>
        private readonly ReadOnlySpan<char> _inlineSpan;

#if RUNTIME_TYPE_NETCORE
        /// <summary>
        /// .NET Core does not keep a reference to the containing object in <see cref="ReadOnlySpan{char}"/>. In particular,
        /// it cannot recover the string if the span represents one. We have to hold the reference separately to be able to
        /// roundtrip String-&gt;InternableString-&gt;String without allocating a new String.
        /// </summary>
        private string? _inlineSpanString;
#endif

        /// <summary>
        /// Additional spans held by this struct. May be null.
        /// </summary>
        private List<ReadOnlyMemory<char>>? _spans;

        /// <summary>
        /// Constructs a new InternableString wrapping the given <see cref="ReadOnlySpan{char}"/>.
        /// </summary>
        /// <param name="span">The span to wrap.</param>
        /// <remarks>
        /// When wrapping a span representing an entire System.String, use Internable(string) for optimum performance.
        /// </remarks>
        internal InternableString(ReadOnlySpan<char> span)
        {
            _inlineSpan = span;
            _spans = null;
            Length = span.Length;
#if RUNTIME_TYPE_NETCORE
            _inlineSpanString = null;
#endif
        }

        /// <summary>
        /// Constructs a new InternableString wrapping the given string.
        /// </summary>
        /// <param name="str">The string to wrap, must be non-null.</param>
        internal InternableString(string str)
        {
            if (str == null)
            {
                throw new ArgumentNullException(nameof(str));
            }

            _inlineSpan = str.AsSpan();
            _spans = null;
            Length = str.Length;
#if RUNTIME_TYPE_NETCORE
            _inlineSpanString = str;
#endif
        }

        /// <summary>
        /// Constructs a new InternableString wrapping the given SpanBasedStringBuilder.
        /// </summary>
        internal InternableString(SpanBasedStringBuilder stringBuilder)
        {
            _inlineSpan = default(ReadOnlySpan<char>);
            _spans = stringBuilder.Spans;
            Length = stringBuilder.Length;
#if RUNTIME_TYPE_NETCORE
            _inlineSpanString = null;
#endif
        }

        /// <summary>
        /// Gets the length of the string.
        /// </summary>
        public int Length { get; private set; }

        /// <summary>
        /// Creates a new enumerator for enumerating characters in this string. Does not allocate.
        /// </summary>
        /// <returns>The enumerator.</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(ref this);
        }

        /// <summary>
        /// Returns true if the string is equal to another string by ordinal comparison.
        /// </summary>
        /// <param name="other">Another string.</param>
        /// <returns>True if this string is equal to <paramref name="other"/>.</returns>
        public bool Equals(string other)
        {
            if (other.Length != Length)
            {
                return false;
            }

            if (_inlineSpan.SequenceCompareTo(other.AsSpan(0, _inlineSpan.Length)) != 0)
            {
                return false;
            }

            if (_spans != null)
            {
                int otherStart = _inlineSpan.Length;
                foreach (ReadOnlyMemory<char> span in _spans)
                {
                    if (span.Span.SequenceCompareTo(other.AsSpan(otherStart, span.Length)) != 0)
                    {
                        return false;
                    }
                    otherStart += span.Length;
                }
            }
            return true;
        }

        /// <summary>
        /// Returns a System.String representing this string. Allocates memory unless this InternableString was created by wrapping a
        /// System.String in which case the original string is returned.
        /// </summary>
        /// <returns>The string.</returns>
        public unsafe string ExpensiveConvertToString()
        {
            if (Length == 0)
            {
                return string.Empty;
            }

            // Special case: if we hold just one string, we can directly return it.
            if (_inlineSpan.Length == Length)
            {
#if RUNTIME_TYPE_NETCORE
                if (_inlineSpanString != null)
                {
                    return _inlineSpanString;
                }
#else
                return _inlineSpan.ToString();
#endif
            }
            if (_inlineSpan.IsEmpty && _spans?[0].Length == Length)
            {
                return _spans[0].ToString();
            }

            // In all other cases we create a new string instance and concatenate all spans into it. Note that while technically mutating
            // the System.String, the technique is generally considered safe as we are the sole owners of the new object. It is important
            // to initialize the string with the '\0' characters as this hits an optimized code path in the runtime.
            string result = new string((char)0, Length);

            fixed (char* resultPtr = result)
            {
                char* destPtr = resultPtr;
                if (!_inlineSpan.IsEmpty)
                {
                    fixed (char* sourcePtr = _inlineSpan)
                    {
                        Unsafe.CopyBlockUnaligned(destPtr, sourcePtr, 2 * (uint)_inlineSpan.Length);
                    }
                    destPtr += _inlineSpan.Length;
                }

                if (_spans != null)
                {
                    foreach (ReadOnlyMemory<char> span in _spans)
                    {
                        if (!span.IsEmpty)
                        {
                            fixed (char* sourcePtr = span.Span)
                            {
                                Unsafe.CopyBlockUnaligned(destPtr, sourcePtr, 2 * (uint)span.Length);
                            }
                            destPtr += span.Length;
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Returns true if this InternableString wraps a System.String and the same System.String is passed as the argument.
        /// </summary>
        /// <param name="str">The string to compare to.</param>
        /// <returns>True is this instance wraps the given string.</returns>
        public bool ReferenceEquals(string str)
        {
            if (_inlineSpan.Length == Length)
            {
                return _inlineSpan == str.AsSpan();
            }
            if (_inlineSpan.IsEmpty && _spans?.Count == 1 && _spans[0].Length == Length)
            {
                return _spans[0].Span == str.AsSpan();
            }
            return false;
        }

        /// <summary>
        /// Converts this instance to a System.String while first searching for a match in the intern table.
        /// </summary>
        /// <remarks>
        /// May allocate depending on whether the string has already been interned.
        /// </remarks>
        public override string ToString()
        {
            return WeakStringCacheInterner.Instance.InternableToString(ref this);
        }

        /// <summary>
        /// Implements the simple yet very decently performing djb2 hash function (xor version).
        /// </summary>
        /// <returns>A stable hashcode of the string represented by this instance.</returns>
        public override unsafe int GetHashCode()
        {
            int hashCode = 5381;
            fixed (char* charPtr = _inlineSpan)
            {
                for (int i = 0; i < _inlineSpan.Length; i++)
                {
                    hashCode = unchecked(hashCode * 33 ^ charPtr[i]);
                }
            }
            if (_spans != null)
            {
                foreach (ReadOnlyMemory<char> span in _spans)
                {
                    fixed (char* charPtr = span.Span)
                    {
                        for (int i = 0; i < span.Length; i++)
                        {
                            hashCode = unchecked(hashCode * 33 ^ charPtr[i]);
                        }
                    }
                }
            }
            return hashCode;
        }
    }
}
