// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Tasks;
using Microsoft.Build.Utilities;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Build.UnitTests.GetSDKReferenceFiles_Tests
{
    public class FakeSdkStructure : IDisposable
    {
        private readonly string _fakeSDKStructureRoot;
        public string SdkDirectory { get; }
        public string SdkDirectory2 { get; }

        public FakeSdkStructure()
        {
            string sdkDirectory;
            _fakeSDKStructureRoot = CreateFakeSDKReferenceAssemblyDirectory1(out sdkDirectory);
            SdkDirectory = sdkDirectory;

            string sdkDirectory2;
            CreateFakeSDKReferenceAssemblyDirectory2(out sdkDirectory2);
            SdkDirectory2 = sdkDirectory2;
        }

        public void Dispose()
        {
            if (FileUtilities.DirectoryExistsNoThrow(_fakeSDKStructureRoot))
            {
                FileUtilities.DeleteDirectoryNoThrow(_fakeSDKStructureRoot, true);
            }
        }

        /// <summary>
        /// Create a fake sdk structure on disk
        /// </summary>
        private static string CreateFakeSDKReferenceAssemblyDirectory1(out string sdkDirectory)
        {
            string testDirectoryRoot = Path.Combine(Path.GetTempPath(), "FakeSDKForReferenceAssemblies");
            sdkDirectory = Path.Combine(testDirectoryRoot, "MyPlatform\\8.0\\ExtensionSDKs\\SDkWithManifest\\2.0\\");
            string referenceAssemblyDirectoryConfigx86 = Path.Combine(sdkDirectory, "References\\Retail\\X86");
            string referenceAssemblyDirectoryConfigx64 = Path.Combine(sdkDirectory, "References\\Retail\\X64");
            string referenceAssemblyDirectoryConfigNeutral = Path.Combine(sdkDirectory, "References\\Retail\\Neutral");
            string referenceAssemblyDirectoryCommonConfigNeutral = Path.Combine(sdkDirectory, "References\\CommonConfiguration\\Neutral");
            string referenceAssemblyDirectoryCommonConfigX86 = Path.Combine(sdkDirectory, "References\\CommonConfiguration\\X86");
            string referenceAssemblyDirectoryCommonConfigX64 = Path.Combine(sdkDirectory, "References\\CommonConfiguration\\X64");

            string redistDirectoryConfigx86 = Path.Combine(sdkDirectory, "Redist\\Retail\\X86");
            string redistDirectoryConfigx64 = Path.Combine(sdkDirectory, "Redist\\Retail\\X64");
            string redistDirectoryConfigNeutral = Path.Combine(sdkDirectory, "Redist\\Retail\\Neutral");
            string redistDirectoryCommonConfigNeutral = Path.Combine(sdkDirectory, "Redist\\CommonConfiguration\\Neutral");
            string redistDirectoryCommonConfigX86 = Path.Combine(sdkDirectory, "Redist\\CommonConfiguration\\X86");
            string redistDirectoryCommonConfigX64 = Path.Combine(sdkDirectory, "Redist\\CommonConfiguration\\X64");

            string designTimeDirectoryConfigx86 = Path.Combine(sdkDirectory, "DesignTime\\Retail\\X86");
            string designTimeDirectoryConfigNeutral = Path.Combine(sdkDirectory, "DesignTime\\Retail\\Neutral");
            string designTimeDirectoryCommonConfigNeutral = Path.Combine(sdkDirectory, "DesignTime\\CommonConfiguration\\Neutral");
            string designTimeDirectoryCommonConfigX86 = Path.Combine(sdkDirectory, "DesignTime\\CommonConfiguration\\X86");

            Directory.CreateDirectory(testDirectoryRoot);
            Directory.CreateDirectory(sdkDirectory);

            Directory.CreateDirectory(referenceAssemblyDirectoryConfigx86);
            Directory.CreateDirectory(referenceAssemblyDirectoryConfigx64);
            Directory.CreateDirectory(referenceAssemblyDirectoryConfigNeutral);
            Directory.CreateDirectory(referenceAssemblyDirectoryCommonConfigNeutral);
            Directory.CreateDirectory(referenceAssemblyDirectoryCommonConfigX86);
            Directory.CreateDirectory(referenceAssemblyDirectoryCommonConfigX64);

            Directory.CreateDirectory(redistDirectoryConfigx86);
            Directory.CreateDirectory(redistDirectoryConfigx64);
            Directory.CreateDirectory(redistDirectoryConfigNeutral);
            Directory.CreateDirectory(Path.Combine(redistDirectoryConfigNeutral, "ASubDirectory\\TwoDeep"));
            Directory.CreateDirectory(redistDirectoryCommonConfigNeutral);
            Directory.CreateDirectory(redistDirectoryCommonConfigX86);
            Directory.CreateDirectory(redistDirectoryCommonConfigX64);

            Directory.CreateDirectory(designTimeDirectoryConfigx86);
            Directory.CreateDirectory(designTimeDirectoryConfigNeutral);
            Directory.CreateDirectory(designTimeDirectoryCommonConfigNeutral);
            Directory.CreateDirectory(designTimeDirectoryCommonConfigX86);

            string testWinMD = Path.Combine(referenceAssemblyDirectoryConfigx86, "A.winmd");
            string testWinMD64 = Path.Combine(referenceAssemblyDirectoryConfigx64, "A.winmd");
            string testWinMDNeutral = Path.Combine(referenceAssemblyDirectoryConfigNeutral, "B.winmd");
            string testWinMDNeutralWinXML = Path.Combine(referenceAssemblyDirectoryConfigNeutral, "B.xml");
            string testWinMDCommonConfigurationx86 = Path.Combine(referenceAssemblyDirectoryCommonConfigX86, "C.winmd");
            string testWinMDCommonConfigurationx64 = Path.Combine(referenceAssemblyDirectoryCommonConfigX64, "C.winmd");
            string testWinMDCommonConfigurationNeutral = Path.Combine(referenceAssemblyDirectoryCommonConfigNeutral, "D.winmd");
            string testWinMDCommonConfigurationNeutralDupe = Path.Combine(referenceAssemblyDirectoryCommonConfigNeutral, "A.winmd");

            string testRA = Path.Combine(referenceAssemblyDirectoryConfigx86, "E.dll");
            string testRA64 = Path.Combine(referenceAssemblyDirectoryConfigx64, "E.dll");
            string testRANeutral = Path.Combine(referenceAssemblyDirectoryConfigNeutral, "F.dll");
            string testRACommonConfigurationx86 = Path.Combine(referenceAssemblyDirectoryCommonConfigX86, "G.dll");
            string testRACommonConfigurationx64 = Path.Combine(referenceAssemblyDirectoryCommonConfigX64, "G.dll");
            string testRACommonConfigurationNeutral = Path.Combine(referenceAssemblyDirectoryCommonConfigNeutral, "H.dll");
            // Make duplicate of winmd but change to dll extension so that we can make sure that we eliminate duplicate file names.
            string testRACommonConfigurationNeutralDupe = Path.Combine(referenceAssemblyDirectoryCommonConfigNeutral, "A.dll");

            string redist = Path.Combine(redistDirectoryConfigx86, "A.dll");
            string redist64 = Path.Combine(redistDirectoryConfigx64, "A.dll");
            string redistNeutral = Path.Combine(redistDirectoryConfigNeutral, "ASubDirectory\\TwoDeep\\B.dll");
            string redistNeutralPri = Path.Combine(redistDirectoryConfigNeutral, "B.pri");
            string redistCommonConfigurationx86 = Path.Combine(redistDirectoryCommonConfigX86, "C.dll");
            string redistCommonConfigurationx64 = Path.Combine(redistDirectoryCommonConfigX64, "C.dll");
            string redistCommonConfigurationNeutral = Path.Combine(redistDirectoryCommonConfigNeutral, "D.dll");
            string redistCommonConfigurationNeutralDupe = Path.Combine(redistDirectoryCommonConfigNeutral, "A.dll");

            File.WriteAllText(testWinMDNeutralWinXML, "TestXml");
            File.WriteAllText(testWinMD, "TestWinmd");
            File.WriteAllText(testWinMD64, "TestWinmd");
            File.WriteAllText(testWinMDNeutral, "TestWinmd");
            File.WriteAllText(testWinMDCommonConfigurationNeutral, "TestWinmd");
            File.WriteAllText(testWinMDCommonConfigurationx86, "TestWinmd");
            File.WriteAllText(testWinMDCommonConfigurationx64, "TestWinmd");
            File.WriteAllText(testWinMDCommonConfigurationNeutralDupe, "TestWinmd");
            File.WriteAllText(testRA, "TestWinmd");
            File.WriteAllText(testRA64, "TestWinmd");
            File.WriteAllText(testRANeutral, "TestWinmd");
            File.WriteAllText(testRACommonConfigurationNeutral, "TestWinmd");
            File.WriteAllText(testRACommonConfigurationx86, "TestWinmd");
            File.WriteAllText(testRACommonConfigurationx64, "TestWinmd");
            File.WriteAllText(testRACommonConfigurationNeutralDupe, "TestWinmd");

            File.WriteAllText(redist, "TestWinmd");
            File.WriteAllText(redist64, "TestWinmd");
            File.WriteAllText(redistNeutral, "TestWinmd");
            File.WriteAllText(redistNeutralPri, "TestWinmd");
            File.WriteAllText(redistCommonConfigurationNeutral, "TestWinmd");
            File.WriteAllText(redistCommonConfigurationx86, "TestWinmd");
            File.WriteAllText(redistCommonConfigurationx64, "TestWinmd");
            File.WriteAllText(redistCommonConfigurationNeutralDupe, "TestWinmd");

            return testDirectoryRoot;
        }

        /// <summary>
        /// Create a fake sdk structure on disk
        /// </summary>
        private static string CreateFakeSDKReferenceAssemblyDirectory2(out string sdkDirectory)
        {
            string testDirectoryRoot = Path.Combine(Path.GetTempPath(), "FakeSDKForReferenceAssemblies");
            sdkDirectory = Path.Combine(testDirectoryRoot, "MyPlatform\\8.0\\ExtensionSDKs\\AnotherSDK\\2.0\\");
            string referenceAssemblyDirectoryConfigx86 = Path.Combine(sdkDirectory, "References\\Retail\\X86");
            string redistDirectoryConfigx86 = Path.Combine(sdkDirectory, "Redist\\Retail\\X86");

            Directory.CreateDirectory(testDirectoryRoot);
            Directory.CreateDirectory(sdkDirectory);

            Directory.CreateDirectory(referenceAssemblyDirectoryConfigx86);
            Directory.CreateDirectory(redistDirectoryConfigx86);

            string testWinMD = Path.Combine(referenceAssemblyDirectoryConfigx86, "B.winmd");
            string redist = Path.Combine(redistDirectoryConfigx86, "B.pri");
            string redist2 = Path.Combine(redistDirectoryConfigx86, "B.dll");

            File.WriteAllText(testWinMD, "TestWinmd");
            File.WriteAllText(redist, "TestWinmd");
            File.WriteAllText(redist2, "TestWinmd");

            return testDirectoryRoot;
        }
    }


    /// <summary>
    /// Test the expansion of sdk reference assemblies.
    /// </summary>
    public class GetSDKReferenceFilesTestFixture : IDisposable, IClassFixture<FakeSdkStructure>
    {
        private readonly ITestOutputHelper _output;

        private readonly string _sdkDirectory;
        private readonly string _sdkDirectory2;
        private readonly MockEngine.GetStringDelegate _resourceDelegate;
        private readonly GetAssemblyName _getAssemblyName = GetAssemblyName;
        private readonly GetAssemblyRuntimeVersion _getAssemblyRuntimeVersion = GetImageRuntimeVersion;
        private readonly string _cacheDirectory = Path.Combine(Path.GetTempPath(), "GetSDKReferenceFiles");

        public GetSDKReferenceFilesTestFixture(FakeSdkStructure fakeSdkStructure, ITestOutputHelper output)
        {
            _output = output;

            _sdkDirectory = fakeSdkStructure.SdkDirectory;
            _sdkDirectory2 = fakeSdkStructure.SdkDirectory2;
            _resourceDelegate = AssemblyResources.GetString;

            if (FileUtilities.DirectoryExistsNoThrow(_cacheDirectory))
            {
                _output.WriteLine($"Found existing cache directory {_cacheDirectory}; deleting it.");
                FileUtilities.DeleteDirectoryNoThrow(_cacheDirectory, true);
            }

            Directory.CreateDirectory(_cacheDirectory);

            _output.WriteLine($"Created cache directory {_cacheDirectory}.");
        }

        public void Dispose()
        {
            if (FileUtilities.DirectoryExistsNoThrow(_cacheDirectory))
            {
                _output.WriteLine($"Deleting cache directory {_cacheDirectory}.");
                FileUtilities.DeleteDirectoryNoThrow(_cacheDirectory, true);
            }
        }

        /// <summary>
        /// Make sure there are no outputs if no resolved sdk files are passed in.
        /// </summary>
        [Fact]
        public void PassReferenceWithNoReferenceDirectory()
        {
            var engine = new MockEngine(_output);

            ITaskItem item = new TaskItem("C:\\SDKDoesNotExist");
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            var t = new GetSDKReferenceFiles
            {
                BuildEngine = engine,
                ResolvedSDKReferences = new ITaskItem[] { item },
                CacheFileFolderPath = _cacheDirectory,
            };

            t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true).ShouldBeTrue();
            t.CopyLocalFiles.ShouldBeEmpty();
            t.References.ShouldBeEmpty();
            t.RedistFiles.ShouldBeEmpty();
        }

        private delegate IList<string> GetSDKFolders(string sdkRoot);
        private delegate IList<string> GetSDKFolders2(string sdkRoot, string configuration, string architecture);

        /// <summary>
        /// Make sure we get the correct folder list when asking for it.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void GetSDKReferenceFolders()
        {
            var getReferenceFolders = new GetSDKFolders(ToolLocationHelper.GetSDKReferenceFolders);
            var getReferenceFolders2 = new GetSDKFolders2(ToolLocationHelper.GetSDKReferenceFolders);

            VerifySDKFolders(getReferenceFolders, getReferenceFolders2, "References", _sdkDirectory);
        }

        [Fact]
        public void VerifyGetSdkReferenceTranslator()
        {
            Dictionary<string, GetSDKReferenceFiles.SdkReferenceInfo> pathToReferenceMetadata = new();
            pathToReferenceMetadata.Add("first", new("dat", "dat2", true, false));
            pathToReferenceMetadata.Add("second", new("inf", "inf2", false, false));
            Dictionary<string, List<string>> directoryToFileList = new();
            directoryToFileList.Add("third", new List<string>() { "a", "b", "c" });
            directoryToFileList.Add("fourth", new List<string>() { "1", "2", "3" });
            GetSDKReferenceFiles.SDKInfo writeInfo = new(pathToReferenceMetadata, directoryToFileList, 47);
            GetSDKReferenceFiles.SaveContext contextWriter = new("d", "n", writeInfo);
            GetSDKReferenceFiles.SDKInfo readInfo = null;
            using (TestEnvironment env = TestEnvironment.Create())
            {
                TransientTestFolder folder = env.CreateFolder();
                GetSDKReferenceFiles.SDKFilesCache cache = new(null, folder.Path, null, null, null);
                cache.SaveAssemblyListToCacheFile(contextWriter);
                GetSDKReferenceFiles.SDKFilesCache cache2 = new(null, folder.Path, null, null, null);
                readInfo = cache2.LoadAssemblyListFromCacheFile("d", "n");
            }
            readInfo.DirectoryToFileList.Count.ShouldBe(2);
            readInfo.DirectoryToFileList["fourth"].Count.ShouldBe(3);
            readInfo.DirectoryToFileList["fourth"][1].ShouldBe("2");
            readInfo.DirectoryToFileList["third"][0].ShouldBe("a");
            readInfo.Hash.ShouldBe(47);
            readInfo.PathToReferenceMetadata.Count.ShouldBe(2);
            readInfo.PathToReferenceMetadata["first"].FusionName.ShouldBe("dat");
            readInfo.PathToReferenceMetadata["first"].IsManagedWinmd.ShouldBeFalse();
            readInfo.PathToReferenceMetadata["first"].IsWinMD.ShouldBeTrue();
            readInfo.PathToReferenceMetadata["second"].ImageRuntime.ShouldBe("inf2");
        }

        private static void VerifySDKFolders(GetSDKFolders singleParamDelegate, GetSDKFolders2 multiParamDelegate, string folderName, string sdkDirectory)
        {
            IList<string> sdkFolders = singleParamDelegate(sdkDirectory);
            Assert.Equal(2, sdkFolders.Count);

            Assert.Equal(Path.Combine(sdkDirectory, folderName + "\\Retail\\Neutral\\"), sdkFolders[0]);
            Assert.Equal(Path.Combine(sdkDirectory, folderName + "\\CommonConfiguration\\Neutral\\"), sdkFolders[1]);

            sdkFolders = multiParamDelegate(sdkDirectory, "Retail", "Neutral");
            Assert.Equal(2, sdkFolders.Count);

            Assert.Equal(Path.Combine(sdkDirectory, folderName + "\\Retail\\Neutral\\"), sdkFolders[0]);
            Assert.Equal(Path.Combine(sdkDirectory, folderName + "\\CommonConfiguration\\Neutral\\"), sdkFolders[1]);

            sdkFolders = multiParamDelegate(sdkDirectory, "Retail", "X86");
            Assert.Equal(4, sdkFolders.Count);

            Assert.Equal(Path.Combine(sdkDirectory, folderName + "\\Retail\\X86\\"), sdkFolders[0]);
            Assert.Equal(Path.Combine(sdkDirectory, folderName + "\\Retail\\Neutral\\"), sdkFolders[1]);
            Assert.Equal(Path.Combine(sdkDirectory, folderName + "\\CommonConfiguration\\X86\\"), sdkFolders[2]);
            Assert.Equal(Path.Combine(sdkDirectory, folderName + "\\CommonConfiguration\\Neutral\\"), sdkFolders[3]);
        }

        /// <summary>
        /// Make sure we get the correct folder list when asking for it.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void GetSDKRedistFolders()
        {
            var getRedistFolders = new GetSDKFolders(ToolLocationHelper.GetSDKRedistFolders);
            var getRedistFolders2 = new GetSDKFolders2(ToolLocationHelper.GetSDKRedistFolders);

            VerifySDKFolders(getRedistFolders, getRedistFolders2, "Redist", _sdkDirectory);
        }

        /// <summary>
        /// Make sure we get the correct folder list when asking for it.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void GetSDKDesignTimeFolders()
        {
            var getDesignTimeFolders = new GetSDKFolders(ToolLocationHelper.GetSDKDesignTimeFolders);
            var getDesignTimeFolders2 = new GetSDKFolders2(ToolLocationHelper.GetSDKDesignTimeFolders);

            VerifySDKFolders(getDesignTimeFolders, getDesignTimeFolders2, "DesignTime", _sdkDirectory);
        }

        /// <summary>
        /// Make sure there are no outputs if an sdk which does not exist is passed in.
        /// </summary>
        [Fact]
        public void PassNoSDKReferences()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);

            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Empty(t.References);
            Assert.Empty(t.RedistFiles);
        }

        /// <summary>
        /// Make sure there are no outputs if expand sdks is not true.
        /// </summary>
        [Fact]
        public void PassReferenceWithExpandFalse()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "false");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Empty(t.References);
            Assert.Empty(t.RedistFiles);
        }

        /// <summary>
        /// Make sure there are no redist outputs if CopyRedist is false
        /// </summary>
        [Fact]
        public void PassReferenceWithCopyRedistFalse()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("ExpandReferenceAssemblies", "false");
            item.SetMetadata("CopyRedist", "false");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Empty(t.References);
            Assert.Empty(t.RedistFiles);
        }

        /// <summary>
        /// Verify we get the correct set of reference assemblies and copy local files when the CopyLocal flag is true
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void GetReferenceAssembliesWhenExpandTrueCopyLocalTrue()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;
            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyLocalExpandedReferenceAssemblies", "true");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Equal(9, t.CopyLocalFiles.Length);
            Assert.Equal(8, t.References.Length);

            string winmd = Path.Combine(_sdkDirectory, "References\\Retail\\X86\\A.winmd");

            Assert.Equal(winmd, t.References[0].ItemSpec, true);
            Assert.Equal("A.winmd", Path.GetFileName(t.References[0].ItemSpec), true);
            Assert.Equal("WindowsRuntime 1.0;CLR V2.0.50727", t.References[0].GetMetadata("ImageRuntime"), true);
            Assert.Equal("A, Version=2.0.0.0, Culture=Neutral, PublicKeyToken=null", t.References[0].GetMetadata("FusionName"), true);
            Assert.Equal("true", t.References[0].GetMetadata("WinMDFile"), true);
            Assert.Equal("Managed", t.References[0].GetMetadata("WinMDFileType"), true);
            Assert.Equal("true", t.References[0].GetMetadata("CopyLocal"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[0].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[0].GetMetadata("ResolvedFrom"), true);

            Assert.Equal("E.dll", Path.GetFileName(t.References[4].ItemSpec), true);
            Assert.Equal("CLR V2.0.50727", t.References[4].GetMetadata("ImageRuntime"), true);
            Assert.Equal("E, Version=2.0.0.0, Culture=Neutral, PublicKeyToken=null", t.References[4].GetMetadata("FusionName"), true);
            Assert.Equal("false", t.References[4].GetMetadata("WinMDFile"), true);
            Assert.Empty(t.References[4].GetMetadata("WinMDFileType"));
            Assert.Equal("true", t.References[4].GetMetadata("CopyLocal"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[4].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[4].GetMetadata("ResolvedFrom"), true);

            Assert.Equal("A.winmd", Path.GetFileName(t.CopyLocalFiles[0].ItemSpec), true);
            Assert.Equal("WindowsRuntime 1.0;CLR V2.0.50727", t.CopyLocalFiles[0].GetMetadata("ImageRuntime"), true);
            Assert.Equal("A, Version=2.0.0.0, Culture=Neutral, PublicKeyToken=null", t.CopyLocalFiles[0].GetMetadata("FusionName"), true);
            Assert.Equal("true", t.CopyLocalFiles[0].GetMetadata("WinMDFile"), true);
            Assert.Equal("Managed", t.CopyLocalFiles[0].GetMetadata("WinMDFileType"), true);
            Assert.Equal("true", t.CopyLocalFiles[0].GetMetadata("CopyLocal"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.CopyLocalFiles[0].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.CopyLocalFiles[0].GetMetadata("ResolvedFrom"), true);

            Assert.Equal("E.dll", Path.GetFileName(t.CopyLocalFiles[5].ItemSpec), true);
            Assert.Equal("CLR V2.0.50727", t.CopyLocalFiles[5].GetMetadata("ImageRuntime"), true);
            Assert.Equal("E, Version=2.0.0.0, Culture=Neutral, PublicKeyToken=null", t.CopyLocalFiles[5].GetMetadata("FusionName"), true);
            Assert.Equal("false", t.CopyLocalFiles[5].GetMetadata("WinMDFile"), true);
            Assert.Empty(t.CopyLocalFiles[5].GetMetadata("WinMDFileType"));
            Assert.Equal("true", t.CopyLocalFiles[5].GetMetadata("CopyLocal"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.CopyLocalFiles[5].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.CopyLocalFiles[5].GetMetadata("ResolvedFrom"), true);

            Assert.Equal("B.xml", Path.GetFileName(t.CopyLocalFiles[2].ItemSpec));
        }

        /// <summary>
        /// Verify reference is not processed by GetSDKReferenceFiles when "ReferenceOnly" metadata is set.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyNoCopyWhenReferenceOnlyIsTrue()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item1 = new TaskItem(_sdkDirectory);
            item1.SetMetadata("ExpandReferenceAssemblies", "true");
            item1.SetMetadata("TargetedSDKConfiguration", "Retail");
            item1.SetMetadata("TargetedSDKArchitecture", "x86");
            item1.SetMetadata("CopyLocalExpandedReferenceAssemblies", "false");
            item1.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            ITaskItem item2 = new TaskItem(_sdkDirectory);
            item2.SetMetadata("ExpandReferenceAssemblies", "true");
            item2.SetMetadata("TargetedSDKConfiguration", "Retail");
            item2.SetMetadata("TargetedSDKArchitecture", "x86");
            item2.SetMetadata("CopyLocalExpandedReferenceAssemblies", "false");
            item2.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");
            item2.SetMetadata("RuntimeReferenceOnly", "true");

            // Process both regular and runtime-only references
            t.ResolvedSDKReferences = new ITaskItem[] { item1, item2 };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);

            Assert.Equal(8, t.References.Length);

            // Process regular references
            t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            t.ResolvedSDKReferences = new ITaskItem[] { item1 };
            success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);

            Assert.Equal(8, t.References.Length);

            // Process runtime-only references
            t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            t.ResolvedSDKReferences = new ITaskItem[] { item2 };
            success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);

            Assert.Empty(t.References);
        }

        /// <summary>
        /// Verify we get the correct set of reference assemblies and copy local files when the CopyLocal flag is false
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        [Trait("Category", "mono-windows-failing")]
        public void GetReferenceAssembliesWhenExpandTrueCopyLocalFalse()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles
            {
                BuildEngine = engine,
                CacheFileFolderPath = _cacheDirectory
            };

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyLocalExpandedReferenceAssemblies", "false");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(8, t.References.Length);

            Assert.Equal("A.winmd", Path.GetFileName(t.References[0].ItemSpec), true);
            Assert.Equal("WindowsRuntime 1.0;CLR V2.0.50727", t.References[0].GetMetadata("ImageRuntime"), true);
            Assert.Equal("A, Version=2.0.0.0, Culture=Neutral, PublicKeyToken=null", t.References[0].GetMetadata("FusionName"), true);
            Assert.Equal("true", t.References[0].GetMetadata("WinMDFile"), true);
            Assert.Equal("Managed", t.References[0].GetMetadata("WinMDFileType"), true);
            Assert.Equal("false", t.References[0].GetMetadata("CopyLocal"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[0].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[0].GetMetadata("ResolvedFrom"), true);

            Assert.Equal("B.winmd", Path.GetFileName(t.References[1].ItemSpec), true);
            Assert.Equal("WindowsRuntime 1.0", t.References[1].GetMetadata("ImageRuntime"), true);
            Assert.Equal("B, Version=2.0.0.0, Culture=Neutral, PublicKeyToken=null", t.References[1].GetMetadata("FusionName"), true);
            Assert.Equal("true", t.References[1].GetMetadata("WinMDFile"), true);
            Assert.Equal("Native", t.References[1].GetMetadata("WinMDFileType"), true);
            Assert.Equal("false", t.References[1].GetMetadata("CopyLocal"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[1].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[1].GetMetadata("ResolvedFrom"), true);

            Assert.Equal("E.dll", Path.GetFileName(t.References[4].ItemSpec), true);
            Assert.Equal("CLR V2.0.50727", t.References[4].GetMetadata("ImageRuntime"), true);
            Assert.Equal("E, Version=2.0.0.0, Culture=Neutral, PublicKeyToken=null", t.References[4].GetMetadata("FusionName"), true);
            Assert.Equal("false", t.References[4].GetMetadata("WinMDFile"), true);
            Assert.Empty(t.References[4].GetMetadata("WinMDFileType"));
            Assert.Equal("false", t.References[4].GetMetadata("CopyLocal"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[4].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[4].GetMetadata("ResolvedFrom"), true);
        }

        /// <summary>
        /// Verify that different cache files are created and used correctly for assemblies with the same identity but with files in different directories
        /// Also verifies that when
        /// </summary>
        [Fact]
        public void VerifyCacheFileNames()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            ITaskItem[] references1 = t.References;

            // Verify the task created a cache file
            string sdkIdentity = item.GetMetadata("OriginalItemSpec");
            string sdkRoot = item.ItemSpec;
            string cacheFile = sdkIdentity + ",Set=" + FileUtilities.GetHexHash(sdkIdentity) + "-" + FileUtilities.GetHexHash(sdkRoot) + ",Hash=*.dat";
            Thread.Sleep(100);
            string[] existingCacheFiles = Directory.GetFiles(_cacheDirectory, cacheFile);
            Assert.Single(existingCacheFiles);

            GetSDKReferenceFiles t2 = new GetSDKReferenceFiles();
            t2.BuildEngine = engine;
            t2.CacheFileFolderPath = _cacheDirectory;

            // Same SDK with different path
            ITaskItem item2 = new TaskItem(_sdkDirectory2);
            item2.SetMetadata("ExpandReferenceAssemblies", "true");
            item2.SetMetadata("TargetedSDKConfiguration", "Retail");
            item2.SetMetadata("TargetedSDKArchitecture", "x86");
            item2.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t2.ResolvedSDKReferences = new ITaskItem[] { item2 };
            bool success2 = t2.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            ITaskItem[] references2 = t2.References;
            Assert.True(success2);

            // References from the two builds should not overlap, otherwise the cache files are being misused
            foreach (var ref2 in references2)
            {
                Assert.Empty(references1.Where(i => i.ItemSpec.Equals(ref2.ItemSpec, StringComparison.InvariantCultureIgnoreCase)));
            }

            Thread.Sleep(100);
            string sdkIdentity2 = item.GetMetadata("OriginalItemSpec");
            string sdkRoot2 = item.ItemSpec;
            string cacheFile2 = sdkIdentity2 + ",Set=" + FileUtilities.GetHexHash(sdkIdentity2) + "-" + FileUtilities.GetHexHash(sdkRoot2) + ",Hash=*.dat";
            string[] existingCacheFiles2 = Directory.GetFiles(_cacheDirectory, cacheFile);
            Assert.Single(existingCacheFiles2);

            // There should have two cache files with the same prefix and first hash
            Thread.Sleep(100);
            string[] allCacheFiles = Directory.GetFiles(_cacheDirectory, sdkIdentity2 + ",Set=" + FileUtilities.GetHexHash(sdkIdentity2) + "*");
            Assert.Equal(2, allCacheFiles.Length);
        }

        /// <summary>
        /// Verify the correct reference files are found and that by default we do log the reference files
        /// added.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyReferencesLogged()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(8, t.References.Length);

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[0].ItemSpec.Replace(t.References[0].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[1].ItemSpec.Replace(t.References[1].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[2].ItemSpec.Replace(t.References[2].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[3].ItemSpec.Replace(t.References[3].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[4].ItemSpec.Replace(t.References[4].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[5].ItemSpec.Replace(t.References[5].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[6].ItemSpec.Replace(t.References[6].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[7].ItemSpec.Replace(t.References[7].GetMetadata("SDKRootPath"), String.Empty));

            Assert.Equal("A.winmd", Path.GetFileName(t.References[0].ItemSpec));
            Assert.Equal("true", t.References[0].GetMetadata("WinMDFile"));
            Assert.Equal("Managed", t.References[0].GetMetadata("WinMDFileType"));
            Assert.Equal("false", t.References[0].GetMetadata("CopyLocal"));
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[0].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[0].GetMetadata("ResolvedFrom"), true);

            Assert.Equal("E.dll", Path.GetFileName(t.References[4].ItemSpec));
            Assert.Equal("false", t.References[4].GetMetadata("WinMDFile"));
            Assert.Empty(t.References[4].GetMetadata("WinMDFileType"));
            Assert.Equal("false", t.References[4].GetMetadata("CopyLocal"));
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[4].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[4].GetMetadata("ResolvedFrom"), true);
        }

        /// <summary>
        /// Verify the correct reference files are found and that by default we do log the reference files
        /// added.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyReferencesLoggedFilterOutWinmd()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            t.ReferenceExtensions = new string[] { ".dll" };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(5, t.References.Length);

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[0].ItemSpec.Replace(t.References[0].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[1].ItemSpec.Replace(t.References[1].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[2].ItemSpec.Replace(t.References[2].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[3].ItemSpec.Replace(t.References[3].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[4].ItemSpec.Replace(t.References[4].GetMetadata("SDKRootPath"), String.Empty));

            Assert.Equal("A.dll", Path.GetFileName(t.References[0].ItemSpec), true);
            Assert.Equal("false", t.References[0].GetMetadata("WinMDFile"), true);
            Assert.Empty(t.References[0].GetMetadata("WinMDFileType"));
            Assert.Equal("false", t.References[0].GetMetadata("CopyLocal"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[0].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[0].GetMetadata("ResolvedFrom"), true);

            Assert.Equal("h.dll", Path.GetFileName(t.References[4].ItemSpec), true);
            Assert.Equal("false", t.References[4].GetMetadata("WinMDFile"), true);
            Assert.Empty(t.References[4].GetMetadata("WinMDFileType"));
            Assert.Equal("false", t.References[4].GetMetadata("CopyLocal"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[4].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[4].GetMetadata("ResolvedFrom"), true);
        }

        /// <summary>
        /// Verify we log an error if no configuration is on the sdk reference
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void LogErrorWhenNoConfiguration()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "");
            item.SetMetadata("TargetedSDKArchitecture", "amd64");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.False(success);
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.CannotHaveEmptyTargetConfiguration", _sdkDirectory);
        }

        /// <summary>
        /// Verify we log an error if no configuration is on the sdk reference
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void LogErrorWhenNoArchitecture()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Debug");
            item.SetMetadata("TargetedSDKArchitecture", "");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.False(success);
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.CannotHaveEmptyTargetArchitecture", _sdkDirectory);
        }


        /// <summary>
        /// Verify the correct reference files are found and that by default we do log the reference files
        /// added.
        /// </summary>
        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void VerifyReferencesLoggedAmd64()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "amd64");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(8, t.References.Length);

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[0].ItemSpec.Replace(t.References[0].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[1].ItemSpec.Replace(t.References[1].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[2].ItemSpec.Replace(t.References[2].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[3].ItemSpec.Replace(t.References[3].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[4].ItemSpec.Replace(t.References[4].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[5].ItemSpec.Replace(t.References[5].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[6].ItemSpec.Replace(t.References[6].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[7].ItemSpec.Replace(t.References[7].GetMetadata("SDKRootPath"), String.Empty));

            Assert.True(t.References[0].ItemSpec.IndexOf("x64", StringComparison.OrdinalIgnoreCase) > -1);
            Assert.Equal("A.winmd", Path.GetFileName(t.References[0].ItemSpec));
            Assert.Equal("SDKWithManifest, Version=2.0", t.References[0].GetMetadata("ReferenceGrouping"));
            Assert.Empty(t.References[0].GetMetadata("ReferenceGroupingDisplayName"));
            Assert.Equal("true", t.References[0].GetMetadata("WinMDFile"));
            Assert.Equal("Managed", t.References[0].GetMetadata("WinMDFileType"));
            Assert.Equal("false", t.References[0].GetMetadata("CopyLocal"));
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[0].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[0].GetMetadata("ResolvedFrom"), true);

            Assert.Equal("E.dll", Path.GetFileName(t.References[4].ItemSpec));
            Assert.Equal("false", t.References[4].GetMetadata("WinMDFile"));
            Assert.Empty(t.References[4].GetMetadata("WinMDFileType"));
            Assert.Equal("false", t.References[4].GetMetadata("CopyLocal"));
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[4].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[4].GetMetadata("ResolvedFrom"), true);
        }

        /// <summary>
        /// Verify the correct reference files are found and that by default we do log the reference files
        /// added.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyReferencesLoggedX64()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x64");
            item.SetMetadata("DisplayName", "SDKWithManifestDisplayName");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(8, t.References.Length);

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[0].ItemSpec.Replace(t.References[0].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[1].ItemSpec.Replace(t.References[1].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[2].ItemSpec.Replace(t.References[2].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[3].ItemSpec.Replace(t.References[3].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[4].ItemSpec.Replace(t.References[4].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[5].ItemSpec.Replace(t.References[5].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[6].ItemSpec.Replace(t.References[6].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[7].ItemSpec.Replace(t.References[7].GetMetadata("SDKRootPath"), String.Empty));

            Assert.True(t.References[0].ItemSpec.IndexOf("x64", StringComparison.OrdinalIgnoreCase) > -1);
            Assert.Equal("A.winmd", Path.GetFileName(t.References[0].ItemSpec));
            Assert.Equal("true", t.References[0].GetMetadata("WinMDFile"));
            Assert.Equal("SDKWithManifest, Version=2.0", t.References[0].GetMetadata("ReferenceGrouping"));
            Assert.Equal("Managed", t.References[0].GetMetadata("WinMDFileType"));
            Assert.Equal("SDKWithManifestDisplayName", t.References[0].GetMetadata("ReferenceGroupingDisplayName"));
            Assert.Equal("false", t.References[0].GetMetadata("CopyLocal"));
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[0].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[0].GetMetadata("ResolvedFrom"), true);

            Assert.Equal("E.dll", Path.GetFileName(t.References[4].ItemSpec));
            Assert.Equal("false", t.References[4].GetMetadata("WinMDFile"));
            Assert.Empty(t.References[4].GetMetadata("WinMDFileType"));
            Assert.Equal("false", t.References[4].GetMetadata("CopyLocal"));
            Assert.Equal("SDkWithManifest, Version=2.0", t.References[4].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.References[4].GetMetadata("ResolvedFrom"), true);
        }

        /// <summary>
        /// Verify the correct reference files are found and that if we do not want to log them we can set a property to do so.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyLogReferencesFalse()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            t.LogReferencesList = false;
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(8, t.References.Length);

            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[0].ItemSpec.Replace(t.References[0].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[1].ItemSpec.Replace(t.References[1].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[2].ItemSpec.Replace(t.References[2].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[3].ItemSpec.Replace(t.References[3].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[4].ItemSpec.Replace(t.References[4].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[5].ItemSpec.Replace(t.References[5].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[6].ItemSpec.Replace(t.References[6].GetMetadata("SDKRootPath"), String.Empty));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingReference", t.References[7].ItemSpec.Replace(t.References[7].GetMetadata("SDKRootPath"), String.Empty));
        }

        /// <summary>
        /// Verify the correct redist files are found and that by default we do not log the redist files
        /// added.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyRedistFilesLogRedistFalse()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyRedist", "true");
            item.SetMetadata("CopyRedistToSubDirectory", "Super");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            t.LogRedistFilesList = false;
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(8, t.References.Length);
            Assert.Equal(5, t.RedistFiles.Length);

            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[0].ItemSpec.Replace(t.RedistFiles[0].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[0].GetMetadata("TargetPath"));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[1].ItemSpec.Replace(t.RedistFiles[1].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[1].GetMetadata("TargetPath"));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[2].ItemSpec.Replace(t.RedistFiles[2].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[2].GetMetadata("TargetPath"));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[3].ItemSpec.Replace(t.RedistFiles[3].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[3].GetMetadata("TargetPath"));
            engine.AssertLogDoesntContainMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[4].ItemSpec.Replace(t.RedistFiles[4].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[4].GetMetadata("TargetPath"));

            Assert.Equal("A.dll", Path.GetFileName(t.RedistFiles[0].ItemSpec));
            Assert.Equal("Super\\A.dll", t.RedistFiles[0].GetMetadata("TargetPath"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.RedistFiles[0].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[0].GetMetadata("ResolvedFrom"), true);
            Assert.Empty(t.RedistFiles[0].GetMetadata("Root"));

            Assert.Equal("B.dll", Path.GetFileName(t.RedistFiles[1].ItemSpec), true);
            Assert.Equal("Super\\ASubDirectory\\TwoDeep\\B.dll", t.RedistFiles[1].GetMetadata("TargetPath"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.RedistFiles[1].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[1].GetMetadata("ResolvedFrom"), true);
            Assert.Empty(t.RedistFiles[1].GetMetadata("Root"));

            Assert.Equal("B.PRI", Path.GetFileName(t.RedistFiles[2].ItemSpec), true);
            Assert.Equal("Super\\B.PRI", t.RedistFiles[2].GetMetadata("TargetPath"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.RedistFiles[2].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[2].GetMetadata("ResolvedFrom"), true);
            Assert.Equal("Super", t.RedistFiles[2].GetMetadata("Root"));

            Assert.Equal("C.dll", Path.GetFileName(t.RedistFiles[3].ItemSpec), true);
            Assert.Equal("Super\\C.dll", t.RedistFiles[3].GetMetadata("TargetPath"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.RedistFiles[3].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[3].GetMetadata("ResolvedFrom"), true);
            Assert.Empty(t.RedistFiles[3].GetMetadata("Root"));

            Assert.Equal("D.dll", Path.GetFileName(t.RedistFiles[4].ItemSpec), true);
            Assert.Equal("Super\\D.dll", t.RedistFiles[4].GetMetadata("TargetPath"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.RedistFiles[4].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[4].GetMetadata("ResolvedFrom"), true);
            Assert.Empty(t.RedistFiles[4].GetMetadata("Root"));
        }

        /// <summary>
        /// Verify the correct redist files are found and that by default we do not log the redist files
        /// added.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyRedistFilesLogRedistTrue()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyRedist", "true");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(5, t.RedistFiles.Length);

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[0].ItemSpec.Replace(t.RedistFiles[0].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[0].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[1].ItemSpec.Replace(t.RedistFiles[1].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[1].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[2].ItemSpec.Replace(t.RedistFiles[2].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[2].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[3].ItemSpec.Replace(t.RedistFiles[3].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[3].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[4].ItemSpec.Replace(t.RedistFiles[4].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[4].GetMetadata("TargetPath"));
        }

        /// <summary>
        /// Verify the correct redist files are found and that by default we do not log the redist files
        /// added.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyRedistFilesLogRedistTrueX64()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x64");
            item.SetMetadata("CopyRedist", "true");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(5, t.RedistFiles.Length);

            Assert.True(t.RedistFiles[0].ItemSpec.IndexOf("x64", StringComparison.OrdinalIgnoreCase) > -1);
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[0].ItemSpec.Replace(t.RedistFiles[0].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[0].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[1].ItemSpec.Replace(t.RedistFiles[1].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[1].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[2].ItemSpec.Replace(t.RedistFiles[2].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[2].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[3].ItemSpec.Replace(t.RedistFiles[3].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[3].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[4].ItemSpec.Replace(t.RedistFiles[4].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[4].GetMetadata("TargetPath"));
        }

        /// <summary>
        /// Verify the correct redist files are found and that by default we do not log the redist files
        /// added.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void VerifyRedistFilesLogRedistTrueAmd64()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "amd64");
            item.SetMetadata("CopyRedist", "true");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(5, t.RedistFiles.Length);

            Assert.True(t.RedistFiles[0].ItemSpec.IndexOf("x64", StringComparison.OrdinalIgnoreCase) > -1);
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[0].ItemSpec.Replace(t.RedistFiles[0].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[0].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[1].ItemSpec.Replace(t.RedistFiles[1].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[1].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[2].ItemSpec.Replace(t.RedistFiles[2].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[2].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[3].ItemSpec.Replace(t.RedistFiles[3].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[3].GetMetadata("TargetPath"));
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.AddingRedistFile", t.RedistFiles[4].ItemSpec.Replace(t.RedistFiles[4].GetMetadata("SDKRootPath"), String.Empty), t.RedistFiles[4].GetMetadata("TargetPath"));
        }

        /// <summary>
        /// Make sure by default conflicts between references are logged as a comment if they are within the sdk itself
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void LogNoWarningForReferenceConflictWithinSDK()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyRedist", "false");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Equal(8, t.References.Length);

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictReferenceSameSDK", "SDKWithManifest, Version=2.0", "References\\Retail\\X86\\A.winmd", "References\\CommonConfiguration\\Neutral\\A.dll");
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictReferenceSameSDK", "SDKWithManifest, Version=2.0", "References\\Retail\\X86\\A.winmd", "References\\CommonConfiguration\\Neutral\\A.winmd");
            Assert.Equal(0, engine.Warnings);
        }

        /// <summary>
        /// Make sure that if the LogReferenceConflictsWithinSDKAsWarning is set log a warning for conflicts within an SDK for references.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void LogWarningForReferenceConflictWithinSDK()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyRedist", "false");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            t.LogReferenceConflictWithinSDKAsWarning = true;
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Equal(8, t.References.Length);

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictReferenceSameSDK", "SDKWithManifest, Version=2.0", "References\\Retail\\X86\\A.winmd", "References\\CommonConfiguration\\Neutral\\A.dll");
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictReferenceSameSDK", "SDKWithManifest, Version=2.0", "References\\Retail\\X86\\A.winmd", "References\\CommonConfiguration\\Neutral\\A.winmd");
            Assert.Equal(2, engine.Warnings);
        }

        /// <summary>
        /// Make sure by default conflicts between references are logged as a comment if they are within the sdk itself
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void LogNoWarningForRedistConflictWithinSDK()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "false");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyRedist", "true");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Equal(5, t.RedistFiles.Length);

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictRedistSameSDK", "A.dll", "SDKWithManifest, Version=2.0", "Redist\\Retail\\X86\\A.dll", "Redist\\CommonConfiguration\\Neutral\\A.dll");
            Assert.Equal(0, engine.Warnings);
        }

        /// <summary>
        /// Make sure that if the LogRedistConflictsWithinSDKAsWarning is set log a warning for conflicts within an SDK for redist files.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void LogWarningForRedistConflictWithinSDK()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "false");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyRedist", "true");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item };
            t.LogRedistConflictWithinSDKAsWarning = true;
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);
            Assert.True(success);
            Assert.Equal(5, t.RedistFiles.Length);

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictRedistSameSDK", "A.dll", "SDKWithManifest, Version=2.0", "Redist\\Retail\\X86\\A.dll", "Redist\\CommonConfiguration\\Neutral\\A.dll");
            Assert.Equal(1, engine.Warnings);
        }

        /// <summary>
        /// Verify if there are conflicts between references or redist files between sdks that we log a warning by default.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void LogReferenceAndRedistConflictBetweenSdks()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyRedist", "true");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            ITaskItem item2 = new TaskItem(_sdkDirectory2);
            item2.SetMetadata("ExpandReferenceAssemblies", "true");
            item2.SetMetadata("TargetedSDKConfiguration", "Retail");
            item2.SetMetadata("TargetedSDKArchitecture", "x86");
            item2.SetMetadata("CopyRedist", "true");
            item2.SetMetadata("OriginalItemSpec", "AnotherSDK, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item, item2 };
            t.LogReferencesList = false;
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);

            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(8, t.References.Length);
            Assert.Equal(6, t.RedistFiles.Length);
            Assert.Equal(2, engine.Warnings);

            string redistWinner = Path.Combine(_sdkDirectory, "Redist\\Retail\\Neutral\\B.pri");
            string redistVictim = Path.Combine(_sdkDirectory2, "Redist\\Retail\\X86\\B.pri");
            string referenceWinner = Path.Combine(_sdkDirectory, "References\\Retail\\Neutral\\B.WinMD");
            string referenceVictim = Path.Combine(_sdkDirectory2, "References\\Retail\\X86\\B.WinMD");

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictRedistDifferentSDK", "B.PRI", "SDKWithManifest, Version=2.0", "AnotherSDK, Version=2.0", redistWinner, redistVictim);
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictReferenceDifferentSDK", "SDKWithManifest, Version=2.0", "AnotherSDK, Version=2.0", referenceWinner, referenceVictim);
        }


        /// <summary>
        /// If a user create a target path that causes a conflict between two sdks then we want to warn
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void LogReferenceAndRedistConflictBetweenSdksDueToCustomTargetPath()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyRedist", "true");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            ITaskItem item2 = new TaskItem(_sdkDirectory2);
            item2.SetMetadata("ExpandReferenceAssemblies", "true");
            item2.SetMetadata("TargetedSDKConfiguration", "Retail");
            item2.SetMetadata("TargetedSDKArchitecture", "x86");
            item2.SetMetadata("CopyRedist", "true");
            item2.SetMetadata("OriginalItemSpec", "AnotherSDK, Version=2.0");
            item2.SetMetadata("CopyRedistToSubDirectory", "ASubDirectory\\TwoDeep");

            t.ResolvedSDKReferences = new ITaskItem[] { item, item2 };
            t.LogReferencesList = false;
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);

            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(8, t.References.Length);
            Assert.Equal(6, t.RedistFiles.Length);
            Assert.Equal(2, engine.Warnings);

            string redistWinner = Path.Combine(_sdkDirectory, "Redist\\Retail\\Neutral\\ASubDirectory\\TwoDeep\\B.dll");
            string redistVictim = Path.Combine(_sdkDirectory2, "Redist\\Retail\\X86\\B.dll");

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictRedistDifferentSDK", "ASUBDIRECTORY\\TWODEEP\\B.DLL", "SDKWithManifest, Version=2.0", "AnotherSDK, Version=2.0", redistWinner, redistVictim);
        }

        /// <summary>
        /// Verify if there are conflicts between references or redist files between sdks that we do not log a warning if a certain property (LogxxxConflictBetweenSDKsAsWarning is set to false.
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void LogReferenceAndRedistConflictBetweenSdksNowarning()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "true");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyRedist", "true");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            ITaskItem item2 = new TaskItem(_sdkDirectory2);
            item2.SetMetadata("ExpandReferenceAssemblies", "true");
            item2.SetMetadata("TargetedSDKConfiguration", "Retail");
            item2.SetMetadata("TargetedSDKArchitecture", "x86");
            item2.SetMetadata("CopyRedist", "true");
            item2.SetMetadata("OriginalItemSpec", "AnotherSDK, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item, item2 };
            t.LogReferencesList = false;
            t.LogReferenceConflictBetweenSDKsAsWarning = false;
            t.LogRedistConflictBetweenSDKsAsWarning = false;
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);

            Assert.True(success);
            Assert.Empty(t.CopyLocalFiles);
            Assert.Equal(8, t.References.Length);
            Assert.Equal(6, t.RedistFiles.Length);
            Assert.Equal(0, engine.Warnings);

            string redistWinner = Path.Combine(_sdkDirectory, "Redist\\Retail\\Neutral\\B.pri");
            string redistVictim = Path.Combine(_sdkDirectory2, "Redist\\Retail\\X86\\B.pri");
            string referenceWinner = Path.Combine(_sdkDirectory, "References\\Retail\\Neutral\\B.WinMD");
            string referenceVictim = Path.Combine(_sdkDirectory2, "References\\Retail\\X86\\B.WinMD");

            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictRedistDifferentSDK", "B.PRI", "SDKWithManifest, Version=2.0", "AnotherSDK, Version=2.0", redistWinner, redistVictim);
            engine.AssertLogContainsMessageFromResource(_resourceDelegate, "GetSDKReferenceFiles.ConflictReferenceDifferentSDK", "SDKWithManifest, Version=2.0", "AnotherSDK, Version=2.0", referenceWinner, referenceVictim);
        }

        /// <summary>
        /// If there are conflicting redist files between two sdks but their target paths are different then we should copy both to the appx
        /// </summary>
        [Fact]
        [Trait("Category", "mono-osx-failing")]
        public void TwoSDKSConflictRedistButDifferentTargetPaths()
        {
            var engine = new MockEngine(_output);
            var t = new GetSDKReferenceFiles();
            t.BuildEngine = engine;
            t.CacheFileFolderPath = _cacheDirectory;

            ITaskItem item = new TaskItem(_sdkDirectory);
            item.SetMetadata("ExpandReferenceAssemblies", "false");
            item.SetMetadata("TargetedSDKConfiguration", "Retail");
            item.SetMetadata("TargetedSDKArchitecture", "x86");
            item.SetMetadata("CopyRedistToSubDirectory", "SDK1");
            item.SetMetadata("CopyRedist", "true");
            item.SetMetadata("OriginalItemSpec", "SDKWithManifest, Version=2.0");

            ITaskItem item2 = new TaskItem(_sdkDirectory2);
            item2.SetMetadata("ExpandReferenceAssemblies", "false");
            item2.SetMetadata("TargetedSDKConfiguration", "Retail");
            item2.SetMetadata("TargetedSDKArchitecture", "x86");
            item2.SetMetadata("CopyRedistToSubDirectory", "SDK2");
            item2.SetMetadata("CopyRedist", "true");
            item2.SetMetadata("OriginalItemSpec", "AnotherSDK, Version=2.0");

            t.ResolvedSDKReferences = new ITaskItem[] { item, item2 };
            t.LogReferencesList = false;
            bool success = t.Execute(_getAssemblyName, _getAssemblyRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: true);

            Assert.True(success);
            Assert.Equal(7, t.RedistFiles.Length);
            Assert.Equal(0, engine.Warnings);

            Assert.Equal("A.dll", Path.GetFileName(t.RedistFiles[0].ItemSpec), true);
            Assert.Equal("SDK1\\A.dll", t.RedistFiles[0].GetMetadata("TargetPath"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.RedistFiles[0].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[0].GetMetadata("ResolvedFrom"), true);
            Assert.Empty(t.RedistFiles[0].GetMetadata("Root"));

            Assert.Equal("B.dll", Path.GetFileName(t.RedistFiles[1].ItemSpec), true);
            Assert.Equal("SDK2\\B.dll", t.RedistFiles[1].GetMetadata("TargetPath"), true);
            Assert.Equal("AnotherSDK, Version=2.0", t.RedistFiles[1].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[1].GetMetadata("ResolvedFrom"), true);
            Assert.Empty(t.RedistFiles[1].GetMetadata("Root"));

            Assert.Equal("B.dll", Path.GetFileName(t.RedistFiles[2].ItemSpec), true);
            Assert.Equal("SDK1\\ASubDirectory\\TwoDeep\\B.dll", t.RedistFiles[2].GetMetadata("TargetPath"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.RedistFiles[2].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[2].GetMetadata("ResolvedFrom"), true);
            Assert.Empty(t.RedistFiles[2].GetMetadata("Root"));

            Assert.Equal("B.pri", Path.GetFileName(t.RedistFiles[3].ItemSpec), true);
            Assert.Equal("SDK2\\B.Pri", t.RedistFiles[3].GetMetadata("TargetPath"), true);
            Assert.Equal("AnotherSDK, Version=2.0", t.RedistFiles[3].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[3].GetMetadata("ResolvedFrom"), true);
            Assert.Equal("SDK2", t.RedistFiles[3].GetMetadata("Root"), true);

            Assert.Equal("B.PRI", Path.GetFileName(t.RedistFiles[4].ItemSpec), true);
            Assert.Equal("SDK1\\B.PRI", t.RedistFiles[4].GetMetadata("TargetPath"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.RedistFiles[4].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[4].GetMetadata("ResolvedFrom"), true);
            Assert.Equal("SDK1", t.RedistFiles[4].GetMetadata("Root"), true);

            Assert.Equal("C.dll", Path.GetFileName(t.RedistFiles[5].ItemSpec), true);
            Assert.Equal("SDK1\\C.dll", t.RedistFiles[5].GetMetadata("TargetPath"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.RedistFiles[5].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[5].GetMetadata("ResolvedFrom"), true);
            Assert.Empty(t.RedistFiles[5].GetMetadata("Root"));

            Assert.Equal("D.dll", Path.GetFileName(t.RedistFiles[6].ItemSpec), true);
            Assert.Equal("SDK1\\D.dll", t.RedistFiles[6].GetMetadata("TargetPath"), true);
            Assert.Equal("SDkWithManifest, Version=2.0", t.RedistFiles[6].GetMetadata("OriginalItemSpec"), true);
            Assert.Equal("GetSDKReferenceFiles", t.RedistFiles[6].GetMetadata("ResolvedFrom"), true);
            Assert.Empty(t.RedistFiles[6].GetMetadata("Root"));
        }

        private static AssemblyNameExtension GetAssemblyName(string path)
        {
            if (Path.GetFileName(path).Equals("C.winmd", StringComparison.OrdinalIgnoreCase))
            {
                throw new BadImageFormatException();
            }

            if (Path.GetExtension(path).Equals(".winmd", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                return new AssemblyNameExtension(fileName + ", Version=2.0.0.0, Culture=Neutral, PublicKeyToken=null");
            }

            return null;
        }

        private static string GetImageRuntimeVersion(string path)
        {
            if (Path.GetFileName(path).Equals("A.winmd", StringComparison.OrdinalIgnoreCase))
            {
                return "WindowsRuntime 1.0;CLR V2.0.50727";
            }
            if (Path.GetExtension(path).Equals(".winmd", StringComparison.OrdinalIgnoreCase))
            {
                return "WindowsRuntime 1.0";
            }

            if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return "CLR V2.0.50727";
            }

            return null;
        }
    }
}
