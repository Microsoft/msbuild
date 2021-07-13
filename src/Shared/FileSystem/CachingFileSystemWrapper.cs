// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Build.Shared.FileSystem
{
    internal class CachingFileSystemWrapper : IFileSystem
    {
        private readonly IFileSystem _fileSystem;
        private readonly ConcurrentDictionary<string, bool> _existenceCache = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, DateTime> _lastWriteTimeCache = new ConcurrentDictionary<string, DateTime>();

        public CachingFileSystemWrapper(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public bool FileOrDirectoryExists(string path)
        {
            return CachedExistenceCheck(path, p => _fileSystem.FileOrDirectoryExists(p));
        }

        public FileAttributes GetAttributes(string path)
        {
            return _fileSystem.GetAttributes(path);
        }

        public DateTime GetLastWriteTimeUtc(string path)
        {
            return _lastWriteTimeCache.GetOrAdd(path, p =>_fileSystem.GetLastWriteTimeUtc(p));
        }

        public bool DirectoryExists(string path)
        {
            return CachedExistenceCheck(path, p => _fileSystem.DirectoryExists(p));
        }

        public bool FileExists(string path)
        {
            return CachedExistenceCheck(path, p => _fileSystem.FileExists(p));
        }

        public IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            return _fileSystem.EnumerateDirectories(path, searchPattern, searchOption);
        }

        public TextReader ReadFile(string path)
        {
            return _fileSystem.ReadFile(path);
        }

        public Stream GetFileStream(string path, FileMode mode, FileAccess access, FileShare share)
        {
            return _fileSystem.GetFileStream(path, mode, access, share);
        }

        public string ReadFileAllText(string path)
        {
            return _fileSystem.ReadFileAllText(path);
        }

        public byte[] ReadFileAllBytes(string path)
        {
            return _fileSystem.ReadFileAllBytes(path);
        }

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            return _fileSystem.EnumerateFiles(path, searchPattern, searchOption);
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            return _fileSystem.EnumerateFileSystemEntries(path, searchPattern, searchOption);
        }

        private bool CachedExistenceCheck(string path, Func<string, bool> existenceCheck)
        {
            return _existenceCache.GetOrAdd(path, existenceCheck);
        }
    }
}
