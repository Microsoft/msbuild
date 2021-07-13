// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Shared.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Build.FileSystem
{
    /// <summary>
    /// Abstracts away some file system operations.
    ///
    /// Implementations:
    /// - must be thread safe
    /// - may cache some or all the calls.
    /// </summary>
    public abstract class MSBuildFileSystemBase : IFileSystem
    {
        #region IFileSystem implementation

        /// <summary>
        /// Use this for var sr = new StreamReader(path)
        /// </summary>
        public virtual TextReader ReadFile(string path) => FileSystems.Default.ReadFile(path);

        /// <summary>
        /// Use this for new FileStream(path, mode, access, share)
        /// </summary>
        public virtual Stream GetFileStream(string path, FileMode mode, FileAccess access, FileShare share) => FileSystems.Default.GetFileStream(path, mode, access, share);

        /// <summary>
        /// Use this for File.ReadAllText(path)
        /// </summary>
        public virtual string ReadFileAllText(string path) => FileSystems.Default.ReadFileAllText(path);

        /// <summary>
        /// Use this for File.ReadAllBytes(path)
        /// </summary>
        public virtual byte[] ReadFileAllBytes(string path) => FileSystems.Default.ReadFileAllBytes(path);

        /// <summary>
        /// Use this for Directory.EnumerateFiles(path, pattern, option)
        /// </summary>
        public virtual IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
            => FileSystems.Default.EnumerateFiles(path, searchPattern, searchOption);

        /// <summary>
        /// Use this for Directory.EnumerateFolders(path, pattern, option)
        /// </summary>
        public virtual IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
            => FileSystems.Default.EnumerateDirectories(path, searchPattern, searchOption);

        /// <summary>
        /// Use this for Directory.EnumerateFileSystemEntries(path, pattern, option)
        /// </summary>
        public virtual IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
            => FileSystems.Default.EnumerateFileSystemEntries(path, searchPattern, searchOption);

        /// <summary>
        /// Use this for File.GetAttributes()
        /// </summary>
        public virtual FileAttributes GetAttributes(string path) => FileSystems.Default.GetAttributes(path);

        /// <summary>
        /// Use this for File.GetLastWriteTimeUtc(path)
        /// </summary>
        public virtual DateTime GetLastWriteTimeUtc(string path) => FileSystems.Default.GetLastWriteTimeUtc(path);

        /// <summary>
        /// Use this for Directory.Exists(path)
        /// </summary>
        public virtual bool DirectoryExists(string path) => FileSystems.Default.DirectoryExists(path);

        /// <summary>
        /// Use this for File.Exists(path)
        /// </summary>
        public virtual bool FileExists(string path) => FileSystems.Default.FileExists(path);

        /// <summary>
        /// Use this for File.Exists(path) || Directory.Exists(path)
        /// </summary>
        public virtual bool FileOrDirectoryExists(string path) => FileSystems.Default.FileOrDirectoryExists(path);

        #endregion
    }
}
