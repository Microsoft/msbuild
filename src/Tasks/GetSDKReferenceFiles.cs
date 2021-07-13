// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Shared.FileSystem;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.Tasks
{
    /// <summary>
    /// Resolves an SDKReference to a full path on disk
    /// </summary>
    public class GetSDKReferenceFiles : TaskExtension
    {
        /// <summary>
        /// Set of resolvedSDK references which we will use to find the reference assemblies.
        /// </summary>
        private ITaskItem[] _resolvedSDKReferences = Array.Empty<ITaskItem>();

        /// <summary>
        /// Set of resolved reference assemblies. This removes any duplicate ones between sdks.
        /// </summary>
        private readonly HashSet<ResolvedReferenceAssembly> _resolvedReferences = new HashSet<ResolvedReferenceAssembly>();

        /// <summary>
        /// Set of resolved reference assemblies. This removes any duplicate ones between sdks.
        /// </summary>
        private readonly HashSet<ResolvedRedistFile> _resolveRedistFiles = new HashSet<ResolvedRedistFile>();

        /// <summary>
        /// Set of reference assembly extensions to look for.
        /// </summary>
        private string[] _referenceExtensions = { ".winmd", ".dll" };

        /// <summary>
        /// Dictionary of SDK Identity to the cache file that contains the file information for it.
        /// </summary>
        private readonly ConcurrentDictionary<string, SDKInfo> _cacheFileForSDKs = new ConcurrentDictionary<string, SDKInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Set of exceptions which were thrown while reading or writing to the cache file, this needs to be thread safe since TPL code will add exceptions into this structure at the same time.
        /// </summary>
        private readonly ConcurrentQueue<string> _exceptions = new ConcurrentQueue<string>();

        /// <summary>
        /// Delegate to get the assembly name
        /// </summary>
        private GetAssemblyName _getAssemblyName;

        /// <summary>
        /// Get the image runtime version from a file
        /// </summary>
        private GetAssemblyRuntimeVersion _getRuntimeVersion;

        /// <summary>
        /// File exists delegate
        /// </summary>
        private FileExists _fileExists;

        /// <summary>
        /// When false, allow fire-and-forget background work.
        /// </summary>
        private bool _synchronous;

        /// <summary>
        /// Folder where the cache files are written to
        /// </summary>
        private string _cacheFilePath = Path.GetTempPath();

        #region Properties

        /// <summary>
        /// Path where the cache files should be stored
        /// </summary>
        public string CacheFileFolderPath
        {
            get => _cacheFilePath;

            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, nameof(CacheFileFolderPath));
                _cacheFilePath = value;
            }
        }

        /// <summary>
        /// Resolved SDK references which we will get the reference assemblies from.
        /// </summary>
        public ITaskItem[] ResolvedSDKReferences
        {
            get => _resolvedSDKReferences;

            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, nameof(ResolvedSDKReferences));
                _resolvedSDKReferences = value;
            }
        }

        /// <summary>
        /// Extensions which should be considered reference files, we will look for
        /// the files in the order they are specified in the array.
        /// </summary>
        public string[] ReferenceExtensions
        {
            get => _referenceExtensions;

            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, nameof(ReferenceExtensions));
                _referenceExtensions = value;
            }
        }

        /// <summary>
        /// Should the references found as part of resolving the sdk be logged.
        /// The default is true
        /// </summary>
        public bool LogReferencesList { get; set; } = true;

        /// <summary>
        /// Should the redist files found as part of resolving the sdk be logged.
        /// The default is true
        /// </summary>
        public bool LogRedistFilesList { get; set; } = true;

        /// <summary>
        /// The targetted SDK identifier.
        /// </summary>
        public string TargetSDKIdentifier { get; set; }

        /// <summary>
        /// The targeted SDK version.
        /// </summary>
        public string TargetSDKVersion { get; set; }

        /// <summary>
        /// The targetted platform identifier.
        /// </summary>
        public string TargetPlatformIdentifier { get; set; }

        /// <summary>
        /// The targeted platform version.
        /// </summary>
        public string TargetPlatformVersion { get; set; }

        /// <summary>
        /// Resolved reference items.
        /// </summary>
        [Output]
        public ITaskItem[] References { get; private set; } = Array.Empty<ITaskItem>();

        /// <summary>
        /// Resolved redist files.
        /// </summary>
        [Output]
        public ITaskItem[] RedistFiles { get; private set; } = Array.Empty<ITaskItem>();

        /// <summary>
        /// Files that need to be copied locally, this is the reference assemblies and the xml intellisense files.
        /// </summary>
        [Output]
        public ITaskItem[] CopyLocalFiles { get; private set; } = Array.Empty<ITaskItem>();

        /// <summary>
        /// Should conflicts between redist files within an SDK be logged as a message or a warning.
        /// The default is to log them as a message.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "SDKAs", Justification = "Shipped this way in Dev11 Beta (go-live)")]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "SDKAs", Justification = "SDK and As are two different words")]
        public bool LogRedistConflictWithinSDKAsWarning { get; set; }

        /// <summary>
        /// Should conflicts between redist files across different referenced SDKs be logged as a message or a warning.
        /// The default is to log them as a warning.
        /// </summary>
        [SuppressMessage(
            "Microsoft.Naming",
            "CA1709:IdentifiersShouldBeCasedCorrectly",
            MessageId = "SDKs",
            Justification = "Shipped this way in Dev11 Beta (go-live)")]
        public bool LogRedistConflictBetweenSDKsAsWarning { get; set; } = true;

        /// <summary>
        /// Should conflicts between reference files within an SDK be logged as a message or a warning.
        /// The default is to log them as a message.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1709:IdentifiersShouldBeCasedCorrectly", MessageId = "SDKAs", Justification = "Shipped this way in Dev11 Beta (go-live)")]
        [SuppressMessage("Microsoft.Naming", "CA1704:IdentifiersShouldBeSpelledCorrectly", MessageId = "SDKAs", Justification = "SDK and As are two different words")]
        public bool LogReferenceConflictWithinSDKAsWarning { get; set; }

        /// <summary>
        /// Should conflicts between reference files across different referenced SDKs be logged as a message or a warning.
        /// The default is to log them as a warning.
        /// </summary>
        [SuppressMessage(
            "Microsoft.Naming",
            "CA1709:IdentifiersShouldBeCasedCorrectly",
            MessageId = "SDKs",
            Justification = "Shipped this way in Dev11 Beta (go-live)")]
        public bool LogReferenceConflictBetweenSDKsAsWarning { get; set; } = true;

        /// <summary>
        /// Should we log exceptions which were hit when the cache file is being read and written to
        /// </summary>
        public bool LogCacheFileExceptions { get; set; }
        #endregion

        /// <summary>
        /// Execute the task
        /// </summary>
        public override bool Execute()
        {
            return Execute(AssemblyNameExtension.GetAssemblyNameEx, AssemblyInformation.GetRuntimeVersion, p => FileUtilities.FileExistsNoThrow(p), synchronous: false);
        }

        /// <summary>
        /// Execute the task
        /// </summary>
        internal bool Execute(GetAssemblyName getAssemblyName, GetAssemblyRuntimeVersion getRuntimeVersion, FileExists fileExists, bool synchronous)
        {
            _getAssemblyName = getAssemblyName;
            _getRuntimeVersion = getRuntimeVersion;
            _fileExists = fileExists;
            _synchronous = synchronous;

            try
            {
                // Filter out all references tagged as RuntimeReferenceOnly 
                IEnumerable<ITaskItem> filteredResolvedSDKReferences = ResolvedSDKReferences.Where(
                    sdkReference => !MetadataConversionUtilities.TryConvertItemMetadataToBool(sdkReference, "RuntimeReferenceOnly")
                );

                PopulateReferencesForSDK(filteredResolvedSDKReferences);

                foreach (ITaskItem resolvedSDKReference in filteredResolvedSDKReferences)
                {
                    string sdkName = resolvedSDKReference.GetMetadata("SDKName");
                    string sdkIdentity = resolvedSDKReference.GetMetadata("OriginalItemSpec");
                    string rootDirectory = resolvedSDKReference.ItemSpec;
                    string targetedConfiguration = resolvedSDKReference.GetMetadata("TargetedSDKConfiguration");
                    string targetedArchitecture = resolvedSDKReference.GetMetadata("TargetedSDKArchitecture");

                    if (targetedConfiguration.Length == 0)
                    {
                        Log.LogErrorWithCodeFromResources("GetSDKReferenceFiles.CannotHaveEmptyTargetConfiguration", resolvedSDKReference.ItemSpec);
                        return false;
                    }

                    if (targetedArchitecture.Length == 0)
                    {
                        Log.LogErrorWithCodeFromResources("GetSDKReferenceFiles.CannotHaveEmptyTargetArchitecture", resolvedSDKReference.ItemSpec);
                        return false;
                    }

                    FindReferences(resolvedSDKReference, sdkIdentity, sdkName, rootDirectory, targetedConfiguration, targetedArchitecture);
                    FindRedistFiles(resolvedSDKReference, sdkIdentity, targetedConfiguration, targetedArchitecture);
                }

                GenerateOutputItems();

                if (_exceptions.Count > 0 && LogCacheFileExceptions)
                {
                    foreach (string exceptionMessage in _exceptions)
                    {
                        Log.LogMessageFromText(exceptionMessage, MessageImportance.High);
                    }
                }
            }
            catch (Exception e)
            {
                if (ExceptionHandling.IsCriticalException(e))
                {
                    throw;
                }

                Log.LogErrorWithCodeFromResources("GetSDKReferenceFiles.CouldNotGetSDKReferenceFiles", e.Message);
            }

            return !Log.HasLoggedErrors;
        }

        /// <summary>
        /// Find the redist files
        /// </summary>
        private void FindRedistFiles(ITaskItem resolvedSDKReference, string sdkIdentity, string targetedConfiguration, string targetedArchitecture)
        {
            // Gather the redist files, order is important because we want the most specific match of config and architecture to be the file that returns if there is a collision in destination paths
            var resolvedRedistFileSet = new HashSet<ResolvedRedistFile>();
            IList<string> redistPaths = new List<string>();

            if (targetedConfiguration.Length > 0 && targetedArchitecture.Length > 0)
            {
                redistPaths = ToolLocationHelper.GetSDKRedistFolders(resolvedSDKReference.ItemSpec, targetedConfiguration, targetedArchitecture);
            }

            if (LogRedistFilesList)
            {
                foreach (string path in redistPaths)
                {
                    Log.LogMessageFromResources(MessageImportance.Low, "GetSDKReferenceFiles.ExpandRedistFrom", path.Replace(resolvedSDKReference.ItemSpec, String.Empty));
                }
            }

            if (_cacheFileForSDKs.TryGetValue(sdkIdentity, out SDKInfo sdkCacheInfo) && sdkCacheInfo != null)
            {
                foreach (string path in redistPaths)
                {
                    GatherRedistFiles(resolvedRedistFileSet, resolvedSDKReference, path, sdkCacheInfo);
                }
            }

            // Add the resolved redist files to the master list of resolved redist files also log the fact we have found them.
            foreach (ResolvedRedistFile redist in resolvedRedistFileSet)
            {
                bool success = _resolveRedistFiles.Add(redist);

                if (success)
                {
                    if (LogRedistFilesList)
                    {
                        Log.LogMessageFromResources("GetSDKReferenceFiles.AddingRedistFile", redist.RedistFile.Replace(redist.SDKReferenceItem.ItemSpec, String.Empty), redist.TargetPath);
                    }
                }
                else
                {
                    ResolvedRedistFile winner = _resolveRedistFiles.First(x => x.Equals(redist));

                    if (!LogRedistConflictBetweenSDKsAsWarning)
                    {
                        Log.LogMessageFromResources("GetSDKReferenceFiles.ConflictRedistDifferentSDK", winner.TargetPath, winner.SDKReferenceItem.GetMetadata("OriginalItemSpec"), redist.SDKReferenceItem.GetMetadata("OriginalItemSpec"), winner.RedistFile, redist.RedistFile);
                    }
                    else
                    {
                        string message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("GetSDKReferenceFiles.ConflictRedistDifferentSDK", winner.TargetPath, winner.SDKReferenceItem.GetMetadata("OriginalItemSpec"), redist.SDKReferenceItem.GetMetadata("OriginalItemSpec"), winner.RedistFile, redist.RedistFile);
                        Log.LogWarningWithCodeFromResources("GetSDKReferenceFiles.ConflictBetweenFiles", message);
                    }
                }
            }
        }

        /// <summary>
        /// Find references for the sdk
        /// </summary>
        private void FindReferences(ITaskItem resolvedSDKReference, string sdkIdentity, string sdkName, string rootDirectory, string targetedConfiguration, string targetedArchitecture)
        {
            if (bool.TryParse(resolvedSDKReference.GetMetadata("ExpandReferenceAssemblies"), out bool expandSDK) && expandSDK)
            {
                Log.LogMessageFromResources("GetSDKReferenceFiles.GetSDKReferences", sdkName, rootDirectory);

                // Gather the reference assemblies, order is important because we want the most specific match of config and architecture to be searched for last
                // so it can overwrite any less specific matches.
                var resolvedReferenceAssemblies = new HashSet<ResolvedReferenceAssembly>();

                // If the SDK is manifest driven we want to grab them from the ApiContracts in the manifest if possible- will only happen if TargetSdk is identified
                string[] manifestReferencePaths = GetReferencePathsFromManifest(resolvedSDKReference);

                if (manifestReferencePaths?.Length > 0)
                {
                    // Found ApiContract references, use those
                    foreach (string manifestReferencePath in manifestReferencePaths)
                    {
                        resolvedReferenceAssemblies.Add(new ResolvedReferenceAssembly(resolvedSDKReference, manifestReferencePath));
                    }
                }
                else if (targetedConfiguration.Length > 0 && targetedArchitecture.Length > 0)
                {
                    // Couldn't find any valid ApiContracts, look up references the traditional way
                    IList<string> referencePaths = ToolLocationHelper.GetSDKReferenceFolders(resolvedSDKReference.ItemSpec, targetedConfiguration, targetedArchitecture);

                    if (LogReferencesList)
                    {
                        foreach (string path in referencePaths)
                        {
                            Log.LogMessageFromResources(MessageImportance.Low, "GetSDKReferenceFiles.ExpandReferencesFrom", path.Replace(resolvedSDKReference.ItemSpec, String.Empty));
                        }
                    }

                    if (_cacheFileForSDKs.TryGetValue(sdkIdentity, out SDKInfo sdkCacheInfo) && sdkCacheInfo != null)
                    {
                        foreach (string path in referencePaths)
                        {
                            GatherReferenceAssemblies(resolvedReferenceAssemblies, resolvedSDKReference, path, sdkCacheInfo);
                        }
                    }
                }

                // Add the resolved references to the master list of resolved assemblies also log the fact we have found them.
                foreach (ResolvedReferenceAssembly reference in resolvedReferenceAssemblies)
                {
                    bool success = _resolvedReferences.Add(reference);
                    if (success)
                    {
                        if (LogReferencesList)
                        {
                            Log.LogMessageFromResources("GetSDKReferenceFiles.AddingReference", reference.AssemblyLocation.Replace(reference.SDKReferenceItem.ItemSpec, String.Empty));
                        }
                    }
                    else
                    {
                        // Multiple extension SDKs can reference the exact same WinMD now. If the assembly path is exactly the same go ahead and keep the first silently.
                        // (The normal matching is by filename ONLY)
                        if (_resolvedReferences.Any(x => String.Equals(x.AssemblyLocation, reference.AssemblyLocation, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        ResolvedReferenceAssembly winner = _resolvedReferences.First(x => x.Equals(reference));

                        if (!LogReferenceConflictBetweenSDKsAsWarning)
                        {
                            Log.LogMessageFromResources("GetSDKReferenceFiles.ConflictReferenceDifferentSDK", winner.SDKReferenceItem.GetMetadata("OriginalItemSpec"), reference.SDKReferenceItem.GetMetadata("OriginalItemSpec"), winner.AssemblyLocation, reference.AssemblyLocation);
                        }
                        else
                        {
                            string message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("GetSDKReferenceFiles.ConflictReferenceDifferentSDK", winner.SDKReferenceItem.GetMetadata("OriginalItemSpec"), reference.SDKReferenceItem.GetMetadata("OriginalItemSpec"), winner.AssemblyLocation, reference.AssemblyLocation);
                            Log.LogWarningWithCodeFromResources("GetSDKReferenceFiles.ConflictBetweenFiles", message);
                        }
                    }
                }
            }

            if (!expandSDK)
            {
                Log.LogMessageFromResources("GetSDKReferenceFiles.NotExpanding", sdkName);
            }
        }

        /// <summary>
        /// Generate the output groups
        /// </summary>
        private void GenerateOutputItems()
        {
            var resolvedReferenceAssemblies = new List<ITaskItem>();
            var copyLocalReferenceAssemblies = new List<ITaskItem>();
            var redistReferenceItems = new List<ITaskItem>();

            foreach (ResolvedReferenceAssembly reference in _resolvedReferences)
            {
                var outputItem = new TaskItem(reference.AssemblyLocation);
                resolvedReferenceAssemblies.Add(outputItem);

                if (outputItem.GetMetadata(ItemMetadataNames.msbuildReferenceSourceTarget).Length == 0)
                {
                    outputItem.SetMetadata(ItemMetadataNames.msbuildReferenceSourceTarget, "ExpandSDKReference");
                }

                // Mark the two pieces of metadata with the SDK name
                outputItem.SetMetadata(ItemMetadataNames.msbuildReferenceFromSDK, reference.SDKReferenceItem.GetMetadata("OriginalItemSpec"));
                outputItem.SetMetadata(ItemMetadataNames.msbuildReferenceGrouping, reference.SDKReferenceItem.GetMetadata("OriginalItemSpec"));
                outputItem.SetMetadata(ItemMetadataNames.msbuildReferenceGroupingDisplayName, reference.SDKReferenceItem.GetMetadata("DisplayName"));

                string sdkIdentity = reference.SDKReferenceItem.GetMetadata("OriginalItemSpec");
                outputItem.SetMetadata("OriginalItemSpec", sdkIdentity);
                outputItem.SetMetadata("SDKRootPath", reference.SDKReferenceItem.ItemSpec);
                outputItem.SetMetadata("ResolvedFrom", "GetSDKReferenceFiles");

                if (_cacheFileForSDKs.TryGetValue(sdkIdentity, out SDKInfo sdkInfo) && sdkInfo != null)
                {
                    if (sdkInfo.PathToReferenceMetadata != null && sdkInfo.PathToReferenceMetadata.TryGetValue(reference.AssemblyLocation, out SdkReferenceInfo referenceInfo))
                    {
                        if (referenceInfo?.FusionName != null)
                        {
                            outputItem.SetMetadata(ItemMetadataNames.fusionName, referenceInfo.FusionName);
                        }

                        if (referenceInfo?.ImageRuntime != null)
                        {
                            outputItem.SetMetadata(ItemMetadataNames.imageRuntime, referenceInfo.ImageRuntime);
                        }

                        if (referenceInfo?.IsWinMD == true)
                        {
                            outputItem.SetMetadata(ItemMetadataNames.winMDFile, "true");

                            if (referenceInfo.IsManagedWinmd)
                            {
                                outputItem.SetMetadata(ItemMetadataNames.winMDFileType, "Managed");
                            }
                            else
                            {
                                outputItem.SetMetadata(ItemMetadataNames.winMDFileType, "Native");
                            }
                        }
                        else
                        {
                            outputItem.SetMetadata("WinMDFile", "false");
                        }
                    }
                }

                if (reference.CopyLocal)
                {
                    outputItem.SetMetadata("CopyLocal", "true");
                    copyLocalReferenceAssemblies.Add(outputItem);

                    string directory = Path.GetDirectoryName(reference.AssemblyLocation);
                    string fileNameNoExtension = Path.GetFileNameWithoutExtension(reference.AssemblyLocation);
                    string xmlFile = Path.Combine(directory, fileNameNoExtension + ".xml");

                    if (FileUtilities.FileExistsNoThrow(xmlFile))
                    {
                        var item = new TaskItem(xmlFile);

                        // Add the related item.
                        copyLocalReferenceAssemblies.Add(item);
                    }
                }
                else
                {
                    outputItem.SetMetadata("CopyLocal", "false");
                }
            }

            resolvedReferenceAssemblies.Sort(TaskItemSpecFilenameComparer.GenericComparer);
            copyLocalReferenceAssemblies.Sort(TaskItemSpecFilenameComparer.GenericComparer);

            References = resolvedReferenceAssemblies.ToArray();
            CopyLocalFiles = copyLocalReferenceAssemblies.ToArray();

            foreach (ResolvedRedistFile file in _resolveRedistFiles)
            {
                var outputItem = new TaskItem(file.RedistFile);

                if (outputItem.GetMetadata(ItemMetadataNames.msbuildReferenceSourceTarget).Length == 0)
                {
                    outputItem.SetMetadata(ItemMetadataNames.msbuildReferenceSourceTarget, "ExpandSDKReference");
                }

                outputItem.SetMetadata("OriginalItemSpec", file.SDKReferenceItem.GetMetadata("OriginalItemSpec"));
                outputItem.SetMetadata("SDKRootPath", file.SDKReferenceItem.ItemSpec);
                outputItem.SetMetadata("ResolvedFrom", "GetSDKReferenceFiles");

                // Target path for the file
                outputItem.SetMetadata("TargetPath", file.TargetPath);

                // Pri files need to know the root directory of the target path
                if (Path.GetExtension(file.RedistFile).Equals(".PRI", StringComparison.OrdinalIgnoreCase))
                {
                    outputItem.SetMetadata("Root", file.TargetRoot);
                }

                redistReferenceItems.Add(outputItem);
            }

            redistReferenceItems.Sort(TaskItemSpecFilenameComparer.GenericComparer);
            RedistFiles = redistReferenceItems.ToArray();
        }

        /// <summary>
        /// Gather the reference assemblies from the referenceassembly directory.
        /// </summary>
        private void GatherReferenceAssemblies(HashSet<ResolvedReferenceAssembly> resolvedFiles, ITaskItem sdkReference, string path, SDKInfo info)
        {
            if (info.DirectoryToFileList != null && info.DirectoryToFileList.TryGetValue(FileUtilities.EnsureNoTrailingSlash(path), out List<string> referenceFiles) && referenceFiles != null)
            {
                foreach (string file in referenceFiles)
                {
                    // We only want to find files which match the extensions the user has asked for, this will usually be dll or winmd.
                    bool matchesExtension = false;
                    foreach (var extension in _referenceExtensions)
                    {
                        string fileExtension = Path.GetExtension(file);
                        if (fileExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                        {
                            matchesExtension = true;
                            break;
                        }
                    }

                    if (!matchesExtension)
                    {
                        continue;
                    }

                    var resolvedReference = new ResolvedReferenceAssembly(sdkReference, file);
                    bool success = resolvedFiles.Add(resolvedReference);
                    if (!success)
                    {
                        ResolvedReferenceAssembly winner = resolvedFiles.First(x => x.Equals(resolvedReference));

                        if (!LogReferenceConflictWithinSDKAsWarning)
                        {
                            Log.LogMessageFromResources("GetSDKReferenceFiles.ConflictReferenceSameSDK", winner.SDKReferenceItem.GetMetadata("OriginalItemSpec"), winner.AssemblyLocation.Replace(winner.SDKReferenceItem.ItemSpec, String.Empty), resolvedReference.AssemblyLocation.Replace(resolvedReference.SDKReferenceItem.ItemSpec, String.Empty));
                        }
                        else
                        {
                            string message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("GetSDKReferenceFiles.ConflictReferenceSameSDK", winner.SDKReferenceItem.GetMetadata("OriginalItemSpec"), winner.AssemblyLocation.Replace(winner.SDKReferenceItem.ItemSpec, String.Empty), resolvedReference.AssemblyLocation.Replace(resolvedReference.SDKReferenceItem.ItemSpec, String.Empty));
                            Log.LogWarningWithCodeFromResources("GetSDKReferenceFiles.ConflictBetweenFiles", message);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gather the redist files from the redist directory.
        /// </summary>
        private void GatherRedistFiles(HashSet<ResolvedRedistFile> resolvedRedistFiles, ITaskItem sdkReference, string redistFilePath, SDKInfo info)
        {
            bool copyRedist = MetadataConversionUtilities.TryConvertItemMetadataToBool(sdkReference, "CopyRedist");
            if (copyRedist)
            {
                foreach (KeyValuePair<string, List<string>> directoryToFileList in info.DirectoryToFileList)
                {
                    // Add a trailing slash to ensure we don't match the start of a platform (e.g. ...\ARM matching ...\ARM64)
                    if (FileUtilities.EnsureTrailingSlash(directoryToFileList.Key).StartsWith(FileUtilities.EnsureTrailingSlash(redistFilePath), StringComparison.OrdinalIgnoreCase))
                    {
                        List<string> redistFiles = directoryToFileList.Value;
                        string targetPathRoot = sdkReference.GetMetadata("CopyRedistToSubDirectory");

                        foreach (var file in redistFiles)
                        {
                            string relativeToBase = FileUtilities.MakeRelative(redistFilePath, file);
                            string targetPath = Path.Combine(targetPathRoot, relativeToBase);

                            var redistFile = new ResolvedRedistFile(sdkReference, file, targetPath, targetPathRoot);
                            if (!resolvedRedistFiles.Add(redistFile))
                            {
                                ResolvedRedistFile winner = resolvedRedistFiles.First(x => x.Equals(redistFile));

                                if (!LogRedistConflictWithinSDKAsWarning)
                                {
                                    Log.LogMessageFromResources("GetSDKReferenceFiles.ConflictRedistSameSDK", redistFile.TargetPath, redistFile.SDKReferenceItem.GetMetadata("OriginalItemSpec"), winner.RedistFile.Replace(redistFile.SDKReferenceItem.ItemSpec, String.Empty), redistFile.RedistFile.Replace(redistFile.SDKReferenceItem.ItemSpec, String.Empty));
                                }
                                else
                                {
                                    string message = ResourceUtilities.FormatResourceStringStripCodeAndKeyword("GetSDKReferenceFiles.ConflictRedistSameSDK", redistFile.TargetPath, redistFile.SDKReferenceItem.GetMetadata("OriginalItemSpec"), winner.RedistFile.Replace(redistFile.SDKReferenceItem.ItemSpec, String.Empty), redistFile.RedistFile.Replace(redistFile.SDKReferenceItem.ItemSpec, String.Empty));
                                    Log.LogWarningWithCodeFromResources("GetSDKReferenceFiles.ConflictBetweenFiles", message);
                                }
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gather the contents of all of the SDK into a cache file and save it to disk.
        /// </summary>
        private void PopulateReferencesForSDK(IEnumerable<ITaskItem> sdks)
        {
            var sdkFilesCache = new SDKFilesCache(_exceptions, _cacheFilePath, _getAssemblyName, _getRuntimeVersion, _fileExists);

            // Go through each sdk which has been resolved in this project
            foreach (ITaskItem sdk in sdks)
            {
                string sdkIdentity = sdk.GetMetadata("OriginalItemSpec");
                ErrorUtilities.VerifyThrowArgument(sdkIdentity.Length != 0, "GetSDKReferenceFiles.NoOriginalItemSpec", sdk.ItemSpec);
                string sdkRoot = sdk.ItemSpec;

                // Try and get the cache file for this SDK if it already exists
                SDKInfo info = sdkFilesCache.LoadAssemblyListFromCacheFile(sdkIdentity, sdkRoot);

                if (info == null || !sdkFilesCache.IsAssemblyListCacheFileUpToDate(sdkIdentity, sdkRoot, _cacheFilePath))
                {
                    info = sdkFilesCache.GetCacheFileInfoFromSDK(sdkRoot, GetReferencePathsFromManifest(sdk));

                    var saveContext = new SaveContext(sdkIdentity, sdkRoot, info);

                    if (_synchronous)
                    {
                        // In unit tests, save the file to disk synchronously to force its exercise
                        sdkFilesCache.SaveAssemblyListToCacheFile(saveContext);
                    }
                    else
                    {
                        // On a background thread save the file to disk
                        ThreadPool.QueueUserWorkItem(sdkFilesCache.SaveAssemblyListToCacheFile, saveContext);
                    }
                }

                _cacheFileForSDKs.TryAdd(sdkIdentity, info);
            }
        }

        /// <summary>
        /// Get the referenced file names from the SDK's manifest if applicable- may return null.
        /// </summary>
        private string[] GetReferencePathsFromManifest(ITaskItem sdk)
        {
            string[] manifestReferencePaths = null;

            // It is only useful to look if we have a target SDK specified
            if (!String.IsNullOrEmpty(TargetSDKIdentifier) && !String.IsNullOrEmpty(TargetSDKVersion))
            {
                manifestReferencePaths = ToolLocationHelper.GetPlatformOrFrameworkExtensionSdkReferences(
                    sdk.GetMetadata(GetInstalledSDKLocations.SDKNameMetadataName),
                    TargetSDKIdentifier,
                    TargetSDKVersion,
                    sdk.GetMetadata(GetInstalledSDKLocations.DirectoryRootsMetadataName),
                    sdk.GetMetadata(GetInstalledSDKLocations.ExtensionDirectoryRootsMetadataName),
                    sdk.GetMetadata(GetInstalledSDKLocations.RegistryRootMetadataName),
                    TargetPlatformIdentifier,
                    TargetPlatformVersion);
            }

            return manifestReferencePaths;
        }

        /// <summary>
        /// Class which represents a resolved reference assembly
        /// </summary>
        private class ResolvedReferenceAssembly : IEquatable<ResolvedReferenceAssembly>
        {
            /// <summary>
            ///  Is the reference copy local
            /// </summary>
            private readonly bool _copyLocal;

            /// <summary>
            /// Constructor
            /// </summary>
            public ResolvedReferenceAssembly(ITaskItem sdkReferenceItem, string assemblyLocation)
            {
                FileName = Path.GetFileNameWithoutExtension(assemblyLocation);
                AssemblyLocation = assemblyLocation;
                bool.TryParse(sdkReferenceItem.GetMetadata("CopyLocalExpandedReferenceAssemblies"), out _copyLocal);
                SDKReferenceItem = sdkReferenceItem;
            }

            /// <summary>
            /// What is the file name
            /// </summary>
            private string FileName { get; }

            /// <summary>
            /// What is the location of the assembly on disk.
            /// </summary>
            public string AssemblyLocation { get; }

            /// <summary>
            /// Is the assembly copy local or not.
            /// </summary>
            public bool CopyLocal => _copyLocal;

            /// <summary>
            /// Original resolved SDK reference item passed in.
            /// </summary>
            public ITaskItem SDKReferenceItem { get; }

            /// <summary>
            /// Override object equals to use the equals redist in this object.
            /// </summary>
            public override bool Equals(object obj)
            {
                if (!(obj is ResolvedReferenceAssembly reference))
                {
                    return false;
                }

                return Equals(reference);
            }

            /// <summary>
            /// Override get hash code
            /// </summary>
            public override int GetHashCode()
            {
                return FileName.GetHashCode();
            }

            /// <summary>
            /// Are two resolved references items Equal
            /// </summary>
            public bool Equals(ResolvedReferenceAssembly other)
            {
                if (other == null)
                {
                    return false;
                }

                if (ReferenceEquals(other, this))
                {
                    return true;
                }

                // We only care about the file name and not the path because if they have the same file name but different paths then they will likely contain
                // the same namespaces and the compiler does not like to have two references with the same namespace passed at once without aliasing and 
                // we have no way to do aliasing per assembly since we are grabbing a bunch of files at once.)
                return String.Equals(FileName, other.FileName, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Class which represents a resolved redist file
        /// </summary>
        private class ResolvedRedistFile : IEquatable<ResolvedRedistFile>
        {
            /// <summary>
            /// Constructor
            /// </summary>
            public ResolvedRedistFile(ITaskItem sdkReferenceItem, string redistFile, string targetPath, string targetRoot)
            {
                RedistFile = redistFile;
                TargetPath = targetPath;
                TargetRoot = targetRoot;
                SDKReferenceItem = sdkReferenceItem;
            }

            /// <summary>
            ///  What is the file name
            /// </summary>
            public string RedistFile { get; }

            /// <summary>
            /// What is the targetPath for the redist file.
            /// </summary>
            public string TargetPath { get; }

            /// <summary>
            /// What is the root directory of the target path
            /// </summary>
            public string TargetRoot { get; }

            /// <summary>
            /// Original resolved SDK reference item passed in.
            /// </summary>
            public ITaskItem SDKReferenceItem { get; }

            /// <summary>
            /// Override object equals to use the equals redist in this object.
            /// </summary>
            public override bool Equals(object obj)
            {
                if (!(obj is ResolvedReferenceAssembly reference))
                {
                    return false;
                }

                return Equals(reference);
            }

            /// <summary>
            /// Override get hash code
            /// </summary>
            public override int GetHashCode()
            {
                return TargetPath.GetHashCode();
            }

            /// <summary>
            /// Are two resolved references items Equal
            /// </summary>
            public bool Equals(ResolvedRedistFile other)
            {
                if (other == null)
                {
                    return false;
                }

                if (ReferenceEquals(other, this))
                {
                    return true;
                }

                // We only care about the target path since that is the location relative to the package root where the redist file
                // will be copied.
                return String.Equals(TargetPath, other.TargetPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        #region Cache Serialization

        /// <summary>
        /// Methods which are used to save and read the cache files per sdk from and to disk.
        /// </summary>
        internal class SDKFilesCache
        {
            /// <summary>
            ///  Thread-safe queue which contains exceptions throws during cache file reading and writing.
            /// </summary>
            private readonly ConcurrentQueue<string> _exceptionMessages;

            /// <summary>
            /// Delegate to get the assembly name
            /// </summary>
            private readonly GetAssemblyName _getAssemblyName;

            /// <summary>
            /// Get the image runtime version from a file
            /// </summary>
            private readonly GetAssemblyRuntimeVersion _getRuntimeVersion;

            /// <summary>
            /// File exists delegate
            /// </summary>
            private readonly FileExists _fileExists;

            /// <summary>
            /// Location for the cache files to be written to
            /// </summary>
            private readonly string _cacheFileDirectory;

            /// <summary>
            /// Constructor
            /// </summary>
            internal SDKFilesCache(ConcurrentQueue<string> exceptionQueue, string cacheFileDirectory, GetAssemblyName getAssemblyName, GetAssemblyRuntimeVersion getRuntimeVersion, FileExists fileExists)
            {
                _exceptionMessages = exceptionQueue;
                _cacheFileDirectory = cacheFileDirectory;
                _getAssemblyName = getAssemblyName;
                _getRuntimeVersion = getRuntimeVersion;
                _fileExists = fileExists;
            }

            /// <summary>
            /// Load reference assembly information from the cache file
            /// </summary>
            internal SDKInfo LoadAssemblyListFromCacheFile(string sdkIdentity, string sdkRoot)
            {
                string cacheFile = Directory.EnumerateFiles(_cacheFileDirectory, GetCacheFileName(sdkIdentity, sdkRoot, "*")).FirstOrDefault();

                try
                {
                    if (!string.IsNullOrEmpty(cacheFile))
                    {
                        using FileStream fs = new FileStream(cacheFile, FileMode.Open);
                        using var translator = BinaryTranslator.GetReadTranslator(fs, buffer: null);
                        SDKInfo sdkInfo = new SDKInfo();
                        sdkInfo.Translate(translator);
                        return sdkInfo;
                    }
                }
                catch (Exception e) when (!ExceptionHandling.IsCriticalException(e))
                {
                    // Queue up for later logging, does not matter if the file is deleted or not
                    _exceptionMessages.Enqueue(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("GetSDKReferenceFiles.ProblemReadingCacheFile", cacheFile, e.ToString()));
                }

                return null;
            }

            /// <summary>
            /// Save assembly reference information to the cache file
            /// </summary>
            internal void SaveAssemblyListToCacheFile(object data)
            {
                string referencesCacheFile = String.Empty;
                try
                {
                    SaveContext saveContext = data as SaveContext;
                    SDKInfo cacheFileInfo = saveContext.Assemblies;

                    referencesCacheFile = Path.Combine(_cacheFileDirectory, GetCacheFileName(saveContext.SdkIdentity, saveContext.SdkRoot, cacheFileInfo.Hash.ToString("X", CultureInfo.InvariantCulture)));
                    string[] existingCacheFiles = Directory.GetFiles(_cacheFileDirectory, GetCacheFileName(saveContext.SdkIdentity, saveContext.SdkRoot, "*"));

                    // First delete any existing cache files
                    foreach (string existingCacheFile in existingCacheFiles)
                    {
                        try
                        {
                            File.Delete(existingCacheFile);
                        }
                        catch (Exception e) when (!ExceptionHandling.IsCriticalException(e))
                        {
                            // Queue up for later logging, does not matter if the file is deleted or not
                            _exceptionMessages.Enqueue(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("GetSDKReferenceFiles.ProblemDeletingCacheFile", existingCacheFile, e.Message));
                        }
                    }

                    using (var fs = new FileStream(referencesCacheFile, FileMode.Create))
                    {
                        var translator = BinaryTranslator.GetWriteTranslator(fs);
                        cacheFileInfo.Translate(translator);
                    }
                }
                catch (Exception e) when (!ExceptionHandling.IsCriticalException(e))
                {
                    // Queue up for later logging, does not matter if the cache got written
                    _exceptionMessages.Enqueue(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("GetSDKReferenceFiles.ProblemWritingCacheFile", referencesCacheFile, e.Message));
                }
            }

            /// <summary>
            /// Get references from the paths provided, and populate the provided cache
            /// </summary>
            internal SDKInfo GetCacheFileInfoFromSDK(string sdkRootDirectory, string[] sdkManifestReferences)
            {
                var references = new ConcurrentDictionary<string, SdkReferenceInfo>(StringComparer.OrdinalIgnoreCase);
                var directoryToFileList = new ConcurrentDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                var directoriesToHash = new List<string>();

                IEnumerable<string> referenceDirectories = GetAllReferenceDirectories(sdkRootDirectory);
                IEnumerable<string> redistDirectories = GetAllRedistDirectories(sdkRootDirectory);

                directoriesToHash.AddRange(referenceDirectories);
                directoriesToHash.AddRange(redistDirectories);

                if (sdkManifestReferences?.Length > 0)
                {
                    // Manifest driven- get the info from the known list
                    PopulateReferencesDictionaryFromManifestPaths(directoryToFileList, references, sdkManifestReferences);
                }
                else
                {
                    PopulateReferencesDictionaryFromPaths(directoryToFileList, references, referenceDirectories);
                }

                PopulateRedistDictionaryFromPaths(directoryToFileList, redistDirectories);

                var cacheInfo = new SDKInfo(references, directoryToFileList, FileUtilities.GetPathsHash(directoriesToHash));
                return cacheInfo;
            }

            /// <summary>
            /// Populate an existing assembly dictionary for the given framework moniker utilizing provided manifest reference information
            /// </summary>
            private void PopulateReferencesDictionaryFromManifestPaths(ConcurrentDictionary<string, List<string>> referencesByDirectory, ConcurrentDictionary<string, SdkReferenceInfo> references, string[] sdkManifestReferences)
            {
                // Sort by directory
                IEnumerable<IGrouping<string, string>> groupedByDirectory =
                    from reference in sdkManifestReferences
                    group reference by Path.GetDirectoryName(reference);

                foreach (IGrouping<string, string> group in groupedByDirectory)
                {
                    referencesByDirectory.TryAdd(group.Key, group.ToList());
                }

                Parallel.ForEach(sdkManifestReferences, reference => { references.TryAdd(reference, GetSDKReferenceInfo(reference)); });
            }

            /// <summary>
            /// Populate an existing assembly dictionary for the given framework moniker
            /// </summary>
            private void PopulateReferencesDictionaryFromPaths(ConcurrentDictionary<string, List<string>> referencesByDirectory, ConcurrentDictionary<string, SdkReferenceInfo> references, IEnumerable<string> referenceDirectories)
            {
                // Add each folder to the dictionary along with a list of all of files inside of it
                Parallel.ForEach(
                referenceDirectories,
                path =>
                {
                    List<string> files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly).ToList();
                    referencesByDirectory.TryAdd(path, files);

                    Parallel.ForEach(files, filePath => { references.TryAdd(filePath, GetSDKReferenceInfo(filePath)); });
                });
            }

            /// <summary>
            /// Populate an existing assembly dictionary for the given framework moniker
            /// </summary>
            private static void PopulateRedistDictionaryFromPaths(ConcurrentDictionary<string, List<string>> redistFilesByDirectory, IEnumerable<string> redistDirectories)
            {
                // Add each folder to the dictionary along with a list of all of files inside of it
                Parallel.ForEach(
                redistDirectories,
                path =>
                {
                    List<string> files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly).ToList();
                    redistFilesByDirectory.TryAdd(path, files);
                });
            }

            /// <summary>
            /// Is the assembly list cache file up to date.
            /// This is done by comparing the last write time of the cache file to the last write time of the code.
            /// If our code is newer than the last write time of the cache file then there may be some different serialization used so we should say it is out of date and just regenerate it.
            /// </summary>
            internal bool IsAssemblyListCacheFileUpToDate(string sdkIdentity, string sdkRoot, string cacheFileFolder)
            {
                // The hash is the hash of last modified times for the passed in reference paths. A directory gets modified if a file is added, deleted, or modified  inside of the inside of the directory itself (modifications to child folders are not seen however).
                var directoriesToHash = new List<string>();
                directoriesToHash.AddRange(GetAllReferenceDirectories(sdkRoot));
                directoriesToHash.AddRange(GetAllRedistDirectories(sdkRoot));

                int hash = FileUtilities.GetPathsHash(directoriesToHash);
                string referencesCacheFile = Path.Combine(cacheFileFolder, GetCacheFileName(sdkIdentity, sdkRoot, hash.ToString("X", CultureInfo.InvariantCulture)));

                bool upToDate = false;
                DateTime referencesCacheFileLastWriteTimeUtc = File.GetLastWriteTimeUtc(referencesCacheFile);

                string currentAssembly = String.Empty;
                try
                {
                    currentAssembly = Assembly.GetExecutingAssembly().CodeBase;
                    var codeBase = new Uri(currentAssembly);
                    DateTime currentCodeLastWriteTime = File.GetLastWriteTimeUtc(codeBase.LocalPath);
                    if (FileSystems.Default.FileExists(referencesCacheFile) && currentCodeLastWriteTime < referencesCacheFileLastWriteTimeUtc)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    if (ExceptionHandling.IsCriticalException(ex))
                    {
                        throw;
                    }

                    // Queue up for later logging, does not matter if the cache got written
                    _exceptionMessages.Enqueue(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("GetSDKReferenceFiles.ProblemGeneratingHash", currentAssembly, ex.Message));

                    // Don't care why the check failed we will just say the cache is not up to date.
                }

                return upToDate;
            }

            /// <summary>
            /// Generate an SDKReferenceInfo object
            /// </summary>
            private SdkReferenceInfo GetSDKReferenceInfo(string referencePath)
            {
                string imageRuntimeVersion = null;
                bool isManagedWinMD = false;
                bool isWinMDFile = false;
                string fusionName = null;

                try
                {
                    AssemblyNameExtension assemblyNameExtension = _getAssemblyName(referencePath);
                    if (assemblyNameExtension != null)
                    {
                        AssemblyName assembly = assemblyNameExtension.AssemblyName;
                        isWinMDFile = AssemblyInformation.IsWinMDFile(referencePath, _getRuntimeVersion, _fileExists, out imageRuntimeVersion, out isManagedWinMD);
                        if (assembly != null)
                        {
                            fusionName = assembly.FullName;
                        }
                    }
                }
                catch (Exception e)
                {
                    if (ExceptionHandling.IsCriticalException(e))
                    {
                        throw;
                    }

                    // Queue up for later logging, does not matter if the cache got written
                    _exceptionMessages.Enqueue(ResourceUtilities.FormatResourceStringStripCodeAndKeyword("GetSDKReferenceFiles.ProblemGettingAssemblyMetadata", referencePath, e.Message));
                }

                var referenceInfo = new SdkReferenceInfo(fusionName, imageRuntimeVersion, isWinMDFile, isManagedWinMD);
                return referenceInfo;
            }

            /// <summary>
            /// Generate cache file name from sdkIdentity, sdkRoot and suffixHash.
            /// </summary>
            private static string GetCacheFileName(string sdkIdentity, string sdkRoot, string suffixHash)
            {
                string identityHash = FileUtilities.GetHexHash(sdkIdentity);
                string rootHash = FileUtilities.GetHexHash(sdkRoot);

                return sdkIdentity + ",Set=" + identityHash + "-" + rootHash + ",Hash=" + suffixHash + ".dat";
            }

            /// <summary>
            /// Get all redist subdirectories under the given path
            /// </summary>
            private static IEnumerable<string> GetAllRedistDirectories(string sdkRoot)
            {
                string redistPath = Path.Combine(sdkRoot, "Redist");
                if (FileUtilities.DirectoryExistsNoThrow(redistPath))
                {
                    return Directory.GetDirectories(redistPath, "*", SearchOption.AllDirectories);
                }

                return Enumerable.Empty<string>();
            }

            /// <summary>
            /// Get all reference subdirectories under the given path
            /// </summary>
            private static IEnumerable<string> GetAllReferenceDirectories(string sdkRoot)
            {
                string referencesPath = Path.Combine(sdkRoot, "References");
                if (FileUtilities.DirectoryExistsNoThrow(referencesPath))
                {
                    return Directory.GetDirectories(referencesPath, "*", SearchOption.AllDirectories);
                }

                return Enumerable.Empty<string>();
            }
        }

        /// <summary>
        /// Class to contain some identity information about a file in an sdk
        /// </summary>
        /// <remarks>This is a serialization format. Do not change member naming.</remarks>
        [Serializable]
        internal class SdkReferenceInfo
        {
            public SdkReferenceInfo(string fusionName, string imageRuntime, bool isWinMD, bool isManagedWinmd)
            {
                FusionName = fusionName;
                ImageRuntime = imageRuntime;
                IsWinMD = isWinMD;
                IsManagedWinmd = isManagedWinmd;
            }

            #region Properties

            public string FusionName { get; internal set; }
            public bool IsWinMD { get; internal set; }
            public bool IsManagedWinmd { get; internal set; }
            public string ImageRuntime { get; internal set; }

            #endregion
        }

        /// <summary>
        /// Structure that contains the on disk representation of the SDK in memory.
        /// </summary>
        internal class SDKInfo : ITranslatable
        {
            private IDictionary<string, SdkReferenceInfo> _pathToReferenceMetadata;
            private IDictionary<string, List<string>> _directoryToFileList;
            private int _hash;

            internal SDKInfo()
            {
                _pathToReferenceMetadata = new Dictionary<string, SdkReferenceInfo>(StringComparer.OrdinalIgnoreCase);
                _directoryToFileList = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                _hash = 0;
            }

            public SDKInfo(ITranslator translator) : this()
            {
                Translate(translator);
            }

            public SDKInfo(IDictionary<string, SdkReferenceInfo> pathToReferenceMetadata, IDictionary<string, List<string>> directoryToFileList, int cacheHash)
            {
                this._pathToReferenceMetadata = pathToReferenceMetadata;
                this._directoryToFileList = directoryToFileList;
                this._hash = cacheHash;
            }

            /// <summary>
            /// A dictionary which maps a file path to a structure that contain some metadata information about that file.
            /// </summary>
            public IDictionary<string, SdkReferenceInfo> PathToReferenceMetadata { get { return _pathToReferenceMetadata; } }

            public IDictionary<string, List<string>> DirectoryToFileList { get { return _directoryToFileList; } }

            /// <summary>
            /// Hashset
            /// </summary>
            public int Hash { get { return _hash; } }

            public void Translate(ITranslator translator)
            {
                translator.TranslateDictionary(ref _pathToReferenceMetadata, (ITranslator t, ref string s) => t.Translate(ref s), (ITranslator t, ref SdkReferenceInfo info) =>
                {
                    info ??= new SdkReferenceInfo(null, null, false, false);
                    string fusionName = info.FusionName;
                    string imageRuntime = info.ImageRuntime;
                    bool isManagedWinmd = info.IsManagedWinmd;
                    bool isWinmd = info.IsWinMD;
                    t.Translate(ref fusionName);
                    t.Translate(ref imageRuntime);
                    t.Translate(ref isManagedWinmd);
                    t.Translate(ref isWinmd);
                    info.FusionName = fusionName;
                    info.ImageRuntime = imageRuntime;
                    info.IsManagedWinmd = isManagedWinmd;
                    info.IsWinMD = isWinmd;
                }, count => new Dictionary<string, SdkReferenceInfo>(count, StringComparer.OrdinalIgnoreCase));

                translator.TranslateDictionary(ref _directoryToFileList, (ITranslator t, ref string s) => t.Translate(ref s), (ITranslator t, ref List<string> fileList) =>
                {
                    t.Translate(ref fileList, (ITranslator t, ref string str) => { t.Translate(ref str); });
                }, count => new Dictionary<string, List<string>>(count, StringComparer.OrdinalIgnoreCase));

                translator.Translate(ref _hash);
            }
        }

        /// <summary>
        /// This class represents the context information used by the background cache serialization thread.
        /// </summary>
        internal class SaveContext
        {
            /// <summary>
            /// Constructor
            /// </summary>
            public SaveContext(string sdkIdentity, string sdkRoot, SDKInfo assemblies)
            {
                SdkIdentity = sdkIdentity;
                SdkRoot = sdkRoot;
                Assemblies = assemblies;
            }

            /// <summary>
            /// Identity of the sdk
            /// </summary>
            public string SdkIdentity { get; }

            /// <summary>
            /// Root path of the sdk
            /// </summary>
            public string SdkRoot { get; }

            /// <summary>
            /// Assembly metadata information
            /// </summary>
            public SDKInfo Assemblies { get; }
        }
        #endregion
    }
}
