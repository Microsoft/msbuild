// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if FEATURE_WIN32_REGISTRY

using System;
using System.Collections.Generic;

using Microsoft.Build.Collections;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Internal;
using Microsoft.Build.Shared;
using Microsoft.Win32;

using Xunit;

using InvalidToolsetDefinitionException = Microsoft.Build.Exceptions.InvalidToolsetDefinitionException;

namespace Microsoft.Build.UnitTests.Definition
{
    /// <summary>
    /// Unit test for ToolsetRegistryReader class
    /// </summary>
    [PlatformSpecific(TestPlatforms.Windows)]
    public class ToolsetRegistryReader_Tests : IDisposable
    {
        // The registry key that is passed as the baseKey parameter to the ToolsetRegistryReader class
        private RegistryKey _testRegistryKey = null;
        // Subkey "3.5"
        private RegistryKey _currentVersionRegistryKey = null;
        // Subkey "ToolsVersions"
        private RegistryKey _toolsVersionsRegistryKey = null;

        // Path to the registry key under HKCU
        // Note that this is a test registry key created solely for unit testing.
        private const string testRegistryPath = @"msbuildUnitTests";

        /// <summary>
        /// Store the value of the "VisualStudioVersion" environment variable here so that
        /// we can unset it for the duration of the test.
        /// </summary>
        private string _oldVisualStudioVersion;

        /// <summary>
        /// Reset the testRegistryKey
        /// </summary>
        public ToolsetRegistryReader_Tests()
        {
            DeleteTestRegistryKey();
            _testRegistryKey = Registry.CurrentUser.CreateSubKey(testRegistryPath);
            _currentVersionRegistryKey = Registry.CurrentUser.CreateSubKey(testRegistryPath + "\\" + Constants.AssemblyVersion);
            _toolsVersionsRegistryKey = Registry.CurrentUser.CreateSubKey(testRegistryPath + "\\ToolsVersions");

            _oldVisualStudioVersion = Environment.GetEnvironmentVariable("VisualStudioVersion");
            Environment.SetEnvironmentVariable("VisualStudioVersion", null);
        }

        public void Dispose()
        {
            DeleteTestRegistryKey();

            Environment.SetEnvironmentVariable("VisualStudioVersion", _oldVisualStudioVersion);
        }

        /// <summary>
        /// Helper class to delete the testRegistryKey tree.
        /// </summary>
        private void DeleteTestRegistryKey()
        {
            if (Registry.CurrentUser.OpenSubKey(testRegistryPath) != null)
            {
                Registry.CurrentUser.DeleteSubKeyTree(testRegistryPath);
            }
        }

        /// <summary>
        /// If the base key has been deleted, then we just don't get any information (no exception)
        /// </summary>
        [Fact]
        public void ReadRegistry_DeletedKey()
        {
            DeleteTestRegistryKey();

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
            Assert.Empty(values);
        }

        /// <summary>
        /// Tests the tools version 4.0 is written to the registry at install time
        /// </summary>
        [Fact(Skip = "Test requires installed toolset.")]
        public void DefaultValuesInRegistryCreatedBySetup()
        {
            if (NativeMethodsShared.IsUnixLike)
            {
                return; // "TODO: under Unix this runs out of stack. Investigate"
            }
            ToolsetReader reader = new ToolsetRegistryReader(new ProjectCollection().EnvironmentProperties, new PropertyDictionary<ProjectPropertyInstance>());  //we don't use the test registry key because we want to verify the install

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            // Check the values in the data
            Assert.True(values.ContainsKey("4.0")); // "Tools version 4.0 should be defined by default"
            Assert.True(values.ContainsKey(ObjectModelHelpers.MSBuildDefaultToolsVersion), String.Format("Tools version {0} should be defined by default", ObjectModelHelpers.MSBuildDefaultToolsVersion));
            Assert.Equal("2.0", defaultToolsVersion); // "Default tools version should be 2.0"
        }

        /// <summary>
        /// Tests we handle no default toolset specified in the registry
        /// </summary>
        [Fact]
        public void DefaultValueInRegistryDoesNotExist()
        {
            ToolsetReader reader = new ToolsetRegistryReader(new ProjectCollection().EnvironmentProperties, new PropertyDictionary<ProjectPropertyInstance>(), new MockRegistryKey(testRegistryPath, "3.5" /* fail to find subkey 3.5 */));

            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);


            // Should not throw
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Null(defaultToolsVersion);
        }

        /// <summary>
        /// The base key exists but contains no subkey or values: this is okay
        /// </summary>
        [Fact]
        public void ReadRegistry_NoSubkeyNoValues()
        {
            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Empty(values);
            Assert.Null(defaultToolsVersion);
        }

        /// <summary>
        /// Here we validate that MSBuild does not fail when there are unrecognized values underneath
        /// the ToolsVersion key.
        /// </summary>
        [Fact]
        public void ReadRegistry_NoSubkeysOnlyValues()
        {
            _toolsVersionsRegistryKey.SetValue("Name1", "Value1");
            _toolsVersionsRegistryKey.SetValue("Name2", "Value2");

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Empty(values);
            Assert.Null(defaultToolsVersion);
        }

        /// <summary>
        /// Basekey has only 1 subkey
        /// </summary>
        [Fact]
        public void ReadRegistry_OnlyOneSubkey()
        {
            string xdir = NativeMethodsShared.IsWindows ? "c:\\xxx" : "/xxx";

            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("tv1");
            key1.SetValue("msbuildtoolspath", xdir);

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Null(defaultToolsVersion);
            Assert.Single(values);
            Assert.Empty(values["tv1"].Properties);
            Assert.Equal(0, String.Compare(xdir, values["tv1"].ToolsPath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Basic case
        /// </summary>
        [Fact]
        public void ReadRegistry_Basic()
        {
            string xdir = NativeMethodsShared.IsWindows ? "c:\\xxx" : "/xxx";
            string ydir = NativeMethodsShared.IsWindows ? "c:\\yyy" : "/yyy";
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("tv1");
            key1.SetValue("msbuildtoolspath", xdir);
            key1.SetValue("name1", "value1");
            RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("tv2");
            key2.SetValue("name2", "value2");
            key2.SetValue("msbuildtoolspath", ydir);

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Equal(2, values.Count);
            Assert.Single(values["tv1"].Properties);
            Assert.Equal(0, String.Compare(xdir, values["tv1"].ToolsPath, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, String.Compare("value1", values["tv1"].Properties["name1"].EvaluatedValue, StringComparison.OrdinalIgnoreCase));
            Assert.Single(values["tv2"].Properties);
            Assert.Equal(0, String.Compare(ydir, values["tv2"].ToolsPath, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(0, String.Compare("value2", values["tv2"].Properties["name2"].EvaluatedValue, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// baseKey contains some non-String data
        /// </summary>
        [Fact]
        public void ReadRegistry_NonStringData()
        {
            Assert.Throws<InvalidToolsetDefinitionException>(() =>
            {
                RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("tv1");
                key1.SetValue("msbuildtoolspath", "c:\\xxx");
                key1.SetValue("name1", "value1");
                RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("tv2");
                key2.SetValue("msbuildtoolspath", "c:\\xxx");
                key2.SetValue("name2", new String[] { "value2a", "value2b" }, RegistryValueKind.MultiString);

                ToolsetReader reader = GetStandardRegistryReader();
                string msbuildOverrideTasksPath = null;
                string defaultOverrideToolsVersion = null;

                Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
                reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
            }
           );
        }
        /// <summary>
        /// Registry has the following structure
        /// [HKCU]\basekey\
        ///    Key1
        ///        SubKey1
        ///    Key2
        ///        SubKey2
        ///        SubKey3
        /// </summary>
        [Fact]
        public void ReadRegistry_HasSubToolsets()
        {
            string xdir = NativeMethodsShared.IsWindows ? "c:\\xxx" : "/xxx";
            string ydir = NativeMethodsShared.IsWindows ? "c:\\yyy" : "/yyy";

            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("tv1");
            key1.SetValue("msbuildtoolspath", xdir);
            key1.SetValue("name1", "value1");
            RegistryKey subKey1 = key1.CreateSubKey("SubKey1");
            subKey1.SetValue("name1a", "value1a");
            subKey1.SetValue("name2a", "value2a");
            RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("tv2");
            key2.SetValue("msbuildtoolspath", ydir);
            key2.SetValue("name2", "value2");
            RegistryKey subKey2 = key2.CreateSubKey("SubKey2");
            subKey2.SetValue("name3a", "value3a");
            subKey2.SetValue("name2a", "value2a");
            RegistryKey subKey3 = key2.CreateSubKey("SubKey3");
            subKey3.SetValue("name4a", "value4a");
            subKey3.SetValue("name5a", "value5a");

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Equal(2, values.Count);
            Assert.Single(values["tv1"].Properties);
            Assert.Equal(xdir, values["tv1"].ToolsPath);
            Assert.Equal("value1", values["tv1"].Properties["name1"].EvaluatedValue);
            Assert.Single(values["tv1"].SubToolsets);
            Assert.Equal(2, values["tv1"].SubToolsets["SubKey1"].Properties.Count);
            Assert.Equal("value1a", values["tv1"].SubToolsets["SubKey1"].Properties["name1a"].EvaluatedValue);
            Assert.Equal("value2a", values["tv1"].SubToolsets["SubKey1"].Properties["name2a"].EvaluatedValue);

            Assert.Single(values["tv2"].Properties);
            Assert.Equal(ydir, values["tv2"].ToolsPath);
            Assert.Equal("value2", values["tv2"].Properties["name2"].EvaluatedValue);
            Assert.Equal(2, values["tv2"].SubToolsets.Count);
            Assert.Equal(2, values["tv2"].SubToolsets["SubKey2"].Properties.Count);
            Assert.Equal("value3a", values["tv2"].SubToolsets["SubKey2"].Properties["name3a"].EvaluatedValue);
            Assert.Equal("value2a", values["tv2"].SubToolsets["SubKey2"].Properties["name2a"].EvaluatedValue);
            Assert.Equal(2, values["tv2"].SubToolsets["SubKey3"].Properties.Count);
            Assert.Equal("value4a", values["tv2"].SubToolsets["SubKey3"].Properties["name4a"].EvaluatedValue);
            Assert.Equal("value5a", values["tv2"].SubToolsets["SubKey3"].Properties["name5a"].EvaluatedValue);
        }

        /// <summary>
        /// Registry has the following structure
        /// [HKCU]\basekey\
        ///    Key1
        ///        SubKey1
        ///            SubSubKey1
        /// </summary>
        [Fact]
        public void ReadRegistry_IgnoreSubToolsetSubKeys()
        {
            string xdir = NativeMethodsShared.IsWindows ? "c:\\xxx" : "/xxx";

            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("tv1");
            key1.SetValue("msbuildtoolspath", xdir);
            key1.SetValue("name1", "value1");
            RegistryKey subKey1 = key1.CreateSubKey("SubKey1");
            subKey1.SetValue("name1a", "value1a");
            subKey1.SetValue("name2a", "value2a");
            RegistryKey subSubKey1 = subKey1.CreateSubKey("SubSubKey1");
            subSubKey1.SetValue("name2b", "value2b");

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Single(values);
            Assert.Single(values["tv1"].Properties);
            Assert.Equal(xdir, values["tv1"].ToolsPath);
            Assert.Equal("value1", values["tv1"].Properties["name1"].EvaluatedValue);
            Assert.Single(values["tv1"].SubToolsets);
            Assert.Equal(2, values["tv1"].SubToolsets["SubKey1"].Properties.Count);
            Assert.Equal("value1a", values["tv1"].SubToolsets["SubKey1"].Properties["name1a"].EvaluatedValue);
            Assert.Equal("value2a", values["tv1"].SubToolsets["SubKey1"].Properties["name2a"].EvaluatedValue);
        }

        /// <summary>
        /// Verifies that if a value is defined in both the base toolset and the
        /// selected subtoolset, the subtoolset value overrides -- even if that
        /// value is empty.
        /// </summary>
        [Fact]
        public void ReadRegistry_SubToolsetOverridesBaseToolsetEntries()
        {
            string xdir = NativeMethodsShared.IsWindows ? "c:\\xxx" : "/xxx";

            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("tv1");
            key1.SetValue("msbuildtoolspath", xdir);
            key1.SetValue("name1", "value1");
            key1.SetValue("name2", "value2");
            RegistryKey subKey1 = key1.CreateSubKey("Foo");
            subKey1.SetValue("name1", "value1a");
            subKey1.SetValue("name2", "");

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Single(values);
            Assert.Equal(2, values["tv1"].Properties.Count);
            Assert.Equal(xdir, values["tv1"].ToolsPath);
            Assert.Equal("value1", values["tv1"].Properties["name1"].EvaluatedValue);
            Assert.Equal("value2", values["tv1"].Properties["name2"].EvaluatedValue);
            Assert.Single(values["tv1"].SubToolsets);
            Assert.Equal(2, values["tv1"].SubToolsets["Foo"].Properties.Count);
            Assert.Equal("value1a", values["tv1"].SubToolsets["Foo"].Properties["name1"].EvaluatedValue);
            Assert.Equal("", values["tv1"].SubToolsets["Foo"].Properties["name2"].EvaluatedValue);

            // Check when requesting the final evaluated value of the property in the context of its sub-toolset
            // that the sub-toolset overrides
            Assert.Equal("value1a", values["tv1"].GetProperty("name1", "Foo").EvaluatedValue);
            Assert.Equal("", values["tv1"].GetProperty("name2", "Foo").EvaluatedValue);
        }

        /// <summary>
        /// Verifies that if a value is defined in both the base toolset and the
        /// selected subtoolset, the subtoolset value overrides -- even if that
        /// value is empty.
        /// </summary>
        [Fact]
        public void ReadRegistry_UnselectedSubToolsetIsIgnored()
        {
            string xdir = NativeMethodsShared.IsWindows ? "c:\\xxx" : "/xxx";

            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("tv1");
            key1.SetValue("msbuildtoolspath", xdir);
            key1.SetValue("name1", "value1");
            key1.SetValue("name2", "value2");
            RegistryKey subKey1 = key1.CreateSubKey("Foo");
            subKey1.SetValue("name1", "value1a");
            subKey1.SetValue("name2", "");

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Single(values);
            Assert.Equal(2, values["tv1"].Properties.Count);
            Assert.Equal(xdir, values["tv1"].ToolsPath);
            Assert.Equal("value1", values["tv1"].Properties["name1"].EvaluatedValue);
            Assert.Equal("value2", values["tv1"].Properties["name2"].EvaluatedValue);
        }

        /// <summary>
        /// Regular case of getting default tools version
        /// </summary>
        [Fact]
        public void GetDefaultToolsVersionFromRegistry_Basic()
        {
            _currentVersionRegistryKey.SetValue("DefaultToolsVersion", "tv1");
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("tv1"); // Need matching tools version
            key1.SetValue("msbuildtoolspath", "c:\\xxx");

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Equal("tv1", defaultToolsVersion);
        }

        /// <summary>
        /// Default value is not set
        /// </summary>
        [Fact]
        public void GetDefaultToolsVersionFromRegistry_DefaultValueNotSet()
        {
            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            string defaultToolsVersion = reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Null(defaultToolsVersion);
        }

        /// <summary>
        /// "DefaultToolsVersion" has non-String data
        /// </summary>
        [Fact]
        public void GetDefaultToolsVersionFromRegistry_NonStringData()
        {
            Assert.Throws<InvalidToolsetDefinitionException>(() =>
            {
                _currentVersionRegistryKey.SetValue("DefaultToolsVersion", new String[] { "2.0.xxxx.a", "2.0.xxxx.b" }, RegistryValueKind.MultiString);

                ToolsetReader reader = GetStandardRegistryReader();
                string msbuildOverrideTasksPath = null;
                string defaultOverrideToolsVersion = null;

                Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
                reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
            }
           );
        }
        private ToolsetRegistryReader GetStandardRegistryReader()
        {
            return new ToolsetRegistryReader(new ProjectCollection().EnvironmentProperties, new PropertyDictionary<ProjectPropertyInstance>(), new MockRegistryKey(testRegistryPath));
        }

        /// <summary>
        /// Regular case of getting overridetaskspath
        /// </summary>
        [Fact]
        public void GetOverrideTasksPathFromRegistry_Basic()
        {
            if (NativeMethodsShared.IsUnixLike)
            {
                return; // "Registry is not supported under Unix"
            }

            _currentVersionRegistryKey.SetValue("MsBuildOverrideTasksPath", "c:\\Foo");

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Equal("c:\\Foo", msbuildOverrideTasksPath);
        }

        /// <summary>
        /// OverrideTasksPath is not set
        /// </summary>
        [Fact]
        public void GetOverrideTasksPathFromRegistry_ValueNotSet()
        {
            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Null(msbuildOverrideTasksPath);
        }

        /// <summary>
        /// "OverrideTasksPath" has non-String data
        /// </summary>
        [Fact]
        public void GetOverrideTasksPathFromRegistry_NonStringData()
        {
            Assert.Throws<InvalidToolsetDefinitionException>(() =>
            {
                _currentVersionRegistryKey.SetValue("MsBuildOverrideTasksPath", new String[] { "2938304894", "3948394.2.3.3.3" }, RegistryValueKind.MultiString);

                ToolsetReader reader = GetStandardRegistryReader();
                Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
                string msbuildOverrideTasksPath = null;
                string defaultOverrideToolsVersion = null;
                reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
            }
           );
        }
        /// <summary>
        /// Regular case of getting the default override toolsversion
        /// </summary>
        [Fact]
        public void GetDefaultOverrideToolsVersionFromRegistry_Basic()
        {
            _currentVersionRegistryKey.SetValue("DefaultOverrideToolsVersion", "Current");

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Equal("Current", defaultOverrideToolsVersion);
        }

        /// <summary>
        /// DefaultOverrideToolsVersion is not set
        /// </summary>
        [Fact]
        public void GetDefaultOverrideToolsVersionFromRegistry_ValueNotSet()
        {
            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.Null(defaultOverrideToolsVersion);
        }

        /// <summary>
        /// "DefaultOverrideToolsVersion" has non-String data
        /// </summary>
        [Fact]
        public void GetDefaultOverrideToolsVersionFromRegistry_NonStringData()
        {
            Assert.Throws<InvalidToolsetDefinitionException>(() =>
            {
                _currentVersionRegistryKey.SetValue("DefaultOverrideToolsVersion", new String[] { "2938304894", "3948394.2.3.3.3" }, RegistryValueKind.MultiString);

                ToolsetReader reader = GetStandardRegistryReader();
                Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);
                string msbuildOverrideTasksPath = null;
                string defaultOverrideToolsVersion = null;
                reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);
            }
           );
        }
        [Fact]
        public void ReadToolsets_NoBinPathOrToolsPath()
        {
            RegistryKey key1 = _toolsVersionsRegistryKey.CreateSubKey("tv1");
            key1.SetValue("msbuildtoolspath", "c:\\xxx");
            key1.SetValue("name1", "value1");
            RegistryKey key2 = _toolsVersionsRegistryKey.CreateSubKey("tv2");
            key2.SetValue("name2", "value2");
            RegistryKey key3 = _toolsVersionsRegistryKey.CreateSubKey("tv3");
            key3.SetValue("msbuildtoolspath", "c:\\zzz");
            key3.SetValue("name3", "value3");

            ToolsetReader reader = GetStandardRegistryReader();
            Dictionary<string, Toolset> values = new Dictionary<string, Toolset>(StringComparer.OrdinalIgnoreCase);

            string msbuildOverrideTasksPath;
            string defaultOverrideToolsVersion;
            //should not throw
            reader.ReadToolsets(values, new PropertyDictionary<ProjectPropertyInstance>(), new PropertyDictionary<ProjectPropertyInstance>(), false, out msbuildOverrideTasksPath, out defaultOverrideToolsVersion);

            Assert.True(values.ContainsKey("tv1"));

            //should not contain the second toolset because it does not define a tools/bin path
            Assert.False(values.ContainsKey("tv2"));

            Assert.True(values.ContainsKey("tv3"));
        }
    }
}
#endif
