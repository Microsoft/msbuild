// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma warning disable 0219

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Utilities;
using Shouldly;
#if FEATURE_WIN32_REGISTRY
using Microsoft.Win32;
#endif

using FrameworkNameVersioning = System.Runtime.Versioning.FrameworkName;
using UtilitiesDotNetFrameworkArchitecture = Microsoft.Build.Utilities.DotNetFrameworkArchitecture;
using SharedDotNetFrameworkArchitecture = Microsoft.Build.Shared.DotNetFrameworkArchitecture;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Build.UnitTests
{
    public sealed class ToolLocationHelper_Tests
    {
        private readonly ITestOutputHelper _output;

#if USE_MSBUILD_DLL_EXTN
        private const string MSBuildExeName = "MSBuild.dll";
#else
        private const string MSBuildExeName = "MSBuild.exe";
#endif

        public ToolLocationHelper_Tests(ITestOutputHelper output)
        {
            _output = output;
            ToolLocationHelper.ClearStaticCaches();
        }

        [Fact]
        public void GetApiContractReferencesHandlesEmptyContracts()
        {
            string[] returnValue = ToolLocationHelper.GetApiContractReferences(Enumerable.Empty<ApiContract>(), string.Empty);
            returnValue.Length.ShouldBe(0);
        }

        [Fact]
        public void GetApiContractReferencesHandlesNullContracts()
        {
            string[] returnValue = ToolLocationHelper.GetApiContractReferences(null, string.Empty);
            returnValue.Length.ShouldBe(0);
        }

        [Fact]
        [Trait("Category", "netcore-osx-failing")]
        [Trait("Category", "netcore-linux-failing")]
        public void GetApiContractReferencesHandlesNonExistingLocation()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string[] returnValue = ToolLocationHelper.GetApiContractReferences(new[] { new ApiContract { Name = "Foo", Version = "Bar" } }, tempDirectory);
            returnValue.Length.ShouldBe(0);
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void GetApiContractReferencesFindsWinMDs()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string referenceDirectory = Path.Combine(tempDirectory, Path.Combine("References", "Foo", "Bar"));

            try
            {
                Directory.CreateDirectory(referenceDirectory);
                File.WriteAllText(Path.Combine(referenceDirectory, "One.winmd"), "First");
                File.WriteAllText(Path.Combine(referenceDirectory, "Two.winmd"), "Second");
                File.WriteAllText(Path.Combine(referenceDirectory, "Three.winmd"), "Third");
                string[] returnValue = ToolLocationHelper.GetApiContractReferences(new[] { new ApiContract { Name = "Foo", Version = "Bar" } }, tempDirectory);
                returnValue.Length.ShouldBe(3);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(tempDirectory, true);
                }
            }
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void GetApiContractReferencesFindsVersionedWinMDs()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string tempVersion = "10.0.12345.0";
            string referenceDirectory = Path.Combine(tempDirectory, @"References", tempVersion, @"Foo\Bar");

            try
            {
                Directory.CreateDirectory(referenceDirectory);
                File.WriteAllText(Path.Combine(referenceDirectory, "One.winmd"), "First");
                string[] returnValue = ToolLocationHelper.GetApiContractReferences(new[] { new ApiContract { Name = "Foo", Version = "Bar" } }, tempDirectory, tempVersion);
                returnValue.Length.ShouldBe(1);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, true);
                }
            }
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void GetWinBlueSDKLocation()
        {
            string sdkRootPath = ToolLocationHelper.GetPlatformSDKLocation("Windows", "8.1");

            string returnValue = ToolLocationHelper.GetSDKContentFolderPath("Windows", "8.1", null, null, null, null);
            returnValue.ShouldBe(sdkRootPath);
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void GetWinBlueContentFolderPath()
        {
            string sdkRootPath = ToolLocationHelper.GetPlatformSDKLocation("Windows", "8.1");

            string returnValue = ToolLocationHelper.GetSDKContentFolderPath("Windows", "8.1", null, null, null, @"DesignTime\CommonConfiguration\Neutral");
            returnValue.ShouldBe(Path.Combine(sdkRootPath, @"DesignTime\CommonConfiguration\Neutral"));
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void GetSDKRootLocation()
        {
            string expectedValue = ToolLocationHelper.GetPlatformSDKLocation("Windows", "10.0");

            string versionedSDKValue = ToolLocationHelper.GetSDKContentFolderPath("Windows", "10.0", "UAP", "10.0.14944.0", "10.0.14944.0", null);
            versionedSDKValue.ShouldBe(expectedValue);

            string unversionedSDKValue = ToolLocationHelper.GetSDKContentFolderPath("Windows", "10.0", "UAP", "10.0.10586.0", "10.0.10586.0", null);
            unversionedSDKValue.ShouldBe(expectedValue);
        }

#if RUNTIME_TYPE_NETCORE
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/1250")]
#else
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/2569")]
#endif
        [PlatformSpecific(TestPlatforms.Windows)]
        public void GetUnversionedSDKUnionMetadataLocation()
        {
            string sdkRootPath = ToolLocationHelper.GetPlatformSDKLocation("Windows", "10.0");
            string returnValue = ToolLocationHelper.GetSDKContentFolderPath("Windows", "10.0", "UAP", "10.0.14393.0", "10.0.14393.0", "UnionMetadata");

            returnValue.ShouldNotContain("10.0.14393.0");
            returnValue.ShouldBe(Path.Combine(sdkRootPath, "UnionMetadata"));
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void GetVersionedSDKUnionMetadataLocation()
        {
            // Create manifest file
            string platformRootFolder = Path.Combine(Path.GetTempPath(), @"MockSDK");
            string sdkRootFolder = Path.Combine(platformRootFolder, @"Windows Kits\10");
            string platformFolder = Path.Combine(sdkRootFolder, @"Platforms\UAP\10.0.14944.0");
            string platformFilePath = Path.Combine(platformFolder, "Platform.xml");
            string sdkManifestFilePath = Path.Combine(sdkRootFolder, "SDKManifest.xml");

            bool useTempPlatformFile = false;
            try
            {
                if (!File.Exists(platformFilePath))
                {
                    if (!Directory.Exists(sdkRootFolder))
                    {
                        Directory.CreateDirectory(sdkRootFolder);
                    }

                    if (!Directory.Exists(platformFolder))
                    {
                        Directory.CreateDirectory(platformFolder);
                    }

                    string sdkManifestFileContent = @"<?xml version=""1.0"" encoding=""utf-8"" ?>
<FileList
  TargetPlatform=""UAP""
  TargetPlatformMinVersion=""10.0.0.0""
  TargetPlatformVersion=""10.0.14944.0""
  DisplayName = ""Microsoft Mock SDK for UAP 10.0.14944.0""
  AppliesTo = ""WindowsAppContainer + (Managed | Javascript | Native)""
  MinVSVersion = ""14.0""
  SupportsMultipleVersions=""Error""
  SupportedArchitectures=""x86;x64;ARM;ARM64"">
</FileList>";
                    string platformFileContent = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ApplicationPlatform name=""UAP"" friendlyName=""Windows 10 Anniversary Edition Insider Preview"" version=""10.0.14944.0"">
   <VersionedContent>true</VersionedContent>
</ApplicationPlatform>";

                    File.WriteAllText(platformFilePath, platformFileContent);
                    File.WriteAllText(sdkManifestFilePath, sdkManifestFileContent);

                    useTempPlatformFile = true;
                }

                // Get and verify return value
                string returnValue = ToolLocationHelper.GetSDKContentFolderPath("Windows", "10.0", "UAP", "10.0.14944.0", "10.0.14944.0", "UnionMetadata", platformRootFolder);
                returnValue.ShouldBe(Path.Combine(sdkRootFolder, "UnionMetadata", "10.0.14944.0"));
            }
            finally
            {
                if (useTempPlatformFile)
                {
                    FileUtilities.DeleteDirectoryNoThrow(platformRootFolder, true);
                }
            }
        }

        [Fact]
        [Trait("Category", "netcore-osx-failing")]
        [Trait("Category", "netcore-linux-failing")]
        public void GatherExtensionSDKsInvalidVersionDirectory()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string sdkDirectory = Path.Combine(tempDirectory, "Foo", "Bar");

            try
            {
                Directory.CreateDirectory(sdkDirectory);
                DirectoryInfo info = new DirectoryInfo(tempDirectory);
                TargetPlatformSDK sdk = new TargetPlatformSDK("Foo", new Version(0, 0), string.Empty);
                ToolLocationHelper.GatherExtensionSDKs(info, sdk);
                sdk.ExtensionSDKs.Count.ShouldBe(0);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(tempDirectory, true);
                }
            }
        }

        [Fact]
        [Trait("Category", "netcore-osx-failing")]
        [Trait("Category", "netcore-linux-failing")]
        public void GatherExtensionSDKsNoManifest()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string sdkDirectory = Path.Combine(tempDirectory, "Foo", "1.0");

            try
            {
                Directory.CreateDirectory(sdkDirectory);
                DirectoryInfo info = new DirectoryInfo(tempDirectory);
                TargetPlatformSDK sdk = new TargetPlatformSDK("Foo", new Version(0, 0), string.Empty);
                ToolLocationHelper.GatherExtensionSDKs(info, sdk);
                sdk.ExtensionSDKs.Count.ShouldBe(0);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(tempDirectory, true);
                }
            }
        }

        [Fact]
        [Trait("Category", "netcore-osx-failing")]
        [Trait("Category", "netcore-linux-failing")]
        public void GatherExtensionSDKsEmptyManifest()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string sdkDirectory = Path.Combine(tempDirectory, "Foo", "1.0");

            try
            {
                Directory.CreateDirectory(sdkDirectory);
                File.WriteAllText(Path.Combine(sdkDirectory, "SDKManifest.xml"), "");
                DirectoryInfo info = new DirectoryInfo(tempDirectory);
                TargetPlatformSDK sdk = new TargetPlatformSDK("Foo", new Version(0, 0), string.Empty);
                ToolLocationHelper.GatherExtensionSDKs(info, sdk);
                sdk.ExtensionSDKs.Count.ShouldBe(1);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(tempDirectory, true);
                }
            }
        }

        [Fact]
        [Trait("Category", "netcore-osx-failing")]
        [Trait("Category", "netcore-linux-failing")]
        public void GatherExtensionSDKsGarbageManifest()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string sdkDirectory = Path.Combine(tempDirectory, "Foo", "1.0");

            try
            {
                Directory.CreateDirectory(sdkDirectory);
                File.WriteAllText(Path.Combine(sdkDirectory, "SDKManifest.xml"), "Garbaggggge");
                DirectoryInfo info = new DirectoryInfo(tempDirectory);
                TargetPlatformSDK sdk = new TargetPlatformSDK("Foo", new Version(0, 0), string.Empty);
                ToolLocationHelper.GatherExtensionSDKs(info, sdk);
                sdk.ExtensionSDKs.Count.ShouldBe(1);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(tempDirectory, true);
                }
            }
        }

        /// <summary>
        /// Verify the case where we ask for a tool using a target framework version of 3.5
        /// We make sure in the fake sdk path we also create a 4.0 folder in order to make sure we do not return that when we only want the bin directory.
        /// </summary>
        [Fact]
        public void VerifyinternalGetPathToDotNetFrameworkSdkFileNot40()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "VGPTDNFSFN40");
            string temp35Directory = Path.Combine(tempDirectory, "bin");
            string temp40Directory = Path.Combine(temp35Directory, "NETFX 4.0 Tools");
            string toolPath = Path.Combine(temp35Directory, "MyTool.exe");
            string toolPath40 = Path.Combine(temp40Directory, "MyTool.exe");

            try
            {
                if (!Directory.Exists(temp35Directory))
                {
                    Directory.CreateDirectory(temp35Directory);
                }

                // Make a .NET 4.0 Tools so that we can make sure that we do not return it if we are not targeting 4.0
                if (!Directory.Exists(temp40Directory))
                {
                    Directory.CreateDirectory(temp40Directory);
                }

                // Write a tool to disk to the existence check works
                File.WriteAllText(toolPath, "Contents");
                File.WriteAllText(toolPath40, "Contents");

                string foundToolPath = ToolLocationHelper.GetPathToDotNetFrameworkSdkFile("MyTool.exe", temp35Directory, "x86");
                foundToolPath.ShouldNotBeNull();
                foundToolPath.ShouldBe(toolPath, StringCompareShould.IgnoreCase);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(tempDirectory, true);
                }
            }
        }

        /// <summary>
        /// Make sure that if a unknown framework identifier with a root directory which does not exist in it is passed in then we get an empty list back out.
        /// </summary>
        [Fact]
        public void GetFrameworkIdentifiersNoReferenceAssemblies()
        {
            IList<string> installedIdentifiers =
                ToolLocationHelper.GetFrameworkIdentifiers(
                    NativeMethodsShared.IsWindows ? "f:\\IDontExistAtAll" : "/IDontExistAtAll");
            installedIdentifiers.Count.ShouldBe(0);
        }

        /// <summary>
        /// When the root does not exist make sure nothing is returned
        /// </summary>
        [Fact]
        public void HighestVersionOfTargetFrameworkIdentifierRootDoesNotExist()
        {
            FrameworkNameVersioning highestMoniker =
                ToolLocationHelper.HighestVersionOfTargetFrameworkIdentifier(
                    NativeMethodsShared.IsWindows ? "f:\\IDontExistAtAll" : "/IDontExistAtAll",
                    ".UnKNownFramework");
            highestMoniker.ShouldBeNull();
        }

        /// <summary>
        /// When the root contains no folders with versions on them make sure nothing is returned
        /// </summary>
        [Fact]
        public void HighestVersionOfTargetFrameworkIdentifierRootNoVersions()
        {
            string tempPath = Path.GetTempPath();
            string testPath = Path.Combine(tempPath, "HighestVersionOfTargetFrameworkIdentifierRootNoVersions");
            string nonVersionFolder = Path.Combine(testPath, ".UnknownFramework", "NotAVersion");

            if (!Directory.Exists(nonVersionFolder))
            {
                Directory.CreateDirectory(nonVersionFolder);
            }

            FrameworkNameVersioning highestMoniker = ToolLocationHelper.HighestVersionOfTargetFrameworkIdentifier(testPath, ".UnKNownFramework");
            highestMoniker.ShouldBeNull();
        }

        /// <summary>
        /// If a directory contains multiple versions make sure we pick the highest one.
        /// </summary>
        [Fact]
        public void HighestVersionOfTargetFrameworkIdentifierRootMultipleVersions()
        {
            string tempPath = Path.GetTempPath();
            string testPath = Path.Combine(tempPath, "HighestVersionOfTargetFrameworkIdentifierRootMultipleVersions");
            string folder10 = Path.Combine(testPath, ".UnknownFramework", "v1.0");
            string folder20 = Path.Combine(testPath, ".UnknownFramework", "v2.0");
            string folder40 = Path.Combine(testPath, ".UnknownFramework", "v4.0");

            if (!Directory.Exists(folder10))
            {
                Directory.CreateDirectory(folder10);
            }

            if (!Directory.Exists(folder20))
            {
                Directory.CreateDirectory(folder20);
            }

            if (!Directory.Exists(folder40))
            {
                Directory.CreateDirectory(folder40);
            }

            FrameworkNameVersioning highestMoniker =
                ToolLocationHelper.HighestVersionOfTargetFrameworkIdentifier(testPath, ".UnknownFramework");
            highestMoniker.ShouldNotBeNull();
            highestMoniker.Version.Major.ShouldBe(4);
        }

        /// <summary>
        /// Verify the case where we ask for a tool using a target framework version of 4.0
        /// </summary>
        [Fact]
        public void VerifyinternalGetPathToDotNetFrameworkSdkFile40()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "VGPTDNFSF40");
            string temp35Directory = Path.Combine(tempDirectory, "bin");
            string temp40Directory = Path.Combine(temp35Directory, "NETFX 4.0 Tools");
            string toolPath = Path.Combine(temp35Directory, "MyTool.exe");
            string toolPath40 = Path.Combine(temp40Directory, "MyTool.exe");

            try
            {
                if (!Directory.Exists(temp35Directory))
                {
                    Directory.CreateDirectory(temp35Directory);
                }

                // Make a .NET 4.0 Tools so that we can make sure that we do not return it if we are not targeting 4.0
                if (!Directory.Exists(temp40Directory))
                {
                    Directory.CreateDirectory(temp40Directory);
                }

                // Write a tool to disk to the existence check works
                File.WriteAllText(toolPath, "Contents");
                File.WriteAllText(toolPath40, "Contents");

                string foundToolPath = ToolLocationHelper.GetPathToDotNetFrameworkSdkFile("MyTool.exe", temp40Directory, "x86");
                foundToolPath.ShouldNotBeNull();
                foundToolPath.ShouldBe(toolPath40, StringCompareShould.IgnoreCase);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(tempDirectory, true);
                }
            }
        }

        /// <summary>
        /// Make sure if null is passed in for any of the arguments that the method returns null and does not crash.
        /// </summary>
        [Fact]
        public void VerifyinternalGetPathToDotNetFrameworkSdkFileNullPassedIn()
        {
            string foundToolPath = ToolLocationHelper.GetPathToDotNetFrameworkSdkFile("MyTool.exe", "C:\\Path", null);
            foundToolPath.ShouldBeNull();

            foundToolPath = ToolLocationHelper.GetPathToDotNetFrameworkSdkFile("MyTool.exe", null, "x86");
            foundToolPath.ShouldBeNull();

            foundToolPath = ToolLocationHelper.GetPathToDotNetFrameworkSdkFile(null, "c:\\path", "x86");
            foundToolPath.ShouldBeNull();
        }

        /*
          * Method:   FindFrameworksPathRunningThisTest
          *
          * Our FX path should be resolved as the one we're running on by default
          */
        [Fact]
        public void FindFrameworksPathRunningThisTest()
        {
            string path = FrameworkLocationHelper.FindDotNetFrameworkPath(
                Path.GetDirectoryName(typeof(object).GetTypeInfo().Module.FullyQualifiedName),
                ToolLocationHelper.GetDotNetFrameworkVersionFolderPrefix(TargetDotNetFrameworkVersion.Version40),
                DirectoryExists,
                GetDirectories,
                SharedDotNetFrameworkArchitecture.Current
            );

#if FEATURE_INSTALLED_MSBUILD
            path.ShouldBe(Path.GetDirectoryName(typeof(object).GetTypeInfo().Module.FullyQualifiedName));
#else
            path.ShouldBeNull();
#endif
        }

        /*
         * Method:   FindFrameworksPathRunningUnderWhidbey
         *
         * Search for a whidbey when whidbey is the current version.
         */
        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void FindFrameworksPathRunningUnderWhidbey()
        {
            string path = FrameworkLocationHelper.FindDotNetFrameworkPath
                (
                    @"{runtime-base}\v1.2.x86dbg",    // Simulate "Whidbey" as the current runtime.
                    "v1.2",
                    DirectoryExists,
                    GetDirectories,
                    SharedDotNetFrameworkArchitecture.Current
                );
            path.ShouldBe(Path.Combine("{runtime-base}", "v1.2.x86dbg"));
        }

        /*
        * Method:   FindFrameworksPathRunningUnderOrcas
        *
        * Search for a whidbey when orcas is the current version.
        */
        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void FindFrameworksPathRunningUnderOrcas()
        {
            string path = FrameworkLocationHelper.FindDotNetFrameworkPath
                (
                    Path.Combine("{runtime-base}", "v1.3.x86dbg"),        // Simulate "Orcas" as the current runtime.}
                    "v1.2",                                              // But we're looking for "Whidbey"
                    DirectoryExists,
                    GetDirectories,
                    SharedDotNetFrameworkArchitecture.Current
                );
            path.ShouldBe(Path.Combine("{runtime-base}", "v1.2.x86fre"));
        }

        /*
        * Method:   FindFrameworksPathRunningUnderEverett
        *
        * Search for a whidbey when orcas is the current version.
        */
        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void FindFrameworksPathRunningUnderEverett()
        {
            string path = FrameworkLocationHelper.FindDotNetFrameworkPath
                (
                    Path.Combine("{runtime-base}", "v1.1.x86dbg"),       // Simulate "Everett" as the current runtime.
                    "v1.2",                                              // But we're looking for "Whidbey"
                    DirectoryExists,
                    GetDirectories,
                    SharedDotNetFrameworkArchitecture.Current
                );

            path.ShouldBe(Path.Combine("{runtime-base}", "v1.2.x86fre"));
        }

        /*
        * Method:   FindPathForNonexistentFrameworks
        *
        * Trying to find a non-existent path should return null.
        */
        [Fact]
        public void FindPathForNonexistentFrameworks()
        {
            string path = FrameworkLocationHelper.FindDotNetFrameworkPath
                (
                    Path.Combine(@"{runtime-base}", "v1.1"),             // Simulate "everett" as the current runtime
                    "v1.3",                                              // And we're trying to find "orchas" runtime which isn't installed.
                    DirectoryExists,
                    GetDirectories,
                    SharedDotNetFrameworkArchitecture.Current
                );

            path.ShouldBeNull();
        }

        /*
        * Method:   FindPathForEverettThatIsntProperlyInstalled
        *
        * Trying to find a path if GetRequestedRuntimeInfo fails and useHeuristic=false should return null.
        */
        [Fact]
        public void FindPathForEverettThatIsntProperlyInstalled()
        {
            string tempPath = Path.GetTempPath();
            string fakeWhidbeyPath = Path.Combine(tempPath, "v2.0.50224");
            string fakeEverettPath = Path.Combine(tempPath, "v1.1.43225");
            Directory.CreateDirectory(fakeEverettPath);

            string path = FrameworkLocationHelper.FindDotNetFrameworkPath
                (
                    fakeWhidbeyPath,  // Simulate "whidbey" as the current runtime
                    "v1.1",                 // We're looking for "everett"
                    DirectoryExists,
                    GetDirectories,
                    SharedDotNetFrameworkArchitecture.Current
                );

            FileUtilities.DeleteWithoutTrailingBackslash(fakeEverettPath);
            path.ShouldBeNull();
        }

        [Fact]
        [Trait("Category", "mono-osx-failing")]
        [Trait("Category", "netcore-osx-failing")]
        [Trait("Category", "netcore-linux-failing")]
        public void ExerciseMiscToolLocationHelperMethods()
        {
            ToolLocationHelper.GetDotNetFrameworkVersionFolderPrefix(TargetDotNetFrameworkVersion.Version11).ShouldBe(FrameworkLocationHelper.dotNetFrameworkVersionFolderPrefixV11);
            ToolLocationHelper.GetDotNetFrameworkVersionFolderPrefix(TargetDotNetFrameworkVersion.Version20).ShouldBe(FrameworkLocationHelper.dotNetFrameworkVersionFolderPrefixV20);
            ToolLocationHelper.GetDotNetFrameworkVersionFolderPrefix(TargetDotNetFrameworkVersion.Version30).ShouldBe(FrameworkLocationHelper.dotNetFrameworkVersionFolderPrefixV30);
            ToolLocationHelper.GetDotNetFrameworkVersionFolderPrefix(TargetDotNetFrameworkVersion.Version35).ShouldBe(FrameworkLocationHelper.dotNetFrameworkVersionFolderPrefixV35);
            ToolLocationHelper.GetDotNetFrameworkVersionFolderPrefix(TargetDotNetFrameworkVersion.Version40).ShouldBe(FrameworkLocationHelper.dotNetFrameworkVersionFolderPrefixV40);
            ToolLocationHelper.GetDotNetFrameworkVersionFolderPrefix(TargetDotNetFrameworkVersion.VersionLatest).ShouldBe(FrameworkLocationHelper.dotNetFrameworkVersionFolderPrefixV40);
            ToolLocationHelper.GetDotNetFrameworkRootRegistryKey(TargetDotNetFrameworkVersion.VersionLatest).ShouldBe(FrameworkLocationHelper.fullDotNetFrameworkRegistryKey);
            ToolLocationHelper.GetDotNetFrameworkVersionFolderPrefix(TargetDotNetFrameworkVersion.Latest).ShouldBe(FrameworkLocationHelper.dotNetFrameworkVersionFolderPrefixV40);
            ToolLocationHelper.GetDotNetFrameworkRootRegistryKey(TargetDotNetFrameworkVersion.Latest).ShouldBe(FrameworkLocationHelper.fullDotNetFrameworkRegistryKey);

            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version11).ShouldBe(FrameworkLocationHelper.PathToDotNetFrameworkV11);
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version20).ShouldBe(FrameworkLocationHelper.PathToDotNetFrameworkV20);
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version30).ShouldBe(FrameworkLocationHelper.PathToDotNetFrameworkV30);
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version35).ShouldBe(FrameworkLocationHelper.PathToDotNetFrameworkV35);
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version40).ShouldBe(FrameworkLocationHelper.PathToDotNetFrameworkV40);
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.VersionLatest).ShouldBe(FrameworkLocationHelper.PathToDotNetFrameworkV40);
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Latest).ShouldBe(FrameworkLocationHelper.PathToDotNetFrameworkV40);

            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version11, UtilitiesDotNetFrameworkArchitecture.Bitness32).ShouldBe(
                    FrameworkLocationHelper.GetPathToDotNetFrameworkV11(SharedDotNetFrameworkArchitecture.Bitness32)
                );
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version20, UtilitiesDotNetFrameworkArchitecture.Bitness32).ShouldBe(
                    FrameworkLocationHelper.GetPathToDotNetFrameworkV20(SharedDotNetFrameworkArchitecture.Bitness32)
                );
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version30, UtilitiesDotNetFrameworkArchitecture.Bitness32).ShouldBe(
                    FrameworkLocationHelper.GetPathToDotNetFrameworkV30(SharedDotNetFrameworkArchitecture.Bitness32)
                );
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version35, UtilitiesDotNetFrameworkArchitecture.Bitness32).ShouldBe(
                    FrameworkLocationHelper.GetPathToDotNetFrameworkV35(SharedDotNetFrameworkArchitecture.Bitness32)
                );

            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version40, UtilitiesDotNetFrameworkArchitecture.Bitness32).ShouldBe(
                    FrameworkLocationHelper.GetPathToDotNetFrameworkV40(SharedDotNetFrameworkArchitecture.Bitness32)
                );
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.VersionLatest, UtilitiesDotNetFrameworkArchitecture.Bitness32).ShouldBe(
                    FrameworkLocationHelper.GetPathToDotNetFrameworkV40(SharedDotNetFrameworkArchitecture.Bitness32)
                );
            ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Latest, UtilitiesDotNetFrameworkArchitecture.Bitness32).ShouldBe(
                    FrameworkLocationHelper.GetPathToDotNetFrameworkV40(SharedDotNetFrameworkArchitecture.Bitness32)
                );

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ProgramFiles(x86)")))
            {
                // 64-bit machine, so we should test the 64-bit overloads as well
                ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version11, UtilitiesDotNetFrameworkArchitecture.Bitness64).ShouldBe(
                    FrameworkLocationHelper.GetPathToDotNetFrameworkV11(SharedDotNetFrameworkArchitecture.Bitness64)
                );
                ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version20, UtilitiesDotNetFrameworkArchitecture.Bitness64).ShouldBe(
                        FrameworkLocationHelper.GetPathToDotNetFrameworkV20(SharedDotNetFrameworkArchitecture.Bitness64)
                    );
                ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version30, UtilitiesDotNetFrameworkArchitecture.Bitness64).ShouldBe(
                        FrameworkLocationHelper.GetPathToDotNetFrameworkV30(SharedDotNetFrameworkArchitecture.Bitness64)
                    );
                ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version35, UtilitiesDotNetFrameworkArchitecture.Bitness64).ShouldBe(
                        FrameworkLocationHelper.GetPathToDotNetFrameworkV35(SharedDotNetFrameworkArchitecture.Bitness64)
                    );

                ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Version40, UtilitiesDotNetFrameworkArchitecture.Bitness64).ShouldBe(
                        FrameworkLocationHelper.GetPathToDotNetFrameworkV40(SharedDotNetFrameworkArchitecture.Bitness64)
                    );
                ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.VersionLatest, UtilitiesDotNetFrameworkArchitecture.Bitness64).ShouldBe(
                        FrameworkLocationHelper.GetPathToDotNetFrameworkV40(SharedDotNetFrameworkArchitecture.Bitness64)
                    );
                ToolLocationHelper.GetPathToDotNetFramework(TargetDotNetFrameworkVersion.Latest, UtilitiesDotNetFrameworkArchitecture.Bitness64).ShouldBe(
                        FrameworkLocationHelper.GetPathToDotNetFrameworkV40(SharedDotNetFrameworkArchitecture.Bitness64)
                    );
            }
        }

        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void TestGetPathToBuildToolsFile()
        {
            string net20Path = ToolLocationHelper.GetPathToDotNetFrameworkFile("MSBuild.exe", TargetDotNetFrameworkVersion.Version20);

            net20Path?.ShouldBe(ToolLocationHelper.GetPathToBuildToolsFile("MSBuild.exe", "2.0"));

            string net35Path = ToolLocationHelper.GetPathToDotNetFrameworkFile("MSBuild.exe", TargetDotNetFrameworkVersion.Version35);

            net35Path?.ShouldBe(ToolLocationHelper.GetPathToBuildToolsFile("MSBuild.exe", "3.5"));

            ToolLocationHelper.GetPathToDotNetFrameworkFile("MSBuild.exe", TargetDotNetFrameworkVersion.Version40).ShouldBe(ToolLocationHelper.GetPathToBuildToolsFile("MSBuild.exe", "4.0"));

            string tv12path = Path.Combine(ProjectCollection.GlobalProjectCollection.GetToolset(ObjectModelHelpers.MSBuildDefaultToolsVersion).ToolsPath, MSBuildExeName);

            tv12path.ShouldBe(ToolLocationHelper.GetPathToBuildToolsFile(MSBuildExeName, ObjectModelHelpers.MSBuildDefaultToolsVersion));
            tv12path.ShouldBe(ToolLocationHelper.GetPathToBuildToolsFile(MSBuildExeName, ToolLocationHelper.CurrentToolsVersion));
        }

#if RUNTIME_TYPE_NETCORE
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/722")]
#else
        [Fact]
        [Trait("Category", "mono-osx-failing")]
#endif
        public void TestGetPathToBuildToolsFile_32Bit()
        {
            string net20Path = ToolLocationHelper.GetPathToDotNetFrameworkFile("msbuild.exe", TargetDotNetFrameworkVersion.Version20, UtilitiesDotNetFrameworkArchitecture.Bitness32);
            net20Path?.ShouldBe(ToolLocationHelper.GetPathToBuildToolsFile("msbuild.exe", "2.0", UtilitiesDotNetFrameworkArchitecture.Bitness32));

            string net35Path = ToolLocationHelper.GetPathToDotNetFrameworkFile("msbuild.exe", TargetDotNetFrameworkVersion.Version35, UtilitiesDotNetFrameworkArchitecture.Bitness32);
            net35Path?.ShouldBe(ToolLocationHelper.GetPathToBuildToolsFile("msbuild.exe", "3.5", UtilitiesDotNetFrameworkArchitecture.Bitness32));

            ToolLocationHelper.GetPathToDotNetFrameworkFile("msbuild.exe", TargetDotNetFrameworkVersion.Version40, UtilitiesDotNetFrameworkArchitecture.Bitness32).ShouldBe(
                    ToolLocationHelper.GetPathToBuildToolsFile("msbuild.exe", "4.0", UtilitiesDotNetFrameworkArchitecture.Bitness32));

            var toolsPath32 = ProjectCollection.GlobalProjectCollection.GetToolset(ObjectModelHelpers.MSBuildDefaultToolsVersion).Properties["MSBuildToolsPath32"];
            string tv12path = Path.Combine(Path.GetFullPath(toolsPath32.EvaluatedValue), "msbuild.exe");

            tv12path.ShouldBe(ToolLocationHelper.GetPathToBuildToolsFile("msbuild.exe", ObjectModelHelpers.MSBuildDefaultToolsVersion, UtilitiesDotNetFrameworkArchitecture.Bitness32));
            tv12path.ShouldBe(ToolLocationHelper.GetPathToBuildToolsFile("msbuild.exe", ToolLocationHelper.CurrentToolsVersion, UtilitiesDotNetFrameworkArchitecture.Bitness32));
        }

        [Fact]
        public void TestGetDotNetFrameworkSdkRootRegistryKey()
        {
            // Test out of range .net version.
            foreach (var vsVersion in EnumVisualStudioVersions())
            {
                Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey((TargetDotNetFrameworkVersion)99, vsVersion) );
            }

            // Test out of range visual studio version.
            foreach (var dotNetVersion in EnumDotNetFrameworkVersions())
            {
                Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(dotNetVersion, (VisualStudioVersion)99) );
            }

            foreach (var vsVersion in EnumVisualStudioVersions())
            {
                // v1.1
                ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version11, vsVersion).ShouldBe(FrameworkLocationHelper.fullDotNetFrameworkRegistryKey);

                // v2.0
                ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version20, vsVersion).ShouldBe(FrameworkLocationHelper.fullDotNetFrameworkRegistryKey);

                // v3.0
                Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version30, vsVersion) );

                // v3.5
                ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version35, vsVersion).ShouldBe(
                    vsVersion == VisualStudioVersion.Version100 ? FrameworkLocationHelper.fullDotNetFrameworkSdkRegistryKeyV35OnVS10 : FrameworkLocationHelper.fullDotNetFrameworkSdkRegistryKeyV35OnVS11);
            }

            string fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK70A = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\Windows\v7.0A\WinSDK-NetFx40Tools-x86";
            string fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK80A = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\Windows\v8.0A\WinSDK-NetFx40Tools-x86";
            string fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK81A = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\Windows\v8.1A\WinSDK-NetFx40Tools-x86";
            string fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK46 = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\NETFXSDK\4.6\WinSDK-NetFx40Tools-x86";
            string fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK461 = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\NETFXSDK\4.6.1\WinSDK-NetFx40Tools-x86";
            string fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK462 = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\NETFXSDK\4.6.2\WinSDK-NetFx40Tools-x86";
            string fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK47 = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\NETFXSDK\4.7\WinSDK-NetFx40Tools-x86";
            string fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK471 = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\NETFXSDK\4.7.1\WinSDK-NetFx40Tools-x86";
            string fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK472 = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\NETFXSDK\4.7.2\WinSDK-NetFx40Tools-x86";
            string fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK48 = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Microsoft SDKs\NETFXSDK\4.8\WinSDK-NetFx40Tools-x86";

            // v4.0
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version40, VisualStudioVersion.Version100).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK70A);
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version40, VisualStudioVersion.Version110).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK80A);
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version40, VisualStudioVersion.Version120).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK81A);
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version40, VisualStudioVersion.Version140).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK46);

            // v4.5
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version45, VisualStudioVersion.Version100).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK80A);
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version45, VisualStudioVersion.Version110).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK80A);
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version45, VisualStudioVersion.Version120).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK81A);
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version45, VisualStudioVersion.Version140).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK46);

            // v4.5.1
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version100) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version110) );
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version120).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK81A);
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version140).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK46);

            // v4.5.2
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version452, VisualStudioVersion.Version100) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version452, VisualStudioVersion.Version110) );
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version452, VisualStudioVersion.Version120).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK81A);
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version452, VisualStudioVersion.Version140).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK46);

            // v4.6
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version100) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version110) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version120) );
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version140).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK46);

            // v4.6.1
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version461, VisualStudioVersion.Version100) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version461, VisualStudioVersion.Version110) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version461, VisualStudioVersion.Version120) );
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version461, VisualStudioVersion.Version140).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK461);

            // v4.6.2
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version462, VisualStudioVersion.Version100) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version462, VisualStudioVersion.Version110) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version462, VisualStudioVersion.Version120) );
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version462, VisualStudioVersion.Version150).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK462);

            // v4.7
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version47, VisualStudioVersion.Version100) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version47, VisualStudioVersion.Version110) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version47, VisualStudioVersion.Version120) );
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version47, VisualStudioVersion.Version150).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK47);

            // v4.7.1
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version471, VisualStudioVersion.Version100));
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version471, VisualStudioVersion.Version110));
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version471, VisualStudioVersion.Version120));
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version471, VisualStudioVersion.Version150).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK471);

            // v4.7.2
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version472, VisualStudioVersion.Version100));
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version472, VisualStudioVersion.Version110));
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version472, VisualStudioVersion.Version120));
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version472, VisualStudioVersion.Version150).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK472);

            // v4.8
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version48, VisualStudioVersion.Version100));
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version48, VisualStudioVersion.Version110));
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version48, VisualStudioVersion.Version120));
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version48, VisualStudioVersion.Version140));
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Version48, VisualStudioVersion.Version150).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK48);

            // Latest
            ToolLocationHelper.GetDotNetFrameworkSdkRootRegistryKey(TargetDotNetFrameworkVersion.Latest, VisualStudioVersion.Version150).ShouldBe(fullDotNetFrameworkSdkRegistryPathForV4ToolsOnManagedToolsSDK48);
        }

        [Fact]
        public void TestGetDotNetFrameworkSdkInstallKeyValue()
        {
            // Test out of range .net version.
            foreach (var vsVersion in EnumVisualStudioVersions())
            {
                Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue((TargetDotNetFrameworkVersion)99, vsVersion) );
            }

            // Test out of range visual studio version.
            foreach (var dotNetVersion in EnumDotNetFrameworkVersions())
            {
                Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(dotNetVersion, (VisualStudioVersion)99) );
            }

            string InstallationFolder = "InstallationFolder";

            foreach (var vsVersion in EnumVisualStudioVersions())
            {
                // v1.1
                ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version11, vsVersion).ShouldBe(FrameworkLocationHelper.dotNetFrameworkSdkInstallKeyValueV11);

                // v2.0
                ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version20, vsVersion).ShouldBe(FrameworkLocationHelper.dotNetFrameworkSdkInstallKeyValueV20);

                // v3.0
                Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version30, vsVersion) );

                // v3.5
                ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version35, vsVersion).ShouldBe(InstallationFolder);

                // v4.0
                ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version40, vsVersion).ShouldBe(InstallationFolder);

                // v4.5
                ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version45, vsVersion).ShouldBe(InstallationFolder);
            }

            // v4.5.1
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version100) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version110) );
            ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version120).ShouldBe(InstallationFolder);
            ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version140).ShouldBe(InstallationFolder);

            // v4.6
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version100) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version110) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version120) );
            ToolLocationHelper.GetDotNetFrameworkSdkInstallKeyValue(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version140).ShouldBe(InstallationFolder);
        }

#if FEATURE_REGISTRY_SDKS
        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/995")]
#else
        [Fact (Skip="Registry SDKs not supported")]
#endif
        [PlatformSpecific(TestPlatforms.Windows)]
        public void GetPathToDotNetFrameworkSdk()
        {
            // Test out of range .net version.
            foreach (var vsVersion in EnumVisualStudioVersions())
            {
                Should.Throw<ArgumentException>( () => ToolLocationHelper.GetPathToDotNetFrameworkSdk((TargetDotNetFrameworkVersion)99, vsVersion) );
            }

            // Test out of range visual studio version.
            foreach (var dotNetVersion in EnumDotNetFrameworkVersions())
            {
                Should.Throw<ArgumentException>( () => ToolLocationHelper.GetPathToDotNetFrameworkSdk(dotNetVersion, (VisualStudioVersion)99) );
            }

            string pathToSdk35InstallRoot = Path.Combine(FrameworkLocationHelper.programFiles32, @"Microsoft SDKs\Windows\v7.0A\");
            string pathToSdkV4InstallRootOnVS10 = Path.Combine(FrameworkLocationHelper.programFiles32, @"Microsoft SDKs\Windows\v7.0A\");
            string pathToSdkV4InstallRootOnVS11 = Path.Combine(FrameworkLocationHelper.programFiles32, @"Microsoft SDKs\Windows\v8.0A\");

            // After uninstalling the 4.5 (Dev11) SDK, the Bootstrapper folder is left behind, so we can't
            // just check for the root folder.
            if (!Directory.Exists(Path.Combine(pathToSdkV4InstallRootOnVS11, "bin")))
            {
                // falls back to the Dev10 location (7.0A)
                pathToSdkV4InstallRootOnVS11 = pathToSdkV4InstallRootOnVS10;
            }

            string pathToSdkV4InstallRootOnVS12 = Path.Combine(FrameworkLocationHelper.programFiles32, @"Microsoft SDKs\Windows\v8.1A\");

            if (!Directory.Exists(pathToSdkV4InstallRootOnVS12))
            {
                // falls back to the Dev11 location (8.0A)
                pathToSdkV4InstallRootOnVS12 = pathToSdkV4InstallRootOnVS11;
            }

            string pathToSdkV4InstallRootOnVS14 = Path.Combine(FrameworkLocationHelper.programFiles32, @"Microsoft SDKs\Windows\v10.0A\");

            foreach (var vsVersion in EnumVisualStudioVersions())
            {
                // v1.1
                ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version11, vsVersion).ShouldBe(FrameworkLocationHelper.PathToDotNetFrameworkSdkV11);

                // v2.0
                ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version20, vsVersion).ShouldBe(FrameworkLocationHelper.PathToDotNetFrameworkSdkV20);

                // v3.0
                Should.Throw<ArgumentException>( () => ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version30, vsVersion) );

                // v3.5
                ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version35, vsVersion).ShouldBe(pathToSdk35InstallRoot);
            }

            // v4.0
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version40, VisualStudioVersion.Version100).ShouldBe(pathToSdkV4InstallRootOnVS10);
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version40, VisualStudioVersion.Version110).ShouldBe(pathToSdkV4InstallRootOnVS11);
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version40, VisualStudioVersion.Version120).ShouldBe(pathToSdkV4InstallRootOnVS12);
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version40, VisualStudioVersion.Version140).ShouldBe(pathToSdkV4InstallRootOnVS14);

            // v4.5
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version45, VisualStudioVersion.Version100).ShouldBe(pathToSdkV4InstallRootOnVS11);
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version45, VisualStudioVersion.Version110).ShouldBe(pathToSdkV4InstallRootOnVS11);
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version45, VisualStudioVersion.Version120).ShouldBe(pathToSdkV4InstallRootOnVS12);
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version45, VisualStudioVersion.Version140).ShouldBe(pathToSdkV4InstallRootOnVS14);

            // v4.5.1
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version100) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version110) );
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version120).ShouldBe(pathToSdkV4InstallRootOnVS12);
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version451, VisualStudioVersion.Version140).ShouldBe(pathToSdkV4InstallRootOnVS14);

            // v4.6
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version100) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version110) );
            Should.Throw<ArgumentException>( () => ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version120) );
            ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version46, VisualStudioVersion.Version140).ShouldBe(pathToSdkV4InstallRootOnVS14);
        }

#pragma warning disable 618 //The test below tests a deprecated API. We disable the warning for obsolete methods for this particular test
#if FEATURE_WIN32_REGISTRY
        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void GetPathToWindowsSdk()
        {
            // Test out of range .net version.
            foreach (var vsVersion in EnumVisualStudioVersions())
            {
                Should.Throw<ArgumentException>( () => ToolLocationHelper.GetPathToWindowsSdk((TargetDotNetFrameworkVersion)99, vsVersion) );
            }

            string pathToWindowsSdkV80 = GetRegistryValueHelper(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Microsoft SDKs\Windows\v8.0", "InstallationFolder");
            string pathToWindowsSdkV81 = GetRegistryValueHelper(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Microsoft SDKs\Windows\v8.1", "InstallationFolder");

            foreach (var vsVersion in EnumVisualStudioVersions().Concat(new[] { (VisualStudioVersion)99 }))
            {
                // v1.1, v2.0, v3.0, v3.5, v4.0
                foreach (var dotNetVersion in EnumDotNetFrameworkVersions().Where(v => v <= TargetDotNetFrameworkVersion.Version40))
                {
                    Should.Throw<ArgumentException>( () => ToolLocationHelper.GetPathToWindowsSdk(dotNetVersion, vsVersion) );
                }

                // v4.5
                ToolLocationHelper.GetPathToWindowsSdk(TargetDotNetFrameworkVersion.Version45, vsVersion).ShouldBe(pathToWindowsSdkV80);

                // v4.5.1
                ToolLocationHelper.GetPathToWindowsSdk(TargetDotNetFrameworkVersion.Version451, vsVersion).ShouldBe(pathToWindowsSdkV81);

                // v4.6
                ToolLocationHelper.GetPathToWindowsSdk(TargetDotNetFrameworkVersion.Version46, vsVersion).ShouldBe(pathToWindowsSdkV81);
            }
        }
#endif

#pragma warning restore 618

#if FEATURE_CODETASKFACTORY
        private static readonly string s_verifyToolsetAndToolLocationHelperProjectCommonContent = @"
                                    string currentInstallFolderLocation = null;

                                    using (RegistryKey baseKey = Registry.LocalMachine.OpenSubKey(""SOFTWARE\\Microsoft\\Microsoft SDKs\\Windows""))
                                    {
                                        if (baseKey != null)
                                        {
                                            object keyValue = baseKey.GetValue(""CurrentInstallFolder"");

                                            if (keyValue != null)
                                            {
                                                currentInstallFolderLocation = keyValue.ToString();
                                            }
                                        }
                                    }

                                    string sdk35ToolsPath = Sdk35ToolsPath == null ? Sdk35ToolsPath : Path.GetFullPath(Sdk35ToolsPath);
                                    string sdk40ToolsPath = Sdk40ToolsPath == null ? Sdk40ToolsPath : Path.GetFullPath(Sdk40ToolsPath);
                                    pathTo35Sdk = pathTo35Sdk == null ? pathTo35Sdk : Path.GetFullPath(pathTo35Sdk);
                                    pathTo40Sdk = pathTo40Sdk == null ? pathTo40Sdk : Path.GetFullPath(pathTo40Sdk);
                                    string currentInstall35Location = null;
                                    string currentInstall40Location = null;

                                    if (currentInstallFolderLocation != null)
                                    {
                                        currentInstall35Location = Path.GetFullPath(Path.Combine(currentInstallFolderLocation, ""bin\\""));
                                        currentInstall40Location = Path.GetFullPath(Path.Combine(currentInstallFolderLocation, ""bin\\NetFX 4.0 Tools\\""));
                                    }

                                    Log.LogMessage(MessageImportance.High, ""SDK35ToolsPath           = {0}"", Sdk35ToolsPath);
                                    Log.LogMessage(MessageImportance.High, ""SDK40ToolsPath           = {0}"", Sdk40ToolsPath);
                                    Log.LogMessage(MessageImportance.High, ""pathTo35Sdk              = {0}"", pathTo35Sdk);
                                    Log.LogMessage(MessageImportance.High, ""pathTo40Sdk              = {0}"", pathTo40Sdk);
                                    Log.LogMessage(MessageImportance.High, ""currentInstall35Location = {0}"", currentInstall35Location);
                                    Log.LogMessage(MessageImportance.High, ""currentInstall40Location = {0}"", currentInstall40Location);

                                    if (!String.Equals(sdk35ToolsPath, pathTo35Sdk, StringComparison.OrdinalIgnoreCase) &&
                                        (currentInstall35Location != null &&  /* this will be null on win8 express since 35 tools and this registry key will not be written, for vsultimate it is written*/
                                        !String.Equals(currentInstall35Location, pathTo35Sdk, StringComparison.OrdinalIgnoreCase))
                                       )
                                    {
                                        Log.LogError(""Sdk35ToolsPath is incorrect! Registry: {0}  ToolLocationHelper: {1}  CurrentInstallFolder: {2}"", sdk35ToolsPath, pathTo35Sdk, currentInstall35Location);
                                    }

                                    if (!String.Equals(sdk40ToolsPath, pathTo40Sdk, StringComparison.OrdinalIgnoreCase) &&
                                        (currentInstall40Location != null &&  /* this will be null on win8 express since 35 tools and this registry key will not be written, for vsultimate it is written*/
                                        !String.Equals(currentInstall40Location, pathTo40Sdk, StringComparison.OrdinalIgnoreCase))
                                       )
                                    {
                                        Log.LogError(""Sdk40ToolsPath is incorrect! Registry: {0}  ToolLocationHelper: {1}  CurrentInstallFolder: {2}"", sdk40ToolsPath, pathTo40Sdk, currentInstall40Location);
                                    }
  ";

        [Fact(Skip = "https://github.com/Microsoft/msbuild/issues/995")]
        public void VerifyToolsetAndToolLocationHelperAgree()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
                        <UsingTask TaskName='VerifySdkPaths' TaskFactory='CodeTaskFactory' AssemblyFile='$(MSBuildToolsPath)\Microsoft.Build.Tasks.Core.dll' >
                         <ParameterGroup>     
                             <Sdk35ToolsPath />
                             <Sdk40ToolsPath />
                             <WindowsSDK80Path />
                          </ParameterGroup>
                            <Task>
                                <Using Namespace='Microsoft.Win32'/>
                                <Code>
                                <![CDATA[
                                    string pathTo35Sdk = ToolLocationHelper.GetPathToDotNetFrameworkSdkFile(""gacutil.exe"", TargetDotNetFrameworkVersion.Version35);
                                    if (!String.IsNullOrEmpty(pathTo35Sdk))
                                    {
                                        pathTo35Sdk = Path.GetDirectoryName(pathTo35Sdk) + ""\\"";
                                    }

                                    string pathTo40Sdk = ToolLocationHelper.GetPathToDotNetFrameworkSdkFile(""gacutil.exe"", TargetDotNetFrameworkVersion.VersionLatest);

                                    if (!String.IsNullOrEmpty(pathTo40Sdk))
                                    {
                                        pathTo40Sdk = Path.GetDirectoryName(pathTo40Sdk) + ""\\"";
                                    }

                                    string pathTo81WinSDK = ToolLocationHelper.GetPathToWindowsSdk(TargetDotNetFrameworkVersion.VersionLatest, VisualStudioVersion.VersionLatest);" +
                                    s_verifyToolsetAndToolLocationHelperProjectCommonContent +
                                  @"if (!String.Equals(WindowsSDK80Path, pathTo81WinSDK, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Log.LogError(""WindowsSDK80Path is incorrect! Registry: {0}  ToolLocationHelper: {1}"", WindowsSDK80Path, pathTo81WinSDK);
                                    }

                                    return !Log.HasLoggedErrors;
                                ]]>
                              </Code>
                            </Task>
                        </UsingTask>
                        <Target Name='Build'>
                            <VerifySdkPaths Sdk35ToolsPath='$(Sdk35ToolsPath)' Sdk40ToolsPath='$(Sdk40ToolsPath)' WindowsSDK80Path='$(WindowsSDK80Path)' />
                        </Target>
                    </Project>");

            ILogger logger = new MockLogger(_output);
            ProjectCollection collection = new ProjectCollection();
            Project p = ObjectModelHelpers.CreateInMemoryProject(collection, projectContents, logger);

            bool success = p.Build(logger);

            success.ShouldBeTrue(); // "Build Failed.  See Std Out for details."
        }

        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyToolsetAndToolLocationHelperAgreeWhenVisualStudioVersionIsEmpty()
        {
            string projectContents = @"
                    <Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003' ToolsVersion='4.0'>
                        <UsingTask TaskName='VerifySdkPaths' TaskFactory='CodeTaskFactory' AssemblyName='Microsoft.Build.Tasks.v4.0, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a' >
                         <ParameterGroup>     
                             <Sdk35ToolsPath />
                             <Sdk40ToolsPath />
                             <WindowsSDK80Path />
                          </ParameterGroup>
                            <Task>
                                <Using Namespace='Microsoft.Win32'/>
                                <Code>
                                <![CDATA[
                                    string pathTo35Sdk = ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version35);
                                    string pathTo40Sdk = ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version40);

                                    pathTo35Sdk = pathTo35Sdk == null ? pathTo35Sdk : Path.Combine(pathTo35Sdk, ""bin\\"");
                                    pathTo40Sdk = pathTo40Sdk == null ? pathTo40Sdk : Path.Combine(pathTo40Sdk, ""bin\\NetFX 4.0 Tools\\"");" +
                                    s_verifyToolsetAndToolLocationHelperProjectCommonContent +
                                  @"return !Log.HasLoggedErrors;
                                ]]>
                              </Code>
                            </Task>
                        </UsingTask>
                        <Target Name='Build'>
                            <VerifySdkPaths Sdk35ToolsPath='$(Sdk35ToolsPath)' Sdk40ToolsPath='$(Sdk40ToolsPath)' WindowsSDK80Path='$(WindowsSDK80Path)' />
                        </Target>
                    </Project>";

            ILogger logger = new MockLogger(_output);

            ProjectCollection collection = new ProjectCollection();
            Project p = ObjectModelHelpers.CreateInMemoryProject(collection, projectContents, logger, "4.0");

            bool success = p.Build(logger);

            success.ShouldBeTrue(); // "Build Failed.  See Std Out for details."
        }

        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyToolsetAndToolLocationHelperAgreeWhenVisualStudioVersionIs10()
        {
            string projectContents = @"
                    <Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003' ToolsVersion='4.0'>
                        <UsingTask TaskName='VerifySdkPaths' TaskFactory='CodeTaskFactory' AssemblyName='Microsoft.Build.Tasks.v4.0, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a' >
                         <ParameterGroup>     
                             <Sdk35ToolsPath />
                             <Sdk40ToolsPath />
                             <WindowsSDK80Path />
                          </ParameterGroup>
                            <Task>
                                <Using Namespace='Microsoft.Win32'/>
                                <Code>
                                <![CDATA[
                                    string pathTo35Sdk = ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version35, VisualStudioVersion.Version100);
                                    string pathTo40Sdk = ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version40, VisualStudioVersion.Version100);

                                    pathTo35Sdk = pathTo35Sdk == null ? pathTo35Sdk : Path.Combine(pathTo35Sdk, ""bin\\"");
                                    pathTo40Sdk = pathTo40Sdk == null ? pathTo40Sdk : Path.Combine(pathTo40Sdk, ""bin\\NetFX 4.0 Tools\\"");" +
                                    s_verifyToolsetAndToolLocationHelperProjectCommonContent +
                                  @"return !Log.HasLoggedErrors;
                                ]]>
                              </Code>
                            </Task>
                        </UsingTask>
                        <Target Name='Build'>
                            <VerifySdkPaths Sdk35ToolsPath='$(Sdk35ToolsPath)' Sdk40ToolsPath='$(Sdk40ToolsPath)' WindowsSDK80Path='$(WindowsSDK80Path)' />
                        </Target>
                    </Project>";

            ILogger logger = new MockLogger(_output);
            IDictionary<string, string> globalProperties = new Dictionary<string, string>();
            globalProperties.Add("VisualStudioVersion", "10.0");

            ProjectCollection collection = new ProjectCollection(globalProperties);
            Project p = ObjectModelHelpers.CreateInMemoryProject(collection, projectContents, logger, "4.0");

            bool success = p.Build(logger);

            success.ShouldBeTrue(); // "Build Failed.  See Std Out for details."
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void VerifyToolsetAndToolLocationHelperAgreeWhenVisualStudioVersionIs11()
        {
            string projectContents = @"
                    <Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003' ToolsVersion='4.0'>
                        <UsingTask TaskName='VerifySdkPaths' TaskFactory='CodeTaskFactory' AssemblyName='Microsoft.Build.Tasks.v4.0, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a' >
                         <ParameterGroup>     
                             <Sdk35ToolsPath />
                             <Sdk40ToolsPath />
                             <WindowsSDK80Path />
                          </ParameterGroup>
                            <Task>
                                <Using Namespace='Microsoft.Win32'/>
                                <Code>
                                <![CDATA[
                                    string pathTo35Sdk = ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version35, VisualStudioVersion.Version110);
                                    string pathTo40Sdk = ToolLocationHelper.GetPathToDotNetFrameworkSdk(TargetDotNetFrameworkVersion.Version40, VisualStudioVersion.Version110);
                                    string pathTo80WinSDK = ToolLocationHelper.GetPathToWindowsSdk(TargetDotNetFrameworkVersion.Version45, VisualStudioVersion.Version110);

                                    pathTo35Sdk = pathTo35Sdk == null ? pathTo35Sdk : Path.Combine(pathTo35Sdk, ""bin\\"");
                                    pathTo40Sdk = pathTo40Sdk == null ? pathTo40Sdk : Path.Combine(pathTo40Sdk, ""bin\\NetFX 4.0 Tools\\"");" +
                                    s_verifyToolsetAndToolLocationHelperProjectCommonContent +
                                   @"if (String.IsNullOrEmpty(WindowsSDK80Path))
                                    {
                                        Log.LogWarning(""WindowsSDK80Path is empty, which is technically not correct, but we're letting it slide for now because the OTG build won't have the updated registry for a while.  Make sure we don't see this warning on PURITs runs, though!"");
                                    }
                                    else if (!String.Equals(WindowsSDK80Path, pathTo80WinSDK, StringComparison.OrdinalIgnoreCase))
                                    {
                                        Log.LogError(""WindowsSDK80Path is incorrect! Registry: {0}  ToolLocationHelper: {1}"", WindowsSDK80Path, pathTo80WinSDK);
                                    }

                                    return !Log.HasLoggedErrors;
                                ]]>
                             </Code>
                            </Task>
                        </UsingTask>
                        <Target Name='Build'>
                            <VerifySdkPaths Sdk35ToolsPath='$(Sdk35ToolsPath)' Sdk40ToolsPath='$(Sdk40ToolsPath)' WindowsSDK80Path='$(WindowsSDK80Path)' />
                        </Target>
                    </Project>";

            ILogger logger = new MockLogger(_output);
            IDictionary<string, string> globalProperties = new Dictionary<string, string>();
            globalProperties.Add("VisualStudioVersion", "11.0");

            ProjectCollection collection = new ProjectCollection(globalProperties);
            Project p = ObjectModelHelpers.CreateInMemoryProject(collection, projectContents, logger, "4.0");

            bool success = p.Build(logger);

            success.ShouldBeTrue(); // "Build Failed.  See Std Out for details."
        }
#endif

        #region GenerateReferenceAssemblyPath
        [Fact]
        public void GenerateReferencAssemblyPathAllElements()
        {
            string targetFrameworkRootPath = NativeMethodsShared.IsWindows
                                                 ? "c:\\Program Files\\Reference Assemblies\\Microsoft\\Framework"
                                                 : "/usr/lib";
            string targetFrameworkIdentifier = "Compact Framework";
            Version targetFrameworkVersion = new Version("1.0");
            string targetFrameworkProfile = "PocketPC";

            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning(targetFrameworkIdentifier, targetFrameworkVersion, targetFrameworkProfile);

            string expectedPath = Path.Combine(targetFrameworkRootPath, targetFrameworkIdentifier);
            expectedPath = Path.Combine(expectedPath, "v" + targetFrameworkVersion);
            expectedPath = Path.Combine(expectedPath, "Profile");
            expectedPath = Path.Combine(expectedPath, targetFrameworkProfile);

            string path = FrameworkLocationHelper.GenerateReferenceAssemblyPath(targetFrameworkRootPath, frameworkName);
            path.ShouldBe(expectedPath, StringCompareShould.IgnoreCase);
        }

        [Fact]
        public void GenerateReferencAssemblyPathNoProfile()
        {
            string targetFrameworkRootPath = NativeMethodsShared.IsWindows
                                                 ? "c:\\Program Files\\Reference Assemblies\\Microsoft\\Framework"
                                                 : "/usr/lib";
            string targetFrameworkIdentifier = "Compact Framework";
            Version targetFrameworkVersion = new Version("1.0");
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning(targetFrameworkIdentifier, targetFrameworkVersion, string.Empty);
            string expectedPath = Path.Combine(targetFrameworkRootPath, targetFrameworkIdentifier);
            expectedPath = Path.Combine(expectedPath, "v" + targetFrameworkVersion);

            string path = FrameworkLocationHelper.GenerateReferenceAssemblyPath(targetFrameworkRootPath, frameworkName);
            path.ShouldBe(expectedPath, StringCompareShould.IgnoreCase);
        }

        /// <summary>
        /// Make sure if the profile has invalid chars which would be used as part of path generation that we get an InvalidOperationException
        /// which indicates there was a problem generating the reference assembly path.
        /// </summary>
        [Fact]
        public void GenerateReferencAssemblyInvalidProfile()
        {
            Should.Throw<InvalidOperationException>(() =>
            {
                string targetFrameworkRootPath = NativeMethodsShared.IsWindows
                                                     ? "c:\\Program Files\\Reference Assemblies\\Microsoft\\Framework"
                                                     : "/usr/lib";
                string targetFrameworkIdentifier = "Compact Framework";
                Version targetFrameworkVersion = new Version("1.0");
                string targetFrameworkProfile = "PocketPC" + new string(Path.GetInvalidFileNameChars());

                FrameworkNameVersioning frameworkName = new FrameworkNameVersioning(targetFrameworkIdentifier, targetFrameworkVersion, targetFrameworkProfile);

                FrameworkLocationHelper.GenerateReferenceAssemblyPath(targetFrameworkRootPath, frameworkName);
            }
           );
        }
        /// <summary>
        /// Make sure if the identifier has invalid chars which would be used as part of path generation that we get an InvalidOperationException
        /// which indicates there was a problem generating the reference assembly path.
        /// </summary>
        [Fact]
        public void GenerateReferencAssemblyInvalidIdentifier()
        {
            Should.Throw<InvalidOperationException>(() =>
            {
                string targetFrameworkRootPath = NativeMethodsShared.IsWindows
                                                     ? "c:\\Program Files\\Reference Assemblies\\Microsoft\\Framework"
                                                     : "/usr/lib";
                string targetFrameworkIdentifier = "Compact Framework" + new string(Path.GetInvalidFileNameChars());
                Version targetFrameworkVersion = new Version("1.0");
                string targetFrameworkProfile = "PocketPC";

                FrameworkNameVersioning frameworkName = new FrameworkNameVersioning(targetFrameworkIdentifier, targetFrameworkVersion, targetFrameworkProfile);

                FrameworkLocationHelper.GenerateReferenceAssemblyPath(targetFrameworkRootPath, frameworkName);
            }
           );
        }
        /// <summary>
        /// Make sure if the moniker and the root make a too long path that an InvalidOperationException is raised
        /// which indicates there was a problem generating the reference assembly path.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp)]
        public void GenerateReferencAssemblyPathTooLong()
        {
            Should.Throw<InvalidOperationException>(() =>
            {
                string pathTooLong = new string('a', 500);

                string targetFrameworkRootPath = NativeMethodsShared.IsWindows
                                                     ? "c:\\Program Files\\Reference Assemblies\\Microsoft\\Framework"
                                                     : "/usr/lib";
                string targetFrameworkIdentifier = "Compact Framework" + pathTooLong;
                Version targetFrameworkVersion = new Version("1.0");
                string targetFrameworkProfile = "PocketPC";

                FrameworkNameVersioning frameworkName = new FrameworkNameVersioning(targetFrameworkIdentifier, targetFrameworkVersion, targetFrameworkProfile);

                FrameworkLocationHelper.GenerateReferenceAssemblyPath(targetFrameworkRootPath, frameworkName);
            }
           );
        }
        #endregion

        #region ChainReferenceAssemblyPath

        /// <summary>
        /// Verify the chaining method returns a null if there is no redist list file for the framework we are trying to chain with. This is ok because the lack of a redist list file means we
        /// do not have anything to chain with.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void ChainReferenceAssembliesRedistExistsNoRedistList()
        {
            string path = ToolLocationHelper.ChainReferenceAssemblyPath(@"PathDoesNotExistSoICannotChain");
            path.ShouldBeNull(); // " Expected the path to be null when the path to the FrameworkList.xml does not exist"
        }

        /// <summary>
        /// Verify we do not hang, crash, go on forever if there is a circular reference with the include frameworks. What should happen is
        /// we should notice that we have already chained to a given framework and not try and chain with it again.
        /// </summary>
        [Fact]
        public void ChainReferenceAssembliesRedistExistsCircularRefernce()
        {
            string redistString41 = "<FileList Redist='Random' IncludeFramework='v4.0'>" +
                                     "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                  "</FileList >";

            string redistString40 = "<FileList Redist='Random'>" +
                                       "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                    "</FileList >";

            string tempDirectory = Path.Combine(Path.GetTempPath(), "ChainReferenceAssembliesRedistExistsChain");

            string redist41Directory = Path.Combine(tempDirectory, "v4.1", "RedistList") + Path.DirectorySeparatorChar;
            string redist41 = Path.Combine(redist41Directory, "FrameworkList.xml");
            string redist40Directory = Path.Combine(tempDirectory, "v4.0", "RedistList") + Path.DirectorySeparatorChar;
            string redist40 = Path.Combine(redist40Directory, "FrameworkList.xml");
            try
            {
                Directory.CreateDirectory(redist41Directory);
                Directory.CreateDirectory(redist40Directory);
                File.WriteAllText(redist40, redistString40);
                File.WriteAllText(redist41, redistString41);

                string path = ToolLocationHelper.ChainReferenceAssemblyPath(Path.Combine(tempDirectory, "v4.1"));

                string expectedChainedPath = Path.Combine(tempDirectory, "v4.0");
                expectedChainedPath.ShouldBe(path, StringCompareShould.IgnoreCase);
            }
            finally
            {
                if (Directory.Exists(redist40Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(redist40Directory, true);
                }

                if (Directory.Exists(redist41Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(redist41Directory, true);
                }
            }
        }

        /// <summary>
        /// Verify the case where there is no Included framework attribute, there should be no errors and we should continue on as if there were no further framework chained with the current one
        /// </summary>
        [Fact]
        public void ChainReferenceAssembliesRedistExistsNoInclude()
        {
            string redistString41 = "<FileList Redist='Random'>" +
                                        "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                     "</FileList >";

            string tempDirectory = Path.Combine(Path.GetTempPath(), "ChainReferenceAssembliesRedistExistsNoInclude");

            string redist41Directory = Path.Combine(tempDirectory, "v4.1", "RedistList") + Path.DirectorySeparatorChar;
            string redist41 = Path.Combine(redist41Directory, "FrameworkList.xml");
            try
            {
                Directory.CreateDirectory(redist41Directory);
                File.WriteAllText(redist41, redistString41);
                string path = ToolLocationHelper.ChainReferenceAssemblyPath(Path.Combine(tempDirectory, "v4.1"));
                path.ShouldBe(string.Empty); // "Expected the path to be empty"
            }
            finally
            {
                if (Directory.Exists(redist41Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(redist41Directory, true);
                }
            }
        }

        /// <summary>
        /// Verify the case where the include framework is empty, this is ok, we should error but should just continue on as if there was no chaining of the redist list file.
        /// </summary>
        [Fact]
        public void ChainReferenceAssembliesRedistExistsEmptyInclude()
        {
            string redistString41 = "<FileList Redist='Random' IncludeFramework=''>" +
                                        "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                     "</FileList >";

            string tempDirectory = Path.Combine(Path.GetTempPath(), "ChainReferenceAssembliesRedistExistsNoInclude");

            string redist41Directory = Path.Combine(tempDirectory, "v4.1", "RedistList") + Path.DirectorySeparatorChar;
            string redist41 = Path.Combine(redist41Directory, "FrameworkList.xml");
            try
            {
                Directory.CreateDirectory(redist41Directory);
                File.WriteAllText(redist41, redistString41);
                string path = ToolLocationHelper.ChainReferenceAssemblyPath(Path.Combine(tempDirectory, "v4.1"));
                path.ShouldBe(string.Empty); // "Expected the path to be empty"
            }
            finally
            {
                if (Directory.Exists(redist41Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(redist41Directory, true);
                }
            }
        }

        /// <summary>
        /// Verify the case where the redist is a valid xml file but does not have the FileListElement, this is to make sure we do not crash or get an exception if the FileList element cannot be found
        /// </summary>
        [Fact]
        public void ChainReferenceAssembliesRedistExistsNoFileList()
        {
            string redistString41 = "<FileListNOT Redist='Random'>" +
                                        "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                     "</FileListNOT >";

            string tempDirectory = Path.Combine(Path.GetTempPath(), "ChainReferenceAssembliesRedistExistsNoFileList");

            string redist41Directory = Path.Combine(tempDirectory, "v4.1", "RedistList") + Path.DirectorySeparatorChar;
            string redist41 = Path.Combine(redist41Directory, "FrameworkList.xml");
            try
            {
                Directory.CreateDirectory(redist41Directory);
                File.WriteAllText(redist41, redistString41);
                string path = ToolLocationHelper.ChainReferenceAssemblyPath(Path.Combine(tempDirectory, "v4.1"));
                path.ShouldBe(string.Empty); // "Expected the path to be empty"
            }
            finally
            {
                if (Directory.Exists(redist41Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(redist41Directory, true);
                }
            }
        }

        /// <summary>
        /// Make sure we get the correct exception when there is no xml in the redist list file
        /// </summary>
        [Fact]
        public void ChainReferenceAssembliesRedistExistsBadFile()
        {
            Should.Throw<InvalidOperationException>(() =>
            {
                string redistString40 = "GARBAGE";
                string tempDirectory = Path.Combine(Path.GetTempPath(), "ChainReferenceAssembliesRedistExistsBadFile");

                string redist40Directory = Path.Combine(tempDirectory, "v4.0", "RedistList") + Path.DirectorySeparatorChar;
                string redist40 = Path.Combine(redist40Directory, "FrameworkList.xml");
                try
                {
                    Directory.CreateDirectory(redist40Directory);
                    File.WriteAllText(redist40, redistString40);

                    string path = ToolLocationHelper.ChainReferenceAssemblyPath(Path.Combine(tempDirectory, "v4.0"));
                    path.ShouldBeNull(); // "Expected the path to be null"
                }
                finally
                {
                    if (Directory.Exists(redist40Directory))
                    {
                        FileUtilities.DeleteWithoutTrailingBackslash(redist40Directory, true);
                    }
                }
            }
           );
        }
        /// <summary>
        /// Make sure we get the correct exception when the xml file points to an included framework which does not exist.
        /// </summary>
        [Fact]
        public void ChainReferenceAssembliesRedistPointsToInvalidInclude()
        {
            string redistString41 = "<FileList Redist='Random' IncludeFramework='IDontExist'>" +
                                              "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                           "</FileList>";

            string tempDirectory = Path.Combine(Path.GetTempPath(), "ChainReferenceAssembliesRedistPointsToInvalidInclude");

            string redist41Directory = Path.Combine(tempDirectory, "v4.1", "RedistList") + Path.DirectorySeparatorChar;
            string redist41 = Path.Combine(redist41Directory, "FrameworkList.xml");
            string tempDirectoryPath = Path.Combine(tempDirectory, "v4.1");
            try
            {
                Directory.CreateDirectory(redist41Directory);
                File.WriteAllText(redist41, redistString41);

                string path = ToolLocationHelper.ChainReferenceAssemblyPath(tempDirectoryPath);
                path.ShouldBeNull();
            }
            finally
            {
                if (Directory.Exists(redist41Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(redist41Directory, true);
                }
            }
        }

        /// <summary>
        /// Make sure we get the correct exception when the xml file points to an included framework which has invalid path chars.
        /// </summary>
        [Fact]
        public void ChainReferenceAssembliesRedistInvalidPathChars()
        {
            Should.Throw<InvalidOperationException>(() =>
            {
                char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

                string redistString41 = "<FileList Redist='Random' IncludeFramework='" + new string(invalidFileNameChars) + "'>" +
                                                  "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                               "</FileList>";

                string tempDirectory = Path.Combine(Path.GetTempPath(), "ChainReferenceAssembliesRedistInvalidPathChars");

                string redist41Directory = Path.Combine(tempDirectory, "v4.1", "RedistList") + Path.DirectorySeparatorChar;
                string redist41 = Path.Combine(redist41Directory, "FrameworkList.xml");
                string tempDirectoryPath = Path.Combine(tempDirectory, "v4.1");
                try
                {
                    Directory.CreateDirectory(redist41Directory);
                    File.WriteAllText(redist41, redistString41);

                    ToolLocationHelper.ChainReferenceAssemblyPath(tempDirectoryPath);
                }
                finally
                {
                    if (Directory.Exists(redist41Directory))
                    {
                        FileUtilities.DeleteWithoutTrailingBackslash(redist41Directory, true);
                    }
                }
            }
           );
        }
        /// <summary>
        /// Make sure we get the correct exception when the xml file points to an included framework which has invalid path chars.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp)]
        public void ChainReferenceAssembliesRedistPathTooLong()
        {
            Should.Throw<InvalidOperationException>(() =>
            {
                string tooLong = new string('a', 500);
                string redistString41 = "<FileList Redist='Random' IncludeFramework='" + tooLong + "'>" +
                                                  "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                               "</FileList>";

                string tempDirectory = Path.Combine(Path.GetTempPath(), "ChainReferenceAssembliesRedistPathTooLong");

                string redist41Directory = Path.Combine(tempDirectory, "v4.1", "RedistList") + Path.DirectorySeparatorChar;
                string redist41 = Path.Combine(redist41Directory, "FrameworkList.xml");
                string tempDirectoryPath = Path.Combine(tempDirectory, "v4.1");
                try
                {
                    Directory.CreateDirectory(redist41Directory);
                    File.WriteAllText(redist41, redistString41);

                    ToolLocationHelper.ChainReferenceAssemblyPath(tempDirectoryPath);
                }
                finally
                {
                    if (Directory.Exists(redist41Directory))
                    {
                        FileUtilities.DeleteWithoutTrailingBackslash(redist41Directory, true);
                    }
                }
            }
           );
        }
        #endregion

        #region GetReferenceAssemblyPathWithRootPath

        /// <summary>
        /// Verify the case where we are chaining redist lists and they are properly formatted
        /// </summary>
        [Fact]
        public void GetPathToReferenceAssembliesWithRootGoodWithChain()
        {
            string redistString41 = "<FileList Redist='Random' IncludeFramework='v4.0'>" +
                                     "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                  "</FileList >";

            string redistString40 = "<FileList Redist='Random' IncludeFramework='v3.9'>" +
                                       "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                    "</FileList >";

            string redistString39 = "<FileList Redist='Random'>" +
                                         "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                      "</FileList >";

            string tempDirectory = Path.Combine(Path.GetTempPath(), "GetPathToReferenceAssembliesWithRootGoodWithChain");

            string framework41Directory = Path.Combine(tempDirectory, "MyFramework", "v4.1") + Path.DirectorySeparatorChar;
            string framework41redistDirectory = Path.Combine(framework41Directory, "RedistList");
            string framework41RedistList = Path.Combine(framework41redistDirectory, "FrameworkList.xml");

            string framework40Directory = Path.Combine(tempDirectory, "MyFramework", "v4.0") + Path.DirectorySeparatorChar;
            string framework40redistDirectory = Path.Combine(framework40Directory, "RedistList");
            string framework40RedistList = Path.Combine(framework40redistDirectory, "FrameworkList.xml");

            string framework39Directory = Path.Combine(tempDirectory, "MyFramework", "v3.9") + Path.DirectorySeparatorChar;
            string framework39redistDirectory = Path.Combine(framework39Directory, "RedistList");
            string framework39RedistList = Path.Combine(framework39redistDirectory, "FrameworkList.xml");

            try
            {
                Directory.CreateDirectory(framework41redistDirectory);
                Directory.CreateDirectory(framework40redistDirectory);
                Directory.CreateDirectory(framework39redistDirectory);

                File.WriteAllText(framework39RedistList, redistString39);
                File.WriteAllText(framework40RedistList, redistString40);
                File.WriteAllText(framework41RedistList, redistString41);

                FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("MyFramework", new Version("4.1"));
                IList<string> directories = ToolLocationHelper.GetPathToReferenceAssemblies(tempDirectory, frameworkName);

                directories.Count.ShouldBe(3); // "Expected the method to return three paths."
                directories[0].ShouldBe(framework41Directory, StringCompareShould.IgnoreCase);
                directories[1].ShouldBe(framework40Directory, StringCompareShould.IgnoreCase);
                directories[2].ShouldBe(framework39Directory, StringCompareShould.IgnoreCase);
            }
            finally
            {
                if (Directory.Exists(framework41Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(framework41Directory, true);
                }

                if (Directory.Exists(framework40Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(framework40Directory, true);
                }

                if (Directory.Exists(framework39Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(framework39Directory, true);
                }
            }
        }

        /// <summary>
        /// Verify the correct display name returned
        /// </summary>
        [Fact]
        public void DisplayNameGeneration()
        {
            string redistString40 = "<FileList Redist='Random' Name='MyFramework 4.0' >" +
                                       "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                    "</FileList >";

            string redistString39 = "<FileList Redist='Random'>" +
                                         "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                      "</FileList >";

            string tempDirectory = Path.Combine(Path.GetTempPath(), "DisplayNameGeneration");

            string framework40Directory = Path.Combine(tempDirectory, "MyFramework", "v4.0")
                                          + Path.DirectorySeparatorChar;
            string framework40redistDirectory = Path.Combine(framework40Directory, "RedistList");
            string framework40RedistList = Path.Combine(framework40redistDirectory, "FrameworkList.xml");

            string framework39Directory =
                Path.Combine(tempDirectory, "MyFramework", "v3.9", "Profile", "Client");
            string framework39redistDirectory = Path.Combine(framework39Directory, "RedistList");
            string framework39RedistList = Path.Combine(framework39redistDirectory, "FrameworkList.xml");

            try
            {
                Directory.CreateDirectory(framework40redistDirectory);
                Directory.CreateDirectory(framework39redistDirectory);

                File.WriteAllText(framework39RedistList, redistString39);
                File.WriteAllText(framework40RedistList, redistString40);

                FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("MyFramework", new Version("4.0"));
                string displayName40 = ToolLocationHelper.GetDisplayNameForTargetFrameworkDirectory(framework40Directory, frameworkName);

                frameworkName = new FrameworkNameVersioning("MyFramework", new Version("3.9"), "Client");
                string displayName39 = ToolLocationHelper.GetDisplayNameForTargetFrameworkDirectory(framework39Directory, frameworkName);
                displayName40.ShouldBe("MyFramework 4.0", StringCompareShould.IgnoreCase);
                displayName39.ShouldBe("MyFramework v3.9 Client", StringCompareShould.IgnoreCase);
            }
            finally
            {
                if (Directory.Exists(framework40Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(framework40Directory, true);
                }

                if (Directory.Exists(framework39Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(framework39Directory, true);
                }
            }
        }

        /// <summary>
        /// Make sure we do not crash if there is a circular reference in the redist lists, we should only have a path in our reference assembly list once.
        ///
        /// </summary>
        [Fact]
        public void GetPathToReferenceAssembliesWithRootCircularReference()
        {
            string redistString41 = "<FileList Redist='Random' IncludeFramework='v4.0'>" +
                                     "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                  "</FileList >";

            string redistString40 = "<FileList Redist='Random' IncludeFramework='v4.1'>" +
                                       "<File AssemblyName='System' Version='4.0.0.0' PublicKeyToken='b77a5c561934e089' Culture='neutral' ProcessorArchitecture='MSIL' FileVersion='4.0.0.0' InGAC='false' />" +
                                    "</FileList >";

            string tempDirectory = Path.Combine(Path.GetTempPath(), "GetPathToReferenceAssembliesWithRootCircularReference");

            string framework41Directory = Path.Combine(tempDirectory, "MyFramework", "v4.1")
                                          + Path.DirectorySeparatorChar;
            string framework41redistDirectory = Path.Combine(framework41Directory, "RedistList");
            string framework41RedistList = Path.Combine(framework41redistDirectory, "FrameworkList.xml");

            string framework40Directory = Path.Combine(tempDirectory, "MyFramework", "v4.0")
                                          + Path.DirectorySeparatorChar;
            string framework40redistDirectory = Path.Combine(framework40Directory, "RedistList");
            string framework40RedistList = Path.Combine(framework40redistDirectory, "FrameworkList.xml");

            try
            {
                Directory.CreateDirectory(framework41redistDirectory);
                Directory.CreateDirectory(framework40redistDirectory);

                File.WriteAllText(framework40RedistList, redistString40);
                File.WriteAllText(framework41RedistList, redistString41);

                FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("MyFramework", new Version("4.1"));
                IList<string> directories = ToolLocationHelper.GetPathToReferenceAssemblies(tempDirectory, frameworkName);

                directories.Count.ShouldBe(2); // "Expected the method to return two paths."
                directories[0].ShouldBe(framework41Directory, StringCompareShould.IgnoreCase);
                directories[1].ShouldBe(framework40Directory, StringCompareShould.IgnoreCase);
            }
            finally
            {
                if (Directory.Exists(framework41Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(framework41Directory, true);
                }

                if (Directory.Exists(framework40Directory))
                {
                    FileUtilities.DeleteWithoutTrailingBackslash(framework40Directory, true);
                }
            }
        }

        /// <summary>
        /// Test the case where the root path is a string but the framework name is null.
        /// We should expect the correct argument null exception
        /// </summary>
        [Fact]
        public void GetPathToReferenceAssembliesNullFrameworkName()
        {
            Should.Throw<ArgumentNullException>(() =>
            {
                ToolLocationHelper.GetPathToReferenceAssemblies("Not Null String", (FrameworkNameVersioning)null);
            }
           );
        }
        /// <summary>
        /// Make sure we get the correct exception when both parameters are null
        /// </summary>
        [Fact]
        public void GetPathToReferenceAssembliesNullArgumentNameandFrameworkName()
        {
            Should.Throw<ArgumentNullException>(() =>
            {
                ToolLocationHelper.GetPathToReferenceAssemblies(null, (FrameworkNameVersioning)null);
            }
           );
        }
        /// <summary>
        /// Make sure we get the correct exception when the root is null but the frameworkname is not null
        /// </summary>
        [Fact]
        public void GetPathToReferenceAssembliesNullArgumentGoodFrameworkNameNullRoot()
        {
            Should.Throw<ArgumentNullException>(() =>
            {
                FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Ident", new Version("2.0"));
                ToolLocationHelper.GetPathToReferenceAssemblies(null, frameworkName);
            }
           );
        }
        /// <summary>
        /// Make sure we get the correct exception when the root is null but the frameworkname is not null
        /// With no framework name we cannot generate the path
        /// </summary>
        [Fact]
        public void GetPathToReferenceAssembliesNullArgumentGoodFrameworkNameEmptyRoot()
        {
            Should.Throw<ArgumentException>(() =>
            {
                FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Ident", new Version("2.0"));
                ToolLocationHelper.GetPathToReferenceAssemblies(string.Empty, frameworkName);
            }
           );
        }
        /// <summary>
        /// Make sure we get the correct exception when the root is null but the frameworkname is not empty to make sure we cover the different input cases
        /// With no root we cannot properly generate the path.
        /// </summary>
        [Fact]
        public void GetPathToReferenceAssembliesNullArgumentGoodFrameworkNameEmptyRoot2()
        {
            Should.Throw<ArgumentException>(() =>
            {
                FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Ident", new Version("2.0"));
                ToolLocationHelper.GetPathToReferenceAssemblies(string.Empty, frameworkName);
            }
           );
        }
        #endregion

        #region GetReferenceAssemblyPathWithDefaultRoot

        /// <summary>
        /// Test the case where the method which only takes in a FrameworkName will throw an exception when
        /// the input is null since a null framework name is not useful
        /// </summary>
        [Fact]
        public void GetPathToReferenceAssembliesDefaultLocationNullFrameworkName()
        {
            Should.Throw<ArgumentNullException>(() =>
            {
                ToolLocationHelper.GetPathToReferenceAssemblies((FrameworkNameVersioning)null);
            }
           );
        }
        /// <summary>
        /// Verify the method correctly returns the 4.5 reference assembly location information if .net 4.5 and
        /// its corresponding reference assemblies are installed.
        /// If they are not installed, the test should be ignored.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void GetPathToReferenceAssembliesDefaultLocation45()
        {
            if (ToolLocationHelper.GetPathToDotNetFrameworkReferenceAssemblies(TargetDotNetFrameworkVersion.Version45) != null)
            {
                FrameworkNameVersioning frameworkName = new FrameworkNameVersioning(".NETFramework", new Version("4.5"));
                IList<string> directories = ToolLocationHelper.GetPathToReferenceAssemblies(frameworkName);
                directories.Count.ShouldBe(1); // "Expected the method to return one path."

                string referenceAssemblyPath = ToolLocationHelper.GetPathToDotNetFrameworkReferenceAssemblies(TargetDotNetFrameworkVersion.Version45);
                directories[0].ShouldBe(referenceAssemblyPath, StringCompareShould.IgnoreCase);
            }
            // else
            // "Ignored because v4.5 did not seem to be installed"
        }

        /// <summary>
        /// Test the case where the framework requested does not exist. Since we do an existence check before returning the path this non existent path should return an empty list
        /// </summary>
        [Fact]
        public void GetPathToReferenceAssembliesDefaultLocation99()
        {
            string targetFrameworkIdentifier = ".Net Framework";
            Version targetFrameworkVersion = new Version("99.99");

            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning(targetFrameworkIdentifier, targetFrameworkVersion, string.Empty);

            IList<string> directories = ToolLocationHelper.GetPathToReferenceAssemblies(frameworkName);
            directories.Count.ShouldBe(0); // "Expected the method to return no paths."
        }

        /// <summary>
        /// Make sure we choose the correct path for program files based on the operating system
        /// </summary>
        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void TestGenerateProgramFiles32()
        {
            Environment.SpecialFolder folder = Environment.Is64BitOperatingSystem ? Environment.SpecialFolder.ProgramFilesX86 : Environment.SpecialFolder.ProgramFiles;
            string programFilesX86 = Environment.GetFolderPath(folder);
            string result = FrameworkLocationHelper.GenerateProgramFiles32();

            // 32-bit OS: "Expected to use program files"
            // 64-bit OS: "Expected to use program files x86"
            result.ShouldBe(programFilesX86, StringCompareShould.IgnoreCase);
        }

        /// <summary>
        /// Verify we get the correct reference assembly path out of the framework location helper
        /// </summary>
        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)] // "No ProgramFiles known location outside Windows"
        public void TestGeneratedReferenceAssemblyPath()
        {
            string programFiles32 = FrameworkLocationHelper.GenerateProgramFiles32();
            string referenceAssemblyRoot = FrameworkLocationHelper.GenerateProgramFilesReferenceAssemblyRoot();
            string pathToCombineWith = "Reference Assemblies\\Microsoft\\Framework";
            string combinedPath = Path.Combine(programFiles32, pathToCombineWith);
            string fullPath = Path.GetFullPath(combinedPath);

            referenceAssemblyRoot.ShouldBe(fullPath, StringCompareShould.IgnoreCase);
        }

        #endregion

        #region HandleLegacyFrameworks

        /// <summary>
        /// Verify when 20 is simulated to be installed that the method returns the 2.0 directory
        /// </summary>
        [Fact]
        public void LegacyFramework20Good()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("2.0"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNet20Installed = true;

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(1);
            list[0].ShouldBe(LegacyFrameworkTestHelper.DotNet20FrameworkPath);
        }

        /// <summary>
        /// Verify when 20 is simulated to not be installed that the method returns an empty list
        /// </summary>
        [Fact]
        public void LegacyFramework20NotInstalled()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("2.0"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(0);
        }

        /// <summary>
        /// Verify when 30 is simulated to be installed that the method returns the 3.0 directory
        /// </summary>
        [Fact]
        public void LegacyFramework30Good()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("3.0"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNetReferenceAssemblies30Installed = true;
            legacyHelper.DotNet30Installed = true;
            legacyHelper.DotNet20Installed = true;

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(3);
            list[0].ShouldBe(LegacyFrameworkTestHelper.DotNet30ReferenceAssemblyPath);
            list[1].ShouldBe(LegacyFrameworkTestHelper.DotNet30FrameworkPath);
            list[2].ShouldBe(LegacyFrameworkTestHelper.DotNet20FrameworkPath);
        }

        /// <summary>
        /// Verify when 30 is simulated to not be installed that the method returns an empty list
        /// </summary>
        [Fact]
        public void LegacyFramework30NotInstalled()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("3.0"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNetReferenceAssemblies30Installed = true;

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(0);
        }

        /// <summary>
        /// Verify when the 30 reference assemblies are simulated to not be installed that the method returns an empty list
        /// </summary>
        [Fact]
        public void LegacyFramework30ReferenceAssembliesNotInstalled()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("3.0"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNet30Installed = true;

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(0);
        }

        /// <summary>
        /// Verify when 30 is installed but 2.0 is not installed that we only get one of the paths back.
        /// </summary>
        [Fact]
        public void LegacyFramework30WithNo20Installed()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("3.0"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNet30Installed = true;
            legacyHelper.DotNetReferenceAssemblies30Installed = true;
            // Note no 2.0 installed

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(2);
            list[0].ShouldBe(LegacyFrameworkTestHelper.DotNet30ReferenceAssemblyPath, StringCompareShould.IgnoreCase);
            list[1].ShouldBe(LegacyFrameworkTestHelper.DotNet30FrameworkPath, StringCompareShould.IgnoreCase);
        }

        /// <summary>
        /// Verify when 35 is simulated to be installed that the method returns the 3.5 directory
        /// </summary>
        [Fact]
        public void LegacyFramework35Good()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("3.5"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNetReferenceAssemblies35Installed = true;
            legacyHelper.DotNetReferenceAssemblies30Installed = true;
            legacyHelper.DotNet30Installed = true;
            legacyHelper.DotNet35Installed = true;
            legacyHelper.DotNet20Installed = true;

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(5);
            list[0].ShouldBe(LegacyFrameworkTestHelper.DotNet35ReferenceAssemblyPath);
            list[1].ShouldBe(LegacyFrameworkTestHelper.DotNet35FrameworkPath);
            list[2].ShouldBe(LegacyFrameworkTestHelper.DotNet30ReferenceAssemblyPath);
            list[3].ShouldBe(LegacyFrameworkTestHelper.DotNet30FrameworkPath);
            list[4].ShouldBe(LegacyFrameworkTestHelper.DotNet20FrameworkPath);
        }

        /// <summary>
        /// Verify when 35 is simulated to not be installed that the method returns an empty list
        /// </summary>
        [Fact]
        public void LegacyFramework35NotInstalled()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("3.5"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNetReferenceAssemblies35Installed = true;

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(0);
        }

        /// <summary>
        /// Verify when 35 reference assembly are simulated to not be installed that the method returns an empty list
        /// </summary>
        [Fact]
        public void LegacyFramework35ReferenceAssembliesNotInstalled()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("3.5"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNet35Installed = true;

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(0);
        }

        /// <summary>
        /// Make sure when we are targeting .net framework 3.5 and are on a 64 bit machine we get the correct framework path.
        ///
        /// We are on a 64 bit machine
        /// Targeting .net framework 3.5
        ///
        /// 1) Target platform is x86. We expect to get the 32 bit framework directory
        /// 2) Target platform is x64, we expect to get the 64 bit framework directory
        /// 3) Target platform is Itanium, we expect to get the 64 bit framework directory
        /// 3) Target platform is some other value (AnyCpu, or anything else)  expect the framework directory for the "current" bitness of the process we are running under.
        ///
        /// </summary>
        [Fact]
        public void GetPathToStandardLibraries64Bit35()
        {
            string frameworkDirectory2032bit = FrameworkLocationHelper.GetPathToDotNetFrameworkV20(SharedDotNetFrameworkArchitecture.Bitness32);
            string frameworkDirectory2064bit = FrameworkLocationHelper.GetPathToDotNetFrameworkV20(SharedDotNetFrameworkArchitecture.Bitness64);
            string frameworkDirectory20Current = FrameworkLocationHelper.GetPathToDotNetFrameworkV20(SharedDotNetFrameworkArchitecture.Current);

            if (!EnvironmentUtilities.Is64BitOperatingSystem)
            {
                // "Not 64 bit OS "
                return;
            }

            if (string.IsNullOrEmpty(frameworkDirectory2032bit) || string.IsNullOrEmpty(frameworkDirectory2064bit) || string.IsNullOrEmpty(frameworkDirectory20Current))
            {
                // ".Net 2.0 not installed: checked current {0} :: 64 bit :: {1} :: 32 bit {2}", frameworkDirectory20Current, frameworkDirectory2064bit, frameworkDirectory2032bit
                return;
            }

            string pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v3.5", string.Empty, "x86");
            pathToFramework.ShouldBe(frameworkDirectory2032bit, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v3.5", string.Empty, "x64");
            pathToFramework.ShouldBe(frameworkDirectory2064bit, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v3.5", string.Empty, "itanium");
            pathToFramework.ShouldBe(frameworkDirectory2064bit, StringCompareShould.IgnoreCase);

            if (!EnvironmentUtilities.Is64BitProcess)
            {
                pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v3.5", string.Empty, "RandomPlatform");
                pathToFramework.ShouldBe(frameworkDirectory2032bit, StringCompareShould.IgnoreCase);
            }
            else
            {
                pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v3.5", string.Empty, "RandomPlatform");
                pathToFramework.ShouldBe(frameworkDirectory2064bit, StringCompareShould.IgnoreCase);
            }
        }

        /// <summary>
        /// Make sure when we are targeting .net framework 3.5 and are on a 64 bit machine we get the correct framework path.
        ///
        /// We are on a 64 bit machine
        /// Targeting .net framework 4.0
        ///
        /// We expect to always get the same path which is returned by GetPathToReferenceAssemblies.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void GetPathToStandardLibraries64Bit40()
        {
            IList<string> referencePaths = ToolLocationHelper.GetPathToReferenceAssemblies(new FrameworkNameVersioning(".NETFramework", new Version("4.0")));

            if (!EnvironmentUtilities.Is64BitOperatingSystem)
            {
                // "Not 64 bit OS "
                return;
            }

            if (referencePaths.Count == 0)
            {
                // ".Net 4.0 not installed"
                return;
            }

            string pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v4.0", string.Empty, "x86");
            string dotNet40Path = FileUtilities.EnsureNoTrailingSlash(referencePaths[0]);
            pathToFramework.ShouldBe(dotNet40Path, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v4.0", string.Empty, "x64");
            pathToFramework.ShouldBe(dotNet40Path, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v4.0", string.Empty, "itanium");
            pathToFramework.ShouldBe(dotNet40Path, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v4.0", string.Empty, "RandomPlatform");
            pathToFramework.ShouldBe(dotNet40Path, StringCompareShould.IgnoreCase);
        }

        /// <summary>
        /// Make sure when we are targeting .net framework 3.5 and are on a 32 bit machine we get the correct framework path.
        ///
        /// We are on a 32 bit machine
        /// Targeting .net framework 3.5
        ///
        /// 1) Target platform is x86. We expect to get the 32 bit framework directory
        /// 2) Target platform is x64, we expect to get the 32 bit framework directory
        /// 3) Target platform is Itanium, we expect to get the 32 bit framework directory
        /// 3) Target platform is some other value (AnyCpu, or anything else)  expect the framework directory for the "current" bitness of the process we are running under. In the
        ///    case of the unit test this should be the 32 bit framework directory.
        ///
        /// </summary>
        [Fact]
        public void GetPathToStandardLibraries32Bit35()
        {
            string frameworkDirectory2032bit = FrameworkLocationHelper.GetPathToDotNetFrameworkV20(SharedDotNetFrameworkArchitecture.Bitness32);
            string frameworkDirectory20Current = FrameworkLocationHelper.GetPathToDotNetFrameworkV20(SharedDotNetFrameworkArchitecture.Current);

            if (EnvironmentUtilities.Is64BitOperatingSystem)
            {
                // "Is a 64 bit OS "
                return;
            }

            if (string.IsNullOrEmpty(frameworkDirectory2032bit) || string.IsNullOrEmpty(frameworkDirectory20Current))
            {
                // ".Net 2.0 not installed: checked current {0} :: 32 bit {2}", frameworkDirectory20Current, frameworkDirectory2032bit
                return;
            }

            string pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v3.5", string.Empty, "x86");
            pathToFramework.ShouldBe(frameworkDirectory2032bit, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v3.5", string.Empty, "x64");
            pathToFramework.ShouldBe(frameworkDirectory2032bit, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v3.5", string.Empty, "itanium");
            pathToFramework.ShouldBe(frameworkDirectory2032bit, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v3.5", string.Empty, "RandomPlatform");
            pathToFramework.ShouldBe(frameworkDirectory2032bit, StringCompareShould.IgnoreCase);
        }

        /// <summary>
        /// Make sure when we are targeting .net framework 4.0 and are on a 32 bit machine we get the correct framework path.
        ///
        /// We are on a 32 bit machine
        /// Targeting .net framework 4.0
        ///
        /// We expect to always get the same path which is returned by GetPathToReferenceAssemblies.
        /// </summary>
        [Fact]
        public void GetPathToStandardLibraries32Bit40()
        {
            IList<string> referencePaths = ToolLocationHelper.GetPathToReferenceAssemblies(new FrameworkNameVersioning(".NETFramework", new Version("4.0")));

            if (EnvironmentUtilities.Is64BitOperatingSystem)
            {
                // "Is 64 bit OS "
                return;
            }

            if (referencePaths.Count == 0)
            {
                // ".Net 4.0 not installed"
                return;
            }

            string pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v4.0", string.Empty, "x86");
            string dotNet40Path = FileUtilities.EnsureNoTrailingSlash(referencePaths[0]);
            pathToFramework.ShouldBe(dotNet40Path, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v4.0", string.Empty, "x64");
            pathToFramework.ShouldBe(dotNet40Path, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v4.0", string.Empty, "itanium");
            pathToFramework.ShouldBe(dotNet40Path, StringCompareShould.IgnoreCase);

            pathToFramework = ToolLocationHelper.GetPathToStandardLibraries(".NetFramework", "v4.0", string.Empty, "RandomPlatform");
            pathToFramework.ShouldBe(dotNet40Path, StringCompareShould.IgnoreCase);
        }

        /// <summary>
        /// Verify when 35 is installed but 2.0 is not installed we to find 3.5 and 3.0 but no 2.0 because it does not exist.
        /// </summary>
        [Fact]
        public void LegacyFramework35WithNo20Installed()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("3.5"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNetReferenceAssemblies35Installed = true;
            legacyHelper.DotNetReferenceAssemblies30Installed = true;
            legacyHelper.DotNet35Installed = true;
            legacyHelper.DotNet30Installed = true;
            // Note no 2.0 installed

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(4);
            list[0].ShouldBe(LegacyFrameworkTestHelper.DotNet35ReferenceAssemblyPath, StringCompareShould.IgnoreCase);
            list[1].ShouldBe(LegacyFrameworkTestHelper.DotNet35FrameworkPath, StringCompareShould.IgnoreCase);
            list[2].ShouldBe(LegacyFrameworkTestHelper.DotNet30ReferenceAssemblyPath, StringCompareShould.IgnoreCase);
            list[3].ShouldBe(LegacyFrameworkTestHelper.DotNet30FrameworkPath, StringCompareShould.IgnoreCase);
        }

        /// <summary>
        /// Verify when 35 is installed but 3.0 is not installed we expect not to find 3.0 or 2.0.
        /// </summary>
        [Fact]
        public void LegacyFramework35WithNo30Installed()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("3.5"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNetReferenceAssemblies35Installed = true;
            legacyHelper.DotNet35Installed = true;
            legacyHelper.DotNet20Installed = true;
            // Note no 3.0

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(2);
            list[0].ShouldBe(LegacyFrameworkTestHelper.DotNet35ReferenceAssemblyPath, StringCompareShould.IgnoreCase);
            list[1].ShouldBe(LegacyFrameworkTestHelper.DotNet35FrameworkPath, StringCompareShould.IgnoreCase);
        }

        /// <summary>
        /// Verify when 40 is simulated to not be installed that the method returns an empty list
        /// </summary>
        [Fact]
        public void LegacyFramework40NotInstalled()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("4.0"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(0);
        }

        /// <summary>
        /// Verify when 40 reference assemblies are installed but the dot net framework is not, in this case we return empty indicating .net 4.0 is not properly installed
        /// </summary>
        [Fact]
        public void LegacyFramework40DotNetFrameworkDirectoryNotInstalled()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("4.0"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNetReferenceAssemblies40Installed = true;

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(0);
        }

        /// <summary>
        /// Verify when 40 reference assemblies are installed but the dot net framework is not we only get one of the paths back, this is because right now the assemblies are not in the right location
        /// </summary>
        [Fact]
        public void LegacyFramework40DotNetReferenceAssemblyDirectoryNotInstalled()
        {
            FrameworkNameVersioning frameworkName = new FrameworkNameVersioning("Anything", new Version("4.0"));
            LegacyFrameworkTestHelper legacyHelper = new LegacyFrameworkTestHelper();
            legacyHelper.DotNet40Installed = true;

            IList<string> list = ToolLocationHelper.HandleLegacyDotNetFrameworkReferenceAssemblyPaths(legacyHelper.GetDotNetVersionToPathDelegate, legacyHelper.GetDotNetReferenceAssemblyDelegate, frameworkName);
            list.Count.ShouldBe(0);
        }
        #endregion

#if FEATURE_WIN32_REGISTRY
        /// <summary>
        /// Verify we can an argument exception if we try and pass a empty registry root
        /// </summary>
        [Fact]
        public void GetAssemblyFoldersExInfoTestEmptyRegistryRoot()
        {
            Should.Throw<ArgumentException>(() =>
            {
                ToolLocationHelper.GetAssemblyFoldersExInfo("", "v3.0", "AssemblyFoldersEx", null, null, System.Reflection.ProcessorArchitecture.MSIL);
            }
           );
        }
        /// <summary>
        /// Verify we can an argumentNull exception if we try and pass a null registry root
        /// </summary>
        [Fact]
        public void GetAssemblyFoldersExInfoListTestNullRegistryRoot()
        {
            Should.Throw<ArgumentNullException>(() =>
            {
                ToolLocationHelper.GetAssemblyFoldersExInfo(null, "v3.0", "AssemblyFoldersEx", null, null, System.Reflection.ProcessorArchitecture.MSIL);
            }
           );
        }
        /// <summary>
        /// Verify we can an argument exception if we try and pass a empty registry suffix
        /// </summary>
        [Fact]
        public void GetAssemblyFoldersExInfoTestEmptyRegistrySuffix()
        {
            Should.Throw<ArgumentException>(() =>
            {
                ToolLocationHelper.GetAssemblyFoldersExInfo(@"SOFTWARE\Microsoft\.UnitTest", "v3.0", "", null, null, System.Reflection.ProcessorArchitecture.MSIL);
            }
           );
        }
        /// <summary>
        /// Verify we can an argumentNull exception if we try and pass a null registry suffix
        /// </summary>
        [Fact]
        public void GetAssemblyFoldersExInfoTestNullRegistrySuffix()
        {
            Should.Throw<ArgumentNullException>(() =>
            {
                ToolLocationHelper.GetAssemblyFoldersExInfo(@"SOFTWARE\Microsoft\.UnitTest", "v3.0", null, null, null, System.Reflection.ProcessorArchitecture.MSIL);
            }
           );
        }
        /// <summary>
        /// Verify we can an argument exception if we try and pass a empty registry suffix
        /// </summary>
        [Fact]
        public void GetAssemblyFoldersExInfoTestEmptyTargetRuntime()
        {
            Should.Throw<ArgumentException>(() =>
            {
                ToolLocationHelper.GetAssemblyFoldersExInfo(@"SOFTWARE\Microsoft\.UnitTest", "", "AssemblyFoldersEx", null, null, System.Reflection.ProcessorArchitecture.MSIL);
            }
           );
        }
        /// <summary>
        /// Verify we can an argumentNull exception if we try and pass a null target runtime version
        /// </summary>
        [Fact]
        public void GetAssemblyFoldersExInfoTestNullTargetRuntimeVersion()
        {
            Should.Throw<ArgumentNullException>(() =>
            {
                ToolLocationHelper.GetAssemblyFoldersExInfo(@"SOFTWARE\Microsoft\.UnitTest", null, "AssemblyFoldersEx", null, null, System.Reflection.ProcessorArchitecture.MSIL);
            }
           );
        }
        /// <summary>
        /// Verify we can get a list of directories out of the public API.
        /// </summary>
        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void GetAssemblyFoldersExInfoTest()
        {
            SetupAssemblyFoldersExTestConditionRegistryKey();
            IList<AssemblyFoldersExInfo> directories;
            try
            {
                directories = ToolLocationHelper.GetAssemblyFoldersExInfo(@"SOFTWARE\Microsoft\.UnitTest", "v3.0", "AssemblyFoldersEx", null, null, System.Reflection.ProcessorArchitecture.MSIL);
            }
            finally
            {
                RemoveAssemblyFoldersExTestConditionRegistryKey();
            }
            directories.ShouldNotBeNull();
            directories.Count.ShouldBe(2);
            directories[0].DirectoryPath.ShouldBe(@"C:\V1Control2", StringCompareShould.IgnoreCase);
            directories[1].DirectoryPath.ShouldBe(@"C:\V1Control", StringCompareShould.IgnoreCase);
        }

        private static void SetupAssemblyFoldersExTestConditionRegistryKey()
        {
            RegistryKey baseKey = Registry.CurrentUser;
            baseKey.DeleteSubKeyTree(@"SOFTWARE\Microsoft\.UnitTest", false);
            RegistryKey folderKey = baseKey.CreateSubKey(@"SOFTWARE\Microsoft\.UnitTest\v2.0.3600\AssemblyFoldersEx\Component1");
            folderKey.SetValue("", @"C:\V1Control");

            RegistryKey servicePackKey = baseKey.CreateSubKey(@"SOFTWARE\Microsoft\.UnitTest\v2.0.3600\AssemblyFoldersEx\Component2");
            servicePackKey.SetValue("", @"C:\V1Control2");
        }

        private static void RemoveAssemblyFoldersExTestConditionRegistryKey()
        {
            RegistryKey baseKey = Registry.CurrentUser;
            try
            {
                baseKey.DeleteSubKeyTree(@"SOFTWARE\Microsoft\.UnitTest\v2.0.3600\AssemblyFoldersEx\Component1");
                baseKey.DeleteSubKeyTree(@"SOFTWARE\Microsoft\.UnitTest\v2.0.3600\AssemblyFoldersEx\Component2");
            }
            catch (Exception)
            {
            }
        }
#endif

        [Fact]
        public void GetPathToStandardLibrariesWithCustomTargetFrameworkRoot()
        {
            using (var env = TestEnvironment.Create())
            {
                string frameworkName = "Foo Framework";
                string frameworkVersion = "v0.1";
                string rootDir = Path.Combine(env.DefaultTestDirectory.Path, "framework-root");

                string asmPath = CreateNewFrameworkAndGetAssembliesPath(env, frameworkName, frameworkVersion, rootDir);

                string stdLibPath = ToolLocationHelper.GetPathToStandardLibraries(frameworkName, frameworkVersion, string.Empty, null, rootDir);
                stdLibPath.ShouldBe(asmPath);
            }
        }

        [Fact]
        public void GetPathToStandardLibrariesWithNullTargetFrameworkRootPath()
        {
            string frameworkName = ".NETFramework";
            string frameworkVersion = "v4.5";

            string v45Path = ToolLocationHelper.GetPathToStandardLibraries(frameworkName, frameworkVersion, string.Empty);
            // This look up should fall back the default path with the .NET frameworks
            string v45PathWithNullRoot = ToolLocationHelper.GetPathToStandardLibraries(frameworkName, frameworkVersion, string.Empty, null);

            v45PathWithNullRoot.ShouldBe(v45Path);
        }

        [Fact]
        public void GetPathToStandardLibrariesWithCustomTargetFrameworkInFallbackSearchPathAndNullRoot()
        {
            using (var env = TestEnvironment.Create())
            {
                string frameworkName = "Foo Framework";
                string frameworkVersion = "v0.1";
                string customFrameworkRootPath = Path.Combine(env.DefaultTestDirectory.Path, "framework-root");

                string asmPath = CreateNewFrameworkAndGetAssembliesPath(env, frameworkName, frameworkVersion, customFrameworkRootPath);
                string fallbackSearchPaths = $"/foo/bar;{customFrameworkRootPath};/a/b";

                string stdLibPath = ToolLocationHelper.GetPathToStandardLibraries(frameworkName, frameworkVersion, string.Empty, null, null, fallbackSearchPaths);
                stdLibPath.ShouldBe(asmPath);
            }
        }

        [Fact]
        public void GetPathToStandardLibrariesWithCustomRootAndCustomTargetFrameworkInFallbackSearchPath()
        {
            // We are creating the same framework in the root path and in a second location, used as a
            // fallback search path. When trying to find the framework, we should always resolve to
            // the framework in the root path, because the search order is:
            //  1. rootPath or default path, if null
            //  2. fallback search paths

            using (var env = TestEnvironment.Create())
            {
                string frameworkName = "Foo Framework";
                string frameworkVersion = "v0.1";
                string rootDir = Path.Combine(env.CreateFolder().Path, "framework-root");
                string fallbackPath = Path.Combine(env.CreateFolder().Path, "framework-root");

                string asmPathForRoot = CreateNewFrameworkAndGetAssembliesPath(env, frameworkName, frameworkVersion, rootDir);
                CreateNewFrameworkAndGetAssembliesPath(env, frameworkName, frameworkVersion, fallbackPath);
                string fallbackSearchPaths = $"/foo/bar;{fallbackPath};/a/b";

                string stdLibPath = ToolLocationHelper.GetPathToStandardLibraries(frameworkName, frameworkVersion, string.Empty, null, rootDir, fallbackSearchPaths);
                // the path should be for the framework in the root path
                stdLibPath.ShouldBe(asmPathForRoot);
            }
        }

        [Fact]
        public void GetPathToStandardLibrariesWithNullTargetFrameworkFallbackSearchPaths()
        {
            string frameworkName = ".NETFramework";
            string frameworkVersion = "v4.5";

            string v45Path = ToolLocationHelper.GetPathToStandardLibraries(frameworkName, frameworkVersion, string.Empty);
            // This look up should fall back the default path with the .NET frameworks
            string v45PathWithNullRoot = ToolLocationHelper.GetPathToStandardLibraries(frameworkName, frameworkVersion, string.Empty, null, null);

            v45PathWithNullRoot.ShouldBe(v45Path);
        }

        [Fact]
        public void GetPathToReferenceAssembliesWithCustomTargetFrameworkInRoot()
        {
            using (var env = TestEnvironment.Create())
            {
                string customFrameworkDirToUse = Path.Combine(env.CreateFolder().Path, "framework-root");

                CheckGetPathToReferenceAssemblies(env, customFrameworkDirToUse, null,
                                                    (string frameworkName, string frameworkVersion, string frameworkProfile, string customFrameworkDir, string fallbackSearchPaths)
                                                         => ToolLocationHelper.GetPathToReferenceAssemblies(frameworkName, "v" + frameworkVersion, frameworkProfile, customFrameworkDir));
            }
        }

        [Fact]
        public void GetPathToReferenceAssembliesWithCustomTargetFrameworkInFallbackPath()
        {
            using (var env = TestEnvironment.Create())
            {
                string customFrameworkDirToUse = Path.Combine(env.CreateFolder().Path, "framework-root");
                string searchPaths = $"/foo/bar;{customFrameworkDirToUse}";
                string rootDir = env.CreateFolder().Path;

                CheckGetPathToReferenceAssemblies(env, customFrameworkDirToUse, searchPaths,
                                                    (string frameworkName, string frameworkVersion, string frameworkProfile, string customFrameworkDir, string fallbackSearchPaths)
                                                        => ToolLocationHelper.GetPathToReferenceAssemblies(
                                                                                frameworkName, "v" + frameworkVersion, frameworkProfile,
                                                                                rootDir, fallbackSearchPaths));
            }
        }

        [Fact]
        public void GetPathToReferenceAssembliesWithCustomTargetFrameworkInFallbackPathAndNullRoot()
        {
            using (var env = TestEnvironment.Create())
            {
                string customFrameworkDirToUse = Path.Combine(env.CreateFolder().Path, "framework-root");
                string searchPaths = $"/foo/bar;{customFrameworkDirToUse}";

                CheckGetPathToReferenceAssemblies(env, customFrameworkDirToUse, searchPaths,
                                                    (string frameworkName, string frameworkVersion, string frameworkProfile, string customFrameworkDir, string fallbackSearchPaths)
                                                        => ToolLocationHelper.GetPathToReferenceAssemblies(
                                                                            frameworkName, "v" + frameworkVersion, frameworkProfile,
                                                                            targetFrameworkRootPath:null, targetFrameworkFallbackSearchPaths:fallbackSearchPaths));
            }
        }

        // second overload of GetPathToReferenceAssemblies
        [Fact]
        public void GetPathToReferenceAssembliesWithCustomTargetFrameworkInRoot2()
        {
            using (var env = TestEnvironment.Create())
            {
                string customFrameworkDirToUse = Path.Combine(env.CreateFolder().Path, "framework-root");
                CheckGetPathToReferenceAssemblies(env, customFrameworkDirToUse, null,
                                                    (string frameworkName, string frameworkVersion, string frameworkProfile, string customFrameworkDir, string fallbackSearchPaths)
                                                         => ToolLocationHelper.GetPathToReferenceAssemblies(
                                                                            customFrameworkDir, fallbackSearchPaths,
                                                                            new FrameworkNameVersioning(frameworkName, new Version(frameworkVersion), frameworkProfile)));
            }
        }

        // second overload of GetPathToReferenceAssemblies
        [Fact]
        public void GetPathToReferenceAssembliesWithCustomTargetFrameworkRootInFallbackPath2()
        {
            using (var env = TestEnvironment.Create())
            {
                string customFrameworkDirToUse = Path.Combine(env.CreateFolder().Path, "framework-root");
                string searchPaths = $"{customFrameworkDirToUse};/a/b";
                string rootDir = env.CreateFolder().Path;

                CheckGetPathToReferenceAssemblies(env, customFrameworkDirToUse, searchPaths,
                                                    (string frameworkName, string frameworkVersion, string frameworkProfile, string customFrameworkDir, string fallbackSearchPaths)
                                                         => ToolLocationHelper.GetPathToReferenceAssemblies(
                                                                            rootDir, fallbackSearchPaths,
                                                                            new FrameworkNameVersioning(frameworkName, new Version(frameworkVersion), frameworkProfile)));
            }
        }

        [Fact]
        public void GetPathToReferenceAssembliesWithCustomTargetFrameworkRootInFallbackPathAndNullRoot2()
        {
            using (var env = TestEnvironment.Create())
            {
                string customFrameworkDirToUse = Path.Combine(env.CreateFolder().Path, "framework-root");
                string searchPaths = $"{customFrameworkDirToUse};/a/b";
                CheckGetPathToReferenceAssemblies(env, customFrameworkDirToUse, searchPaths,
                                                    (string frameworkName, string frameworkVersion, string frameworkProfile, string customFrameworkDir, string fallbackSearchPaths)
                                                         => ToolLocationHelper.GetPathToReferenceAssemblies(
                                                                            null, fallbackSearchPaths,
                                                                            new FrameworkNameVersioning(frameworkName, new Version(frameworkVersion), frameworkProfile)));
            }
        }

        private void CheckGetPathToReferenceAssemblies(TestEnvironment env, string customFrameworkDir, string fallbackSearchPaths, Func<string, string, string, string, string, IList<string>> getPathToReferenceAssemblies)
         {
            string frameworkName = "Foo Framework";
            string frameworkVersion = "0.1";
            string frameworkVersionWithV = "v" + frameworkVersion;
            string frameworkProfile = string.Empty;

            string asmPath = CreateNewFrameworkAndGetAssembliesPath(env, frameworkName, frameworkVersionWithV, customFrameworkDir);

            var stdLibPaths = getPathToReferenceAssemblies(frameworkName, frameworkVersion, frameworkProfile, customFrameworkDir, fallbackSearchPaths);
            if (NativeMethodsShared.IsMono)
            {
                stdLibPaths.Count.ShouldBe(2);
                stdLibPaths[0].ShouldBe(Path.Combine(customFrameworkDir, frameworkName, frameworkVersionWithV) + Path.DirectorySeparatorChar, stdLibPaths[0]);
                stdLibPaths[1].ShouldBe(asmPath + Path.DirectorySeparatorChar);
            }
            else
            {
                stdLibPaths.Count.ShouldBe(1);
                stdLibPaths[0].ShouldBe(Path.Combine(customFrameworkDir, frameworkName, frameworkVersionWithV) + Path.DirectorySeparatorChar, stdLibPaths[0]);
             }
         }

        [Fact]
        public void GetPathToReferenceAssembliesWithNullTargetFrameworkRootPath()
        {
            string frameworkName = ".NETFramework";
            string frameworkVersion = "v4.5";

            var v45Paths = ToolLocationHelper.GetPathToReferenceAssemblies(frameworkName, frameworkVersion, string.Empty);

            // This look up should fall back the default path with the .NET frameworks
            var v45PathsWithNullRoot = ToolLocationHelper.GetPathToReferenceAssemblies(frameworkName, frameworkVersion, string.Empty, null);

            v45PathsWithNullRoot.ShouldBe(v45Paths);
        }

        [Fact]
        public void GetPathToReferenceAssembliesWithNullTargetFrameworkFallbackSearchPaths()
        {
            string frameworkName = ".NETFramework";
            string frameworkVersion = "v4.5";

            var v45Paths = ToolLocationHelper.GetPathToReferenceAssemblies(frameworkName, frameworkVersion, string.Empty);

            // This look up should fall back the default path with the .NET frameworks
            var v45PathsWithNullRoot = ToolLocationHelper.GetPathToReferenceAssemblies(frameworkName, frameworkVersion, string.Empty, null, null);

            v45PathsWithNullRoot.ShouldBe(v45Paths);
        }

        private static string CreateNewFrameworkAndGetAssembliesPath(TestEnvironment env, string frameworkName, string frameworkVersion, string rootDir)
        {
            string frameworkListXml;
            if (NativeMethodsShared.IsMono)
            {
                // Mono uses an extra attribute to point to the location of the corresponding
                // assemblies
                frameworkListXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
                    <FileList  Name=""{0}"" TargetFrameworkDirectory=""..\assemblies"" />";
            }
            else
            {
                frameworkListXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
                    <FileList  Name=""{0}""/>";
            }

            string redistPath = Path.Combine(rootDir, frameworkName, frameworkVersion, "RedistList");
            string asmPath = Path.Combine(rootDir, frameworkName, frameworkVersion, NativeMethodsShared.IsMono ? "assemblies" : string.Empty);

            env.CreateFolder(redistPath);
            env.CreateFolder(asmPath);

            File.WriteAllText(Path.Combine(redistPath, "FrameworkList.xml"), string.Format(frameworkListXml, frameworkName));
            File.WriteAllText(Path.Combine(asmPath, "mscorlib.dll"), string.Empty);

            return asmPath;
        }

        /*
        * Method:   GetDirectories
        *
        * Delegate method simulates a file system for testing location methods.
        */
        private static string[] GetDirectories(string path, string pattern)
        {
            if (path == "{runtime-base}" && pattern == "v1.2*")
            {
                return new[] { @"{runtime-base}\v1.2.30617", @"{runtime-base}\v1.2.x86dbg", @"{runtime-base}\v1.2.x86fre" };
            }
            return new string[0];
        }

        /// <summary>
        /// Delegate method simulates a file system for testing location methods.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool DirectoryExists(string path)
        {
            return path.Contains("{runtime-base}") || Directory.Exists(path);
        }

#if FEATURE_WIN32_REGISTRY
        private static string GetRegistryValueHelper(RegistryHive hive, RegistryView view, string subKeyPath, string name)
        {
            using (var key = RegistryHelper.OpenBaseKey(hive, view))
            using (var subKey = key.OpenSubKey(subKeyPath))
            {
                if (subKey != null)
                {
                    return (string)subKey.GetValue(name);
                }
            }

            return null;
        }
#endif
        private static IEnumerable<VisualStudioVersion> EnumVisualStudioVersions()
        {
            for (VisualStudioVersion vsVersion = VisualStudioVersion.Version100; vsVersion <= VisualStudioVersion.VersionLatest; ++vsVersion)
            {
                yield return vsVersion;
            }
        }

        private static IEnumerable<TargetDotNetFrameworkVersion> EnumDotNetFrameworkVersions()
        {
            for (TargetDotNetFrameworkVersion dotNetVersion = TargetDotNetFrameworkVersion.Version11; dotNetVersion <= TargetDotNetFrameworkVersion.VersionLatest; ++dotNetVersion)
            {
                yield return dotNetVersion;
            }
        }

        /// <summary>
        /// This class will provide delegates and properties to allow differen combinations of ToolLocationHelper GetDotNetFrameworkPaths and GetReferenceAssemblyPaths to be simulated.
        /// </summary>
        internal class LegacyFrameworkTestHelper
        {
            /// <summary>
            /// Paths which simulate the fact that the frameworks are installed including their reference assemblies
            /// </summary>
            internal const string DotNet40ReferenceAssemblyPath = "C:\\Program Files\\Reference Assemblies\\Framework\\V4.0";
            internal const string DotNet35ReferenceAssemblyPath = "C:\\Program Files\\Reference Assemblies\\Framework\\V3.5";
            internal const string DotNet30ReferenceAssemblyPath = "C:\\Program Files\\Reference Assemblies\\Framework\\V3.0";
            internal const string DotNet20FrameworkPath = "C:\\Microsoft\\.Net Framework\\V2.0.57027";
            internal const string DotNet30FrameworkPath = "C:\\Microsoft\\.Net Framework\\V3.0";
            internal const string DotNet35FrameworkPath = "C:\\Microsoft\\.Net Framework\\V3.5";
            internal const string DotNet40FrameworkPath = "C:\\Microsoft\\.Net Framework\\V4.0";

            /// <summary>
            /// Should the delegate respond with a path or null when asked for Version20 on the delegate which gets the DotNetFrameworkPath
            /// </summary>
            internal bool DotNet20Installed { get; set; }

            /// <summary>
            /// Should the delegate respond with a path or null when asked for Version30 on the delegate which gets the DotNetFrameworkPath
            /// </summary>
            internal bool DotNet30Installed { get; set; }

            /// <summary>
            /// Should the delegate respond with a path or null when asked for Version35 on the delegate which gets the DotNetFrameworkPath
            /// </summary>
            internal bool DotNet35Installed { get; set; }

            /// <summary>
            /// Should the delegate respond with a path or null when asked for Version40 on the delegate which gets the DotNetFrameworkPath
            /// </summary>
            internal bool DotNet40Installed { get; set; }

            /// <summary>
            /// Should the delegate respond with a path or null when asked for Version40 on the delegate which gets the DotNetReferenceAssembliesPath is called
            /// </summary>
            internal bool DotNetReferenceAssemblies40Installed { get; set; }

            /// <summary>
            /// Should the delegate respond with a path or null when asked for Version35 on the delegate which gets the DotNetReferenceAssembliesPath is called
            /// </summary>
            internal bool DotNetReferenceAssemblies35Installed { get; set; }

            /// <summary>
            /// Should the delegate respond with a path or null when asked for Version30 on the delegate which gets the DotNetReferenceAssembliesPath is called
            /// </summary>
            internal bool DotNetReferenceAssemblies30Installed { get; set; }

            /// <summary>
            /// Return a delegate which will return a path or null depending on whether or not frameworks and their reference assembly paths are being simulated as being installed
            /// </summary>
            internal ToolLocationHelper.VersionToPath GetDotNetVersionToPathDelegate => GetDotNetFramework;

            /// <summary>
            /// Return a delegate which will return a path or null depending on whether or not frameworks and their reference assembly paths are being simulated as being installed
            /// </summary>
            internal ToolLocationHelper.VersionToPath GetDotNetReferenceAssemblyDelegate => GetDotNetFrameworkReferenceAssemblies;

            /// <summary>
            /// Return a path to the .net framework reference assemblies if the boolean property said we should return one.
            /// Return null if we should not fake the fact that the framework reference assemblies are installed
            /// </summary>
            private string GetDotNetFrameworkReferenceAssemblies(TargetDotNetFrameworkVersion version)
            {
                switch (version)
                {
                    case TargetDotNetFrameworkVersion.Version40:
                    {
                        return DotNetReferenceAssemblies40Installed ? DotNet40ReferenceAssemblyPath : null;
                    }
                    case TargetDotNetFrameworkVersion.Version35:
                    {
                        return DotNetReferenceAssemblies35Installed ? DotNet35ReferenceAssemblyPath : null;
                    }
                    case TargetDotNetFrameworkVersion.Version30:
                    {
                        return DotNetReferenceAssemblies30Installed ? DotNet30ReferenceAssemblyPath : null;
                    }
                    default:
                    {
                        return null;
                    }
                }
            }

            /// <summary>
            /// Return a path to the .net framework if the boolean property said we should return one.
            /// Return null if we should not fake the fact that the framework is installed
            /// </summary>
            private string GetDotNetFramework(TargetDotNetFrameworkVersion version)
            {
                switch (version)
                {
                    case TargetDotNetFrameworkVersion.Version20:
                    {
                        return DotNet20Installed ? DotNet20FrameworkPath : null;
                    }
                    case TargetDotNetFrameworkVersion.Version30:
                    {
                        return DotNet30Installed ? DotNet30FrameworkPath : null;
                    }
                    case TargetDotNetFrameworkVersion.Version35:
                    {
                        return DotNet35Installed ? DotNet35FrameworkPath : null;
                    }
                    case TargetDotNetFrameworkVersion.Version40:
                    {
                        return DotNet40Installed ? DotNet40FrameworkPath : null;
                    }
                    default:
                    {
                        return null;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Verify the toolLocation helper method that enumerates the disk and registry to get the list of installed SDKs.
    /// </summary>
    public class GetPlatformExtensionSDKLocationsTestFixture : IDisposable
    {
#if FEATURE_WIN32_REGISTRY
        // Create delegates to mock the registry for the registry portion of the test.
        private readonly OpenBaseKey _openBaseKey = GetBaseKey;
        private readonly GetRegistrySubKeyNames getRegistrySubKeyNames = GetRegistrySubKeyNames;
        private readonly GetRegistrySubKeyDefaultValue getRegistrySubKeyDefaultValue;
#endif

        // Path to the fake SDk directory structure created under the temp directory.
        private readonly string _fakeStructureRoot;
        private readonly string _fakeStructureRoot2;
        private readonly ITestOutputHelper _output;

        public GetPlatformExtensionSDKLocationsTestFixture(ITestOutputHelper output)
        {
#if FEATURE_WIN32_REGISTRY
            getRegistrySubKeyDefaultValue = GetRegistrySubKeyDefaultValue;
#endif

            _output = output;

            _fakeStructureRoot = MakeFakeSDKStructure();
            _fakeStructureRoot2 = MakeFakeSDKStructure2();
        }

        #region TestMethods

        public void Dispose()
        {
            if (_fakeStructureRoot != null)
            {
                if (FileUtilities.DirectoryExistsNoThrow(_fakeStructureRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(_fakeStructureRoot, true);
                }
            }

            if (_fakeStructureRoot2 != null)
            {
                if (FileUtilities.DirectoryExistsNoThrow(_fakeStructureRoot2))
                {
                    FileUtilities.DeleteDirectoryNoThrow(_fakeStructureRoot2, true);
                }
            }
        }

        /// <summary>
        /// Pass empty and null target platform identifier and target platform version string to make sure we get the correct exceptions out.
        /// </summary>
        [Fact]
        public void PassEmptyAndNullTPM()
        {
            VerifyExceptionOnEmptyOrNullPlatformAttributes(string.Empty, new Version("1.0"));
            VerifyExceptionOnEmptyOrNullPlatformAttributes(null, new Version("1.0"));
            VerifyExceptionOnEmptyOrNullPlatformAttributes(null, null);
            VerifyExceptionOnEmptyOrNullPlatformAttributes("Windows", null);
        }

        /// <summary>
        /// Verify that we get argument exceptions where different combinations of identifier and version are passed in.
        /// </summary>
        private static void VerifyExceptionOnEmptyOrNullPlatformAttributes(string identifier, Version version)
        {
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetPlatformExtensionSDKLocations(identifier, version));
            Should.Throw<ArgumentException>(() => ToolLocationHelper.GetPlatformSDKLocation(identifier, version));
        }

        /// <summary>
        /// Verify we can get a list of extension sdks out of the API
        /// </summary>
        [Fact]
        public void TestGetExtensionSDKLocations()
        {
            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", "true");

                // Identifier does not exist
                IDictionary<string, string> sdks = ToolLocationHelper.GetPlatformExtensionSDKLocations(new[] { _fakeStructureRoot }, null, "FOO", new Version(1, 0));
                sdks.Count.ShouldBe(0);

                // Identifier exists
                sdks = ToolLocationHelper.GetPlatformExtensionSDKLocations(new[] { _fakeStructureRoot }, null, "MyPlatform", new Version(3, 0));
                sdks.ShouldContainKey("MyAssembly, Version=1.0");
                sdks.Count.ShouldBe(1);

                // Targeting version higher than exists, however since we are using a russian doll model for extension sdks we will return ones in lower versions of the targeted platform.
                sdks = ToolLocationHelper.GetPlatformExtensionSDKLocations(new[] { _fakeStructureRoot }, null, "MyPlatform", new Version(4, 0));
                sdks.ShouldContainKey("MyAssembly, Version=1.0");
                sdks["MyAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "3.0", "ExtensionSDKs", "MyAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
                sdks.ShouldContainKey("AnotherAssembly, Version=1.0");
                sdks["AnotherAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "4.0", "ExtensionSDKs", "AnotherAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
                sdks.Count.ShouldBe(2);

                // Identifier exists but no extensions are in sdks this version or lower
                sdks = ToolLocationHelper.GetPlatformExtensionSDKLocations(new[] { _fakeStructureRoot }, null, "MyPlatform", new Version(1, 0));
                sdks.Count.ShouldBe(0);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", null);
            }
        }

        /// <summary>
        /// Verify we can get a single extension sdk location out of the API
        /// </summary>
        [Fact]
        public void TestGetExtensionSDKLocation()
        {
            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", "true");

                // Identifier does not exist
                IDictionary<string, string> sdks = ToolLocationHelper.GetPlatformExtensionSDKLocations(new[] { _fakeStructureRoot }, null, "FOO", new Version(1, 0));
                sdks.Count.ShouldBe(0);

                string targetPath =
                    Path.Combine(_fakeStructureRoot, "MyPlatform", "3.0", "ExtensionSDKs", "MyAssembly", "1.0")
                    + Path.DirectorySeparatorChar;

                // Identifier exists
                string path = ToolLocationHelper.GetPlatformExtensionSDKLocation(
                    "MyAssembly, Version=1.0",
                    "MyPlatform",
                    new Version(3, 0),
                    new[] { _fakeStructureRoot },
                    null);
                path.ShouldBe(targetPath, StringCompareShould.IgnoreCase);

                // Identifier exists in lower version
                path = ToolLocationHelper.GetPlatformExtensionSDKLocation(
                    "MyAssembly, Version=1.0",
                    "MyPlatform",
                    new Version(4, 0),
                    new[] { _fakeStructureRoot },
                    null);
                path.ShouldBe(targetPath, StringCompareShould.IgnoreCase);

                // Identifier does not exist
                path = ToolLocationHelper.GetPlatformExtensionSDKLocation("Something, Version=1.0", "MyPlatform", new Version(4, 0), new[] { _fakeStructureRoot }, null);
                path.Length.ShouldBe(0);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", null);
            }
        }

        /// <summary>
        /// Verify we do not get any resolved paths when we pass in a root which is too long
        ///
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp)]
        public void ResolveFromDirectoryPathTooLong()
        {
            Should.Throw<PathTooLongException>(() =>
            {
                // Try a path too long, which does not exist
                string tooLongPath = NativeMethodsShared.IsWindows
                                     ? ("C:\\" + new string('g', 1800))
                                     : ("/" + new string('g', 10000));

                var paths = new List<string> { tooLongPath };
                var targetPlatform = new Dictionary<TargetPlatformSDK, TargetPlatformSDK>();

                ToolLocationHelper.GatherSDKListFromDirectory(paths, targetPlatform);
            }
           );
        }

        /// <summary>
        /// Verify we get no resolved paths when we pass in a root with invalid chars
        /// </summary>
        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)] // No invalid characters on Unix
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, ".NET Core 2.1+ no longer validates paths: https://github.com/dotnet/corefx/issues/27779#issuecomment-371253486")]
        public void ResolveFromDirectoryInvalidChar()
        {
            var targetPlatform = new Dictionary<TargetPlatformSDK, TargetPlatformSDK>();

            // Try a path with invalid chars which does not exist
            string directoryWithInvalidChars = "c:\\<>?";
            var paths = new List<string> {directoryWithInvalidChars};
            Should.Throw<ArgumentException>(() => { ToolLocationHelper.GatherSDKListFromDirectory(paths, targetPlatform); });
        }

        /// <summary>
        /// Verify we get no resolved paths when we pass in a path which does not exist.
        ///
        /// </summary>
        [Fact]
        public void ResolveFromDirectoryNotExist()
        {
            var targetPlatform = new Dictionary<TargetPlatformSDK, TargetPlatformSDK>();

            // Try a regular path which does not exist.
            string normalDirectory = NativeMethodsShared.IsWindows ? "c:\\SDKPath" : "/SDKPath";
            var paths = new List<string> { normalDirectory };
            ToolLocationHelper.GatherSDKListFromDirectory(paths, targetPlatform);
            targetPlatform.Count.ShouldBe(0);
        }

        [Fact]
        public void VerifySDKManifestWithNullOrEmptyParameter()
        {
            Should.Throw<ArgumentNullException>(() => new SDKManifest(null));
            Should.Throw<ArgumentException>(() => new SDKManifest(""));
        }

        /// <summary>
        /// Verify SDKManifest defaults values for MaxPlatformVersion, MinOSVersion, MaxOSVersion when these are not
        /// present in the manifest and the SDK is a framework extension SDK
        /// </summary>
        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void VerifyFrameworkSdkWithOldManifest()
        {
            string tmpRootDirectory = Path.GetTempPath();
            string frameworkPathPattern = NativeMethodsShared.IsWindows ? @"Microsoft SDKs\Windows\v8.0\ExtensionSDKs\MyFramework" : "Microsoft SDKs/Windows/v8.0/ExtensionSDKs/MyFramework";
            string frameworkPathPattern2 = NativeMethodsShared.IsWindows ? @"ExtensionSDKs\MyFramework" : "ExtensionSDKs/MyFramework";

            string frameworkPath = Path.Combine(tmpRootDirectory, frameworkPathPattern);
            string manifestFile = Path.Combine(frameworkPath, "SDKManifest.xml");

            string frameworkPath2 = Path.Combine(tmpRootDirectory, frameworkPathPattern2);
            string manifestFile2 = Path.Combine(frameworkPath, "SDKManifest.xml");

            try
            {
                Directory.CreateDirectory(frameworkPath);
                Directory.CreateDirectory(frameworkPath2);

                // This is a framework SDK with specified values, no default ones are used
                string manifestExtensionSDK = @"
                <FileList
	                DisplayName = ""My SDK""
	                ProductFamilyName = ""UnitTest SDKs""
	                FrameworkIdentity = ""Name=MySDK.10.Debug, MinVersion=1.0.0.0""
                    MoreInfo = ""http://msdn.microsoft.com/MySDK""
                    MaxPlatformVersion = ""9.0""
                    MinOSVersion = ""6.2.3""
                    MaxOSVersionTested = ""6.2.2"">

                    <File Reference = ""MySDK.Sprint.winmd"" Implementation = ""XNASprintImpl.dll"">
                        <Registration Type = ""Flipper"" Implementation = ""XNASprintFlipperImpl.dll"" />
                        <Registration Type = ""Flexer"" Implementation = ""XNASprintFlexerImpl.dll"" />
                        <ToolboxItems VSCategory = ""Toolbox.Default"" />
                    </File>
                </FileList>";

                File.WriteAllText(manifestFile, manifestExtensionSDK);
                SDKManifest sdkManifest = new SDKManifest(frameworkPath);

                sdkManifest.FrameworkIdentities.ShouldNotBeNull();
                sdkManifest.FrameworkIdentities.Count.ShouldBeGreaterThan(0);
                sdkManifest.MaxPlatformVersion.ShouldBe("9.0");
                sdkManifest.MinOSVersion.ShouldBe("6.2.3");
                sdkManifest.MaxOSVersionTested.ShouldBe("6.2.2");

                // This is a framework SDK and the values default b/c they are not in the manifest
                string manifestExtensionSDK2 = @"
                <FileList
	                DisplayName = ""My SDK""
	                ProductFamilyName = ""UnitTest SDKs""
	                FrameworkIdentity = ""Name=MySDK.10.Debug, MinVersion=1.0.0.0""
                    MoreInfo = ""http://msdn.microsoft.com/MySDK"">


                    <File Reference = ""MySDK.Sprint.winmd"" Implementation = ""XNASprintImpl.dll"">
                        <Registration Type = ""Flipper"" Implementation = ""XNASprintFlipperImpl.dll"" />
                        <Registration Type = ""Flexer"" Implementation = ""XNASprintFlexerImpl.dll"" />
                        <ToolboxItems VSCategory = ""Toolbox.Default"" />
                    </File>
                </FileList>";

                File.WriteAllText(manifestFile, manifestExtensionSDK2);
                SDKManifest sdkManifest2 = new SDKManifest(frameworkPath);

                sdkManifest2.FrameworkIdentities.ShouldNotBeNull();
                sdkManifest2.FrameworkIdentities.Count.ShouldBeGreaterThan(0);
                sdkManifest2.MaxPlatformVersion.ShouldBe("8.0");
                sdkManifest2.MinOSVersion.ShouldBe("6.2.1");
                sdkManifest2.MaxOSVersionTested.ShouldBe("6.2.1");

                // This is not a framework SDK because it does not have FrameworkIdentity set
                string manifestExtensionSDK3 = @"
                <FileList
	                DisplayName = ""My SDK""
	                ProductFamilyName = ""UnitTest SDKs""
                    MoreInfo = ""http://msdn.microsoft.com/MySDK"">


                    <File Reference = ""MySDK.Sprint.winmd"" Implementation = ""XNASprintImpl.dll"">
                        <Registration Type = ""Flipper"" Implementation = ""XNASprintFlipperImpl.dll"" />
                        <Registration Type = ""Flexer"" Implementation = ""XNASprintFlexerImpl.dll"" />
                        <ToolboxItems VSCategory = ""Toolbox.Default"" />
                    </File>
                </FileList>";

                File.WriteAllText(manifestFile, manifestExtensionSDK3);
                SDKManifest sdkManifest3 = new SDKManifest(frameworkPath);

                sdkManifest3.FrameworkIdentity.ShouldBeNull();
                sdkManifest3.MaxPlatformVersion.ShouldBeNull();
                sdkManifest3.MinOSVersion.ShouldBeNull();
                sdkManifest3.MaxOSVersionTested.ShouldBeNull();

                // This is not a framework SDK because of its location
                string manifestExtensionSDK4 = @"
                <FileList
	                DisplayName = ""My SDK""
	                ProductFamilyName = ""UnitTest SDKs""
	                FrameworkIdentity = ""Name=MySDK.10.Debug, MinVersion=1.0.0.0""
                    MoreInfo = ""http://msdn.microsoft.com/MySDK""


                    <File Reference = ""MySDK.Sprint.winmd"" Implementation = ""XNASprintImpl.dll"">
                        <Registration Type = ""Flipper"" Implementation = ""XNASprintFlipperImpl.dll"" />
                        <Registration Type = ""Flexer"" Implementation = ""XNASprintFlexerImpl.dll"" />
                        <ToolboxItems VSCategory = ""Toolbox.Default"" />
                    </File>
                </FileList>";

                File.WriteAllText(manifestFile2, manifestExtensionSDK4);
                SDKManifest sdkManifest4 = new SDKManifest(frameworkPath2);

                sdkManifest4.FrameworkIdentity.ShouldBeNull();
                sdkManifest4.MaxPlatformVersion.ShouldBeNull();
                sdkManifest4.MinOSVersion.ShouldBeNull();
                sdkManifest4.MaxOSVersionTested.ShouldBeNull();

                // This is a framework SDK with partially specified values, some default values are used
                string manifestExtensionSDK5 = @"
                <FileList
	                DisplayName = ""My SDK""
	                ProductFamilyName = ""UnitTest SDKs""
	                FrameworkIdentity = ""Name=MySDK.10.Debug, MinVersion=1.0.0.0""
                    MoreInfo = ""http://msdn.microsoft.com/MySDK""
                    MaxOSVersionTested = ""6.2.2"">

                    <File Reference = ""MySDK.Sprint.winmd"" Implementation = ""XNASprintImpl.dll"">
                        <Registration Type = ""Flipper"" Implementation = ""XNASprintFlipperImpl.dll"" />
                        <Registration Type = ""Flexer"" Implementation = ""XNASprintFlexerImpl.dll"" />
                        <ToolboxItems VSCategory = ""Toolbox.Default"" />
                    </File>
                </FileList>";

                File.WriteAllText(manifestFile, manifestExtensionSDK5);
                SDKManifest sdkManifest5 = new SDKManifest(frameworkPath);

                sdkManifest5.FrameworkIdentities.ShouldNotBeNull();
                sdkManifest5.FrameworkIdentities.Count.ShouldBeGreaterThan(0);
                sdkManifest5.MaxPlatformVersion.ShouldBe("8.0");
                sdkManifest5.MinOSVersion.ShouldBe("6.2.1");
                sdkManifest5.MaxOSVersionTested.ShouldBe("6.2.2");
            }
            finally
            {
                FileUtilities.DeleteWithoutTrailingBackslash(frameworkPath, true /* for recursive deletion */);
                FileUtilities.DeleteWithoutTrailingBackslash(frameworkPath2, true /* for recursive deletion */);
            }
        }
        /// <summary>
        /// Verify that SDKManifest properties map correctly to properties in SDKManifest.xml.
        /// </summary>
        [Fact]
        public void VerifySDKManifest()
        {
            string manifestPath = Path.Combine(Path.GetTempPath(), "ManifestTmp");

            try
            {
                Directory.CreateDirectory(manifestPath);

                string manifestFile = Path.Combine(manifestPath, "SDKManifest.xml");

                string manifestPlatformSDK = @"
                <FileList
                    DisplayName = ""Windows""
                    PlatformIdentity = ""Windows, version=8.0""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""11.0""
                    MinOSVersion = ""6.2.1""
                    MaxOSVersionTested = ""6.2.1""
                    UnsupportedDowntarget = ""Windows, version=8.0"">

                <File Reference = ""Windows"">
	                <ToolboxItems VSCategory = ""Toolbox.Default""/>
                </File>
                </FileList>";

                File.WriteAllText(manifestFile, manifestPlatformSDK);
                SDKManifest sdkManifest = new SDKManifest(manifestPath);

                sdkManifest.AppxLocations.ShouldBeNull();
                sdkManifest.CopyRedistToSubDirectory.ShouldBeNull();
                sdkManifest.DependsOnSDK.ShouldBeNull();
                sdkManifest.DisplayName.ShouldBe("Windows");
                sdkManifest.FrameworkIdentities.ShouldBeNull();
                sdkManifest.FrameworkIdentity.ShouldBeNull();
                sdkManifest.MaxPlatformVersion.ShouldBeNull();
                sdkManifest.MinVSVersion.ShouldBe("11.0");
                sdkManifest.MinOSVersion.ShouldBe("6.2.1");
                sdkManifest.PlatformIdentity.ShouldBe("Windows, version=8.0");
                sdkManifest.ProductFamilyName.ShouldBeNull();
                sdkManifest.SDKType.ShouldBe(SDKType.Unspecified);
                sdkManifest.SupportedArchitectures.ShouldBeNull();
                sdkManifest.SupportPrefer32Bit.ShouldBeNull();
                sdkManifest.SupportsMultipleVersions.ShouldBe(MultipleVersionSupport.Allow);
                sdkManifest.ReadError.ShouldBeFalse();

                string manifestExtensionSDK = @"
                <FileList
                    DisplayName = ""My SDK""
                    ProductFamilyName = ""UnitTest SDKs""
                    FrameworkIdentity-Debug = ""Name=MySDK.10.Debug, MinVersion=1.0.0.0""
                    FrameworkIdentity-Retail = ""Name=MySDK.10, MinVersion=1.0.0.0""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""11.0""
                    AppliesTo = ""WindowsAppContainer + WindowsXAML""
                    SupportPrefer32Bit = ""True""
                    SupportedArchitectures = ""x86;x64;ARM""
                    SupportsMultipleVersions = ""Error""
                    AppX-Debug-x86 = "".\AppX\Debug\x86\Microsoft.MySDK.x86.Debug.1.0.appx""
                    AppX-Debug-x64 = "".\AppX\Debug\x64\Microsoft.MySDK.x64.Debug.1.0.appx""
                    AppX-Debug-ARM = "".\AppX\Debug\ARM\Microsoft.MySDK.ARM.Debug.1.0.appx""
                    AppX-Retail-x86 = "".\AppX\Retail\x86\Microsoft.MySDK.x86.1.0.appx""
                    AppX-Retail-x64 = "".\AppX\Retail\x64\Microsoft.MySDK.x64.1.0.appx""
                    AppX-Retail-ARM = "".\AppX\Retail\ARM\Microsoft.MySDK.ARM.1.0.appx""
                    CopyRedistToSubDirectory = "".""
                    DependsOn = ""SDKB, version=2.0""
                    MoreInfo = ""http://msdn.microsoft.com/MySDK""
                    MaxPlatformVersion = ""8.0""
                    MinOSVersion = ""6.2.1""
                    MaxOSVersionTested = ""6.2.3"">

                    <File Reference = ""MySDK.Sprint.winmd"" Implementation = ""XNASprintImpl.dll"">
                        <Registration Type = ""Flipper"" Implementation = ""XNASprintFlipperImpl.dll"" />
                        <Registration Type = ""Flexer"" Implementation = ""XNASprintFlexerImpl.dll"" />
                        <ToolboxItems VSCategory = ""Toolbox.Default"" />
                    </File>
                </FileList>";

                File.WriteAllText(manifestFile, manifestExtensionSDK);
                sdkManifest = new SDKManifest(manifestPath);

                sdkManifest.AppxLocations.ShouldContainKey("AppX-Debug-x86");
                sdkManifest.AppxLocations.ShouldContainKey("AppX-Debug-x64");
                sdkManifest.AppxLocations.ShouldContainKey("AppX-Debug-ARM");

                sdkManifest.AppxLocations.ShouldContainKey("AppX-Retail-x86");
                sdkManifest.AppxLocations.ShouldContainKey("AppX-Retail-x64");
                sdkManifest.AppxLocations.ShouldContainKey("AppX-Retail-ARM");

                sdkManifest.AppxLocations["AppX-Debug-x86"].ShouldBe(".\\AppX\\Debug\\x86\\Microsoft.MySDK.x86.Debug.1.0.appx");
                sdkManifest.AppxLocations["AppX-Debug-x64"].ShouldBe(".\\AppX\\Debug\\x64\\Microsoft.MySDK.x64.Debug.1.0.appx");
                sdkManifest.AppxLocations["AppX-Debug-ARM"].ShouldBe(".\\AppX\\Debug\\ARM\\Microsoft.MySDK.ARM.Debug.1.0.appx");

                sdkManifest.AppxLocations["AppX-Retail-x86"].ShouldBe(".\\AppX\\Retail\\x86\\Microsoft.MySDK.x86.1.0.appx");
                sdkManifest.AppxLocations["AppX-Retail-x64"].ShouldBe(".\\AppX\\Retail\\x64\\Microsoft.MySDK.x64.1.0.appx");
                sdkManifest.AppxLocations["AppX-Retail-ARM"].ShouldBe(".\\AppX\\Retail\\ARM\\Microsoft.MySDK.ARM.1.0.appx");

                sdkManifest.CopyRedistToSubDirectory.ShouldBe(".");
                sdkManifest.DependsOnSDK.ShouldBe("SDKB, version=2.0");
                sdkManifest.DisplayName.ShouldBe("My SDK");

                sdkManifest.FrameworkIdentities.ShouldContainKey("FrameworkIdentity-Debug");
                sdkManifest.FrameworkIdentities.ShouldContainKey("FrameworkIdentity-Retail");

                sdkManifest.FrameworkIdentities["FrameworkIdentity-Debug"].ShouldBe("Name=MySDK.10.Debug, MinVersion=1.0.0.0");
                sdkManifest.FrameworkIdentities["FrameworkIdentity-Retail"].ShouldBe("Name=MySDK.10, MinVersion=1.0.0.0");

                sdkManifest.FrameworkIdentity.ShouldBeNull();
                sdkManifest.MaxPlatformVersion.ShouldBe("8.0");
                sdkManifest.MinVSVersion.ShouldBe("11.0");
                sdkManifest.MinOSVersion.ShouldBe("6.2.1");
                sdkManifest.MaxOSVersionTested.ShouldBe("6.2.3");
                sdkManifest.PlatformIdentity.ShouldBeNull();
                sdkManifest.ProductFamilyName.ShouldBe("UnitTest SDKs");
                sdkManifest.SDKType.ShouldBe(SDKType.Unspecified);
                sdkManifest.SupportedArchitectures.ShouldBe("x86;x64;ARM");
                sdkManifest.SupportPrefer32Bit.ShouldBe("True");
                sdkManifest.SupportsMultipleVersions.ShouldBe(MultipleVersionSupport.Error);
                sdkManifest.MoreInfo.ShouldBe("http://msdn.microsoft.com/MySDK");
                sdkManifest.ReadError.ShouldBeFalse();

                File.WriteAllText(manifestFile, "Hello");
                sdkManifest = new SDKManifest(manifestPath);

                sdkManifest.ReadError.ShouldBeTrue();
            }
            finally
            {
                FileUtilities.DeleteWithoutTrailingBackslash(manifestPath, true /* for recursive deletion */);
            }
        }

        /// <summary>
        /// Verify ExtensionSDK
        /// </summary>
        [Fact]
        public void VerifyExtensionSDK()
        {
            string manifestPath = Path.Combine(Path.GetTempPath(), "ManifestTmp");

            try
            {
                Directory.CreateDirectory(manifestPath);

                string manifestFile = Path.Combine(manifestPath, "SDKManifest.xml");

                string manifestExtensionSDK = @"
                <FileList
	                DisplayName = ""My SDK""
	                ProductFamilyName = ""UnitTest SDKs""
	                FrameworkIdentity-Debug = ""Name=MySDK.10.Debug, MinVersion=1.0.0.0""
                    FrameworkIdentity-Retail = ""Name=MySDK.10, MinVersion=1.0.0.0""
	                TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
	                MinVSVersion = ""11.0""
                    AppliesTo = ""WindowsAppContainer + WindowsXAML""
	                SupportPrefer32Bit = ""True""
	                SupportedArchitectures = ""x86;x64;ARM""
	                SupportsMultipleVersions = ""Error""
	                AppX-Debug-x86 = "".\AppX\Debug\x86\Microsoft.MySDK.x86.Debug.1.0.appx""
	                AppX-Debug-x64 = "".\AppX\Debug\x64\Microsoft.MySDK.x64.Debug.1.0.appx""
	                AppX-Debug-ARM = "".\AppX\Debug\ARM\Microsoft.MySDK.ARM.Debug.1.0.appx""
	                AppX-Retail-x86 = "".\AppX\Retail\x86\Microsoft.MySDK.x86.1.0.appx""
	                AppX-Retail-x64 = "".\AppX\Retail\x64\Microsoft.MySDK.x64.1.0.appx""
	                AppX-Retail-ARM = "".\AppX\Retail\ARM\Microsoft.MySDK.ARM.1.0.appx""
                    CopyRedistToSubDirectory = "".""
                    DependsOn = ""SDKB, version=2.0""
                    MoreInfo = ""http://msdn.microsoft.com/MySDK""
                    MaxPlatformVersion = ""8.0""
                    MinOSVersion = ""6.2.1""
                    MaxOSVersionTested = ""6.2.1"">

                    <File Reference = ""MySDK.Sprint.winmd"" Implementation = ""XNASprintImpl.dll"">
                        <Registration Type = ""Flipper"" Implementation = ""XNASprintFlipperImpl.dll"" />
                        <Registration Type = ""Flexer"" Implementation = ""XNASprintFlexerImpl.dll"" />
                        <ToolboxItems VSCategory = ""Toolbox.Default"" />
                    </File>
                </FileList>";

                File.WriteAllText(manifestFile, manifestExtensionSDK);
                ExtensionSDK extensionSDK = new ExtensionSDK(
                    $"CppUnitTestFramework, Version={ObjectModelHelpers.CurrentVisualStudioVersion}", manifestPath);

                extensionSDK.Identifier.ShouldBe("CppUnitTestFramework");
                extensionSDK.MaxPlatformVersion.ShouldBe(new Version("8.0"));
                extensionSDK.MinVSVersion.ShouldBe(new Version("11.0"));
                extensionSDK.Version.ShouldBe(new Version(ObjectModelHelpers.CurrentVisualStudioVersion));
            }
            finally
            {
                FileUtilities.DeleteWithoutTrailingBackslash(manifestPath, true /* for recursive deletion */);
            }
        }

        /// <summary>
        /// Verify Platform SDKs are filtered correctly
        /// </summary>
        [Fact]
        public void VerifyFilterPlatformSdks()
        {
            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", "True");

                IList<TargetPlatformSDK> sdkList = ToolLocationHelper.GetTargetPlatformSdks(new[] { _fakeStructureRoot }, null);
                IList<TargetPlatformSDK> filteredSdkList = ToolLocationHelper.FilterTargetPlatformSdks(sdkList, new Version(6, 2, 5), new Version(12, 0));
                IList<TargetPlatformSDK> filteredSdkList1 = ToolLocationHelper.FilterTargetPlatformSdks(sdkList, new Version(6, 2, 1), new Version(10, 0));
                IList<TargetPlatformSDK> filteredSdkList2 = ToolLocationHelper.FilterTargetPlatformSdks(sdkList, new Version(6, 2, 3), new Version(10, 0));
                IList<TargetPlatformSDK> filteredSdkList3 = ToolLocationHelper.FilterTargetPlatformSdks(sdkList, new Version(6, 2, 3), new Version(11, 0));

                // Filter based only on OS version
                IList<TargetPlatformSDK> filteredSdkList4 = ToolLocationHelper.FilterTargetPlatformSdks(sdkList, new Version(6, 2, 3), null);

                // Filter based only on VS version
                IList<TargetPlatformSDK> filteredSdkList5 = ToolLocationHelper.FilterTargetPlatformSdks(sdkList, null, new Version(10, 0));

                // Pass both versions as null. Don't filter anything
                IList<TargetPlatformSDK> filteredSdkList6 = ToolLocationHelper.FilterTargetPlatformSdks(sdkList, null, null);

                sdkList.Count.ShouldBe(7);
                filteredSdkList.Count.ShouldBe(7);
                filteredSdkList1.Count.ShouldBe(2);
                filteredSdkList2.Count.ShouldBe(3);
                filteredSdkList3.Count.ShouldBe(4);
                filteredSdkList4.Count.ShouldBe(5);
                filteredSdkList5.Count.ShouldBe(5);
                filteredSdkList6.Count.ShouldBe(7);

                filteredSdkList2[0].TargetPlatformIdentifier.ShouldBe("MyPlatform");
                filteredSdkList2[2].TargetPlatformVersion.ShouldBe(new Version(3, 0));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", null);
            }
        }

        /// <summary>
        /// Verify Extension SDKs are filtered correctly
        /// </summary>
        [Fact]
        public void VerifyFilterPlatformExtensionSdks()
        {
            // Create fake directory tree
            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", "True");

                IDictionary<string, string> extensionSDKs = ToolLocationHelper.GetPlatformExtensionSDKLocations(new[] { _fakeStructureRoot }, null, "MyPlatform", new Version(4, 0));
                IDictionary<string, string> filteredExtensionSDKs1 = ToolLocationHelper.FilterPlatformExtensionSDKs(new Version(8, 0), extensionSDKs);
                IDictionary<string, string> filteredExtensionSDKs2 = ToolLocationHelper.FilterPlatformExtensionSDKs(new Version(9, 0), extensionSDKs);
                IDictionary<string, string> filteredExtensionSDKs3 = ToolLocationHelper.FilterPlatformExtensionSDKs(new Version(10, 0), extensionSDKs);

                filteredExtensionSDKs1.Count.ShouldBe(2);
                filteredExtensionSDKs2.Count.ShouldBe(1);
                filteredExtensionSDKs3.Count.ShouldBe(0);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", null);
            }
        }

        /// <summary>
        /// Verify that the GetPlatformExtensionSDKLocation method can be correctly called during evaluation time as a msbuild function.
        /// </summary>
        [Fact]
        public void VerifyGetInstalledSDKLocations()
        {
            string testDirectoryRoot = Path.Combine(Path.GetTempPath(), "VerifyGetInstalledSDKLocations");
            string platformDirectory = Path.Combine(testDirectoryRoot, "MyPlatform", "8.0")
                                       + Path.DirectorySeparatorChar;
            string sdkDirectory = Path.Combine(platformDirectory, "ExtensionSDKs", "SDkWithManifest", "2.0")
                                  + Path.DirectorySeparatorChar;

            string tempProjectContents = ObjectModelHelpers.CleanupFileContents(@"
             <Project DefaultTargets=""ExpandSDKReferenceAssemblies"" ToolsVersion=""msbuilddefaulttoolsversion"" xmlns=""msbuildnamespace"">
                 <PropertyGroup>
                    <TargetPlatformIdentifier>MyPlatform</TargetPlatformIdentifier>
                    <TargetPlatformVersion>8.0</TargetPlatformVersion>
                    <SDKLocation1>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformExtensionSDKLocation('SDkWithManifest, Version=2.0','MyPlatform','8.0'))</SDKLocation1>
                    <SDKLocation2>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformExtensionSDKLocation('SDkWithManifest, Version=V2.0','MyPlatform','8.0'))</SDKLocation2>
                    <SDKLocation3>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformSDKLocation('MyPlatform','8.0'))</SDKLocation3>
                    <SDKName>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformSDKDisplayName('MyPlatform','8.0'))</SDKName>
                 </PropertyGroup>

                 <Import Project=""$(MSBuildBinPath)\Microsoft.Common.targets""/>
              </Project>");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDSDKREFERENCEDIRECTORY", testDirectoryRoot);
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", "true");

                if (Directory.Exists(testDirectoryRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(testDirectoryRoot, true);
                }

                Directory.CreateDirectory(testDirectoryRoot);
                Directory.CreateDirectory(sdkDirectory);
                File.WriteAllText(Path.Combine(platformDirectory, "SDKManifest.xml"), "HI");
                File.WriteAllText(Path.Combine(sdkDirectory, "SDKManifest.xml"), "HI");
                string testProjectFile = Path.Combine(testDirectoryRoot, "testproject.csproj");

                File.WriteAllText(testProjectFile, tempProjectContents);

                ProjectCollection pc = new ProjectCollection();
                Project project = pc.LoadProject(testProjectFile);
                string propertyValue1 = project.GetPropertyValue("SDKLocation1");
                string propertyValue2 = project.GetPropertyValue("SDKLocation2");
                string propertyValue3 = project.GetPropertyValue("SDKLocation3");
                string sdkName = project.GetPropertyValue("SDKName");

                propertyValue1.ShouldBe(sdkDirectory, StringCompareShould.IgnoreCase);
                propertyValue2.Length.ShouldBe(0);
                propertyValue3.ShouldBe(platformDirectory, StringCompareShould.IgnoreCase);

                // No displayname set in the SDK manifest, so it mocks one up
                sdkName.ShouldBe("MyPlatform 8.0");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDSDKREFERENCEDIRECTORY", null);
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", null);
                if (Directory.Exists(testDirectoryRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(testDirectoryRoot, true);
                }
            }
        }

        /// <summary>
        /// Verify that the GetPlatformExtensionSDKLocation method can be correctly called during evaluation time as a msbuild function.
        /// </summary>
        [Fact]
        public void VerifyGetInstalledSDKLocations2()
        {
            string testDirectoryRoot = Path.Combine(Path.GetTempPath(), "VerifyGetInstalledSDKLocations2");
            string platformDirectory = Path.Combine(testDirectoryRoot, "MyPlatform", "8.0")
                                       + Path.DirectorySeparatorChar;
            string sdkDirectory = Path.Combine(platformDirectory, "ExtensionSDKs", "SDkWithManifest", "2.0")
                                  + Path.DirectorySeparatorChar;

            string tempProjectContents = ObjectModelHelpers.CleanupFileContents(@"
             <Project DefaultTargets=""ExpandSDKReferenceAssemblies"" ToolsVersion=""msbuilddefaulttoolsversion"" xmlns=""msbuildnamespace"">
                 <PropertyGroup>
                    <TargetPlatformIdentifier>MyPlatform</TargetPlatformIdentifier>
                    <TargetPlatformVersion>8.0</TargetPlatformVersion>" +
                   @"<SDKDirectoryRoot>" + testDirectoryRoot + "</SDKDirectoryRoot>" +
                    @"<SDKLocation1>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformExtensionSDKLocation('SDkWithManifest, Version=2.0','MyPlatform','8.0', '$(SDKDirectoryRoot)',''))</SDKLocation1>
                      <SDKLocation2>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformExtensionSDKLocation('SDkWithManifest, Version=V2.0','MyPlatform','8.0', '$(SDKDirectoryRoot)',''))</SDKLocation2>                 
                      <SDKLocation3>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformSDKLocation('MyPlatform','8.0', '$(SDKDirectoryRoot)',''))</SDKLocation3>
                      <SDKName>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformSDKDisplayName('MyPlatform','8.0', '$(SDKDirectoryRoot)', ''))</SDKName>
                 </PropertyGroup>

                 <Import Project=""$(MSBuildBinPath)\Microsoft.Common.targets""/>
              </Project>");

            string platformSDKManifestContents = @"<FileList
                    DisplayName = ""My cool platform SDK!"">
                </FileList>";

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", "true");
                if (Directory.Exists(testDirectoryRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(testDirectoryRoot, true);
                }

                Directory.CreateDirectory(testDirectoryRoot);
                Directory.CreateDirectory(sdkDirectory);
                File.WriteAllText(Path.Combine(platformDirectory, "SDKManifest.xml"), platformSDKManifestContents);
                File.WriteAllText(Path.Combine(sdkDirectory, "SDKManifest.xml"), "HI");
                string testProjectFile = Path.Combine(testDirectoryRoot, "testproject.csproj");

                File.WriteAllText(testProjectFile, tempProjectContents);

                ProjectCollection pc = new ProjectCollection();
                Project project = pc.LoadProject(testProjectFile);
                string propertyValue1 = project.GetPropertyValue("SDKLocation1");
                string propertyValue2 = project.GetPropertyValue("SDKLocation2");
                string propertyValue3 = project.GetPropertyValue("SDKLocation3");
                string sdkName = project.GetPropertyValue("SDKName");

                propertyValue1.ShouldBe(sdkDirectory, StringCompareShould.IgnoreCase);
                propertyValue3.ShouldBe(platformDirectory, StringCompareShould.IgnoreCase);
                propertyValue2.Length.ShouldBe(0);
                sdkName.ShouldBe("My cool platform SDK!");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", null);
                if (Directory.Exists(testDirectoryRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(testDirectoryRoot, true);
                }
            }
        }

#if FEATURE_REGISTRY_SDKS
        /// <summary>
        /// Setup some fake entries in the registry and verify we get the correct sdk from there.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyGetInstalledSDKLocations3()
        {
            string testDirectoryRoot = Path.Combine(Path.GetTempPath(), "VerifyGetInstalledSDKLocations3");
            string platformDirectory = Path.Combine(testDirectoryRoot, "MyPlatform", "8.0")
                                       + Path.DirectorySeparatorChar;
            string sdkDirectory = Path.Combine(platformDirectory, "ExtensionSDKs", "SDkWithManifes", "2.0")
                                  + Path.DirectorySeparatorChar;

            string tempProjectContents = ObjectModelHelpers.CleanupFileContents(@"
             <Project DefaultTargets=""ExpandSDKReferenceAssemblies"" ToolsVersion=""msbuilddefaulttoolsversion"" xmlns=""msbuildnamespace"">
                 <PropertyGroup>
                    <TargetPlatformIdentifier>MyPlatform</TargetPlatformIdentifier>
                    <TargetPlatformVersion>8.0</TargetPlatformVersion>
                    <SDKRegistryRoot>SOFTWARE\Microsoft\VerifyGetInstalledSDKLocations3</SDKRegistryRoot>
                    <SDKDiskRoot>Somewhere</SDKDiskRoot>
                    <SDKLocation1>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformExtensionSDKLocation('SDkWithManifest, Version=2.0','MyPlatform','8.0', '$(SDKDirectoryRoot)','$(SDKRegistryRoot)'))</SDKLocation1>
                    <SDKLocation2>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformExtensionSDKLocation('SDkWithManifest, Version=V2.0','MyPlatform','8.0', '$(SDKDirectoryRoot)','$(SDKRegistryRoot)'))</SDKLocation2>
                    <SDKLocation3>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformSDKLocation('MyPlatform','8.0', '$(SDKDirectoryRoot)','$(SDKRegistryRoot)'))</SDKLocation3>
                    <SDKName>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformSDKDisplayName('MyPlatform','8.0', '$(SDKDirectoryRoot)', '$(SDKRegistryRoot)'))</SDKName>
                 </PropertyGroup>
                 <Import Project=""$(MSBuildBinPath)\Microsoft.Common.targets""/>
              </Project>");

            string platformSDKManifestContents = @"<FileList
                    DisplayName = ""MyPlatform from the registry""
                    PlatformIdentity = ""MyPlatform, version=8.0""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""12.0""
                    MinOSVersion = ""6.2.1""
                    MaxOSVersionTested = ""6.2.1""
                    UnsupportedDowntarget = ""MyPlatform, version=8.0"">

                <File Reference = ""Windows"">
	                <ToolboxItems VSCategory = ""Toolbox.Default""/>
                </File>
            </FileList>";

            string registryKey = @"SOFTWARE\Microsoft\VerifyGetInstalledSDKLocations3\";
            RegistryKey baseKey = Registry.CurrentUser;

            try
            {
                if (NativeMethodsShared.IsWindows)
                {
                    RegistryKey folderKey = baseKey.CreateSubKey(registryKey + @"\MyPlatform\v8.0\ExtensionSDKS\SDKWithManifest\2.0");
                    folderKey.SetValue("", Path.Combine(testDirectoryRoot, sdkDirectory));

                    folderKey = baseKey.CreateSubKey(registryKey + @"\MyPlatform\v8.0");
                    folderKey.SetValue("", Path.Combine(testDirectoryRoot, platformDirectory));
                }

                if (Directory.Exists(testDirectoryRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(testDirectoryRoot, true);
                }

                Directory.CreateDirectory(testDirectoryRoot);
                Directory.CreateDirectory(sdkDirectory);
                File.WriteAllText(Path.Combine(sdkDirectory, "SDKManifest.xml"), "HI");
                File.WriteAllText(Path.Combine(platformDirectory, "SDKManifest.xml"), platformSDKManifestContents);

                string testProjectFile = Path.Combine(testDirectoryRoot, "testproject.csproj");

                File.WriteAllText(testProjectFile, tempProjectContents);

                ProjectCollection pc = new ProjectCollection();
                Project project = pc.LoadProject(testProjectFile);
                string propertyValue1 = project.GetPropertyValue("SDKLocation1");
                string propertyValue2 = project.GetPropertyValue("SDKLocation2");
                string propertyValue3 = project.GetPropertyValue("SDKLocation3");
                string sdkName = project.GetPropertyValue("SDKName");

                propertyValue1.ShouldBe(sdkDirectory, StringCompareShould.IgnoreCase);
                propertyValue3.ShouldBe(platformDirectory, StringCompareShould.IgnoreCase);
                propertyValue2.Length.ShouldBe(0);
                sdkName.ShouldBe("MyPlatform from the registry");
            }
            finally
            {
                try
                {
                    if (NativeMethodsShared.IsWindows)
                    {
                        baseKey.DeleteSubKeyTree(registryKey);
                    }
                }
                catch (Exception)
                {
                }
                if (Directory.Exists(testDirectoryRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(testDirectoryRoot, true);
                }
            }
        }
#endif

        /// <summary>
        /// Verify based on a fake directory structure with some good directories and some invalid ones at each level that we
        /// get the expected set out.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void ResolveSDKFromDirectory()
        {
            var paths = new List<string> { _fakeStructureRoot, _fakeStructureRoot2 };
            var targetPlatforms = new Dictionary<TargetPlatformSDK, TargetPlatformSDK>();

            ToolLocationHelper.GatherSDKListFromDirectory(paths, targetPlatforms);

            TargetPlatformSDK key = new TargetPlatformSDK("Windows", new Version("1.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(2);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("MyAssembly, Version=1.0");
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "v1.0", "ExtensionSDKs", "MyAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("MyAssembly, Version=2.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=2.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "1.0", "ExtensionSDKs", "MyAssembly", "2.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("Windows", new Version("2.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(2);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "2.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("MyAssembly, Version=3.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=3.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "2.0", "ExtensionSDKs", "MyAssembly", "3.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("MyAssembly, Version=4.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=4.0"].ShouldBe(Path.Combine(_fakeStructureRoot2, "Windows", "2.0", "ExtensionSDKs", "MyAssembly", "4.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            // Windows kits special case is only in registry
            key = new TargetPlatformSDK("MyPlatform", new Version("6.0"), null);
            targetPlatforms.ContainsKey(key).ShouldBeFalse();

            key = new TargetPlatformSDK("MyPlatform", new Version("4.0"), null);
            targetPlatforms[key].Path.ShouldBeNull();
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(1);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("AnotherAssembly, Version=1.0");
            targetPlatforms[key].ExtensionSDKs["AnotherAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "4.0", "ExtensionSDKs", "AnotherAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("MyPlatform", new Version("3.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(1);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "3.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("MyAssembly, Version=1.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "3.0", "ExtensionSDKs", "MyAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("MyPlatform", new Version("2.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(1);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "2.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("MyAssembly, Version=1.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "2.0", "ExtensionSDKs", "MyAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("MyPlatform", new Version("1.0"), null);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(0);

            key = new TargetPlatformSDK("MyPlatform", new Version("8.0"), null);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "8.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(0);
            targetPlatforms[key].Platforms.Count.ShouldBe(3);
            targetPlatforms[key].ContainsPlatform("PlatformAssembly", "0.1.2.3").ShouldBeTrue();
            targetPlatforms[key].Platforms["PlatformAssembly, Version=0.1.2.3"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "8.0", "Platforms", "PlatformAssembly", "0.1.2.3") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ContainsPlatform("PlatformAssembly", "1.2.3.0").ShouldBeTrue();
            targetPlatforms[key].ContainsPlatform("Sparkle", "3.3.3.3").ShouldBeTrue();

            key = new TargetPlatformSDK("MyPlatform", new Version("9.0"), null);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "9.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(0);
            targetPlatforms[key].Platforms.Count.ShouldBe(1);
            targetPlatforms[key].ContainsPlatform("PlatformAssembly", "0.1.2.3").ShouldBeTrue();
            targetPlatforms[key].Platforms["PlatformAssembly, Version=0.1.2.3"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "9.0", "Platforms", "PlatformAssembly", "0.1.2.3") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
        }

#if FEATURE_REGISTRY_SDKS
        /// <summary>
        /// Verify based on a fake directory structure with some good directories and some invalid ones at each level that we
        /// get the expected set out.
        /// </summary>
        [Fact]
        public void ResolveSDKFromRegistry()
        {
            if (!NativeMethodsShared.IsWindows)
            {
                return; // "No registry unless under Windows"
            }

            var targetPlatforms = new Dictionary<TargetPlatformSDK, TargetPlatformSDK>();

            ToolLocationHelper.GatherSDKsFromRegistryImpl(targetPlatforms, "Software\\Microsoft\\MicrosoftSDks", RegistryView.Registry32, RegistryHive.CurrentUser, getRegistrySubKeyNames, getRegistrySubKeyDefaultValue, _openBaseKey, File.Exists);
            ToolLocationHelper.GatherSDKsFromRegistryImpl(targetPlatforms, "Software\\Microsoft\\MicrosoftSDks", RegistryView.Registry32, RegistryHive.LocalMachine, getRegistrySubKeyNames, getRegistrySubKeyDefaultValue, _openBaseKey, File.Exists);

            TargetPlatformSDK key = new TargetPlatformSDK("Windows", new Version("1.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(2);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("MyAssembly, Version=1.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "v1.0", "ExtensionSDKs", "MyAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("MyAssembly, Version=2.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=2.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "1.0", "ExtensionSDKs", "MyAssembly", "2.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("Windows", new Version("2.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(1);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "2.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("MyAssembly, Version=3.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=3.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "2.0", "ExtensionSDKs", "MyAssembly", "3.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("MyPlatform", new Version("5.0"), null);
            targetPlatforms.ShouldContainKey(key);
            targetPlatforms[key].Path.ShouldBeNull();

            key = new TargetPlatformSDK("MyPlatform", new Version("6.0"), null);
            targetPlatforms.ShouldContainKey(key);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "Windows Kits", "6.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("MyPlatform", new Version("4.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(1);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "SomeOtherPlace", "MyPlatformOtherLocation", "4.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.ShouldContainKey("MyAssembly, Version=1.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "SomeOtherPlace", "MyPlatformOtherLocation", "4.0", "ExtensionSDKs", "MyAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("MyPlatform", new Version("9.0"), null);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "9.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(0);
            targetPlatforms[key].Platforms.Count.ShouldBe(1);
            targetPlatforms[key].ContainsPlatform("PlatformAssembly", "0.1.2.3").ShouldBeTrue();
            targetPlatforms[key].Platforms["PlatformAssembly, Version=0.1.2.3"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "9.0", "Platforms", "PlatformAssembly", "0.1.2.3") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
        }

        /// <summary>
        /// Verify based on a fake directory structure with some good directories and some invalid ones at each level that we
        /// get the expected set out. Make sure that when we resolve from both the disk and registry that there are no duplicates
        /// and make sure we get the expected results.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void ResolveSDKFromRegistryAndDisk()
        {
            var targetPlatforms = new Dictionary<TargetPlatformSDK, TargetPlatformSDK>();

            var paths = new List<string> { _fakeStructureRoot };

            ToolLocationHelper.GatherSDKListFromDirectory(paths, targetPlatforms);

            if (NativeMethodsShared.IsWindows)
            {
                ToolLocationHelper.GatherSDKsFromRegistryImpl(targetPlatforms, "Software\\Microsoft\\MicrosoftSDks", RegistryView.Registry32, RegistryHive.CurrentUser, getRegistrySubKeyNames, getRegistrySubKeyDefaultValue, _openBaseKey, File.Exists);
                ToolLocationHelper.GatherSDKsFromRegistryImpl(targetPlatforms, "Software\\Microsoft\\MicrosoftSDks", RegistryView.Registry32, RegistryHive.LocalMachine, getRegistrySubKeyNames, getRegistrySubKeyDefaultValue, _openBaseKey, File.Exists);
            }

            TargetPlatformSDK key = new TargetPlatformSDK("Windows", new Version("1.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(2);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.Keys.ShouldContain("MyAssembly, Version=1.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "v1.0", "ExtensionSDKs", "MyAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.Keys.ShouldContain("MyAssembly, Version=2.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=2.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "1.0", "ExtensionSDKs", "MyAssembly", "2.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("Windows", new Version("2.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(1);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "2.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.Keys.ShouldContain("MyAssembly, Version=3.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=3.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "2.0", "ExtensionSDKs", "MyAssembly", "3.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            // This is present in the registry but not on disk.
            key = new TargetPlatformSDK("MyPlatform", new Version("6.0"), null);
            targetPlatforms.Keys.ShouldContain(key);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "Windows Kits", "6.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("MyPlatform", new Version("5.0"), null);
            targetPlatforms.Keys.ShouldContain(key);
            targetPlatforms[key].ExtensionSDKs.ShouldBeEmpty();
            targetPlatforms[key].Path.ShouldBeNull();

            key = new TargetPlatformSDK("MyPlatform", new Version("4.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(2);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "SomeOtherPlace", "MyPlatformOtherLocation", "4.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.Keys.ShouldContain("MyAssembly, Version=1.0");
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "SomeOtherPlace", "MyPlatformOtherLocation", "4.0", "ExtensionSDKs", "MyAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.Keys.ShouldContain("AnotherAssembly, Version=1.0");
            targetPlatforms[key].ExtensionSDKs["AnotherAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "4.0", "ExtensionSDKs", "AnotherAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("MyPlatform", new Version("3.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(1);
            targetPlatforms[key].ExtensionSDKs.Keys.ShouldContain("MyAssembly, Version=1.0");
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "3.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "3.0", "ExtensionSDKs", "MyAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("MyPlatform", new Version("2.0"), null);
            targetPlatforms[key].ExtensionSDKs.Count.ShouldBe(1);
            targetPlatforms[key].ExtensionSDKs.Keys.ShouldContain("MyAssembly, Version=1.0");
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "2.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs["MyAssembly, Version=1.0"].ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "2.0", "ExtensionSDKs", "MyAssembly", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

            key = new TargetPlatformSDK("MyPlatform", new Version("1.0"), null);
            targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);
            targetPlatforms[key].ExtensionSDKs.ShouldBeEmpty();
        }
#endif

        /// <summary>
        /// Make sure if the sdk identifier is null we get an ArgumentNullException because without specifying the
        /// sdk identifier we can't get any platforms back.
        /// </summary>
        [Fact]
        public void GetPlatformsForSDKNullSDKIdentifier()
        {
            Should.Throw<ArgumentNullException>(() => ToolLocationHelper.GetPlatformsForSDK(null, new Version("1.0")));
        }

        /// <summary>
        /// Make sure if the sdk version is null we get an ArgumentNullException because without specifying the
        /// sdk version we can't get any platforms back.
        /// </summary>
        [Fact]
        public void GetPlatformsForSDKNullSDKVersion()
        {
            Should.Throw<ArgumentNullException>(() => ToolLocationHelper.GetPlatformsForSDK("AnySDK", null));
        }

        /// <summary>
        /// Verify that when there are no sdks with target platforms installed, our list of platforms is empty
        /// to make sure we are not getting platforms from somewhere else.
        /// </summary>
        [Fact]
        public void GetPlatformsForSDKWithNoInstalledTargetPlatforms()
        {
            ToolLocationHelper.GetPlatformsForSDK("AnySDK", new Version("1.0"), new string[0], "").Any().ShouldBeFalse();
        }

        /// <summary>
        /// Verify that the list of platforms returned is exactly as we expect when we have platforms
        /// installed and we pass in a matching sdk identifier and version number for one of the
        /// installed platforms.
        /// </summary>
        [Fact]
        public void GetPlatformsForSDKWithMatchingInstalledTargetPlatforms()
        {
            IEnumerable<string> myPlatforms = ToolLocationHelper.GetPlatformsForSDK("MyPlatform", new Version("8.0"), new[] { _fakeStructureRoot }, null);
            myPlatforms.ShouldContain("Sparkle, Version=3.3.3.3");
            myPlatforms.ShouldContain("PlatformAssembly, Version=0.1.2.3");
            myPlatforms.ShouldContain("PlatformAssembly, Version=1.2.3.0");
            myPlatforms.Count().ShouldBe(3);
        }

        /// <summary>
        /// Verify that the list of platforms is empty if we ask for an sdk that is not installed.
        /// </summary>
        [Fact]
        public void GetPlatformsForSDKWithInstalledTargetPlatformsNoMatch()
        {
            ToolLocationHelper.GetPlatformsForSDK("DoesNotExistPlatform", new Version("0.0.0.0"), new[] { _fakeStructureRoot }, null).Any().ShouldBeFalse();
        }

        /// <summary>
        /// Verify that the list of platforms is empty if we ask for a valid sdk identifier but
        /// a version number that isn't installed.
        /// </summary>
        [Fact]
        public void GetPlatformsForSDKWithMatchingPlatformNotMatchingVersion()
        {
            ToolLocationHelper.GetPlatformsForSDK("MyPlatform", new Version("0.0.0.0"), new[] {_fakeStructureRoot}, null).Any().ShouldBeFalse();
        }

        /// <summary>
        /// Verify that if we pass in an sdk identifier and version for an installed legacy platform sdk
        /// that the list of platforms is empty because it has no platforms.
        /// </summary>
        [Fact]
        public void GetPlatformsForSDKForLegacyPlatformSDK()
        {
            ToolLocationHelper.GetPlatformsForSDK("Windows", new Version("8.0"), new[] {_fakeStructureRoot}, null).Any().ShouldBeFalse();
        }

        /// <summary>
        /// Verify based on a fake directory structure with some good directories and some invalid ones at each level that we
        /// get the expected set out. Make sure that when we resolve from both the disk and registry that there are no duplicates
        /// and make sure we get the expected results.
        /// </summary>
        [Fact]
        public void GetALLTargetPlatformSDKs()
        {
            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", "true");
                IList<TargetPlatformSDK> sdks = ToolLocationHelper.GetTargetPlatformSdks(new[] { _fakeStructureRoot }, null);

                var targetPlatforms = new Dictionary<TargetPlatformSDK, TargetPlatformSDK>();
                foreach (TargetPlatformSDK sdk in sdks)
                {
                    targetPlatforms.Add(sdk, sdk);
                }

                TargetPlatformSDK key = new TargetPlatformSDK("Windows", new Version("1.0"), null);
                targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

                key = new TargetPlatformSDK("Windows", new Version("2.0"), null);
                targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "Windows", "2.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

                key = new TargetPlatformSDK("MyPlatform", new Version("3.0"), null);
                targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "3.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

                key = new TargetPlatformSDK("MyPlatform", new Version("2.0"), null);
                targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "2.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

                key = new TargetPlatformSDK("MyPlatform", new Version("1.0"), null);
                targetPlatforms[key].Path.ShouldBe(Path.Combine(_fakeStructureRoot, "MyPlatform", "1.0") + Path.DirectorySeparatorChar, StringCompareShould.IgnoreCase);

                key = new TargetPlatformSDK("MyPlatform", new Version("5.0"), null);
                targetPlatforms.ContainsKey(key).ShouldBeFalse();
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDISABLEREGISTRYFORSDKLOOKUP", null);
            }
        }

        /// <summary>
        /// Verify that the list of platforms is empty if we ask for an sdk that is not installed.
        /// </summary>
        [Fact]
        [PlatformSpecific(TestPlatforms.AnyUnix)]
        public void VerifyGetFoldersInVSInstalls_Unix()
        {
            ToolLocationHelper.GetFoldersInVSInstalls(null, null, "relativePath").Count().ShouldBe(0);
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void VerifyFindRootFolderWhereAllFilesExist()
        {
            // create directories and files in them

            //  root1
            //     subdir
            //         file1.txt
            //     file1.txt
            //  root2
            //     subdir
            //         file2.txt
            //     file1.txt

            string testDirectoryRoot = Path.Combine(Path.GetTempPath(), "VerifyFindRootFolderWhereAllFilesExist");
            string[] rootDirectories = new string[] { Path.Combine(testDirectoryRoot, "Root1"), Path.Combine(testDirectoryRoot, "Root2") };
            
            for(int i = 0; i < rootDirectories.Count(); i++)
            {
                // create directory
                string subdir = Path.Combine(rootDirectories[i], "Subdir");
                Directory.CreateDirectory(subdir);
                var fileInSubDir = string.Format("file{0}.txt", i+1);
                File.Create(Path.Combine(rootDirectories[i], "file1.txt")).Close();
                File.Create(Path.Combine(subdir, fileInSubDir)).Close();
            }

            string roots = string.Join(";", rootDirectories);

            ToolLocationHelper.FindRootFolderWhereAllFilesExist(roots, "file1.txt").ShouldBe(rootDirectories[0]);
            ToolLocationHelper.FindRootFolderWhereAllFilesExist(roots, @"file1.txt;subdir\file2.txt").ShouldBe(rootDirectories[1]);
            ToolLocationHelper.FindRootFolderWhereAllFilesExist(roots, @"file1.txt;subdir\file3.txt").ShouldBe(String.Empty);
            ToolLocationHelper.FindRootFolderWhereAllFilesExist(@"c:<>;" + roots, "file1.txt").ShouldBe(rootDirectories[0]); // should ignore invalid dir
        }

#if FEATURE_REGISTRY_SDKS
        /// <summary>
        /// Verify that the GetPlatformSDKPropsFileLocation method can be correctly called for pre-OneCore SDKs during evaluation time as a msbuild function.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyGetPreOneCoreSDKPropsLocation()
        {
            // This is the mockup layout for SDKs before One Core SDK.
            string testDirectoryRoot = Path.Combine(Path.GetTempPath(), "VerifyGetPreOneCoreSDKPropsLocation");
            string platformDirectory = Path.Combine(testDirectoryRoot, "MyPlatform", "8.0")
                                       + Path.DirectorySeparatorChar;
            string propsDirectory = Path.Combine(platformDirectory, "DesignTime", "CommonConfiguration", "Neutral");

            string tempProjectContents = ObjectModelHelpers.CleanupFileContents(@"
             <Project DefaultTargets=""ExpandSDKReferenceAssemblies"" ToolsVersion=""msbuilddefaulttoolsversion"" xmlns=""msbuildnamespace"">
                 <PropertyGroup>
                    <TargetPlatformIdentifier>MyPlatform</TargetPlatformIdentifier>
                    <TargetPlatformVersion>8.0</TargetPlatformVersion>
                    <SDKRegistryRoot>SOFTWARE\Microsoft\VerifyGetPlatformSDKPropsLocation</SDKRegistryRoot>
                    <SDKDiskRoot>Somewhere</SDKDiskRoot>
                    <PlatformSDKLocation>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformSDKLocation('MyPlatform', '8.0', '$(SDKDirectoryRoot)', '$(SDKRegistryRoot)'))</PlatformSDKLocation>
                    <PropsLocation>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformSDKPropsFileLocation('',' ','MyPlatform',' ','8.0', '$(SDKDirectoryRoot)', '$(SDKRegistryRoot)'))</PropsLocation>
                 </PropertyGroup>
                 <Import Project=""$(MSBuildBinPath)\Microsoft.Common.targets""/>
              </Project>");

            string registryKey = @"SOFTWARE\Microsoft\VerifyGetPlatformSDKPropsLocation\";
            RegistryKey baseKey = Registry.CurrentUser;

            try
            {
                if (NativeMethodsShared.IsWindows)
                {
                    using (RegistryKey platformKey = baseKey.CreateSubKey(registryKey + @"\MyPlatform\v8.0"))
                    {
                        platformKey.SetValue("InstallationFolder", platformDirectory);
                    }
                }

                if (Directory.Exists(testDirectoryRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(testDirectoryRoot, true);
                }

                Directory.CreateDirectory(testDirectoryRoot);
                Directory.CreateDirectory(propsDirectory);

                File.WriteAllText(Path.Combine(platformDirectory, "SDKManifest.xml"), "Test");

                Project project = ObjectModelHelpers.CreateInMemoryProject(new ProjectCollection(), tempProjectContents, null);

                string propertyValue = project.GetPropertyValue("PlatformSDKLocation");
                string propsLocation = project.GetPropertyValue("PropsLocation");

                propertyValue.ShouldBe(platformDirectory, StringCompareShould.IgnoreCase);
                propsLocation.ShouldBe(propsDirectory, StringCompareShould.IgnoreCase);
            }
            finally
            {
                try
                {
                    if (NativeMethodsShared.IsWindows)
                    {
                        baseKey.DeleteSubKeyTree(registryKey);
                    }
                }
                catch (Exception)
                {
                }
                if (Directory.Exists(testDirectoryRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(testDirectoryRoot, true);
                }
            }
        }

        /// <summary>
        /// Verify that the GetPlatformSDKPropsFileLocation method can be correctly called for OneCore SDK during evaluation time as a msbuild function.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyGetOneCoreSDKPropsLocation()
        {
            // This is the mockup layout for One Core SDK.
            string testDirectoryRoot = Path.Combine(Path.GetTempPath(), "VerifyGetOneCoreSDKPropsLocation");
            string platformDirectory = Path.Combine(testDirectoryRoot, "OneCoreSDK", "1.0") + Path.DirectorySeparatorChar;
            string propsDirectory =
                Path.Combine(platformDirectory, "DesignTime", "CommonConfiguration", "Neutral", "MyPlatform", "0.8.0.0");
            string platformDirectory2 = Path.Combine(platformDirectory, "Platforms", "MyPlatform", "0.8.0.0");

            string tempProjectContents = ObjectModelHelpers.CleanupFileContents(@"
             <Project DefaultTargets=""ExpandSDKReferenceAssemblies"" ToolsVersion=""msbuilddefaulttoolsversion"" xmlns=""msbuildnamespace"">
                 <PropertyGroup>
                    <TargetPlatformIdentifier>MyPlatform</TargetPlatformIdentifier>
                    <TargetPlatformVersion>8.0</TargetPlatformVersion>
                    <SDKRegistryRoot>SOFTWARE\Microsoft\VerifyGetOneCoreSDKPropsLocation</SDKRegistryRoot>
                    <SDKDiskRoot>Somewhere</SDKDiskRoot>
                    <PlatformSDKLocation>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformSDKLocation('OneCoreSDK', '1.0', '', '$(SDKRegistryRoot)'))</PlatformSDKLocation>
                    <PropsLocation>$([Microsoft.Build.Utilities.ToolLocationHelper]::GetPlatformSDKPropsFileLocation('OneCoreSDK','1.0','MyPlatform',' ','0.8.0.0', '', '$(SDKRegistryRoot)'))</PropsLocation>
                 </PropertyGroup>
                 <Import Project=""$(MSBuildBinPath)\Microsoft.Common.targets""/>
              </Project>");

            string registryKey = @"SOFTWARE\Microsoft\VerifyGetOneCoreSDKPropsLocation\";
            RegistryKey baseKey = Registry.CurrentUser;

            try
            {
                if (NativeMethodsShared.IsWindows)
                {
                    using (RegistryKey platformKey = baseKey.CreateSubKey(registryKey + @"\OneCoreSDK\1.0"))
                    {
                        platformKey.SetValue("InstallationFolder", platformDirectory);
                    }
                }

                if (Directory.Exists(testDirectoryRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(testDirectoryRoot, true);
                }

                Directory.CreateDirectory(testDirectoryRoot);
                Directory.CreateDirectory(propsDirectory);
                Directory.CreateDirectory(platformDirectory2);

                File.WriteAllText(Path.Combine(platformDirectory, "SDKManifest.xml"), "Test");
                File.WriteAllText(Path.Combine(platformDirectory2, "Platform.xml"), "Test");

                Project project = ObjectModelHelpers.CreateInMemoryProject(new ProjectCollection(), tempProjectContents, null);

                string propertyValue = project.GetPropertyValue("PlatformSDKLocation");
                string propsLocation = project.GetPropertyValue("PropsLocation");

                propertyValue.ShouldBe(platformDirectory, StringCompareShould.IgnoreCase);
                propsLocation.ShouldBe(propsDirectory, StringCompareShould.IgnoreCase);
            }
            finally
            {
                try
                {
                    if (NativeMethodsShared.IsWindows)
                    {
                        baseKey.DeleteSubKeyTree(registryKey);
                    }
                }
                catch (Exception)
                {
                }
                if (Directory.Exists(testDirectoryRoot))
                {
                    FileUtilities.DeleteDirectoryNoThrow(testDirectoryRoot, true);
                }
            }
        }
#endif

        /// <summary>
        /// Make a fake SDK structure on disk for testing.
        /// </summary>
        private static string MakeFakeSDKStructure()
        {
            string manifestPlatformSDK1 = @"
                <FileList
                    DisplayName = ""Windows""
                    PlatformIdentity = ""Windows, version=8.0""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""12.0""
                    MinOSVersion = ""6.2.1""
                    MaxOSVersionTested = ""6.2.1""
                    UnsupportedDowntarget = ""Windows, version=8.0"">

                <File Reference = ""Windows"">
	                <ToolboxItems VSCategory = ""Toolbox.Default""/>
                </File>
                </FileList>";

            string manifestPlatformSDK2 = @"
                <FileList
                    DisplayName = ""Windows""
                    PlatformIdentity = ""Windows, version=8.0""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""11.0""
                    MinOSVersion = ""6.2.2""
                    MaxOSVersionTested = ""6.2.1""
                    UnsupportedDowntarget = ""Windows, version=8.0"">

                <File Reference = ""Windows"">
	                <ToolboxItems VSCategory = ""Toolbox.Default""/>
                </File>
                </FileList>";

            string manifestPlatformSDK3 = @"
                <FileList
                    DisplayName = ""Windows""
                    PlatformIdentity = ""Windows, version=8.0""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""10.0""
                    MinOSVersion = ""6.2.3""
                    MaxOSVersionTested = ""6.2.1""
                    UnsupportedDowntarget = ""Windows, version=8.0"">

                <File Reference = ""Windows"">
	                <ToolboxItems VSCategory = ""Toolbox.Default""/>
                </File>
                </FileList>";

            string manifestPlatformSDK4 = @"
                <FileList
                    DisplayName = ""Windows""
                    PlatformIdentity = ""Windows, version=8.0""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""9.0""
                    MinOSVersion = ""6.2.4""
                    MaxOSVersionTested = ""6.2.1""
                    UnsupportedDowntarget = ""Windows, version=8.0"">

                <File Reference = ""Windows"">
	                <ToolboxItems VSCategory = ""Toolbox.Default""/>
                </File>
                </FileList>";

            string manifestPlatformSDK5 = @"
                <FileList
                    DisplayName = ""Windows""
                    PlatformIdentity = ""Windows, version=8.0""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""8.0""
                    MinOSVersion = ""6.2.5""
                    MaxOSVersionTested = ""6.2.1""
                    UnsupportedDowntarget = ""Windows, version=8.0"">

                <File Reference = ""Windows"">
	                <ToolboxItems VSCategory = ""Toolbox.Default""/>
                </File>
                </FileList>";

            string manifestPlatformSDK6 = @"
                <FileList
                    DisplayName = ""Windows""
                    PlatformIdentity = ""Windows, version=8.0""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MaxOSVersionTested = ""6.2.1""
                    UnsupportedDowntarget = ""Windows, version=8.0"">

                <File Reference = ""Windows"">
	                <ToolboxItems VSCategory = ""Toolbox.Default""/>
                </File>
                </FileList>";

            string manifestPlatformSDK7 = @"
                <FileList
                    DisplayName = ""Windows""
                    PlatformIdentity = ""Windows, version=8.0""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""8""
                    MinOSVersion = ""Blah""
                    MaxOSVersionTested = ""6.2.1""
                    UnsupportedDowntarget = ""Windows, version=8.0"">

                <File Reference = ""Windows"">
                    <ToolboxItems VSCategory = ""Toolbox.Default""/>
                </File>
                </FileList>";

            string manifestExtensionSDK1 = @"
                <FileList
                    DisplayName = ""ExtensionSDK2""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""11.0""
                    MaxPlatformVersion = ""8.0""
                    MinOSVersion = ""6.2.1""
                    MaxOSVersionTested = ""6.2.1"">

                    <File Reference = ""MySDK.Sprint.winmd"" Implementation = ""XNASprintImpl.dll"">
                    </File>
                </FileList>";

            string manifestExtensionSDK2 = @"
                <FileList
                    DisplayName = ""ExtensionSDK2""
                    TargetFramework = "".NETCore, version=v4.5; .NETFramework, version=v4.5""
                    MinVSVersion = ""11.0""
                    MaxPlatformVersion = ""9.0""
                    MinOSVersion = ""6.2.1""
                    MaxOSVersionTested = ""6.2.1"">

                    <File Reference = ""MySDK.Sprint.winmd"" Implementation = ""XNASprintImpl.dll"">
                    </File>
                </FileList>";

            string tempPath = Path.Combine(Path.GetTempPath(), "FakeSDKDirectory");
            try
            {
                // Good
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "Windows", "v1.0", "ExtensionSDKs", "MyAssembly", "1.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "Windows", "1.0", "ExtensionSDKs", "MyAssembly", "2.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "Windows", "2.0", "ExtensionSDKs", "MyAssembly", "3.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "SomeOtherPlace", "MyPlatformOtherLocation", "4.0", "ExtensionSDKs", "MyAssembly", "1.0"));
                Directory.CreateDirectory(Path.Combine(tempPath, "WindowsKits", "6.0"));
                Directory.CreateDirectory(Path.Combine(tempPath, "MyPlatform", "5.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "MyPlatform", "4.0", "ExtensionSDKs", "AnotherAssembly", "1.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "MyPlatform", "3.0", "ExtensionSDKs", "MyAssembly", "1.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "MyPlatform", "2.0", "ExtensionSDKs", "MyAssembly", "1.0"));
                Directory.CreateDirectory(Path.Combine(tempPath, "MyPlatform", "1.0"));
                Directory.CreateDirectory(Path.Combine(tempPath, "MyPlatform", "8.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "MyPlatform", "8.0", "Platforms", "PlatformAssembly", "0.1.2.3"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "MyPlatform", "8.0", "Platforms", "PlatformAssembly", "1.2.3.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "MyPlatform", "8.0", "Platforms", "Sparkle", "3.3.3.3"));
                Directory.CreateDirectory(Path.Combine(tempPath, "MyPlatform", "9.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "MyPlatform", "9.0", "Platforms", "PlatformAssembly", "0.1.2.3"));
                Directory.CreateDirectory(Path.Combine(tempPath, "MyPlatform", "9.0", "PlatformAssembly", "Sparkle"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "MyPlatform", "9.0", "Platforms", "PlatformAssembly", "Sparkle"));

                File.WriteAllText(
                    Path.Combine(tempPath, "Windows", "v1.0", "ExtensionSDKs", "MyAssembly", "1.0", "SDKManifest.xml"),
                    "Hello");
                File.WriteAllText(
                    Path.Combine(tempPath, "Windows", "1.0", "ExtensionSDKs", "MyAssembly", "2.0", "SDKManifest.xml"),
                    "Hello");
                File.WriteAllText(
                    Path.Combine(tempPath, "Windows", "2.0", "ExtensionSDKs", "MyAssembly", "3.0", "SDKManifest.xml"),
                    "Hello");

                File.WriteAllText(
                    Path.Combine(tempPath, "SomeOtherPlace", "MyPlatformOtherLocation", "4.0", "SDKManifest.xml"),
                    "Hello");
                File.WriteAllText(
                    Path.Combine(tempPath, "SomeOtherPlace", "MyPlatformOtherLocation", "4.0", "ExtensionSDKs", "MyAssembly", "1.0", "SDKManifest.xml"),
                    "Hello");
                File.WriteAllText(Path.Combine(tempPath, "Windows", "1.0", "SDKManifest.xml"), manifestPlatformSDK1);
                File.WriteAllText(Path.Combine(tempPath, "Windows", "2.0", "SDKManifest.xml"), manifestPlatformSDK2);
                File.WriteAllText(
                    Path.Combine(tempPath, "MyPlatform", "4.0", "ExtensionSDKs", "AnotherAssembly", "1.0", "SDKManifest.xml"),
                    manifestExtensionSDK2);
                File.WriteAllText(Path.Combine(tempPath, "MyPlatform", "3.0", "SDKManifest.xml"), manifestPlatformSDK3);
                File.WriteAllText(Path.Combine(tempPath, "MyPlatform", "2.0", "SDKManifest.xml"), manifestPlatformSDK4);
                File.WriteAllText(
                    Path.Combine(tempPath, "MyPlatform", "3.0", "ExtensionSDKs", "MyAssembly", "1.0", "SDKManifest.xml"),
                    manifestExtensionSDK1);
                File.WriteAllText(
                    Path.Combine(tempPath, "MyPlatform", "2.0", "ExtensionSDKs", "MyAssembly", "1.0", "SDKManifest.xml"),
                    "Hello");
                File.WriteAllText(Path.Combine(tempPath, "MyPlatform", "1.0", "SDKManifest.xml"), manifestPlatformSDK5);

                // Contains a couple of sub-platforms
                File.WriteAllText(
                    Path.Combine(tempPath, "MyPlatform", "8.0", "SDKManifest.xml"),
                    manifestPlatformSDK6);
                File.WriteAllText(
                    Path.Combine(tempPath, "MyPlatform", "8.0", "Platforms", "PlatformAssembly", "0.1.2.3", "Platform.xml"),
                    "Hello");
                File.WriteAllText(
                    Path.Combine(tempPath, "MyPlatform", "8.0", "Platforms", "PlatformAssembly", "1.2.3.0", "Platform.xml"),
                    "Hello");
                File.WriteAllText(
                    Path.Combine(tempPath, "MyPlatform", "8.0", "Platforms", "Sparkle", "3.3.3.3", "Platform.xml"),
                    "Hello");

                // Contains invalid sub-platforms as well as valid ones
                File.WriteAllText(Path.Combine(tempPath, "MyPlatform", "9.0", "SDKManifest.xml"), manifestPlatformSDK7);
                File.WriteAllText(
                    Path.Combine(tempPath, "MyPlatform", "9.0", "Platforms", "PlatformAssembly", "0.1.2.3", "Platform.xml"),
                    "Hello");
                File.WriteAllText(
                    Path.Combine(tempPath, "MyPlatform", "9.0", "PlatformAssembly", "Sparkle", "Platform.xml"),
                    "Hello"); // not under the Platforms directory
                File.WriteAllText(
                    Path.Combine(tempPath, "MyPlatform", "9.0", "Platforms", "PlatformAssembly", "Sparkle", "Platform.xml"),
                    "Hello"); // bad version
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "MyPlatform", "9.0", "Platforms", "Sparkle", "3.3.3.3")); // no platform.xml

                //Bad because of v in the sdk version
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "Windows", "v1.0", "ExtensionSDKs", "AnotherAssembly", "v1.1"));

                //Bad because no extensionsdks directory under the platform version
                Directory.CreateDirectory(Path.Combine(tempPath, "Windows", "v3.0") + Path.DirectorySeparatorChar);

                // Bad because the directory under the identifier is not a version
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "Windows", "NotAVersion") + Path.DirectorySeparatorChar);

                // Bad because the directory under the identifier is not a version
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "Windows", "NotAVersion", "ExtensionSDKs", "Assembly", "1.0"));
            }
            catch (Exception)
            {
                FileUtilities.DeleteDirectoryNoThrow(tempPath, true);
                return null;
            }

            return tempPath;
        }

        /// <summary>
        /// Make a fake SDK structure on disk for testing.
        /// </summary>
        private static string MakeFakeSDKStructure2()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "FakeSDKDirectory2");
            try
            {
                // Good
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "Windows", "v1.0", "ExtensionSDKs", "MyAssembly", "1.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "Windows", "1.0", "ExtensionSDKs", "MyAssembly", "2.0"));
                Directory.CreateDirectory(
                    Path.Combine(tempPath, "Windows", "2.0", "ExtensionSDKs", "MyAssembly", "4.0"));

                File.WriteAllText(
                    Path.Combine(tempPath, "Windows", "v1.0", "ExtensionSDKs", "MyAssembly", "1.0", "SDKManifest.xml"),
                    "Hello");
                File.WriteAllText(
                    Path.Combine(tempPath, "Windows", "1.0", "ExtensionSDKs", "MyAssembly", "2.0", "SDKManifest.xml"),
                    "Hello");
                File.WriteAllText(
                    Path.Combine(tempPath, "Windows", "2.0", "ExtensionSDKs", "MyAssembly", "4.0", "SDKManifest.xml"),
                    "Hello");
            }
            catch (Exception)
            {
                FileUtilities.DeleteDirectoryNoThrow(tempPath, true);
                return null;
            }

            return tempPath;
        }
        #endregion

        #region HelperMethods

#if FEATURE_WIN32_REGISTRY
        /// <summary>
        /// Simplified registry access delegate. Given a baseKey and a subKey, get all of the subkey
        /// names.
        /// </summary>
        /// <param name="baseKey">The base registry key.</param>
        /// <param name="subKey">The subkey</param>
        /// <returns>An enumeration of strings.</returns>
        private static IEnumerable<string> GetRegistrySubKeyNames(RegistryKey baseKey, string subKey)
        {
            if (baseKey == Registry.CurrentUser)
            {
                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "Windows", "MyPlatform" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "v1.0", "1.0" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\v1.0\ExtensionSDKs", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "MyAssembly" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\1.0\ExtensionSDKs", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "MyAssembly" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\v1.0\ExtensionSDKs\MyAssembly", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "v1.1", "1.0", "2.0", "3.0" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\1.0\ExtensionSDKs\MyAssembly", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "2.0" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\MyPlatform", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "4.0", "5.0", "6.0", "9.0" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\MyPlatform\4.0\ExtensionSDKs", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "MyAssembly" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\MyPlatform\5.0\ExtensionSDKs", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { string.Empty };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\MyPlatform\4.0\ExtensionSDKs\MyAssembly", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "1.0" };
                }
            }

            if (baseKey == Registry.LocalMachine)
            {
                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "Windows" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "v2.0" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\v2.0\ExtensionSDKs", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "MyAssembly" };
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\v2.0\ExtensionSDKs\MyAssembly", StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { "3.0" };
                }
            }

            return new string[] { };
        }

        /// <summary>
        /// Simplified registry access delegate. Given a baseKey and subKey, get the default value
        /// of the subKey.
        /// </summary>
        /// <param name="baseKey">The base registry key.</param>
        /// <param name="subKey">The subkey</param>
        /// <returns>A string containing the default value.</returns>
        private string GetRegistrySubKeyDefaultValue(RegistryKey baseKey, string subKey)
        {
            if (baseKey == Registry.CurrentUser)
            {
                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\v1.0\ExtensionSDKs\MyAssembly\1.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "Windows\\v1.0\\ExtensionSDKs\\MyAssembly\\1.0");
                }

                // This has a v in the sdk version and should not be found but we need a real path in case it is so it will show up in the returned list and fail the test.
                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\v1.0\ExtensionSDKs\MyAssembly\v1.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "Windows\\v1.0\\ExtensionSDKs\\MyAssembly\\1.0");
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\1.0\ExtensionSDKs\MyAssembly\2.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "Windows\\1.0\\ExtensionSDKs\\MyAssembly\\2.0");
                }

                // This has a set of bad char in the returned directory so it should not be allowed.
                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\v1.0\ExtensionSDKs\MyAssembly\3.0", StringComparison.OrdinalIgnoreCase))
                {
                    return _fakeStructureRoot + @"\Windows\1.0\ExtensionSDKs\MyAssembly\<>?/";
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\MyPlatform\5.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "MyPlatform\\5.0");
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\MyPlatform\4.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "SomeOtherPlace\\MyPlatformOtherLocation\\4.0");
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\MyPlatform\6.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "Windows Kits\\6.0");
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\MyPlatform\9.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "MyPlatform\\9.0");
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\MyPlatform\4.0\ExtensionSDKs\MyAssembly\1.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "SomeOtherPlace\\MyPlatformOtherLocation\\4.0\\ExtensionSDKs\\MyAssembly\\1.0");
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\v1.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "Windows\\1.0");
                }
            }

            if (baseKey == Registry.LocalMachine)
            {
                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\v2.0\ExtensionSDKs\MyAssembly\3.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "Windows\\2.0\\ExtensionSDKs\\MyAssembly\\3.0");
                }

                if (string.Equals(subKey, @"Software\Microsoft\MicrosoftSDKs\Windows\v2.0", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(_fakeStructureRoot, "Windows\\2.0");
                }
            }

            return null;
        }

        /// <summary>
        /// Registry access delegate. Given a hive and a view, return the registry base key.
        /// </summary>
        private static RegistryKey GetBaseKey(RegistryHive hive, RegistryView view)
        {
            switch (hive)
            {
                case RegistryHive.CurrentUser:
                {
                    return Registry.CurrentUser;
                }
                case RegistryHive.LocalMachine:
                {
                    return Registry.LocalMachine;
                }
                default:
                {
                    return null;
                }
            }
        }
#endif
        #endregion
    }
}
