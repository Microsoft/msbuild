// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Build.Shared;
using Microsoft.Build.Collections;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Build.UnitTests.BackEnd
{
    public class BuildRequestConfiguration_Tests : IDisposable
    {
        private TestEnvironment _env;

        public BuildRequestConfiguration_Tests(ITestOutputHelper testOutput)
        {
            _env = TestEnvironment.Create(testOutput);
        }

        public void Dispose()
        {
            _env.Dispose();
        }

        [Fact]
        public void TestConstructorNullFile()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                BuildRequestData config1 = new BuildRequestData(null, new Dictionary<string, string>(), "toolsVersion", new string[0], null);
            }
           );
        }
        [Fact]
        public void TestConstructorNullProps()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                BuildRequestData config1 = new BuildRequestData("file", null, "toolsVersion", new string[0], null);
            }
           );
        }
        [Fact]
        public void TestConstructor1()
        {
            BuildRequestData config1 = new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion", new string[0], null);
        }

        [Fact]
        public void TestConstructorInvalidConfigId()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                BuildRequestData data = new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion", new string[0], null);
                BuildRequestConfiguration config1 = new BuildRequestConfiguration(1, data, "2.0");
                config1.ShallowCloneWithNewId(0);
            }
           );
        }
        [Fact]
        public void TestConstructor2PositiveConfigId()
        {
            BuildRequestData config1 = new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion", new string[0], null);
            new BuildRequestConfiguration(1, config1, "2.0");
        }

        [Fact]
        public void TestConstructor2NegativeConfigId()
        {
            BuildRequestData config1 = new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion", new string[0], null);
            new BuildRequestConfiguration(-1, config1, "2.0");
        }

        [Fact]
        public void TestConstructor2NullFile()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                BuildRequestData config1 = new BuildRequestData(null, new Dictionary<string, string>(), "toolsVersion", new string[0], null);
            }
           );
        }

        [Fact]
        public void TestConstructor2NullProps()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                BuildRequestData config1 = new BuildRequestData("file", null, "toolsVersion", new string[0], null);
            }
           );
        }
        [Fact]
        public void TestWasGeneratedByNode()
        {
            BuildRequestData data1 = new BuildRequestData("file", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config1 = new BuildRequestConfiguration(-1, data1, "2.0");
            Assert.True(config1.WasGeneratedByNode);

            BuildRequestData data2 = new BuildRequestData("file", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config2 = new BuildRequestConfiguration(1, data2, "2.0");
            Assert.False(config2.WasGeneratedByNode);

            BuildRequestData data3 = new BuildRequestData("file", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config3 = new BuildRequestConfiguration(data3, "2.0");
            Assert.False(config3.WasGeneratedByNode);
        }

        [Fact]
        public void TestDefaultConfigurationId()
        {
            BuildRequestData data1 = new BuildRequestData("file", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config1 = new BuildRequestConfiguration(-1, data1, "2.0");
            Assert.Equal(-1, config1.ConfigurationId);

            BuildRequestData data2 = new BuildRequestData("file", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config2 = new BuildRequestConfiguration(1, data2, "2.0");
            Assert.Equal(1, config2.ConfigurationId);

            BuildRequestData data3 = new BuildRequestData("file", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config3 = new BuildRequestConfiguration(0, data3, "2.0");
            Assert.Equal(0, config3.ConfigurationId);
        }

        [Fact]
        public void TestSetConfigurationIdBad()
        {
            Assert.Throws<InternalErrorException>(() =>
            {
                BuildRequestData data = new BuildRequestData("file", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "toolsVersion", new string[0], null);
                BuildRequestConfiguration config1 = new BuildRequestConfiguration(-1, data, "2.0");
                config1.ConfigurationId = -2;
            }
           );
        }
        [Fact]
        public void TestSetConfigurationIdGood()
        {
            BuildRequestData data = new BuildRequestData("file", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config1 = new BuildRequestConfiguration(data, "2.0");
            Assert.Equal(0, config1.ConfigurationId);
            config1.ConfigurationId = 1;
            Assert.Equal(1, config1.ConfigurationId);
        }

        [Fact]
        public void TestGetFileName()
        {
            BuildRequestData data = new BuildRequestData("file", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config1 = new BuildRequestConfiguration(data, "2.0");
            Assert.Equal(config1.ProjectFullPath, Path.GetFullPath("file"));
        }

        [Fact]
        public void TestGetToolsVersion()
        {
            BuildRequestData data1 = new BuildRequestData("file", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config1 = new BuildRequestConfiguration(data1, "2.0");
            Assert.Equal("toolsVersion", config1.ToolsVersion);
        }

        [Fact]
        public void TestGetProperties()
        {
            Dictionary<string, string> props = new Dictionary<string, string>();
            BuildRequestConfiguration config1 = new BuildRequestConfiguration(new BuildRequestData("file", props, "toolsVersion", new string[0], null), "2.0");

            Assert.Equal(props.Count, Helpers.MakeList((IEnumerable<ProjectPropertyInstance>)(config1.GlobalProperties)).Count);
        }

        [Fact]
        public void TestSetProjectGood()
        {
            BuildRequestData data1 = new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config1 = new BuildRequestConfiguration(data1, "2.0");
            Assert.Null(config1.Project);
            Project project = new Project(XmlReader.Create(new StringReader(ObjectModelHelpers.CleanupFileContents(@"<Project ToolsVersion='msbuilddefaulttoolsversion' xmlns='msbuildnamespace' />"))));

            ProjectInstance projectInstance = project.CreateProjectInstance();
            config1.Project = projectInstance;
            Assert.Same(config1.Project, projectInstance);
        }

        [Fact]
        public void TestPacketType()
        {
            BuildRequestData data1 = new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config1 = new BuildRequestConfiguration(data1, "2.0");
            Assert.Equal(NodePacketType.BuildRequestConfiguration, config1.Type);
        }

        [Fact]
        public void TestGetHashCode()
        {
            BuildRequestConfiguration config1 = new BuildRequestConfiguration(new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion", new string[0], null), "2.0");
            BuildRequestConfiguration config2 = new BuildRequestConfiguration(new BuildRequestData("File", new Dictionary<string, string>(), "ToolsVersion", new string[0], null), "2.0");
            BuildRequestConfiguration config3 = new BuildRequestConfiguration(new BuildRequestData("file2", new Dictionary<string, string>(), "toolsVersion", new string[0], null), "2.0");
            BuildRequestConfiguration config4 = new BuildRequestConfiguration(new BuildRequestData("file2", new Dictionary<string, string>(), "toolsVersion2", new string[0], null), "2.0");
            BuildRequestConfiguration config5 = new BuildRequestConfiguration(new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion2", new string[0], null), "2.0");

            Assert.Equal(config1.GetHashCode(), config2.GetHashCode());
            Assert.NotEqual(config1.GetHashCode(), config3.GetHashCode());
            Assert.NotEqual(config1.GetHashCode(), config5.GetHashCode());
            Assert.NotEqual(config4.GetHashCode(), config5.GetHashCode());
        }

        [Fact]
        public void TestEquals()
        {
            BuildRequestConfiguration config1 = new BuildRequestConfiguration(new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion", new string[0], null), "2.0");
            Assert.Equal(config1, config1);
            BuildRequestConfiguration config2 = new BuildRequestConfiguration(new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion", new string[0], null), "2.0");
            Assert.Equal(config1, config2);

            BuildRequestConfiguration config3 = new BuildRequestConfiguration(new BuildRequestData("file2", new Dictionary<string, string>(), "toolsVersion", new string[0], null), "2.0");
            Assert.NotEqual(config1, config3);

            BuildRequestConfiguration config4 = new BuildRequestConfiguration(new BuildRequestData("file", new Dictionary<string, string>(), "toolsVersion2", new string[0], null), "2.0");
            Assert.NotEqual(config1, config4);

            PropertyDictionary<ProjectPropertyInstance> props = new PropertyDictionary<ProjectPropertyInstance>();
            props.Set(ProjectPropertyInstance.Create("prop1", "value1"));
            BuildRequestData data = new BuildRequestData("file", props.ToDictionary(), "toolsVersion", new string[0], null);
            BuildRequestConfiguration config5 = new BuildRequestConfiguration(data, "2.0");
            Assert.NotEqual(config1, config5);

            Assert.Equal(config1, config2);
            Assert.NotEqual(config1, config3);
        }

        [Fact]
        public void TestTranslation()
        {
            PropertyDictionary<ProjectPropertyInstance> properties = new PropertyDictionary<ProjectPropertyInstance>();
            properties.Set(ProjectPropertyInstance.Create("this", "that"));
            properties.Set(ProjectPropertyInstance.Create("foo", "bar"));

            BuildRequestData data = new BuildRequestData("file", properties.ToDictionary(), "4.0", new string[0], null);
            BuildRequestConfiguration config = new BuildRequestConfiguration(data, "2.0");

            Assert.Equal(NodePacketType.BuildRequestConfiguration, config.Type);

            ((ITranslatable)config).Translate(TranslationHelpers.GetWriteTranslator());
            INodePacket packet = BuildRequestConfiguration.FactoryForDeserialization(TranslationHelpers.GetReadTranslator());

            BuildRequestConfiguration deserializedConfig = packet as BuildRequestConfiguration;

            Assert.Equal(config, deserializedConfig);
        }

        [Fact]
        public void TestTranslationWithEntireProjectState()
        {
            string projectBody = ObjectModelHelpers.CleanupFileContents(@"
<Project ToolsVersion='msbuilddefaulttoolsversion' xmlns='msbuildnamespace'>
<PropertyGroup>
    <One>1</One>
    <Two>2</Two>
    <Three>$(ThreeIn)</Three>
</PropertyGroup>
<Target Name='Build'>
    <CallTarget Targets='Foo'/>
</Target>
</Project>");

            Dictionary<string, string> globalProperties = new (StringComparer.OrdinalIgnoreCase);
            globalProperties["ThreeIn"] = "3";

            Project project = new Project(
                XmlReader.Create(new StringReader(projectBody)),
                globalProperties,
                ObjectModelHelpers.MSBuildDefaultToolsVersion,
                new ProjectCollection());
            project.FullPath = "foo";
            ProjectInstance instance = project.CreateProjectInstance();

            instance.TranslateEntireState = true;

            BuildRequestConfiguration configuration = new BuildRequestConfiguration(new BuildRequestData(instance, new string[] { }, null), "2.0");
            configuration.ConfigurationId = 1;

            ((ITranslatable)configuration).Translate(TranslationHelpers.GetWriteTranslator());
            INodePacket packet = BuildRequestConfiguration.FactoryForDeserialization(TranslationHelpers.GetReadTranslator());

            BuildRequestConfiguration deserializedConfig = packet as BuildRequestConfiguration;

            deserializedConfig.ShouldNotBeNull();
            deserializedConfig.ShouldBe(configuration);
            deserializedConfig.Project.ShouldNotBeNull();

            // Verify that at least some data from 'entire project state' has been deserialized.
            deserializedConfig.Project.Directory.ShouldNotBeEmpty();
            deserializedConfig.Project.Directory.ShouldBe(configuration.Project.Directory);
        }

        [Fact]
        public void TestProperties()
        {
            BuildRequestConfiguration configuration = new BuildRequestConfiguration(new BuildRequestData("path", new Dictionary<string, string>(), "2.0", new string[] { }, null), "2.0");
            Assert.True(configuration.IsCacheable);
            Assert.False(configuration.IsLoaded);
            Assert.False(configuration.IsCached);
            Assert.False(configuration.IsActivelyBuilding);
        }

        [Fact]
        public void TestCache()
        {
            string projectBody = ObjectModelHelpers.CleanupFileContents(@"
<Project ToolsVersion='msbuilddefaulttoolsversion' xmlns='msbuildnamespace'>
<PropertyGroup>
    <One>1</One>
    <Two>2</Two>
    <Three>$(ThreeIn)</Three>
</PropertyGroup>
<ItemGroup>
    <Foo Include=""*""/>
    <Bar Include=""msbuild.out"">
        <One>1</One>
    </Bar>
    <Baz Include=""$(BazIn)""/>
</ItemGroup>
<Target Name='Build'>
    <CallTarget Targets='Foo;Goo'/>
</Target>

<Target Name='Foo' DependsOnTargets='Foo2'>
    <FooTarget/>
</Target>

<Target Name='Goo'>
    <GooTarget/>
</Target>

<Target Name='Foo2'>
    <Foo2Target/>
</Target>
</Project>");

            Dictionary<string, string> globalProperties =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties["ThreeIn"] = "3";
            globalProperties["BazIn"] = "bazfile";

            Project project = new Project(
                XmlReader.Create(new StringReader(projectBody)),
                globalProperties,
                ObjectModelHelpers.MSBuildDefaultToolsVersion,
                new ProjectCollection());
            project.FullPath = "foo";
            ProjectInstance instance = project.CreateProjectInstance();
            BuildRequestConfiguration configuration = new BuildRequestConfiguration(new BuildRequestData(instance, new string[] { }, null), "2.0");
            configuration.ConfigurationId = 1;

            string originalValue = Environment.GetEnvironmentVariable("MSBUILDCACHE");
            try
            {
                Environment.SetEnvironmentVariable("MSBUILDCACHE", "1");
                Assert.Equal("3", instance.GlobalProperties["ThreeIn"]);
                Assert.Equal("bazfile", instance.GlobalProperties["BazIn"]);
                Assert.Equal("1", instance.PropertiesToBuildWith["One"].EvaluatedValue);
                Assert.Equal("2", instance.PropertiesToBuildWith["Two"].EvaluatedValue);
                Assert.Equal("3", instance.PropertiesToBuildWith["Three"].EvaluatedValue);

                int fooCount = instance.ItemsToBuildWith["Foo"].Count;
                Assert.True(fooCount > 0);
                Assert.Single(instance.ItemsToBuildWith["Bar"]);
                Assert.Single(instance.ItemsToBuildWith["Baz"]);
                Assert.Equal("bazfile", instance.ItemsToBuildWith["Baz"].First().EvaluatedInclude);

                Lookup lookup = configuration.BaseLookup;

                Assert.NotNull(lookup);
                Assert.Equal(fooCount, lookup.GetItems("Foo").Count);

                // Configuration initialized with a ProjectInstance should not be cacheable by default.
                Assert.False(configuration.IsCacheable);
                configuration.IsCacheable = true;
                configuration.CacheIfPossible();

                Assert.Null(instance.GlobalPropertiesDictionary);
                Assert.Null(instance.ItemsToBuildWith);
                Assert.Null(instance.PropertiesToBuildWith);

                configuration.RetrieveFromCache();

                Assert.Equal("3", instance.GlobalProperties["ThreeIn"]);
                Assert.Equal("bazfile", instance.GlobalProperties["BazIn"]);
                Assert.Equal("1", instance.PropertiesToBuildWith["One"].EvaluatedValue);
                Assert.Equal("2", instance.PropertiesToBuildWith["Two"].EvaluatedValue);
                Assert.Equal("3", instance.PropertiesToBuildWith["Three"].EvaluatedValue);
                Assert.Equal(fooCount, instance.ItemsToBuildWith["Foo"].Count);
                Assert.Single(instance.ItemsToBuildWith["Bar"]);
                Assert.Single(instance.ItemsToBuildWith["Baz"]);
                Assert.Equal("bazfile", instance.ItemsToBuildWith["Baz"].First().EvaluatedInclude);

                lookup = configuration.BaseLookup;

                Assert.NotNull(lookup);
                Assert.Equal(fooCount, lookup.GetItems("Foo").Count);
            }
            finally
            {
                configuration.ClearCacheFile();
                Environment.SetEnvironmentVariable("MSBUILDCACHE", originalValue);
            }
        }

        [Fact]
        [Trait("Category", "netcore-osx-failing")]
        [Trait("Category", "netcore-linux-failing")]
        [Trait("Category", "mono-osx-failing")]
        public void TestCache2()
        {
            string projectBody = ObjectModelHelpers.CleanupFileContents(@"
                <Project ToolsVersion='msbuilddefaulttoolsversion' xmlns='msbuildnamespace'>
                <PropertyGroup>
                    <One>1</One>
                    <Two>2</Two>
                    <Three>$(ThreeIn)</Three>
                </PropertyGroup>
                <ItemGroup>
                    <Foo Include=""*""/>
                    <Bar Include=""msbuild.out"">
                        <One>1</One>
                    </Bar>
                    <Baz Include=""$(BazIn)""/>
                </ItemGroup>
                <Target Name='Build'>
                    <CallTarget Targets='Foo;Bar'/>
                </Target>

                <Target Name='Foo' DependsOnTargets='Foo'>
                    <FooTarget/>
                </Target>

                <Target Name='Bar'>
                    <BarTarget/>
                </Target>

                <Target Name='Foo'>
                    <FooTarget/>
                </Target>
                </Project>");

            Dictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties["ThreeIn"] = "3";
            globalProperties["BazIn"] = "bazfile";

            Project project = new Project(XmlReader.Create(new StringReader(projectBody)), globalProperties, ObjectModelHelpers.MSBuildDefaultToolsVersion, new ProjectCollection());
            project.FullPath = "foo";
            ProjectInstance instance = project.CreateProjectInstance();
            BuildRequestConfiguration configuration = new BuildRequestConfiguration(new BuildRequestData(instance, new string[] { }, null), "2.0");

            string originalTmp = Environment.GetEnvironmentVariable("TMP");
            string originalTemp = Environment.GetEnvironmentVariable("TEMP");

            try
            {
                string problematicTmpPath = @"C:\Users\}\blabla\temp";
                Environment.SetEnvironmentVariable("TMP", problematicTmpPath);
                Environment.SetEnvironmentVariable("TEMP", problematicTmpPath);

                FileUtilities.ClearCacheDirectoryPath();
                string cacheFilePath = configuration.GetCacheFile();
                Assert.StartsWith(problematicTmpPath, cacheFilePath);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TMP", originalTmp);
                Environment.SetEnvironmentVariable("TEMP", originalTemp);
                FileUtilities.ClearCacheDirectoryPath();
            }
        }

        [Fact]
        public void SkipIsolationChecksRejectsMissingEvaluation()
        {
            var configWithoutEvaluation = new BuildRequestConfiguration();

            var exception = Assert.Throws<InternalErrorException>(
                () =>
                {
                    configWithoutEvaluation.ShouldSkipIsolationConstraintsForReference(Path.GetFullPath("foo"));
                });
        }

        [Fact]
        public void SkipIsolationChecksRejectsRelativeReferencePaths()
        {
            var exception = Assert.Throws<InternalErrorException>(
                () =>
                {
                    TestSkipIsolationConstraints("*", "build.proj", false);
                });

            exception.Message.ShouldContain("Method does not treat path normalization cases");
        }

        [Fact]
        public void SkipIsolationConstraintsDoesNotSkipWhenItemDoesNotExist()
        {
            TestSkipIsolationConstraints(@"c:\*.csproj", @"c:\foo.csproj", false, "<Project></Project>");
        }

        [Theory]
        [InlineData("", @"c:\foo", false)]
        [InlineData("*", @"c:\foo.proj", false)] // relative glob is normalized to project directory
        [InlineData("*", @"$(MSBuildProjectDirectory)\foo.proj", true)] // relative glob is normalized to project directory
        [InlineData(@"c:\*.csproj", @"c:\foo.proj", false)]
        [InlineData(@"c:\*.csproj", @"c:\foo.csproj", true)]
        [InlineData(@"c:\*.props;c:\*.csproj", @"c:\foo.csproj", true)]
        [InlineData(@"c:\project\*script*\**\*.proj", @"c:\foo.csproj", false)]
        [InlineData(@"c:\project\*script*\**\*.proj", @"c:\project\scripts\a\b\build.proj", true)]
        [InlineData(@"c:\project\script\Project*.proj", @"c:\project\script\Project.proj", true)]
        [InlineData(@"c:\project\script\Project*.proj", @"c:\project\script\Project1.proj", true)]
        [InlineData(@"c:\project\script\Project*.proj", @"c:\project\script\build.proj", false)]
        public void SkipIsolationCheckShouldFilterReferencesViaMSBuildGlobs(string glob, string referencePath, bool expectedOutput)
        {
            TestSkipIsolationConstraints(glob, referencePath, expectedOutput);
        }

        private void TestSkipIsolationConstraints(string glob, string referencePath, bool expectedOutput, string projectContents = null)
        {
            if (!NativeMethodsShared.IsWindows)
            {
                glob = glob.Replace(@"c:\", "/").ToSlash();
                referencePath = referencePath.Replace(@"c:\", "/").ToSlash();
            }

            glob = $"$([MSBuild]::Escape('{glob}'))";

            projectContents ??= $@"
<Project>
    <ItemGroup>
        <{ItemTypeNames.GraphIsolationExemptReference} Include=`{glob};ShouldNotMatchAnything`/>
    </ItemGroup>
</Project>
".Cleanup();

            var projectCollection = _env.CreateProjectCollection().Collection;
            var project = Project.FromXmlReader(
                XmlReader.Create(new StringReader(projectContents)),
                new ProjectOptions
                {
                    ProjectCollection = projectCollection
                });

            project.FullPath = _env.CreateFolder().Path;

            var projectInstance = project.CreateProjectInstance();

            var configuration = new BuildRequestConfiguration(new BuildRequestData(projectInstance, new string[] {}), MSBuildConstants.CurrentToolsVersion);

            if (referencePath.Contains("$"))
            {
                referencePath = project.ExpandPropertyValueBestEffortLeaveEscaped(referencePath, ElementLocation.EmptyLocation);
            }

            configuration.ShouldSkipIsolationConstraintsForReference(referencePath).ShouldBe(expectedOutput);
        }
    }
}
