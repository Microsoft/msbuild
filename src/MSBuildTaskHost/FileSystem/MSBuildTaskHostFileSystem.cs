// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Build.Shared.FileSystem
{
    /// <summary>
    /// Legacy implementation for MSBuildTaskHost which is stuck on net20 APIs
    /// </summary>
    internal class MSBuildTaskHostFileSystem : IFileSystem
    {
        private static readonly MSBuildTaskHostFileSystem Instance = new MSBuildTaskHostFileSystem();

        public static MSBuildTaskHostFileSystem Singleton() => Instance;

        public bool FileOrDirectoryExists(string path)
        {
            return NativeMethodsShared.FileOrDirectoryExists(path);
        }

        public FileAttributes GetAttributes(string path)
        {
            return File.GetAttributes(path);
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return File.GetLastWriteTimeUtc(path);
        }

        public bool DirectoryExists(string path)
        {
            return NativeMethodsShared.DirectoryExists(path);
        }

        public IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            return Directory.GetDirectories(path, searchPattern, searchOption);
        }

        public TextReader ReadFile(string path)
        {
            return new StreamReader(path);
        }

        public Stream GetFileStream(string path, FileMode mode, FileAccess access, FileShare share)
        {
            return new FileStream(path, mode, access, share);
        }

        public string ReadFileAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public byte[] ReadFileAllBytes(string path)
        {
            return File.ReadAllBytes(path);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            return Directory.GetFiles(path, searchPattern, searchOption);
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            ErrorUtilities.VerifyThrow(searchOption == SearchOption.TopDirectoryOnly, $"In net20 {nameof(Directory.GetFileSystemEntries)} does not take a {nameof(SearchOption)} parameter");

            return Directory.GetFileSystemEntries(path, searchPattern);
        }

        public bool FileExists(string path)
        {
            return NativeMethodsShared.FileExists(path);
        }
    }
}
