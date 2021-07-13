// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using System.Globalization;
using System.Reflection;

namespace Microsoft.Build.BackEnd
{
    /// <summary>
    /// This class is responsible for serializing and deserializing simple types to and
    /// from the byte streams used to communicate INodePacket-implementing classes.
    /// Each class implements a Translate method on INodePacket which takes this class
    /// as a parameter, and uses it to store and retrieve fields to the stream.
    /// </summary>
    static internal class BinaryTranslator
    {
        /// <summary>
        /// Returns a read-only serializer.
        /// </summary>
        /// <returns>The serializer.</returns>
        static internal ITranslator GetReadTranslator(Stream stream, SharedReadBuffer buffer)
        {
            return new BinaryReadTranslator(stream, buffer);
        }

        /// <summary>
        /// Returns a write-only serializer.
        /// </summary>
        /// <param name="stream">The stream containing data to serialize.</param>
        /// <returns>The serializer.</returns>
        static internal ITranslator GetWriteTranslator(Stream stream)
        {
            return new BinaryWriteTranslator(stream);
        }

        /// <summary>
        /// Implementation of ITranslator for reading from a stream.
        /// </summary>
        private class BinaryReadTranslator : ITranslator
        {
            /// <summary>
            /// The stream used as a source or destination for data.
            /// </summary>
            private Stream _packetStream;

            /// <summary>
            /// The binary reader used in read mode.
            /// </summary>
            private BinaryReader _reader;

            /// <summary>
            /// Constructs a serializer from the specified stream, operating in the designated mode.
            /// </summary>
            public BinaryReadTranslator(Stream packetStream, SharedReadBuffer buffer)
            {
                _packetStream = packetStream;
                _reader = InterningBinaryReader.Create(packetStream, buffer);
            }

            /// <summary>
            /// Delegates the Dispose call the to the underlying BinaryReader.
            /// </summary>
            public void Dispose()
            {
                _reader.Close();
            }

            /// <summary>
            /// Gets the reader, if any.
            /// </summary>
            public BinaryReader Reader
            {
                get { return _reader; }
            }

            /// <summary>
            /// Gets the writer, if any.
            /// </summary>
            public BinaryWriter Writer
            {
                get
                {
                    ErrorUtilities.ThrowInternalError("Cannot get writer from reader.");
                    return null;
                }
            }

            /// <summary>
            /// Returns the current serialization mode.
            /// </summary>
            public TranslationDirection Mode
            {
                [DebuggerStepThrough]
                get
                { return TranslationDirection.ReadFromStream; }
            }

            /// <summary>
            /// Translates a boolean.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref bool value)
            {
                value = _reader.ReadBoolean();
            }

            /// <summary>
            /// Translates a byte.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref byte value)
            {
                value = _reader.ReadByte();
            }

            /// <summary>
            /// Translates a short.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref short value)
            {
                value = _reader.ReadInt16();
            }

            /// <summary>
            /// Translates an unsigned short.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref ushort value)
            {
                value = _reader.ReadUInt16();
            }

            /// <summary>
            /// Translates an integer.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref int value)
            {
                value = _reader.ReadInt32();
            }

            /// <summary>
            /// Translates a long.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref long value)
            {
                value = _reader.ReadInt64();
            }

            /// <summary>
            /// Translates a double.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref double value)
            {
                value = _reader.ReadDouble();
            }

            /// <summary>
            /// Translates a string.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref string value)
            {
                if (!TranslateNullable(value))
                {
                    return;
                }

                value = _reader.ReadString();
            }

            /// <summary>
            /// Translates a byte array
            /// </summary>
            /// <param name="byteArray">The array to be translated</param>
            public void Translate(ref byte[] byteArray)
            {
                if (!TranslateNullable(byteArray))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                if (count > 0)
                {
                    byteArray = _reader.ReadBytes(count);
                }
                else
                {
                    byteArray = new byte[0];
                }
            }

            /// <summary>
            /// Translates a byte array
            /// </summary>
            /// <param name="byteArray">The array to be translated.</param>
            /// <param name="length">The length of array which will be used in translation. This parameter is not used when reading</param>
            public void Translate(ref byte[] byteArray, ref int length) 
            {
                Translate(ref byteArray);
                length = byteArray.Length;
            }

            /// <summary>
            /// Translates a string array.
            /// </summary>
            /// <param name="array">The array to be translated.</param>
            public void Translate(ref string[] array)
            {
                if (!TranslateNullable(array))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                array = new string[count];

                for (int i = 0; i < count; i++)
                {
                    array[i] = _reader.ReadString();
                }
            }

            /// <inheritdoc />
            public void Translate(ref HashSet<string> set)
            {
                if (!TranslateNullable(set))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                set = new HashSet<string>();

                for (int i = 0; i < count; i++)
                {
                    set.Add(_reader.ReadString());
                }
            }

            /// <summary>
            /// Translates a list of strings
            /// </summary>
            /// <param name="list">The list to be translated.</param>
            public void Translate(ref List<string> list)
            {
                if (!TranslateNullable(list))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                list = new List<string>(count);

                for (int i = 0; i < count; i++)
                {
                    list.Add(_reader.ReadString());
                }
            }

            /// <summary>
            /// Translates a list of T using an <see cref="ObjectTranslator{T}"/>
            /// </summary>
            /// <param name="list">The list to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the items in the list</param>
            /// <typeparam name="T">TaskItem type</typeparam>
            public void Translate<T>(ref List<T> list, ObjectTranslator<T> objectTranslator)
            {
                IList<T> listAsInterface = list;
                Translate(ref listAsInterface, objectTranslator, count => new List<T>(count));
                list = (List<T>) listAsInterface;
            }

            public void Translate<T, L>(ref IList<T> list, ObjectTranslator<T> objectTranslator, NodePacketCollectionCreator<L> collectionFactory) where L : IList<T>
            {
                if (!TranslateNullable(list))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                list = collectionFactory(count);

                for (int i = 0; i < count; i++)
                {
                    T value = default(T);

                    objectTranslator(this, ref value);
                    list.Add(value);
                }
            }

            /// <summary>
            /// Translates a collection of T into the specified type using an <see cref="ObjectTranslator{T}"/> and <see cref="NodePacketCollectionCreator{L}"/>
            /// </summary>
            /// <param name="collection">The collection to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the values in the collection.</param>
            /// <param name="collectionFactory">The factory to create the ICollection.</param>
            /// <typeparam name="T">The type contained in the collection.</typeparam>
            /// <typeparam name="L">The type of collection to be created.</typeparam>
            public void Translate<T, L>(ref ICollection<T> collection, ObjectTranslator<T> objectTranslator, NodePacketCollectionCreator<L> collectionFactory) where L : ICollection<T>
            {
                if (!TranslateNullable(collection))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                collection = collectionFactory(count);

                for (int i = 0; i < count; i++)
                {
                    T value = default(T);
                    objectTranslator(this, ref value);
                    collection.Add(value);
                }
            }

            /// <summary>
            /// Translates a DateTime.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref DateTime value)
            {
                DateTimeKind kind = DateTimeKind.Unspecified;
                TranslateEnum<DateTimeKind>(ref kind, 0);
                value = new DateTime(_reader.ReadInt64(), kind);
            }

            /// <summary>
            /// Translates a TimeSpan.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref TimeSpan value)
            {
                long ticks = 0;
                Translate(ref ticks);
                value = new System.TimeSpan(ticks);
            }

            // MSBuildTaskHost is based on CLR 3.5, which does not have the 6-parameter constructor for BuildEventContext.  
            // However, it also does not ever need to translate BuildEventContexts, so it should be perfectly safe to 
            // compile this method out of that assembly. 
#if !CLR2COMPATIBILITY

            /// <summary>
            /// Translates a BuildEventContext
            /// </summary>
            /// <remarks>
            /// This method exists only because there is no serialization method built into the BuildEventContext
            /// class, and it lives in Framework and we don't want to add a public method to it.
            /// </remarks>
            /// <param name="value">The context to be translated.</param>
            public void Translate(ref BuildEventContext value)
            {
                value = new BuildEventContext
                    (
                    _reader.ReadInt32(),
                    _reader.ReadInt32(),
                    _reader.ReadInt32(),
                    _reader.ReadInt32(),
                    _reader.ReadInt32(),
                    _reader.ReadInt32(),
                    _reader.ReadInt32()
                    );
            }

#endif

            /// <summary>
            /// Translates a CultureInfo
            /// </summary>
            /// <param name="value">The CultureInfo to translate</param>
            public void TranslateCulture(ref CultureInfo value)
            {
                string cultureName = _reader.ReadString();

#if CLR2COMPATIBILITY
                // It may be that some culture codes are accepted on later .net framework versions
                // but not on the older 3.5 or 2.0. Fallbacks are required in this case to prevent
                // exceptions
                value = LoadCultureWithFallback(cultureName);
#else
                value = new CultureInfo(cultureName);
#endif
            }

#if CLR2COMPATIBILITY
            private static CultureInfo LoadCultureWithFallback(string cultureName)
            {
                CultureInfo cultureInfo;

                return TryLoadCulture(cultureName, out cultureInfo) ? cultureInfo : CultureInfo.CurrentCulture;
            }

            private static bool TryLoadCulture(string cultureName, out CultureInfo cultureInfo)
            {
                try
                {
                    cultureInfo = new CultureInfo(cultureName);
                    return true;
                }
                catch
                {
                    cultureInfo = null;
                    return false;
                }
            }
#endif

            /// <summary>
            /// Translates an enumeration.
            /// </summary>
            /// <typeparam name="T">The enumeration type.</typeparam>
            /// <param name="value">The enumeration instance to be translated.</param>
            /// <param name="numericValue">The enumeration value as an integer.</param>
            /// <remarks>This is a bit ugly, but it doesn't seem like a nice method signature is possible because
            /// you can't pass the enum type as a reference and constrain the generic parameter to Enum.  Nor
            /// can you simply pass as ref Enum, because an enum instance doesn't match that function signature.
            /// Finally, converting the enum to an int assumes that we always want to transport enums as ints.  This
            /// works in all of our current cases, but certainly isn't perfectly generic.</remarks>
            public void TranslateEnum<T>(ref T value, int numericValue)
            {
                numericValue = _reader.ReadInt32();
                Type enumType = value.GetType();
                value = (T)Enum.ToObject(enumType, numericValue);
            }

            /// <summary>
            /// Translates a value using the .Net binary formatter.
            /// </summary>
            /// <typeparam name="T">The reference type.</typeparam>
            /// <param name="value">The value to be translated.</param>
            public void TranslateDotNet<T>(ref T value)
            {
                if (!TranslateNullable(value))
                {
                    return;
                }

                BinaryFormatter formatter = new BinaryFormatter();
                value = (T)formatter.Deserialize(_packetStream);
            }

            public void TranslateException(ref Exception value)
            {
                TranslateDotNet<Exception>(ref value);
            }


            /// <summary>
            /// Translates an object implementing INodePacketTranslatable.
            /// </summary>
            /// <typeparam name="T">The reference type.</typeparam>
            /// <param name="value">The value to be translated.</param>
            public void Translate<T>(ref T value)
                where T : ITranslatable, new()
            {
                if (!TranslateNullable(value))
                {
                    return;
                }

                value = new T();
                value.Translate(this);
            }

            /// <summary>
            /// Translates an array of objects implementing INodePacketTranslatable.
            /// </summary>
            /// <typeparam name="T">The reference type.</typeparam>
            /// <param name="array">The array to be translated.</param>
            public void TranslateArray<T>(ref T[] array)
                where T : ITranslatable, new()
            {
                if (!TranslateNullable(array))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                array = new T[count];

                for (int i = 0; i < count; i++)
                {
                    array[i] = new T();
                    array[i].Translate(this);
                }
            }

            /// <summary>
            /// Translates an array of objects using an <see cref="ObjectTranslator{T}"/>
            /// </summary>
            /// <typeparam name="T">The reference type.</typeparam>
            /// <param name="array">The array to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the elements in the array</param>
            public void TranslateArray<T>(ref T[] array, ObjectTranslator<T> objectTranslator)
            {
                if (!TranslateNullable(array))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                array = new T[count];

                for (int i = 0; i < count; i++)
                {
                    objectTranslator(this, ref array[i]);
                }
            }

            /// <summary>
            /// Translates a dictionary of { string, string }.
            /// </summary>
            /// <param name="dictionary">The dictionary to be translated.</param>
            /// <param name="comparer">The comparer used to instantiate the dictionary.</param>
            public void TranslateDictionary(ref Dictionary<string, string> dictionary, IEqualityComparer<string> comparer)
            {
                IDictionary<string, string> copy = dictionary;

                TranslateDictionary(
                    ref copy,
                    count => new Dictionary<string, string>(count, comparer));

                dictionary = (Dictionary<string, string>) copy;
            }

            public void TranslateDictionary(ref IDictionary<string, string> dictionary, NodePacketCollectionCreator<IDictionary<string, string>> dictionaryCreator)
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                dictionary = dictionaryCreator(count);

                for (int i = 0; i < count; i++)
                {
                    string key = null;
                    Translate(ref key);
                    string value = null;
                    Translate(ref value);
                    dictionary[key] = value;
                }
            }

            public void TranslateDictionary<K, V>(
                ref IDictionary<K, V> dictionary,
                ObjectTranslator<K> keyTranslator,
                ObjectTranslator<V> valueTranslator,
                NodePacketCollectionCreator<IDictionary<K, V>> dictionaryCreator)
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                dictionary = dictionaryCreator(count);

                for (int i = 0; i < count; i++)
                {
                    K key = default(K);
                    keyTranslator(this, ref key);
                    V value = default(V);
                    valueTranslator(this, ref value);
                    dictionary[key] = value;
                }
            }

            /// <summary>
            /// Translates a dictionary of { string, T }.  
            /// </summary>
            /// <typeparam name="T">The reference type for the values</typeparam>
            /// <param name="dictionary">The dictionary to be translated.</param>
            /// <param name="comparer">The comparer used to instantiate the dictionary.</param>
            /// <param name="objectTranslator">The translator to use for the values in the dictionary</param>
            public void TranslateDictionary<T>(ref Dictionary<string, T> dictionary, IEqualityComparer<string> comparer, ObjectTranslator<T> objectTranslator)
                where T : class
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                dictionary = new Dictionary<string, T>(count, comparer);

                for (int i = 0; i < count; i++)
                {
                    string key = null;
                    Translate(ref key);
                    T value = null;
                    objectTranslator(this, ref value);
                    dictionary[key] = value;
                }
            }

            /// <summary>
            /// Translates a dictionary of { string, T } for dictionaries with public parameterless constructors.
            /// </summary>
            /// <typeparam name="D">The reference type for the dictionary.</typeparam>
            /// <typeparam name="T">The reference type for values in the dictionary.</typeparam>
            /// <param name="dictionary">The dictionary to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the values in the dictionary</param>
            public void TranslateDictionary<D, T>(ref D dictionary, ObjectTranslator<T> objectTranslator)
                where D : IDictionary<string, T>, new()
                where T : class
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                dictionary = new D();

                for (int i = 0; i < count; i++)
                {
                    string key = null;
                    Translate(ref key);
                    T value = null;
                    objectTranslator(this, ref value);
                    dictionary[key] = value;
                }
            }

            /// <summary>
            /// Translates a dictionary of { string, T } for dictionaries with public parameterless constructors.
            /// </summary>
            /// <typeparam name="D">The reference type for the dictionary.</typeparam>
            /// <typeparam name="T">The reference type for values in the dictionary.</typeparam>
            /// <param name="dictionary">The dictionary to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the values in the dictionary</param>
            /// <param name="dictionaryCreator">The delegate used to instantiate the dictionary.</param>
            public void TranslateDictionary<D, T>(ref D dictionary, ObjectTranslator<T> objectTranslator, NodePacketCollectionCreator<D> dictionaryCreator)
                where D : IDictionary<string, T>
                where T : class
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                dictionary = dictionaryCreator(count);

                for (int i = 0; i < count; i++)
                {
                    string key = null;
                    Translate(ref key);
                    T value = null;
                    objectTranslator(this, ref value);
                    dictionary[key] = value;
                }
            }

            public void TranslateDictionary(ref Dictionary<string, DateTime> dictionary, StringComparer comparer)
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = _reader.ReadInt32();
                dictionary = new(count, comparer);
                string key = string.Empty;
                DateTime val = DateTime.MinValue;
                for (int i = 0; i < count; i++)
                {
                    Translate(ref key);
                    Translate(ref val);
                    dictionary.Add(key, val);
                }
            }

        /// <summary>
        /// Reads in the boolean which says if this object is null or not.
        /// </summary>
        /// <typeparam name="T">The type of object to test.</typeparam>
        /// <returns>True if the object should be read, false otherwise.</returns>
        public bool TranslateNullable<T>(T value)
            {
                bool haveRef = _reader.ReadBoolean();
                return haveRef;
            }
        }

        /// <summary>
        /// Implementation of ITranslator for writing to a stream.
        /// </summary>
        private class BinaryWriteTranslator : ITranslator
        {
            /// <summary>
            /// The stream used as a source or destination for data.
            /// </summary>
            private Stream _packetStream;

            /// <summary>
            /// The binary writer used in write mode.
            /// </summary>
            private BinaryWriter _writer;

            /// <summary>
            /// Constructs a serializer from the specified stream, operating in the designated mode.
            /// </summary>
            /// <param name="packetStream">The stream serving as the source or destination of data.</param>
            public BinaryWriteTranslator(Stream packetStream)
            {
                _packetStream = packetStream;
                _writer = new BinaryWriter(packetStream);
            }

            /// <summary>
            /// Delegates the Dispose call the to the underlying BinaryWriter.
            /// </summary>
            public void Dispose()
            {
                _writer.Close();
            }

            /// <summary>
            /// Gets the reader, if any.
            /// </summary>
            public BinaryReader Reader
            {
                get
                {
                    ErrorUtilities.ThrowInternalError("Cannot get reader from writer.");
                    return null;
                }
            }

            /// <summary>
            /// Gets the writer, if any.
            /// </summary>
            public BinaryWriter Writer
            {
                get { return _writer; }
            }

            /// <summary>
            /// Returns the current serialization mode.
            /// </summary>
            public TranslationDirection Mode
            {
                [DebuggerStepThrough]
                get
                { return TranslationDirection.WriteToStream; }
            }

            /// <summary>
            /// Translates a boolean.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref bool value)
            {
                _writer.Write(value);
            }

            /// <summary>
            /// Translates a byte.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref byte value)
            {
                _writer.Write(value);
            }

            /// <summary>
            /// Translates a short.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref short value)
            {
                _writer.Write(value);
            }

            /// <summary>
            /// Translates an unsigned short.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref ushort value)
            {
                _writer.Write(value);
            }

            /// <summary>
            /// Translates an integer.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref int value)
            {
                _writer.Write(value);
            }

            /// <summary>
            /// Translates a long.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref long value)
            {
                _writer.Write(value);
            }

            /// <summary>
            /// Translates a double.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref double value)
            {
                _writer.Write(value);
            }

            /// <summary>
            /// Translates a string.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref string value)
            {
                if (!TranslateNullable(value))
                {
                    return;
                }

                _writer.Write(value);
            }

            /// <summary>
            /// Translates a string array.
            /// </summary>
            /// <param name="array">The array to be translated.</param>
            public void Translate(ref string[] array)
            {
                if (!TranslateNullable(array))
                {
                    return;
                }

                int count = array.Length;
                _writer.Write(count);

                for (int i = 0; i < count; i++)
                {
                    _writer.Write(array[i]);
                }
            }

            /// <summary>
            /// Translates a list of strings
            /// </summary>
            /// <param name="list">The list to be translated.</param>
            public void Translate(ref List<string> list)
            {
                if (!TranslateNullable(list))
                {
                    return;
                }

                int count = list.Count;
                _writer.Write(count);

                for (int i = 0; i < count; i++)
                {
                    _writer.Write(list[i]);
                }
            }

            /// <inheritdoc />
            public void Translate(ref HashSet<string> set)
            {
                if (!TranslateNullable(set))
                {
                    return;
                }

                int count = set.Count;
                _writer.Write(count);

                foreach (var item in set)
                {
                    _writer.Write(item);
                }
            }

            /// <summary>
            /// Translates a list of T using an <see cref="ObjectTranslator{T}"/>
            /// </summary>
            /// <param name="list">The list to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the items in the list</param>
            /// <typeparam name="T">A TaskItemType</typeparam>
            public void Translate<T>(ref List<T> list, ObjectTranslator<T> objectTranslator)
            {
                if (!TranslateNullable(list))
                {
                    return;
                }

                int count = list.Count;
                _writer.Write(count);

                for (int i = 0; i < count; i++)
                {
                    T value = list[i];
                    objectTranslator(this, ref value);
                }
            }

            /// <summary>
            /// Translates a list of T using an <see cref="ObjectTranslator{T}"/>
            /// </summary>
            /// <param name="list">The list to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the items in the list</param>
            /// <param name="collectionFactory">factory to create the IList</param>
            /// <typeparam name="T">A TaskItemType</typeparam>
            /// <typeparam name="L">IList subtype</typeparam>
            public void Translate<T, L>(ref IList<T> list, ObjectTranslator<T> objectTranslator, NodePacketCollectionCreator<L> collectionFactory) where L : IList<T>
            {
                if (!TranslateNullable(list))
                {
                    return;
                }

                int count = list.Count;
                _writer.Write(count);

                for (int i = 0; i < count; i++)
                {
                    T value = list[i];
                    objectTranslator(this, ref value);
                }
            }

            /// <summary>
            /// Translates a collection of T into the specified type using an <see cref="ObjectTranslator{T}"/> and <see cref="NodePacketCollectionCreator{L}"/>
            /// </summary>
            /// <param name="collection">The collection to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the values in the collection.</param>
            /// <param name="collectionFactory">The factory to create the ICollection.</param>
            /// <typeparam name="T">The type contained in the collection.</typeparam>
            /// <typeparam name="L">The type of collection to be created.</typeparam>
            public void Translate<T, L>(ref ICollection<T> collection, ObjectTranslator<T> objectTranslator, NodePacketCollectionCreator<L> collectionFactory) where L : ICollection<T>
            {
                if (!TranslateNullable(collection))
                {
                    return;
                }

                _writer.Write(collection.Count);

                foreach (T item in collection)
                {
                    T value = item;
                    objectTranslator(this, ref value);
                }
            }

            /// <summary>
            /// Translates a DateTime.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref DateTime value)
            {
                DateTimeKind kind = value.Kind;
                TranslateEnum<DateTimeKind>(ref kind, (int)kind);
                _writer.Write(value.Ticks);
            }

            /// <summary>
            /// Translates a TimeSpan.
            /// </summary>
            /// <param name="value">The value to be translated.</param>
            public void Translate(ref TimeSpan value)
            {
                _writer.Write(value.Ticks);
            }

            // MSBuildTaskHost is based on CLR 3.5, which does not have the 6-parameter constructor for BuildEventContext.  
            // However, it also does not ever need to translate BuildEventContexts, so it should be perfectly safe to 
            // compile this method out of that assembly. 
#if !CLR2COMPATIBILITY

            /// <summary>
            /// Translates a BuildEventContext
            /// </summary>
            /// <remarks>
            /// This method exists only because there is no serialization method built into the BuildEventContext
            /// class, and it lives in Framework and we don't want to add a public method to it.
            /// </remarks>
            /// <param name="value">The context to be translated.</param>
            public void Translate(ref BuildEventContext value)
            {
                _writer.Write(value.SubmissionId);
                _writer.Write(value.NodeId);
                _writer.Write(value.EvaluationId);
                _writer.Write(value.ProjectInstanceId);
                _writer.Write(value.ProjectContextId);
                _writer.Write(value.TargetId);
                _writer.Write(value.TaskId);
            }

#endif 

            /// <summary>
            /// Translates a CultureInfo
            /// </summary>
            /// <param name="value">The CultureInfo</param>
            public void TranslateCulture(ref CultureInfo value)
            {
                _writer.Write(value.Name);
            }

            /// <summary>
            /// Translates an enumeration.
            /// </summary>
            /// <typeparam name="T">The enumeration type.</typeparam>
            /// <param name="value">The enumeration instance to be translated.</param>
            /// <param name="numericValue">The enumeration value as an integer.</param>
            /// <remarks>This is a bit ugly, but it doesn't seem like a nice method signature is possible because
            /// you can't pass the enum type as a reference and constrain the generic parameter to Enum.  Nor
            /// can you simply pass as ref Enum, because an enum instance doesn't match that function signature.
            /// Finally, converting the enum to an int assumes that we always want to transport enums as ints.  This
            /// works in all of our current cases, but certainly isn't perfectly generic.</remarks>
            public void TranslateEnum<T>(ref T value, int numericValue)
            {
                Type enumType = value.GetType();
                ErrorUtilities.VerifyThrow(enumType.GetTypeInfo().IsEnum, "Must pass an enum type.");

                _writer.Write(numericValue);
            }

            /// <summary>
            /// Translates a value using the .Net binary formatter.
            /// </summary>
            /// <typeparam name="T">The reference type.</typeparam>
            /// <param name="value">The value to be translated.</param>
            public void TranslateDotNet<T>(ref T value)
            {
                if (!TranslateNullable(value))
                {
                    return;
                }

                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(_packetStream, value);
            }

            public void TranslateException(ref Exception value)
            {
                TranslateDotNet<Exception>(ref value);
            }

            /// <summary>
            /// Translates an object implementing INodePacketTranslatable.
            /// </summary>
            /// <typeparam name="T">The reference type.</typeparam>
            /// <param name="value">The value to be translated.</param>
            public void Translate<T>(ref T value)
                where T : ITranslatable, new()
            {
                if (!TranslateNullable(value))
                {
                    return;
                }

                value.Translate(this);
            }

            /// <summary>
            /// Translates a byte array
            /// </summary>
            /// <param name="byteArray">The byte array to be translated</param>
            public void Translate(ref byte[] byteArray)
            {
                var length = byteArray?.Length ?? 0;
                Translate(ref byteArray, ref length);
            }

            /// <summary>
            /// Translates a byte array
            /// </summary>
            /// <param name="byteArray">The array to be translated.</param>
            /// <param name="length">The length of array which will be used in translation</param>
            public void Translate(ref byte[] byteArray, ref int length) 
            {
                if (!TranslateNullable(byteArray))
                {
                    return;
                }

                _writer.Write(length);
                if (length > 0)
                {
                    _writer.Write(byteArray, 0, length);
                }
            }

            /// <summary>
            /// Translates an array of objects implementing INodePacketTranslatable.
            /// </summary>
            /// <typeparam name="T">The reference type.</typeparam>
            /// <param name="array">The array to be translated.</param>
            public void TranslateArray<T>(ref T[] array)
                where T : ITranslatable, new()
            {
                if (!TranslateNullable(array))
                {
                    return;
                }

                int count = array.Length;
                _writer.Write(count);

                for (int i = 0; i < count; i++)
                {
                    array[i].Translate(this);
                }
            }

            /// <summary>
            /// Translates an array of objects using an <see cref="ObjectTranslator{T}"/>
            /// </summary>
            /// <typeparam name="T">The reference type.</typeparam>
            /// <param name="array">The array to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the elements in the array</param>
            public void TranslateArray<T>(ref T[] array, ObjectTranslator<T> objectTranslator)
            {
                if (!TranslateNullable(array))
                {
                    return;
                }

                int count = array.Length;
                _writer.Write(count);

                for (int i = 0; i < count; i++)
                {
                    objectTranslator(this, ref array[i]);
                }
            }

            /// <summary>
            /// Translates a dictionary of { string, string }.
            /// </summary>
            /// <param name="dictionary">The dictionary to be translated.</param>
            /// <param name="comparer">The comparer used to instantiate the dictionary.</param>
            public void TranslateDictionary(ref Dictionary<string, string> dictionary, IEqualityComparer<string> comparer)
            {
                IDictionary<string, string> copy = dictionary;
                TranslateDictionary(ref copy, (NodePacketCollectionCreator<IDictionary<string, string>>)null);
            }

            public void TranslateDictionary(ref IDictionary<string, string> dictionary, NodePacketCollectionCreator<IDictionary<string, string>> dictionaryCreator)
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = dictionary.Count;
                _writer.Write(count);

                foreach (KeyValuePair<string, string> pair in dictionary)
                {
                    string key = pair.Key;
                    Translate(ref key);
                    string value = pair.Value;
                    Translate(ref value);
                }
            }

            public void TranslateDictionary<K, V>(
                ref IDictionary<K, V> dictionary,
                ObjectTranslator<K> keyTranslator,
                ObjectTranslator<V> valueTranslator,
                NodePacketCollectionCreator<IDictionary<K, V>> collectionCreator)
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = dictionary.Count;
                _writer.Write(count);

                foreach (KeyValuePair<K, V> pair in dictionary)
                {
                    K key = pair.Key;
                    keyTranslator(this, ref key);
                    V value = pair.Value;
                    valueTranslator(this, ref value);
                }
            }

            /// <summary>
            /// Translates a dictionary of { string, T }.  
            /// </summary>
            /// <typeparam name="T">The reference type for the values, which implements INodePacketTranslatable.</typeparam>
            /// <param name="dictionary">The dictionary to be translated.</param>
            /// <param name="comparer">The comparer used to instantiate the dictionary.</param>
            /// <param name="objectTranslator">The translator to use for the values in the dictionary</param>
            public void TranslateDictionary<T>(ref Dictionary<string, T> dictionary, IEqualityComparer<string> comparer, ObjectTranslator<T> objectTranslator)
                where T : class
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = dictionary.Count;
                _writer.Write(count);

                foreach (KeyValuePair<string, T> pair in dictionary)
                {
                    string key = pair.Key;
                    Translate(ref key);
                    T value = pair.Value;
                    objectTranslator(this, ref value);
                }
            }

            /// <summary>
            /// Translates a dictionary of { string, T } for dictionaries with public parameterless constructors.
            /// </summary>
            /// <typeparam name="D">The reference type for the dictionary.</typeparam>
            /// <typeparam name="T">The reference type for values in the dictionary.</typeparam>
            /// <param name="dictionary">The dictionary to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the values in the dictionary</param>
            public void TranslateDictionary<D, T>(ref D dictionary, ObjectTranslator<T> objectTranslator)
                where D : IDictionary<string, T>, new()
                where T : class
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = dictionary.Count;
                _writer.Write(count);

                foreach (KeyValuePair<string, T> pair in dictionary)
                {
                    string key = pair.Key;
                    Translate(ref key);
                    T value = pair.Value;
                    objectTranslator(this, ref value);
                }
            }

            /// <summary>
            /// Translates a dictionary of { string, T } for dictionaries with public parameterless constructors.
            /// </summary>
            /// <typeparam name="D">The reference type for the dictionary.</typeparam>
            /// <typeparam name="T">The reference type for values in the dictionary.</typeparam>
            /// <param name="dictionary">The dictionary to be translated.</param>
            /// <param name="objectTranslator">The translator to use for the values in the dictionary</param>
            /// <param name="dictionaryCreator">The delegate used to instantiate the dictionary.</param>
            public void TranslateDictionary<D, T>(ref D dictionary, ObjectTranslator<T> objectTranslator, NodePacketCollectionCreator<D> dictionaryCreator)
                where D : IDictionary<string, T>
                where T : class
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = dictionary.Count;
                _writer.Write(count);

                foreach (KeyValuePair<string, T> pair in dictionary)
                {
                    string key = pair.Key;
                    Translate(ref key);
                    T value = pair.Value;
                    objectTranslator(this, ref value);
                }
            }

            /// <summary>
            /// Translates a dictionary of { string, DateTime }.
            /// </summary>
            /// <param name="dictionary">The dictionary to be translated.</param>
            /// <param name="comparer">Key comparer</param>
            public void TranslateDictionary(ref Dictionary<string, DateTime> dictionary, StringComparer comparer)
            {
                if (!TranslateNullable(dictionary))
                {
                    return;
                }

                int count = dictionary.Count;
                _writer.Write(count);
                foreach (KeyValuePair<string, DateTime> kvp in dictionary)
                {
                    string key = kvp.Key;
                    DateTime val = kvp.Value;
                    Translate(ref key);
                    Translate(ref val);
                }
            }

            /// <summary>
            /// Writes out the boolean which says if this object is null or not.
            /// </summary>
            /// <param name="value">The object to test.</param>
            /// <typeparam name="T">The type of object to test.</typeparam>
            /// <returns>True if the object should be written, false otherwise.</returns>
            public bool TranslateNullable<T>(T value)
            {
                bool haveRef = (value != null);
                _writer.Write(haveRef);
                return haveRef;
            }
        }
    }
}
