// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Build.Shared.FileSystem
{
    /// <summary>
    /// Implementation of file system operations on windows. Combination of native and managed implementations.
    /// TODO Remove this class and replace with WindowsFileSystem. Test perf to ensure no regressions.
    /// </summary>
    internal class MSBuildOnWindowsFileSystem : IFileSystem
    {
        private static readonly MSBuildOnWindowsFileSystem Instance = new MSBuildOnWindowsFileSystem();

        public static MSBuildOnWindowsFileSystem Singleton() => Instance;

        protected MSBuildOnWindowsFileSystem() { }

        public TextReader ReadFile(string path)
        {
            return ManagedFileSystem.Singleton().ReadFile(path);
        }

        public Stream GetFileStream(string path, FileMode mode, FileAccess access, FileShare share)
        {
            return ManagedFileSystem.Singleton().GetFileStream(path, mode, access, share);
        }

        public string ReadFileAllText(string path)
        {
            return ManagedFileSystem.Singleton().ReadFileAllText(path);
        }

        public byte[] ReadFileAllBytes(string path)
        {
            return ManagedFileSystem.Singleton().ReadFileAllBytes(path);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
        {
            return ManagedFileSystem.Singleton().EnumerateFiles(path, searchPattern, searchOption);
        }

        public IEnumerable<string> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
        {
            return ManagedFileSystem.Singleton().EnumerateDirectories(path, searchPattern, searchOption);
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern, SearchOption searchOption)
        {
            return ManagedFileSystem.Singleton().EnumerateFileSystemEntries(path, searchPattern, searchOption);
        }

        public FileAttributes GetAttributes(string path)
        {
            return ManagedFileSystem.Singleton().GetAttributes(path);
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return ManagedFileSystem.Singleton().GetLastWriteTimeUtc(path);
        }

        public bool DirectoryExists(string path)
        {
            return WindowsFileSystem.Singleton().DirectoryExists(path);
        }

        public bool FileExists(string path)
        {
            return WindowsFileSystem.Singleton().FileExists(path);
        }

        public bool FileOrDirectoryExists(string path)
        {
            return WindowsFileSystem.Singleton().FileOrDirectoryExists(path);
        }
    }
}
