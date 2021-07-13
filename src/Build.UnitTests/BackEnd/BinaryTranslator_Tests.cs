// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Configuration.Assemblies;
using System.Globalization;
using Microsoft.Build.BackEnd;
using System.IO;
using System.Reflection;
using Shouldly;
using Xunit;

namespace Microsoft.Build.UnitTests.BackEnd
{
    /// <summary>
    /// Tests for the NodePacketTranslators
    /// </summary>
    public class BinaryTranslator_Tests
    {
        /// <summary>
        /// Tests the SerializationMode property
        /// </summary>
        [Fact]
        public void TestSerializationMode()
        {
            MemoryStream stream = new MemoryStream();
            using ITranslator readTranslator = BinaryTranslator.GetReadTranslator(stream, null);
            Assert.Equal(TranslationDirection.ReadFromStream, readTranslator.Mode);

            using ITranslator writeTranslator = BinaryTranslator.GetWriteTranslator(stream);
            Assert.Equal(TranslationDirection.WriteToStream, writeTranslator.Mode);
        }

        /// <summary>
        /// Tests serializing bools.
        /// </summary>
        [Fact]
        public void TestSerializeBool()
        {
            HelperTestSimpleType(false, true);
            HelperTestSimpleType(true, false);
        }

        /// <summary>
        /// Tests serializing bytes.
        /// </summary>
        [Fact]
        public void TestSerializeByte()
        {
            byte val = 0x55;
            HelperTestSimpleType((byte)0, val);
            HelperTestSimpleType(val, (byte)0);
        }

        /// <summary>
        /// Tests serializing shorts.
        /// </summary>
        [Fact]
        public void TestSerializeShort()
        {
            short val = 0x55AA;
            HelperTestSimpleType((short)0, val);
            HelperTestSimpleType(val, (short)0);
        }

        /// <summary>
        /// Tests serializing longs.
        /// </summary>
        [Fact]
        public void TestSerializeLong()
        {
            long val = 0x55AABBCCDDEE;
            HelperTestSimpleType((long)0, val);
            HelperTestSimpleType(val, (long)0);
        }

        /// <summary>
        /// Tests serializing doubles.
        /// </summary>
        [Fact]
        public void TestSerializeDouble()
        {
            double val = 3.1416;
            HelperTestSimpleType((double)0, val);
            HelperTestSimpleType(val, (double)0);
        }

        /// <summary>
        /// Tests serializing TimeSpan.
        /// </summary>
        [Fact]
        public void TestSerializeTimeSpan()
        {
            TimeSpan val = TimeSpan.FromMilliseconds(123);
            HelperTestSimpleType(TimeSpan.Zero, val);
            HelperTestSimpleType(val, TimeSpan.Zero);
        }

        /// <summary>
        /// Tests serializing ints.
        /// </summary>
        [Fact]
        public void TestSerializeInt()
        {
            int val = 0x55AA55AA;
            HelperTestSimpleType((int)0, val);
            HelperTestSimpleType(val, (int)0);
        }

        /// <summary>
        /// Tests serializing strings.
        /// </summary>
        [Fact]
        public void TestSerializeString()
        {
            HelperTestSimpleType("foo", null);
            HelperTestSimpleType("", null);
            HelperTestSimpleType(null, null);
        }

        /// <summary>
        /// Tests serializing string arrays.
        /// </summary>
        [Fact]
        public void TestSerializeStringArray()
        {
            HelperTestArray(new string[] { }, StringComparer.Ordinal);
            HelperTestArray(new string[] { "foo", "bar" }, StringComparer.Ordinal);
            HelperTestArray(null, StringComparer.Ordinal);
        }

        /// <summary>
        /// Tests serializing string arrays.
        /// </summary>
        [Fact]
        public void TestSerializeStringList()
        {
            HelperTestList(new List<string>(), StringComparer.Ordinal);
            List<string> twoItems = new List<string>(2);
            twoItems.Add("foo");
            twoItems.Add("bar");
            HelperTestList(twoItems, StringComparer.Ordinal);
            HelperTestList(null, StringComparer.Ordinal);
        }

        /// <summary>
        /// Tests serializing DateTimes.
        /// </summary>
        [Fact]
        public void TestSerializeDateTime()
        {
            HelperTestSimpleType(new DateTime(), DateTime.Now);
            HelperTestSimpleType(DateTime.Now, new DateTime());
        }

        /// <summary>
        /// Tests serializing enums.
        /// </summary>
        [Fact]
        public void TestSerializeEnum()
        {
            TranslationDirection value = TranslationDirection.ReadFromStream;
            TranslationHelpers.GetWriteTranslator().TranslateEnum(ref value, (int)value);

            TranslationDirection deserializedValue = TranslationDirection.WriteToStream;
            TranslationHelpers.GetReadTranslator().TranslateEnum(ref deserializedValue, (int)deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Tests serializing using the DotNet serializer.
        /// </summary>
        [Fact]
        public void TestSerializeDotNet()
        {
            ArgumentNullException value = new ArgumentNullException("The argument was null", new InsufficientMemoryException());
            TranslationHelpers.GetWriteTranslator().TranslateDotNet(ref value);

            ArgumentNullException deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateDotNet(ref deserializedValue);

            Assert.True(TranslationHelpers.CompareExceptions(value, deserializedValue));
        }

        /// <summary>
        /// Tests serializing using the DotNet serializer passing in null.
        /// </summary>
        [Fact]
        public void TestSerializeDotNetNull()
        {
            ArgumentNullException value = null;
            TranslationHelpers.GetWriteTranslator().TranslateDotNet(ref value);

            ArgumentNullException deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateDotNet(ref deserializedValue);

            Assert.True(TranslationHelpers.CompareExceptions(value, deserializedValue));
        }

        /// <summary>
        /// Tests serializing an object with a default constructor.
        /// </summary>
        [Fact]
        public void TestSerializeINodePacketSerializable()
        {
            DerivedClass value = new DerivedClass(1, 2);
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            DerivedClass deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value.BaseValue, deserializedValue.BaseValue);
            Assert.Equal(value.DerivedValue, deserializedValue.DerivedValue);
        }

        /// <summary>
        /// Tests serializing an object with a default constructor passed as null.
        /// </summary>
        [Fact]
        public void TestSerializeINodePacketSerializableNull()
        {
            DerivedClass value = null;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            DerivedClass deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Tests serializing an object requiring a factory to construct.
        /// </summary>
        [Fact]
        public void TestSerializeWithFactory()
        {
            BaseClass value = new BaseClass(1);
            TranslationHelpers.GetWriteTranslator().Translate(ref value, BaseClass.FactoryForDeserialization);

            BaseClass deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue, BaseClass.FactoryForDeserialization);

            Assert.Equal(value.BaseValue, deserializedValue.BaseValue);
        }

        /// <summary>
        /// Tests serializing an object requiring a factory to construct, passing null for the value.
        /// </summary>
        [Fact]
        public void TestSerializeWithFactoryNull()
        {
            BaseClass value = null;
            TranslationHelpers.GetWriteTranslator().Translate(ref value, BaseClass.FactoryForDeserialization);

            BaseClass deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue, BaseClass.FactoryForDeserialization);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Tests serializing an array of objects with default constructors.
        /// </summary>
        [Fact]
        public void TestSerializeArray()
        {
            DerivedClass[] value = new DerivedClass[] { new DerivedClass(1, 2), new DerivedClass(3, 4) };
            TranslationHelpers.GetWriteTranslator().TranslateArray(ref value);

            DerivedClass[] deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateArray(ref deserializedValue);

            Assert.True(TranslationHelpers.CompareCollections(value, deserializedValue, DerivedClass.Comparer));
        }

        /// <summary>
        /// Tests serializing an array of objects with default constructors, passing null for the array.
        /// </summary>
        [Fact]
        public void TestSerializeArrayNull()
        {
            DerivedClass[] value = null;
            TranslationHelpers.GetWriteTranslator().TranslateArray(ref value);

            DerivedClass[] deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateArray(ref deserializedValue);

            Assert.True(TranslationHelpers.CompareCollections(value, deserializedValue, DerivedClass.Comparer));
        }

        /// <summary>
        /// Tests serializing an array of objects requiring factories to construct.
        /// </summary>
        [Fact]
        public void TestSerializeArrayWithFactory()
        {
            BaseClass[] value = new BaseClass[] { new BaseClass(1), new BaseClass(2) };
            TranslationHelpers.GetWriteTranslator().TranslateArray(ref value, BaseClass.FactoryForDeserialization);

            BaseClass[] deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateArray(ref deserializedValue, BaseClass.FactoryForDeserialization);

            Assert.True(TranslationHelpers.CompareCollections(value, deserializedValue, BaseClass.Comparer));
        }

        /// <summary>
        /// Tests serializing an array of objects requiring factories to construct, passing null for the array.
        /// </summary>
        [Fact]
        public void TestSerializeArrayWithFactoryNull()
        {
            BaseClass[] value = null;
            TranslationHelpers.GetWriteTranslator().TranslateArray(ref value, BaseClass.FactoryForDeserialization);

            BaseClass[] deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateArray(ref deserializedValue, BaseClass.FactoryForDeserialization);

            Assert.True(TranslationHelpers.CompareCollections(value, deserializedValue, BaseClass.Comparer));
        }

        /// <summary>
        /// Tests serializing a dictionary of { string, string }
        /// </summary>
        [Fact]
        public void TestSerializeDictionaryStringString()
        {
            Dictionary<string, string> value = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            value["foo"] = "bar";
            value["alpha"] = "omega";

            TranslationHelpers.GetWriteTranslator().TranslateDictionary(ref value, StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string> deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateDictionary(ref deserializedValue, StringComparer.OrdinalIgnoreCase);

            Assert.Equal(value.Count, deserializedValue.Count);
            Assert.Equal(value["foo"], deserializedValue["foo"]);
            Assert.Equal(value["alpha"], deserializedValue["alpha"]);
            Assert.Equal(value["FOO"], deserializedValue["FOO"]);
        }

        /// <summary>
        /// Tests serializing a dictionary of { string, string }, passing null.
        /// </summary>
        [Fact]
        public void TestSerializeDictionaryStringStringNull()
        {
            Dictionary<string, string> value = null;

            TranslationHelpers.GetWriteTranslator().TranslateDictionary(ref value, StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string> deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateDictionary(ref deserializedValue, StringComparer.OrdinalIgnoreCase);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Tests serializing a dictionary of { string, T } where T requires a factory to construct and the dictionary
        /// requires a KeyComparer initializer.
        /// </summary>
        [Fact]
        public void TestSerializeDictionaryStringT()
        {
            Dictionary<string, BaseClass> value = new Dictionary<string, BaseClass>(StringComparer.OrdinalIgnoreCase);
            value["foo"] = new BaseClass(1);
            value["alpha"] = new BaseClass(2);

            TranslationHelpers.GetWriteTranslator().TranslateDictionary(ref value, StringComparer.OrdinalIgnoreCase, BaseClass.FactoryForDeserialization);

            Dictionary<string, BaseClass> deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateDictionary(ref deserializedValue, StringComparer.OrdinalIgnoreCase, BaseClass.FactoryForDeserialization);

            Assert.Equal(value.Count, deserializedValue.Count);
            Assert.Equal(0, BaseClass.Comparer.Compare(value["foo"], deserializedValue["foo"]));
            Assert.Equal(0, BaseClass.Comparer.Compare(value["alpha"], deserializedValue["alpha"]));
            Assert.Equal(0, BaseClass.Comparer.Compare(value["FOO"], deserializedValue["FOO"]));
        }

        /// <summary>
        /// Tests serializing a dictionary of { string, T } where T requires a factory to construct and the dictionary
        /// requires a KeyComparer initializer, passing null for the dictionary.
        /// </summary>
        [Fact]
        public void TestSerializeDictionaryStringTNull()
        {
            Dictionary<string, BaseClass> value = null;

            TranslationHelpers.GetWriteTranslator().TranslateDictionary(ref value, StringComparer.OrdinalIgnoreCase, BaseClass.FactoryForDeserialization);

            Dictionary<string, BaseClass> deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateDictionary(ref deserializedValue, StringComparer.OrdinalIgnoreCase, BaseClass.FactoryForDeserialization);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Tests serializing a dictionary of { string, T } where T requires a factory to construct and the dictionary
        /// has a default constructor.
        /// </summary>
        [Fact]
        public void TestSerializeDictionaryStringTNoComparer()
        {
            Dictionary<string, BaseClass> value = new Dictionary<string, BaseClass>();
            value["foo"] = new BaseClass(1);
            value["alpha"] = new BaseClass(2);

            TranslationHelpers.GetWriteTranslator().TranslateDictionary<Dictionary<string, BaseClass>, BaseClass>(ref value, BaseClass.FactoryForDeserialization);

            Dictionary<string, BaseClass> deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateDictionary<Dictionary<string, BaseClass>, BaseClass>(ref deserializedValue, BaseClass.FactoryForDeserialization);

            Assert.Equal(value.Count, deserializedValue.Count);
            Assert.Equal(0, BaseClass.Comparer.Compare(value["foo"], deserializedValue["foo"]));
            Assert.Equal(0, BaseClass.Comparer.Compare(value["alpha"], deserializedValue["alpha"]));
            Assert.False(deserializedValue.ContainsKey("FOO"));
        }

        /// <summary>
        /// Tests serializing a dictionary of { string, T } where T requires a factory to construct and the dictionary
        /// has a default constructor, passing null for the dictionary.
        /// </summary>
        [Fact]
        public void TestSerializeDictionaryStringTNoComparerNull()
        {
            Dictionary<string, BaseClass> value = null;

            TranslationHelpers.GetWriteTranslator().TranslateDictionary<Dictionary<string, BaseClass>, BaseClass>(ref value, BaseClass.FactoryForDeserialization);

            Dictionary<string, BaseClass> deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateDictionary<Dictionary<string, BaseClass>, BaseClass>(ref deserializedValue, BaseClass.FactoryForDeserialization);

            Assert.Equal(value, deserializedValue);
        }

        [Theory]
        [InlineData("en")]
        [InlineData("en-US")]
        [InlineData("en-CA")]
        [InlineData("zh-HK")]
        [InlineData("sr-Cyrl-CS")]
        public void CultureInfo(string name)
        {
            CultureInfo value = new CultureInfo(name);
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            CultureInfo deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            deserializedValue.ShouldBe(value);
        }

        [Fact]
        public void CultureInfoAsNull()
        {
            CultureInfo value = null;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            CultureInfo deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            deserializedValue.ShouldBeNull();
        }

        [Theory]
        [InlineData("1.2")]
        [InlineData("1.2.3")]
        [InlineData("1.2.3.4")]
        public void Version(string version)
        {
            Version value = new Version(version);
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            Version deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            deserializedValue.ShouldBe(value);
        }

        [Fact]
        public void VersionAsNull()
        {
            Version value = null;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            Version deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            deserializedValue.ShouldBeNull();
        }

        [Fact]
        public void HashSetOfT()
        {
            HashSet<BaseClass> values = new()
            {
                new BaseClass(1),
                new BaseClass(2),
                null
            };
            TranslationHelpers.GetWriteTranslator().TranslateHashSet(ref values, BaseClass.FactoryForDeserialization, capacity => new());

            HashSet<BaseClass> deserializedValues = null;
            TranslationHelpers.GetReadTranslator().TranslateHashSet(ref deserializedValues, BaseClass.FactoryForDeserialization, capacity => new());

            deserializedValues.ShouldBe(values, ignoreOrder: true);
        }

        [Fact]
        public void HashSetOfTAsNull()
        {
            HashSet<BaseClass> value = null;
            TranslationHelpers.GetWriteTranslator().TranslateHashSet(ref value, BaseClass.FactoryForDeserialization, capacity => new());

            HashSet<BaseClass> deserializedValue = null;
            TranslationHelpers.GetReadTranslator().TranslateHashSet(ref deserializedValue, BaseClass.FactoryForDeserialization, capacity => new());

            deserializedValue.ShouldBeNull();
        }

        [Fact]
        public void AssemblyNameAsNull()
        {
            AssemblyName value = null;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            AssemblyName deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            deserializedValue.ShouldBeNull();
        }

        [Fact]
        public void AssemblyNameWithAllFields()
        {
            AssemblyName value = new()
            {
                Name = "a",
                Version = new Version(1, 2, 3),
                Flags = AssemblyNameFlags.PublicKey,
                ProcessorArchitecture = ProcessorArchitecture.X86,
                CultureInfo = new CultureInfo("zh-HK"),
                HashAlgorithm = System.Configuration.Assemblies.AssemblyHashAlgorithm.SHA256,
                VersionCompatibility = AssemblyVersionCompatibility.SameMachine,
                CodeBase = "C:\\src",
                KeyPair = new StrongNameKeyPair(new byte[] { 4, 3, 2, 1 }),
                ContentType = AssemblyContentType.WindowsRuntime,
                CultureName = "zh-HK",
            };
            value.SetPublicKey(new byte[]{ 3, 2, 1});
            value.SetPublicKeyToken(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 });

            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            AssemblyName deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            HelperAssertAssemblyNameEqual(value, deserializedValue);
        }

        [Fact]
        public void AssemblyNameWithMinimalFields()
        {
            AssemblyName value = new();

            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            AssemblyName deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            HelperAssertAssemblyNameEqual(value, deserializedValue);
        }

        /// <summary>
        /// Assert two AssemblyName objects values are same.
        /// Ignoring KeyPair, ContentType, CultureName as those are not serialized
        /// </summary>
        private static void HelperAssertAssemblyNameEqual(AssemblyName expected, AssemblyName actual)
        {
            actual.Name.ShouldBe(expected.Name);
            actual.Version.ShouldBe(expected.Version);
            actual.Flags.ShouldBe(expected.Flags);
            actual.ProcessorArchitecture.ShouldBe(expected.ProcessorArchitecture);
            actual.CultureInfo.ShouldBe(expected.CultureInfo);
            actual.HashAlgorithm.ShouldBe(expected.HashAlgorithm);
            actual.VersionCompatibility.ShouldBe(expected.VersionCompatibility);
            actual.CodeBase.ShouldBe(expected.CodeBase);

            actual.GetPublicKey().ShouldBe(expected.GetPublicKey());
            actual.GetPublicKeyToken().ShouldBe(expected.GetPublicKeyToken());
        }

        /// <summary>
        /// Helper for bool serialization.
        /// </summary>
        private void HelperTestSimpleType(bool initialValue, bool deserializedInitialValue)
        {
            bool value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            bool deserializedValue = deserializedInitialValue;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Helper for long serialization.
        /// </summary>
        private void HelperTestSimpleType(long initialValue, long deserializedInitialValue)
        {
            long value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            long deserializedValue = deserializedInitialValue;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Helper for double serialization.
        /// </summary>
        private void HelperTestSimpleType(double initialValue, double deserializedInitialValue)
        {
            double value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            double deserializedValue = deserializedInitialValue;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Helper for TimeSpan serialization.
        /// </summary>
        private void HelperTestSimpleType(TimeSpan initialValue, TimeSpan deserializedInitialValue)
        {
            TimeSpan value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            TimeSpan deserializedValue = deserializedInitialValue;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Helper for byte serialization.
        /// </summary>
        private void HelperTestSimpleType(byte initialValue, byte deserializedInitialValue)
        {
            byte value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            byte deserializedValue = deserializedInitialValue;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Helper for short serialization.
        /// </summary>
        private void HelperTestSimpleType(short initialValue, short deserializedInitialValue)
        {
            short value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            short deserializedValue = deserializedInitialValue;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Helper for int serialization.
        /// </summary>
        private void HelperTestSimpleType(int initialValue, int deserializedInitialValue)
        {
            int value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            int deserializedValue = deserializedInitialValue;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Helper for string serialization.
        /// </summary>
        private void HelperTestSimpleType(string initialValue, string deserializedInitialValue)
        {
            string value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            string deserializedValue = deserializedInitialValue;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Helper for DateTime serialization.
        /// </summary>
        private void HelperTestSimpleType(DateTime initialValue, DateTime deserializedInitialValue)
        {
            DateTime value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            DateTime deserializedValue = deserializedInitialValue;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.Equal(value, deserializedValue);
        }

        /// <summary>
        /// Helper for array serialization.
        /// </summary>
        private void HelperTestArray(string[] initialValue, IComparer<string> comparer)
        {
            string[] value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            string[] deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.True(TranslationHelpers.CompareCollections(value, deserializedValue, comparer));
        }

        /// <summary>
        /// Helper for list serialization.
        /// </summary>
        private void HelperTestList(List<string> initialValue, IComparer<string> comparer)
        {
            List<string> value = initialValue;
            TranslationHelpers.GetWriteTranslator().Translate(ref value);

            List<string> deserializedValue = null;
            TranslationHelpers.GetReadTranslator().Translate(ref deserializedValue);

            Assert.True(TranslationHelpers.CompareCollections(value, deserializedValue, comparer));
        }

        /// <summary>
        /// Base class for testing
        /// </summary>
        private class BaseClass : ITranslatable
        {
            /// <summary>
            /// A field.
            /// </summary>
            private int _baseValue;

            /// <summary>
            /// Constructor with value.
            /// </summary>
            public BaseClass(int val)
            {
                _baseValue = val;
            }

            /// <summary>
            /// Constructor
            /// </summary>
            protected BaseClass()
            {
            }

            protected bool Equals(BaseClass other)
            {
                return _baseValue == other._baseValue;
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((BaseClass) obj);
            }

            public override int GetHashCode()
            {
                return _baseValue;
            }

            /// <summary>
            /// Gets a comparer.
            /// </summary>
            static public IComparer<BaseClass> Comparer
            {
                get { return new BaseClassComparer(); }
            }

            /// <summary>
            /// Gets the value.
            /// </summary>
            public int BaseValue
            {
                get { return _baseValue; }
            }

            #region INodePacketTranslatable Members

            /// <summary>
            /// Factory for serialization.
            /// </summary>
            public static BaseClass FactoryForDeserialization(ITranslator translator)
            {
                BaseClass packet = new BaseClass();
                packet.Translate(translator);
                return packet;
            }

            /// <summary>
            /// Serializes the class.
            /// </summary>
            public virtual void Translate(ITranslator translator)
            {
                translator.Translate(ref _baseValue);
            }

            #endregion

            /// <summary>
            /// Comparer for BaseClass.
            /// </summary>
            private class BaseClassComparer : IComparer<BaseClass>
            {
                /// <summary>
                /// Constructor.
                /// </summary>
                public BaseClassComparer()
                {
                }

                #region IComparer<BaseClass> Members

                /// <summary>
                /// Compare two BaseClass objects.
                /// </summary>
                public int Compare(BaseClass x, BaseClass y)
                {
                    if (x._baseValue == y._baseValue)
                    {
                        return 0;
                    }

                    return -1;
                }
                #endregion
            }
        }

        /// <summary>
        /// Derived class for testing.
        /// </summary>
        private class DerivedClass : BaseClass
        {
            /// <summary>
            /// A field.
            /// </summary>
            private int _derivedValue;

            /// <summary>
            /// Default constructor.
            /// </summary>
            public DerivedClass()
            {
            }

            /// <summary>
            /// Constructor taking two values.
            /// </summary>
            public DerivedClass(int derivedValue, int baseValue)
                : base(baseValue)
            {
                _derivedValue = derivedValue;
            }

            /// <summary>
            /// Gets a comparer.
            /// </summary>
            static new public IComparer<DerivedClass> Comparer
            {
                get { return new DerivedClassComparer(); }
            }

            /// <summary>
            /// Returns the value.
            /// </summary>
            public int DerivedValue
            {
                get { return _derivedValue; }
            }

            #region INodePacketTranslatable Members

            /// <summary>
            /// Serializes the class.
            /// </summary>
            public override void Translate(ITranslator translator)
            {
                base.Translate(translator);
                translator.Translate(ref _derivedValue);
            }

            #endregion

            /// <summary>
            /// Comparer for DerivedClass.
            /// </summary>
            private class DerivedClassComparer : IComparer<DerivedClass>
            {
                /// <summary>
                /// Constructor
                /// </summary>
                public DerivedClassComparer()
                {
                }

                #region IComparer<DerivedClass> Members

                /// <summary>
                /// Compares two DerivedClass objects.
                /// </summary>
                public int Compare(DerivedClass x, DerivedClass y)
                {
                    if (x._derivedValue == y._derivedValue)
                    {
                        return BaseClass.Comparer.Compare(x, y);
                    }

                    return -1;
                }
                #endregion
            }
        }
    }
}
