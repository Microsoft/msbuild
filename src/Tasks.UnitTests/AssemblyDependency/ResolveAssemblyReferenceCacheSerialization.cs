// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Build.Shared;
using Microsoft.Build.Tasks;
using Microsoft.Build.Utilities;
using Shouldly;
using Xunit;

namespace Microsoft.Build.UnitTests.ResolveAssemblyReference_Tests
{
    public class ResolveAssemblyReferenceCacheSerialization : IDisposable
    {
        private readonly string _rarCacheFile;
        private readonly TaskLoggingHelper _taskLoggingHelper;

        public ResolveAssemblyReferenceCacheSerialization()
        {
            var tempPath = Path.GetTempPath();
            _rarCacheFile = Path.Combine(tempPath, Guid.NewGuid() + ".UnitTest.RarCache");
            _taskLoggingHelper = new TaskLoggingHelper(new MockEngine(), "TaskA")
            {
                TaskResources = AssemblyResources.PrimaryResources
            };
        }

        public void Dispose()
        {
            if (File.Exists(_rarCacheFile))
            {
                FileUtilities.DeleteNoThrow(_rarCacheFile);
            }
        }

        [Fact]
        public void RoundTripEmptyState()
        {
            SystemState systemState = new();

            systemState.SerializeCache(_rarCacheFile, _taskLoggingHelper);

            var deserialized = SystemState.DeserializeCache(_rarCacheFile, _taskLoggingHelper, typeof(SystemState));

            deserialized.ShouldNotBeNull();
        }

        [Fact]
        public void CorrectFileVersion()
        {
            SystemState systemState = new();

            systemState.SerializeCache(_rarCacheFile, _taskLoggingHelper);
            using (var cacheStream = new FileStream(_rarCacheFile, FileMode.Open, FileAccess.ReadWrite))
            {
                cacheStream.Seek(0, SeekOrigin.Begin);
                cacheStream.WriteByte(StateFileBase.CurrentSerializationVersion);
                cacheStream.Close();
            }

            var deserialized = SystemState.DeserializeCache(_rarCacheFile, _taskLoggingHelper, typeof(SystemState));

            deserialized.ShouldNotBeNull();
        }

        [Fact]
        public void WrongFileVersion()
        {
            SystemState systemState = new();

            systemState.SerializeCache(_rarCacheFile, _taskLoggingHelper);
            using (var cacheStream = new FileStream(_rarCacheFile, FileMode.Open, FileAccess.ReadWrite))
            {
                cacheStream.Seek(0, SeekOrigin.Begin);
                cacheStream.WriteByte(StateFileBase.CurrentSerializationVersion - 1);
                cacheStream.Close();
            }

            var deserialized = SystemState.DeserializeCache(_rarCacheFile, _taskLoggingHelper, typeof(SystemState));

            deserialized.ShouldBeNull();
        }

        [Fact]
        public void ValidateSerializationAndDeserialization()
        {
            Dictionary<string, SystemState.FileState> cache = new() {
                    { "path1", new SystemState.FileState(DateTime.Now) },
                    { "path2", new SystemState.FileState(DateTime.Now) { Assembly = new AssemblyNameExtension("hi") } },
                    { "dllName", new SystemState.FileState(DateTime.Now.AddSeconds(-10)) {
                        Assembly = null,
                        RuntimeVersion = "v4.0.30319",
                        FrameworkNameAttribute = new FrameworkName(".NETFramework", Version.Parse("4.7.2"), "Profile"),
                        scatterFiles = new string[] { "first", "second" } } } };
            SystemState sysState = new();
            sysState.instanceLocalFileStateCache = cache;
            SystemState sysState2 = null;
            using (TestEnvironment env = TestEnvironment.Create())
            {
                TransientTestFile file = env.CreateFile();
                sysState.SerializeCache(file.Path, null);
                sysState2 = SystemState.DeserializeCache(file.Path, null, typeof(SystemState)) as SystemState;
            }

            Dictionary<string, SystemState.FileState> cache2 = sysState2.instanceLocalFileStateCache;
            cache2.Count.ShouldBe(cache.Count);
            cache2["path2"].Assembly.Name.ShouldBe(cache["path2"].Assembly.Name);
            SystemState.FileState dll = cache["dllName"];
            SystemState.FileState dll2 = cache2["dllName"];
            dll2.Assembly.ShouldBe(dll.Assembly);
            dll2.FrameworkNameAttribute.FullName.ShouldBe(dll.FrameworkNameAttribute.FullName);
            dll2.LastModified.ShouldBe(dll.LastModified);
            dll2.RuntimeVersion.ShouldBe(dll.RuntimeVersion);
            dll2.scatterFiles.Length.ShouldBe(dll.scatterFiles.Length);
            dll2.scatterFiles[1].ShouldBe(dll.scatterFiles[1]);
        }
    }
}
