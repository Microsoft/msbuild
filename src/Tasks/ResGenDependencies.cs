// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Resources;
using System.Xml;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Shared;
using Microsoft.Build.Shared.FileSystem;
using Microsoft.Build.Tasks.ResourceHandling;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks
{
    /// <remarks>
    /// This class is a caching mechanism for the resgen task to keep track of linked
    /// files within processed .resx files.
    /// 
    /// This is an on-disk serialization format, don't change field names or types or use readonly.
    /// </remarks>
    /// Serializable should be included in all state files. It permits BinaryFormatter-based calls, including from GenerateResource, which we cannot move off BinaryFormatter.
    [Serializable]
    internal sealed class ResGenDependencies : StateFileBase, ITranslatable
    {
        /// <summary>
        /// The list of resx files.
        /// </summary>
        internal IDictionary<string, ResXFile> resXFiles = new Dictionary<string, ResXFile>();

        /// <summary>
        /// A list of portable libraries and the ResW files they can produce.
        /// </summary>
        internal IDictionary<string, PortableLibraryFile> portableLibraries = new Dictionary<string, PortableLibraryFile>();

        /// <summary>
        /// A newly-created ResGenDependencies is not dirty.
        /// What would be the point in saving the default?
        /// </summary>
        [NonSerialized]
        private bool _isDirty;

        /// <summary>
        ///  This is the directory that will be used for resolution of files linked within a .resx.
        ///  If this is NULL then we use the directory in which the .resx is in (that should always
        ///  be the default!)
        /// </summary>
        internal string baseLinkedFileDirectory;

        internal string BaseLinkedFileDirectory
        {
            get => baseLinkedFileDirectory;
            set
            {
                if (value == null && baseLinkedFileDirectory == null)
                {
                    // No change
                    return;
                }
                if ((value == null && baseLinkedFileDirectory != null) ||
                     (value != null && baseLinkedFileDirectory == null) ||
                     (!String.Equals(baseLinkedFileDirectory, value, StringComparison.OrdinalIgnoreCase)))
                {
                    // Ok, this is slightly complicated.  Changing the base directory in any manner may
                    // result in changes to how we find .resx files.  Therefore, we must clear our out
                    // cache whenever the base directory changes.  
                    resXFiles.Clear();
                    _isDirty = true;
                    baseLinkedFileDirectory = value;
                }
            }
        }

        internal bool UseSourcePath
        {
            set
            {
                // Ensure that the cache is properly initialized with respect to how resgen will 
                // resolve linked files within .resx files.  ResGen has two different
                // ways for resolving relative file-paths in linked files. The way
                // that ResGen resolved relative paths before Whidbey was always to
                // resolve from the current working directory. In Whidbey a new command-line
                // switch "/useSourcePath" instructs ResGen to use the folder that
                // contains the .resx file as the path from which it should resolve
                // relative paths. So we should base our timestamp/existence checking
                // on the same switch & resolve in the same manner as ResGen.
                BaseLinkedFileDirectory = value ? null : Directory.GetCurrentDirectory();
            }
        }

        public ResGenDependencies() { }

        public ResGenDependencies(ITranslator translator)
        {
            Translate(translator);
        }

        public override void Translate(ITranslator translator)
        {
            translator.TranslateDictionary(ref resXFiles,
                (ITranslator translator, ref string s) => translator.Translate(ref s),
                (ITranslator translator, ref ResXFile resx) => {
                    ResXFile temp = resx ?? new();
                    temp.Translate(translator);
                    resx = temp;
                },
                count => new Dictionary<string, ResXFile>(count));
            translator.TranslateDictionary(ref portableLibraries,
                (ITranslator translator, ref string s) => translator.Translate(ref s),
                (ITranslator translator, ref PortableLibraryFile portableLibrary) => {
                    PortableLibraryFile temp = portableLibrary ?? new();
                    temp.Translate(translator);
                    portableLibrary = temp;
                },
                count => new Dictionary<string, PortableLibraryFile>(count));
            translator.Translate(ref baseLinkedFileDirectory);
        }

        internal ResXFile GetResXFileInfo(string resxFile, bool useMSBuildResXReader)
        {
            // First, try to retrieve the resx information from our hashtable.
            if (!resXFiles.TryGetValue(resxFile, out ResXFile retVal))
            {
                // Ok, the file wasn't there.  Add it to our cache and return it to the caller.  
                retVal = AddResxFile(resxFile, useMSBuildResXReader);
            }
            else
            {
                // The file was there.  Is it up to date?  If not, then we'll have to refresh the file
                // by removing it from the hashtable and readding it.
                if (retVal.HasFileChanged())
                {
                    resXFiles.Remove(resxFile);
                    _isDirty = true;
                    retVal = AddResxFile(resxFile, useMSBuildResXReader);
                }
            }

            return retVal;
        }

        private ResXFile AddResxFile(string file, bool useMSBuildResXReader)
        {
            // This method adds a .resx file "file" to our .resx cache.  The method causes the file
            // to be cracked for contained files.

            var resxFile = new ResXFile(file, BaseLinkedFileDirectory, useMSBuildResXReader);
            resXFiles.Add(file, resxFile);
            _isDirty = true;
            return resxFile;
        }

        internal PortableLibraryFile TryGetPortableLibraryInfo(string libraryPath)
        {
            // First, try to retrieve the portable library information from our hashtable.  
            portableLibraries.TryGetValue(libraryPath, out PortableLibraryFile retVal);

            // The file is in our cache.  Make sure it's up to date.  If not, discard
            // this entry from the cache and rebuild all the state at a later point.
            if (retVal?.HasFileChanged() == true)
            {
                portableLibraries.Remove(libraryPath);
                _isDirty = true;
                retVal = null;
            }

            return retVal;
        }

        internal void UpdatePortableLibrary(PortableLibraryFile library)
        {
            if (!portableLibraries.TryGetValue(library.FileName, out PortableLibraryFile cached) || !library.Equals(cached))
            {
                // Add a new entry or replace the existing one.
                portableLibraries.Add(library.FileName, library);
                _isDirty = true;
            }
        }

        /// <summary>
        /// Writes the contents of this object out to the specified file.
        /// </summary>
        internal override void SerializeCache(string stateFile, TaskLoggingHelper log)
        {
            base.SerializeCache(stateFile, log);
            _isDirty = false;
        }

        /// <summary>
        /// Reads the .cache file from disk into a ResGenDependencies object.
        /// </summary>
        internal static ResGenDependencies DeserializeCache(string stateFile, bool useSourcePath, TaskLoggingHelper log)
        {
            var retVal = (ResGenDependencies)DeserializeCache(stateFile, log, typeof(ResGenDependencies)) ?? new ResGenDependencies();

            // Ensure that the cache is properly initialized with respect to how resgen will 
            // resolve linked files within .resx files.  ResGen has two different
            // ways for resolving relative file-paths in linked files. The way
            // that ResGen resolved relative paths before Whidbey was always to
            // resolve from the current working directory. In Whidbey a new command-line
            // switch "/useSourcePath" instructs ResGen to use the folder that
            // contains the .resx file as the path from which it should resolve
            // relative paths. So we should base our timestamp/existence checking
            // on the same switch & resolve in the same manner as ResGen.
            retVal.UseSourcePath = useSourcePath;

            return retVal;
        }

        /// <remarks>
        /// Represents a single .resx file in the dependency cache.
        /// 
        /// This is an on-disk serialization format, don't change field names or types or use readonly.
        /// </remarks>
        /// Serializable should be included in all state files. It permits BinaryFormatter-based calls, including from GenerateResource, which we cannot move off BinaryFormatter.
        [Serializable]
        internal sealed class ResXFile : DependencyFile, ITranslatable
        {
            // Files contained within this resx file.
            internal string[] linkedFiles;

            internal string[] LinkedFiles => linkedFiles;

            internal ResXFile(string filename, string baseLinkedFileDirectory, bool useMSBuildResXReader) : base(filename)
            {
                // Creates a new ResXFile object and populates the class member variables
                // by computing a list of linked files within the .resx that was passed in.
                //
                // filename is the filename of the .resx file that is to be examined.

                if (FileSystems.Default.FileExists(FileName))
                {
                    linkedFiles = GetLinkedFiles(filename, baseLinkedFileDirectory, useMSBuildResXReader);
                }
            }

            internal ResXFile()
            {
            }

            public void Translate(ITranslator translator)
            {
                translator.Translate(ref linkedFiles);
                translator.Translate(ref filename);
                translator.Translate(ref lastModified);
                translator.Translate(ref exists);
            }

            /// <summary>
            /// Given a .RESX file, returns all the linked files that are referenced within that .RESX.
            /// </summary>
            /// <exception cref="ArgumentException">May be thrown if Resx is invalid. May contain XmlException.</exception>
            /// <exception cref="XmlException">May be thrown if Resx is invalid</exception>
            private static string[] GetLinkedFiles(string filename, string baseLinkedFileDirectory, bool useMSBuildResXReader)
            {
                // This method finds all linked .resx files for the .resx file that is passed in.
                // filename is the filename of the .resx file that is to be examined.

                // Construct the return array
                var retVal = new List<string>();

                if (useMSBuildResXReader)
                {
                    foreach (IResource resource in MSBuildResXReader.GetResourcesFromFile(filename, pathsRelativeToBasePath: baseLinkedFileDirectory == null))
                    {
                        if (resource is FileStreamResource linkedResource)
                        {
                            retVal.Add(linkedResource.FileName);
                        }
                    }
                }
                else
                {
#if FEATURE_RESXREADER_LIVEDESERIALIZATION
                    using (var resxReader = new ResXResourceReader(filename))
                    {
                        // Tell the reader to return ResXDataNode's instead of the object type
                        // the resource becomes at runtime so we can figure out which files
                        // the .resx references
                        resxReader.UseResXDataNodes = true;

                        // First we need to figure out where the linked file resides in order
                        // to see if it exists & compare its timestamp, and we need to do that
                        // comparison in the same way ResGen does it. ResGen has two different
                        // ways for resolving relative file-paths in linked files. The way
                        // that ResGen resolved relative paths before Whidbey was always to
                        // resolve from the current working directory. In Whidbey a new command-line
                        // switch "/useSourcePath" instructs ResGen to use the folder that
                        // contains the .resx file as the path from which it should resolve
                        // relative paths. So we should base our timestamp/existence checking
                        // on the same switch & resolve in the same manner as ResGen.
                        resxReader.BasePath = baseLinkedFileDirectory ?? Path.GetDirectoryName(filename);

                        foreach (DictionaryEntry dictEntry in resxReader)
                        {
                            if (dictEntry.Value is ResXDataNode node)
                            {
                                ResXFileRef resxFileRef = node.FileRef;
                                if (resxFileRef != null)
                                {
                                    retVal.Add(FileUtilities.MaybeAdjustFilePath(resxFileRef.FileName));
                                }
                            }
                        }
                    }
#else
                    // mimic Core behavior prior to MSBuildResXReader: just don't check for linked files
#endif
                }

                return retVal.ToArray();
            }
        }

        /// <remarks>
        /// Represents a single assembly in the dependency cache, which may produce 
        /// 0 to many ResW files.
        /// 
        /// This is an on-disk serialization format, don't change field names or types or use readonly.
        /// </remarks>
        /// Serializable should be included in all state files. It permits BinaryFormatter-based calls, including from GenerateResource, which we cannot move off BinaryFormatter.
        [Serializable]
        internal sealed class PortableLibraryFile : DependencyFile, ITranslatable
        {
            internal string[] outputFiles;
            internal string neutralResourceLanguage;
            internal string assemblySimpleName;

            internal PortableLibraryFile()
            {
            }

            public void Translate(ITranslator translator)
            {
                translator.Translate(ref assemblySimpleName);
                translator.Translate(ref outputFiles);
                translator.Translate(ref neutralResourceLanguage);
                translator.Translate(ref filename);
                translator.Translate(ref lastModified);
                translator.Translate(ref exists);
            }

            internal PortableLibraryFile(string filename)
                : base(filename)
            {
            }

            internal string[] OutputFiles
            {
                get => outputFiles;
                set => outputFiles = value;
            }

            internal string NeutralResourceLanguage
            {
                get => neutralResourceLanguage;
                set => neutralResourceLanguage = value;
            }

            internal string AssemblySimpleName
            {
                get => assemblySimpleName;
                set => assemblySimpleName = value;
            }

            internal bool AllOutputFilesAreUpToDate()
            {
                Debug.Assert(outputFiles != null, "OutputFiles hasn't been set");
                foreach (string outputFileName in outputFiles)
                {
                    var outputFile = new FileInfo(FileUtilities.FixFilePath(outputFileName));
                    if (!outputFile.Exists || outputFile.LastWriteTime < LastModified)
                    {
                        return false;
                    }
                }

                return true;
            }

            internal bool Equals(PortableLibraryFile otherLibrary)
            {
                if (!String.Equals(assemblySimpleName, otherLibrary.assemblySimpleName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!String.Equals(neutralResourceLanguage, otherLibrary.neutralResourceLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                Debug.Assert(OutputFiles != null, "This has not been initialized");
                Debug.Assert(otherLibrary.OutputFiles != null, "The other library has not been initialized");
                if (OutputFiles.Length != otherLibrary.OutputFiles.Length)
                {
                    return false;
                }

                for (int i = 0; i < OutputFiles.Length; i++)
                {
                    if (!String.Equals(OutputFiles[i], otherLibrary.OutputFiles[i], StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                return true;
            }
        }
        
        /// <summary>
        /// Whether this cache is dirty or not.
        /// </summary>
        internal bool IsDirty => _isDirty;
    }
}
