// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Reflection;
using Microsoft.Build.Tasks;
using Microsoft.Build.Shared;
using Xunit;
using Shouldly;
using System;

namespace Microsoft.Build.UnitTests
{
    sealed public class ResGenDependencies_Tests
    {
        [Theory]
        [MemberData(nameof(GenerateResource_Tests.Utilities.UsePreserializedResourceStates), MemberType = typeof(GenerateResource_Tests.Utilities))]

        public void DirtyCleanScenario(bool useMSBuildResXReader)
        {
            ResGenDependencies cache = new();
            string resx = CreateSampleResx();
            string stateFile = FileUtilities.GetTemporaryFile();

            try
            {
                // A newly created cache is not dirty.
                cache.IsDirty.ShouldBeFalse();

                ResGenDependencies.PortableLibraryFile libFile = new("otherFileName");
                libFile.outputFiles = new string[] { "first", "second" };
                libFile.assemblySimpleName = "simpleName";
                libFile.lastModified = DateTime.Now.Subtract(TimeSpan.FromSeconds(10));
                cache.portableLibraries.Add("fileName", libFile);

                // Writing the file to disk should make the cache clean.
                cache.SerializeCache(stateFile, /* Log */ null);
                cache.IsDirty.ShouldBeFalse();

                // Getting a file that wasn't in the cache is a write operation.
                cache.GetResXFileInfo(resx, useMSBuildResXReader);
                cache.IsDirty.ShouldBeTrue();

                // Add linkedFiles to further test serialization and deserialization.
                cache.resXFiles.TryGetValue(resx, out ResGenDependencies.ResXFile file).ShouldBeTrue();
                file.linkedFiles = new string[] { "third", "fourth" };

                // Writing the file to disk should make the cache clean again.
                cache.SerializeCache(stateFile, /* Log */ null);
                cache.IsDirty.ShouldBeFalse();

                // Deserialize from disk. Result should not be dirty.
                ResGenDependencies cache2 = ResGenDependencies.DeserializeCache(stateFile, true, /* Log */ null);
                cache2.IsDirty.ShouldBeFalse();

                // Validate that serialization worked
                cache.portableLibraries.TryGetValue("fileName", out ResGenDependencies.PortableLibraryFile portableLibrary);
                cache2.portableLibraries.TryGetValue("fileName", out ResGenDependencies.PortableLibraryFile portableLibrary2);
                portableLibrary2.filename.ShouldBe(portableLibrary.filename);
                portableLibrary2.exists.ShouldBe(portableLibrary.exists);
                portableLibrary2.assemblySimpleName.ShouldBe(portableLibrary.assemblySimpleName);
                portableLibrary2.lastModified.ShouldBe(portableLibrary.lastModified);
                portableLibrary2.outputFiles.Length.ShouldBe(portableLibrary.outputFiles.Length);
                portableLibrary2.outputFiles[1].ShouldBe(portableLibrary.outputFiles[1]);
                cache.resXFiles.TryGetValue(resx, out ResGenDependencies.ResXFile resX);
                cache2.resXFiles.TryGetValue(resx, out ResGenDependencies.ResXFile resX2);
                resX2.filename.ShouldBe(resX.filename);
                resX2.lastModified.ShouldBe(resX.lastModified);
                resX2.linkedFiles.Length.ShouldBe(resX.linkedFiles.Length);
                resX2.linkedFiles[1].ShouldBe(resX.linkedFiles[1]);

                // Asking for a file that's in the cache should not dirty the cache.
                cache2.GetResXFileInfo(resx, useMSBuildResXReader);
                cache2.IsDirty.ShouldBeFalse();

                // Changing UseSourcePath to false should dirty the cache.
                cache2.UseSourcePath = false;
                cache2.IsDirty.ShouldBeTrue();
            }
            finally
            {
                File.Delete(resx);
                File.Delete(stateFile);
            }
        }

        /// <summary>
        /// Create a sample resx file on disk. Caller is responsible for deleting.
        /// </summary>
        /// <returns></returns>
        private string CreateSampleResx()
        {
            string resx = FileUtilities.GetTemporaryFile();
            File.Delete(resx);
            Stream fileToSend = Assembly.GetExecutingAssembly().GetManifestResourceStream("Microsoft.Build.Tasks.UnitTests.SampleResx");
            using (FileStream f = new FileStream(resx, FileMode.CreateNew))
            {
                byte[] buffer = new byte[2048];
                int bytes;
                while ((bytes = fileToSend.Read(buffer, 0, 2048)) > 0)
                {
                    f.Write(buffer, 0, bytes);
                }
                fileToSend.Close();
            }
            return resx;
        }
    }
}
