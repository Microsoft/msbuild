// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Build.Eventing;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using Microsoft.Build.Tasks.AssemblyDependency;
using Microsoft.Build.Utilities;
using FrameworkNameVersioning = System.Runtime.Versioning.FrameworkName;
using SystemProcessorArchitecture = System.Reflection.ProcessorArchitecture;

namespace Microsoft.Build.Tasks
{
    /// <summary>
    /// A table of references.
    /// </summary>
    internal sealed class ReferenceTable
    {
        /// <summary>version 4.0</summary>
        private static readonly Version s_targetFrameworkVersion_40 = new Version("4.0");

        /// <summary>
        /// A mapping of a framework identifier to the most current redist list on the system based on the target framework identifier on the moniker.
        /// This is used to determine if an assembly is in a redist list for the framework targeted by the moniker.
        /// </summary>
        private static readonly Dictionary<string, Tuple<RedistList, string>> s_monikerToHighestRedistList = new Dictionary<string, Tuple<RedistList, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Reference simple names that were resolved by an external entity to RAR.
        /// </summary>
        private readonly HashSet<string> _externallyResolvedPrimaryReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The table of remapped assemblies. Used for Unification.</summary>
        private IEnumerable<DependentAssembly> _remappedAssemblies = Enumerable.Empty<DependentAssembly>();

        /// <summary>If true, then search for dependencies.</summary>
        private readonly bool _findDependencies;
        /// <summary>
        ///  Should version be ignored for framework primary references
        /// </summary>
        private readonly bool _ignoreVersionForFrameworkReferences;
        /// <summary>If true, then search for satellite files.</summary>
        private readonly bool _findSatellites;
        /// <summary>If true, then search for serialization assembly files.</summary>
        private readonly bool _findSerializationAssemblies;
        /// <summary>If true, then search for related files.</summary>
        private readonly bool _findRelatedFiles;
        /// <summary>
        /// If true, then force framework assembly version check against the target framework version
        /// If false, the default behavior is to disable version checks for target framework versions 4.5 and above.
        /// </summary>
        private readonly bool _checkAssemblyVersionAgainstTargetFrameworkVersion;

        /// <summary>Path to the FX.</summary>
        private readonly string[] _frameworkPaths;
        /// <summary>The allowed assembly extensions.</summary>
        private readonly string[] _allowedAssemblyExtensions;
        /// <summary>These are companion files that typically travel with assemblies</summary>
        private readonly string[] _relatedFileExtensions;
        /// <summary>
        /// Locations where sdks are installed. K:SDKName v: Resolved Reference item
        /// </summary>
        private readonly Dictionary<string, ITaskItem> _resolvedSDKReferences;
        /// <summary>Path to installed assembly XML tables.</summary>
        private readonly InstalledAssemblies _installedAssemblies;
        /// <summary>Like x86 or IA64\AMD64, the processor architecture being targetted.</summary>
        private readonly SystemProcessorArchitecture _targetProcessorArchitecture;
        /// <summary>Delegate used for checking for the existence of a file.</summary>
        private readonly FileExists _fileExists;
        /// <summary>Delegate used for checking for the existence of a directory.</summary>
        private readonly DirectoryExists _directoryExists;
        /// <summary>Delegate used for getting directories.</summary>
        private readonly GetDirectories _getDirectories;
        /// <summary>Delegate used for getting assembly names.</summary>
        private readonly GetAssemblyName _getAssemblyName;
        /// <summary>Delegate used for finding dependencies of a file.</summary>
        private readonly GetAssemblyMetadata _getAssemblyMetadata;
        /// <summary>Delegate used to get the image runtime version of a file</summary>
        private readonly GetAssemblyRuntimeVersion _getRuntimeVersion;
#if FEATURE_WIN32_REGISTRY
        /// <summary> Delegate to get the base registry key for AssemblyFoldersEx</summary>
        private OpenBaseKey _openBaseKey;
#endif
        /// <summary>Version of the runtime we are targeting</summary>
        private readonly Version _targetedRuntimeVersion;

        /// <summary>
        /// Delegate used to get the machineType from the PE header of the dll.
        /// </summary>
        private readonly ReadMachineTypeFromPEHeader _readMachineTypeFromPEHeader;

        /// <summary>
        /// Is the file a winMD file
        /// </summary>
        private readonly IsWinMDFile _isWinMDFile;

        /// <summary>Version of the framework targeted by this project.</summary>
        private readonly Version _projectTargetFramework;

        /// <summary>
        /// Target framework moniker we are targeting.
        /// </summary>
        private readonly FrameworkNameVersioning _targetFrameworkMoniker;

        /// <summary>
        /// Logging helper to allow the logging of meessages from the Reference Table.
        /// </summary>
        private readonly TaskLoggingHelper _log;

        /// <summary>
        /// List of framework directories which are the highest on the machine
        /// </summary>
        private readonly string[] _latestTargetFrameworkDirectories;

        /// <summary>
        /// Should dependencies be set to copy local if the parent reference is in the GAC
        /// </summary>
        private readonly bool _copyLocalDependenciesWhenParentReferenceInGac;

        private readonly bool _doNotCopyLocalIfInGac;

        /// <summary>
        ///  Shoould the framework attribute version mismatch be ignored.
        /// </summary>
        private readonly bool _ignoreFrameworkAttributeVersionMismatch;

        /// <summary>
        /// Delegate to determine if an assembly name is in the GAC.
        /// </summary>
        private readonly GetAssemblyPathInGac _getAssemblyPathInGac;

        /// <summary>
        /// Should a warning or error be emitted on architecture mismatch
        /// </summary>
        private readonly WarnOrErrorOnTargetArchitectureMismatchBehavior _warnOrErrorOnTargetArchitectureMismatch = WarnOrErrorOnTargetArchitectureMismatchBehavior.Warning;

        private readonly ConcurrentDictionary<string, AssemblyMetadata> _assemblyMetadataCache;

        /// <summary>
        /// When we exclude an assembly from resolution because it is part of out exclusion list we need to let the user know why this is.
        /// There can be a number of reasons each for un-resolving a reference, these reasons are encapsulated by a different black list. We need to log a specific message
        /// depending on which black list we have found the offending assembly in. This delegate allows one to tie a set of logging messages to a black list so that when we
        /// discover an assembly in the black list we can log the correct message.
        /// </summary>
        internal delegate void LogExclusionReason(bool displayPrimaryReferenceMessage, AssemblyNameExtension assemblyName, Reference reference, ITaskItem referenceItem, string targetedFramework);

        // Offset to the PE header
        private const int PEOFFSET = 0x3c;

        // PEHeader
        private const int PEHEADER = 0x00004550;

#if FEATURE_WIN32_REGISTRY
        /// <summary>
        /// Construct.
        /// </summary>
        /// <param name="buildEngine"></param>
        /// <param name="findDependencies">If true, then search for dependencies.</param>
        /// <param name="findSatellites">If true, then search for satellite files.</param>
        /// <param name="findSerializationAssemblies">If true, then search for serialization assembly files.</param>
        /// <param name="findRelatedFiles">If true, then search for related files.</param>
        /// <param name="searchPaths">Paths to search for dependent assemblies on.</param>
        /// <param name="relatedFileExtensions"></param>
        /// <param name="candidateAssemblyFiles">List of literal assembly file names to be considered when SearchPaths has {CandidateAssemblyFiles}.</param>
        /// <param name="resolvedSDKItems">Resolved sdk items</param>
        /// <param name="frameworkPaths">Path to the FX.</param>
        /// <param name="installedAssemblies">Installed assembly XML tables.</param>
        /// <param name="targetProcessorArchitecture">Like x86 or IA64\AMD64, the processor architecture being targetted.</param>
        /// <param name="fileExists">Delegate used for checking for the existence of a file.</param>
        /// <param name="directoryExists">Delegate used for files.</param>
        /// <param name="getDirectories">Delegate used for getting directories.</param>
        /// <param name="getAssemblyName">Delegate used for getting assembly names.</param>
        /// <param name="getAssemblyMetadata">Delegate used for finding dependencies of a file.</param>
        /// <param name="getRegistrySubKeyNames">Used to get registry subkey names.</param>
        /// <param name="getRegistrySubKeyDefaultValue">Used to get registry default values.</param>
        /// <param name="openBaseKey"></param>
        /// <param name="unresolveFrameworkAssembliesFromHigherFrameworks"></param>
        /// <param name="assemblyMetadataCache">Cache of metadata already read from paths.</param>
        /// <param name="allowedAssemblyExtensions"></param>
        /// <param name="getRuntimeVersion"></param>
        /// <param name="targetedRuntimeVersion">Version of the runtime to target.</param>
        /// <param name="projectTargetFramework">Version of the framework targeted by the project.</param>
        /// <param name="targetFrameworkMoniker">Target framework moniker we are targeting.</param>
        /// <param name="log">Logging helper to allow the logging of meessages from the Reference Table.</param>
        /// <param name="latestTargetFrameworkDirectories"></param>
        /// <param name="copyLocalDependenciesWhenParentReferenceInGac"></param>
        /// <param name="doNotCopyLocalIfInGac"></param>
        /// <param name="getAssemblyPathInGac"></param>
        /// <param name="isWinMDFile"></param>
        /// <param name="ignoreVersionForFrameworkReferences"></param>
        /// <param name="readMachineTypeFromPEHeader"></param>
        /// <param name="warnOrErrorOnTargetArchitectureMismatch"></param>
        /// <param name="ignoreFrameworkAttributeVersionMismatch"></param>
#else
        /// <summary>
        /// Construct.
        /// </summary>
        /// <param name="buildEngine"></param>
        /// <param name="findDependencies">If true, then search for dependencies.</param>
        /// <param name="findSatellites">If true, then search for satellite files.</param>
        /// <param name="findSerializationAssemblies">If true, then search for serialization assembly files.</param>
        /// <param name="findRelatedFiles">If true, then search for related files.</param>
        /// <param name="searchPaths">Paths to search for dependent assemblies on.</param>
        /// <param name="relatedFileExtensions"></param>
        /// <param name="candidateAssemblyFiles">List of literal assembly file names to be considered when SearchPaths has {CandidateAssemblyFiles}.</param>
        /// <param name="resolvedSDKItems">Resolved sdk items</param>
        /// <param name="frameworkPaths">Path to the FX.</param>
        /// <param name="installedAssemblies">Installed assembly XML tables.</param>
        /// <param name="targetProcessorArchitecture">Like x86 or IA64\AMD64, the processor architecture being targetted.</param>
        /// <param name="fileExists">Delegate used for checking for the existence of a file.</param>
        /// <param name="directoryExists">Delegate used for files.</param>
        /// <param name="getDirectories">Delegate used for getting directories.</param>
        /// <param name="getAssemblyName">Delegate used for getting assembly names.</param>
        /// <param name="getAssemblyMetadata">Delegate used for finding dependencies of a file.</param>
        /// <param name="unresolveFrameworkAssembliesFromHigherFrameworks"></param>
        /// <param name="assemblyMetadataCache">Cache of metadata already read from paths.</param>
        /// <param name="allowedAssemblyExtensions"></param>
        /// <param name="getRuntimeVersion"></param>
        /// <param name="targetedRuntimeVersion">Version of the runtime to target.</param>
        /// <param name="projectTargetFramework">Version of the framework targeted by the project.</param>
        /// <param name="targetFrameworkMoniker">Target framework moniker we are targeting.</param>
        /// <param name="log">Logging helper to allow the logging of meessages from the Reference Table.</param>
        /// <param name="latestTargetFrameworkDirectories"></param>
        /// <param name="copyLocalDependenciesWhenParentReferenceInGac"></param>
        /// <param name="doNotCopyLocalIfInGac"></param>
        /// <param name="getAssemblyPathInGac"></param>
        /// <param name="isWinMDFile"></param>
        /// <param name="ignoreVersionForFrameworkReferences"></param>
        /// <param name="readMachineTypeFromPEHeader"></param>
        /// <param name="warnOrErrorOnTargetArchitectureMismatch"></param>
        /// <param name="ignoreFrameworkAttributeVersionMismatch"></param>
#endif
        internal ReferenceTable
        (
            IBuildEngine buildEngine,
            bool findDependencies,
            bool findSatellites,
            bool findSerializationAssemblies,
            bool findRelatedFiles,
            string[] searchPaths,
            string[] allowedAssemblyExtensions,
            string[] relatedFileExtensions,
            string[] candidateAssemblyFiles,
            ITaskItem[] resolvedSDKItems,
            string[] frameworkPaths,
            InstalledAssemblies installedAssemblies,
            System.Reflection.ProcessorArchitecture targetProcessorArchitecture,
            FileExists fileExists,
            DirectoryExists directoryExists,
            GetDirectories getDirectories,
            GetAssemblyName getAssemblyName,
            GetAssemblyMetadata getAssemblyMetadata,
#if FEATURE_WIN32_REGISTRY
            GetRegistrySubKeyNames getRegistrySubKeyNames,
            GetRegistrySubKeyDefaultValue getRegistrySubKeyDefaultValue,
            OpenBaseKey openBaseKey,
#endif
            GetAssemblyRuntimeVersion getRuntimeVersion,
            Version targetedRuntimeVersion,
            Version projectTargetFramework,
            FrameworkNameVersioning targetFrameworkMoniker,
            TaskLoggingHelper log,
            string[] latestTargetFrameworkDirectories,
            bool copyLocalDependenciesWhenParentReferenceInGac,
            bool doNotCopyLocalIfInGac,
            GetAssemblyPathInGac getAssemblyPathInGac,
            IsWinMDFile isWinMDFile,
            bool ignoreVersionForFrameworkReferences,
            ReadMachineTypeFromPEHeader readMachineTypeFromPEHeader,
            WarnOrErrorOnTargetArchitectureMismatchBehavior warnOrErrorOnTargetArchitectureMismatch,
            bool ignoreFrameworkAttributeVersionMismatch,
            bool unresolveFrameworkAssembliesFromHigherFrameworks,
            ConcurrentDictionary<string, AssemblyMetadata> assemblyMetadataCache)
        {
            _log = log;
            _findDependencies = findDependencies;
            _findSatellites = findSatellites;
            _findSerializationAssemblies = findSerializationAssemblies;
            _findRelatedFiles = findRelatedFiles;
            _frameworkPaths = frameworkPaths;
            _allowedAssemblyExtensions = allowedAssemblyExtensions;
            _relatedFileExtensions = relatedFileExtensions;
            _installedAssemblies = installedAssemblies;
            _targetProcessorArchitecture = targetProcessorArchitecture;
            _fileExists = fileExists;
            _directoryExists = directoryExists;
            _getDirectories = getDirectories;
            _getAssemblyName = getAssemblyName;
            _getAssemblyMetadata = getAssemblyMetadata;
            _getRuntimeVersion = getRuntimeVersion;
            _projectTargetFramework = projectTargetFramework;
            _targetedRuntimeVersion = targetedRuntimeVersion;
#if FEATURE_WIN32_REGISTRY
            _openBaseKey = openBaseKey;
#endif
            _targetFrameworkMoniker = targetFrameworkMoniker;
            _latestTargetFrameworkDirectories = latestTargetFrameworkDirectories;
            _copyLocalDependenciesWhenParentReferenceInGac = copyLocalDependenciesWhenParentReferenceInGac;
            _doNotCopyLocalIfInGac = doNotCopyLocalIfInGac;
            _getAssemblyPathInGac = getAssemblyPathInGac;
            _isWinMDFile = isWinMDFile;
            _readMachineTypeFromPEHeader = readMachineTypeFromPEHeader;
            _warnOrErrorOnTargetArchitectureMismatch = warnOrErrorOnTargetArchitectureMismatch;
            _ignoreFrameworkAttributeVersionMismatch = ignoreFrameworkAttributeVersionMismatch;
            _assemblyMetadataCache = assemblyMetadataCache;

            // Set condition for when to check assembly version against the target framework version 
            _checkAssemblyVersionAgainstTargetFrameworkVersion = unresolveFrameworkAssembliesFromHigherFrameworks || ((_projectTargetFramework ?? ReferenceTable.s_targetFrameworkVersion_40) <= ReferenceTable.s_targetFrameworkVersion_40);

            // Convert the list of installed SDK's to a dictionary for faster lookup
            _resolvedSDKReferences = new Dictionary<string, ITaskItem>(StringComparer.OrdinalIgnoreCase);
            _ignoreVersionForFrameworkReferences = ignoreVersionForFrameworkReferences;

            if (resolvedSDKItems != null)
            {
                foreach (ITaskItem resolvedSDK in resolvedSDKItems)
                {
                    string sdkName = resolvedSDK.GetMetadata("SDKName");

                    if (sdkName.Length > 0)
                    {
                        _resolvedSDKReferences[sdkName] = resolvedSDK;
                    }
                }
            }

            // Compile searchpaths into fast resolver array.
            Resolvers = AssemblyResolution.CompileSearchPaths
                (
                    buildEngine,
                    searchPaths,
                    candidateAssemblyFiles,
                    targetProcessorArchitecture,
                    frameworkPaths,
                    fileExists,
                    getAssemblyName,
#if FEATURE_WIN32_REGISTRY
                    getRegistrySubKeyNames,
                    getRegistrySubKeyDefaultValue,
                    openBaseKey,
#endif
                    installedAssemblies,
                    getRuntimeVersion,
                    targetedRuntimeVersion,
                    getAssemblyPathInGac,
                    log
                );
        }

        /// <summary>
        /// Set of resolvers the reference table uses.
        /// </summary>
        internal Resolver[] Resolvers { get; }

        /// <summary>
        /// Get a table of all vertices.
        /// </summary>
        internal Dictionary<AssemblyNameExtension, Reference> References { get; private set; } = new Dictionary<AssemblyNameExtension, Reference>(AssemblyNameComparer.GenericComparer);

        /// <summary>
        /// If assemblies have been marked for exclusion this contains the list of their full names
        /// This may be null
        /// </summary>
        internal List<string> ListOfExcludedAssemblies { get; private set; }

        /// <summary>
        /// Indicates that at least one reference was <see cref="Reference.ExternallyResolved"/> and
        /// we skipped finding its dependencies as a result.
        /// </summary>
        /// <remarks>
        /// This is currently used to perform a shallow search for System.Runtime/netstandard usage
        /// within the externally resolved graph.
        /// </remarks>
        internal bool SkippedFindingExternallyResolvedDependencies { get; private set; }

        /// <summary>
        /// Force dependencies to be walked even when a reference is marked with ExternallyResolved=true
        /// metadata.
        /// </summary>
        /// <remarks>
        /// This is currently used to ensure that we suggest appropriate binding redirects for
        /// assembly version conflicts within an externally resolved graph.
        /// </remarks>
        internal bool FindDependenciesOfExternallyResolvedReferences { get; set; }

        /// <summary>
        /// Adds a reference to the table.
        /// </summary>
        /// <param name="assemblyName">The assembly name to be used as a key.</param>
        /// <param name="reference">The reference to add.</param>
        internal void AddReference(AssemblyNameExtension assemblyName, Reference reference)
        {
            ErrorUtilities.VerifyThrow(assemblyName.Name != null, "Got an empty assembly name.");
            if (References.TryGetValue(assemblyName, out Reference referenceGoingToBeReplaced))
            {
                foreach (AssemblyRemapping pair in referenceGoingToBeReplaced.RemappedAssemblyNames())
                {
                    reference.AddRemapping(pair.From, pair.To);
                }
            }

            if (reference.FullPath.Length > 0 && ChangeWaves.AreFeaturesEnabled(ChangeWaves.Wave17_0))
            {
                // Saves effort and makes deduplication possible downstream
                reference.NormalizeFullPath();
            }

            References[assemblyName] = reference;
        }

        /// <summary>
        /// Find the reference that corresponds to the given path.
        /// </summary>
        /// <param name="assemblyName">The assembly name  to find the reference for.</param>
        /// <returns>'null' if no reference existed.</returns>
        internal Reference GetReference(AssemblyNameExtension assemblyName)
        {
            ErrorUtilities.VerifyThrow(assemblyName.Name != null, "Got an empty assembly name.");
            References.TryGetValue(assemblyName, out Reference referenceToReturn);
            return referenceToReturn;
        }

        /// <summary>
        /// Give an assembly file name, adjust a Reference to match it.
        /// </summary>
        /// <param name="reference">The reference to work on</param>
        /// <param name="assemblyFileName">The path to the assembly file.</param>
        /// <returns>The AssemblyName of assemblyFileName</returns>
        private AssemblyNameExtension NameAssemblyFileReference
        (
            Reference reference,
            string assemblyFileName
        )
        {
            AssemblyNameExtension assemblyName = null;

            if (!Path.IsPathRooted(assemblyFileName))
            {
                reference.FullPath = Path.GetFullPath(assemblyFileName);
            }
            else
            {
                reference.FullPath = assemblyFileName;
            }

            try
            {
                if (_fileExists(assemblyFileName))
                {
                    assemblyName = _getAssemblyName(assemblyFileName);
                    if (assemblyName != null)
                    {
                        reference.ResolvedSearchPath = assemblyFileName;
                    }
                }
                else if (_directoryExists(assemblyFileName))
                {
                    assemblyName = new AssemblyNameExtension("*directory*");

                    reference.AddError
                    (
                        new ReferenceResolutionException
                        (
                            ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("General.ExpectedFileGotDirectory", reference.FullPath),
                            null
                        )
                    );
                    reference.FullPath = String.Empty;
                }

                if (assemblyName == null)
                {
                    reference.AddError
                    (
                        new DependencyResolutionException(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("General.ExpectedFileMissing", reference.FullPath), null)
                    );
                }
            }
            catch (BadImageFormatException e)
            {
                reference.AddError(new DependencyResolutionException(e.Message, e));
            }
            catch (UnauthorizedAccessException e)
            {
                // If this isn't a valid assembly, then record the exception and continue on
                reference.AddError(new DependencyResolutionException(e.Message, e));
            }

            // If couldn't resolve the assemly name then just use the simple name extracted from
            // the file name.
            if (assemblyName == null)
            {
                string simpleName = Path.GetFileNameWithoutExtension(assemblyFileName);
                assemblyName = new AssemblyNameExtension(simpleName);
            }

            return assemblyName;
        }

        /// <summary>
        /// Given a list of task items, add them all to this table and make them the only primary items.
        /// </summary>
        /// <param name="referenceAssemblyFiles">The task items which contain file names to add.</param>
        /// <param name="referenceAssemblyNames">The task items which contain fusion names to add.</param>
        /// <param name="exceptions">Exceptions encountered while setting primary items. Exceptions are logged, but it doesn't stop the resolution process.</param>
        private void SetPrimaryItems
        (
            ITaskItem[] referenceAssemblyFiles,
            ITaskItem[] referenceAssemblyNames,
            List<Exception> exceptions
        )
        {
            // Loop over the referenceAssemblyFiles provided and add each one that doesn't exist.
            // Set the primary flag to 'true'.
            if (referenceAssemblyFiles != null)
            {
                foreach (ITaskItem i in referenceAssemblyFiles)
                {
                    SetPrimaryFileItem(i);
                }
            }

            // Loop over the referenceAssemblyNames provided and add each one that doesn't exist.
            // Set the primary flag to 'true'.
            if (referenceAssemblyNames != null)
            {
                foreach (ITaskItem n in referenceAssemblyNames)
                {
                    Exception e = SetPrimaryAssemblyReferenceItem(n);

                    if (e != null)
                    {
                        exceptions.Add(e);
                    }
                }
            }
        }

        /// <summary>
        /// Given an item that refers to a assembly name, make it a primary reference.
        /// </summary>
        /// <param name="referenceAssemblyName">The task item which contain fusion names to add.</param>
        /// <returns>Resulting exception containing resolution failure details, if any: too costly to throw it.</returns>
        private Exception SetPrimaryAssemblyReferenceItem
        (
            ITaskItem referenceAssemblyName
        )
        {
            // Get the desired executable extension.
            string executableExtension = referenceAssemblyName.GetMetadata(ItemMetadataNames.executableExtension);

            // Get the assembly name, if possible.
            string itemSpec = referenceAssemblyName.ItemSpec;
            string rawFileNameCandidate = itemSpec;
            AssemblyNameExtension assemblyName = null;
            string fusionName = referenceAssemblyName.GetMetadata(ItemMetadataNames.fusionName);
            bool result = MetadataConversionUtilities.TryConvertItemMetadataToBool(referenceAssemblyName, ItemMetadataNames.IgnoreVersionForFrameworkReference, out bool metadataFound);
            bool ignoreVersionForFrameworkReference;
            if (metadataFound)
            {
                ignoreVersionForFrameworkReference = result;
            }
            else
            {
                ignoreVersionForFrameworkReference = _ignoreVersionForFrameworkReferences;
            }

            TryConvertToAssemblyName(itemSpec, fusionName, ref assemblyName);

            // Figure out the specific version value.
            bool wantSpecificVersion = MetadataConversionUtilities.TryConvertItemMetadataToBool(referenceAssemblyName, ItemMetadataNames.specificVersion, out bool foundSpecificVersionMetadata);

            bool isSimpleName = (assemblyName?.IsSimpleName == true);

            // Create the reference.
            var reference = new Reference(_isWinMDFile, _fileExists, _getRuntimeVersion);
            reference.MakePrimaryAssemblyReference(referenceAssemblyName, wantSpecificVersion, executableExtension);

            // Escape simple names.
            // 1) If the itemSpec for the task is already a simple name 
            // 2) We have found the metadata and it is specifically set to false
            if (assemblyName != null && (isSimpleName || (foundSpecificVersionMetadata && !wantSpecificVersion)))
            {
                assemblyName = new AssemblyNameExtension
                (
                    AssemblyNameExtension.EscapeDisplayNameCharacters(assemblyName.Name)
                );

                isSimpleName = assemblyName.IsSimpleName;
            }

            // Set the HintPath if there is one.
            reference.HintPath = referenceAssemblyName.GetMetadata(ItemMetadataNames.hintPath);

            if (assemblyName != null && !wantSpecificVersion && !isSimpleName && reference.HintPath.Length == 0)
            {
                // Check to see if the assemblyname is in the framework list just use that fusion name
                if (_installedAssemblies != null && ignoreVersionForFrameworkReference)
                {
                    AssemblyEntry entry = _installedAssemblies.FindHighestVersionInRedistList(assemblyName);
                    if (entry != null)
                    {
                        assemblyName = entry.AssemblyNameExtension.Clone();
                    }
                }
            }

            if (assemblyName != null && _installedAssemblies != null && !wantSpecificVersion && reference.HintPath.Length == 0)
            {
                AssemblyNameExtension remappedExtension = _installedAssemblies.RemapAssemblyExtension(assemblyName);

                if (remappedExtension != null)
                {
                    reference.AddRemapping(assemblyName.CloneImmutable(), remappedExtension.CloneImmutable());
                    assemblyName = remappedExtension;
                }
            }

            // Embed Interop Types aka "NOPIAs" support is not available for Fx < 4.0
            // So, we just ignore this setting on down-level platforms
            if (_projectTargetFramework != null && _projectTargetFramework >= s_targetFrameworkVersion_40)
            {
                reference.EmbedInteropTypes = MetadataConversionUtilities.TryConvertItemMetadataToBool
                    (
                        referenceAssemblyName,
                        ItemMetadataNames.embedInteropTypes
                    );
            }

            // Set the AssemblyFolderKey if there is one.
            reference.AssemblyFolderKey = referenceAssemblyName.GetMetadata(ItemMetadataNames.assemblyFolderKey);

            // It's possible, especially in cases where the fusion name was passed in through the item
            // that we'll have a better (more information) fusion name once we know the assembly path.
            try
            {
                ResolveReference(assemblyName, rawFileNameCandidate, reference);

                if (reference.IsResolved)
                {
                    AssemblyNameExtension possiblyBetterAssemblyName;

                    try
                    {
                        // This may throw if, for example, the culture embedded in the assembly's manifest
                        // is not recognised by AssemblyName.GetAssemblyName
                        possiblyBetterAssemblyName = _getAssemblyName(reference.FullPath);
                    }
                    catch (ArgumentException)
                    {
                        // Give up trying to get a better name
                        possiblyBetterAssemblyName = null;
                    }

                    // Use the better name if it exists.
                    if (possiblyBetterAssemblyName?.Name != null)
                    {
                        assemblyName = possiblyBetterAssemblyName;
                    }
                }
            }
            catch (BadImageFormatException e)
            {
                // If this isn't a valid assembly, then record the exception and continue on
                reference.AddError(new BadImageReferenceException(e.Message, e));
            }
            catch (FileNotFoundException e) // Why isn't this covered in NotExpectedException?
            {
                reference.AddError(new BadImageReferenceException(e.Message, e));
            }
            catch (FileLoadException e)
            {
                // Managed assembly was found but could not be loaded.
                reference.AddError(new BadImageReferenceException(e.Message, e));
            }
            catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
            {
                reference.AddError(new BadImageReferenceException(e.Message, e));
            }

            // If there is still no assembly name then this is a case where the assembly metadata
            // just doesn't contain an assembly name. We want to try to tolerate this because
            // mscorlib.dll (sometimes?) doesn't contain an assembly name.
            if (assemblyName == null)
            {
                if (!reference.IsResolved)
                {
                    // The file doesn't exist and the reference was unresolved, there's nothing we can do at this point.
                    // Return, rather than throw, the exception, as in some situations it can happen thousands of times.
                    return new InvalidReferenceAssemblyNameException(referenceAssemblyName.ItemSpec);
                }

                assemblyName = new AssemblyNameExtension
                (
                    AssemblyNameExtension.EscapeDisplayNameCharacters(reference.FileNameWithoutExtension)
                );
            }

            // Check to see if this is a prereq assembly.
            if (_installedAssemblies == null)
            {
                reference.IsPrerequisite = false;
            }
            else
            {
                _installedAssemblies.GetInfo
                (
                    assemblyName,
                    out _,
                    out bool isPrerequisite,
                    out bool? isRedistRoot,
                    out string redistName
                );

                reference.IsPrerequisite = isPrerequisite;
                reference.IsRedistRoot = isRedistRoot;
                reference.RedistName = redistName;
            }

            AddReference(assemblyName, reference);

            if (reference.ExternallyResolved)
            {
                _externallyResolvedPrimaryReferences.Add(assemblyName.Name);
            }

            return null;
        }

        /// <summary>
        /// Attempts to convert an itemSpec and fusionName into an assembly name.
        /// AssemblyName is left unchanged if conversion wasn't possible.
        /// </summary>
        private static void TryConvertToAssemblyName(string itemSpec, string fusionName, ref AssemblyNameExtension assemblyName)
        {
            // FusionName is used if available.
            string finalName = fusionName;
            if (string.IsNullOrEmpty(finalName))
            {
                // Otherwise, its itemSpec.
                finalName = itemSpec;
            }

            bool pathRooted = false;
            try
            {
                pathRooted = Path.IsPathRooted(finalName);
            }
            catch (ArgumentException)
            {
                /* Eat this because it has invalid chars in to and cannot be a path, maybe it can be parsed as a fusion name.*/
            }

            if (!pathRooted)
            {
                // Now try to convert to an AssemblyName.
                try
                {
                    assemblyName = new AssemblyNameExtension(finalName, true /*throw if not valid*/);
                }
                catch (FileLoadException)
                {
                    // Not a valid AssemblyName. Maybe its a file name.
                    TryGatherAssemblyNameEssentials(finalName, ref assemblyName);
                    return;
                }
            }
            else
            {
                // Maybe the string has a fusion name inside of it.
                TryGatherAssemblyNameEssentials(finalName, ref assemblyName);
            }
        }

        /// <summary>
        /// Given a string that may be a fusion name, try to gather the four essential properties:
        ///     Name
        ///     Version
        ///     PublicKeyToken
        ///     Culture
        /// </summary>
        /// <param name="fusionName"></param>
        /// <param name="assemblyName"></param>
        private static void TryGatherAssemblyNameEssentials(string fusionName, ref AssemblyNameExtension assemblyName)
        {
            int firstComma = fusionName.IndexOf(',');
            if (firstComma == -1)
            {
                return;
            }
            string name = fusionName.Substring(0, firstComma);

            string version = null;
            string publicKeyToken = null;
            string culture = null;
            TryGetAssemblyNameComponent(fusionName, "Version", ref version);
            TryGetAssemblyNameComponent(fusionName, "PublicKeyToken", ref publicKeyToken);
            TryGetAssemblyNameComponent(fusionName, "Culture", ref culture);

            if (version == null || publicKeyToken == null || culture == null)
            {
                return;
            }

            string newFusionName = String.Format(CultureInfo.InvariantCulture,
                "{0}, Version={1}, Culture={2}, PublicKeyToken={3}",
                name, version, culture, publicKeyToken);

            // Now try to convert to an AssemblyName.
            try
            {
                assemblyName = new AssemblyNameExtension(newFusionName, true /* throw if not valid */);
            }
            catch (FileLoadException)
            {
                // Not a valid AssemblyName. Maybe it's a file name.
                // TryGatherAssemblyNameEssentials
                return;
            }
        }

        /// <summary>
        /// Attempt to get one field out of an assembly name.
        /// </summary>
        private static void TryGetAssemblyNameComponent(string fusionName, string component, ref string value)
        {
            int position = fusionName.IndexOf(component + "=", StringComparison.Ordinal);
            if (position == -1)
            {
                return;
            }
            position += component.Length + 1;
            int nextDelimiter = fusionName.IndexOfAny(new[] { ',', ' ' }, position);
            if (nextDelimiter == -1)
            {
                value = fusionName.Substring(position);
            }
            else
            {
                value = fusionName.Substring(position, nextDelimiter - position);
            }
        }

        /// <summary>
        /// Given an item that refers to a file name, make it a primary reference.
        /// </summary>
        private void SetPrimaryFileItem(ITaskItem referenceAssemblyFile)
        {
            try
            {
                // Create the reference.
                var reference = new Reference(_isWinMDFile, _fileExists, _getRuntimeVersion);

                string itemSpec = referenceAssemblyFile.ItemSpec;
                bool hasSpecificVersionMetadata = MetadataConversionUtilities.TryConvertItemMetadataToBool(referenceAssemblyFile, ItemMetadataNames.specificVersion);
                reference.MakePrimaryAssemblyReference
                (
                    referenceAssemblyFile,
                    hasSpecificVersionMetadata,
                    Path.GetExtension(itemSpec)
                );

                AssemblyNameExtension assemblyName = NameAssemblyFileReference
                (
                    reference,
                    itemSpec  // Contains the assembly file name.
                );

                // Embed Interop Types aka "NOPIAs" support is not available for Fx < 4.0
                // So, we just ignore this setting on down-level platforms
                if (_projectTargetFramework >= s_targetFrameworkVersion_40)
                {
                    reference.EmbedInteropTypes = MetadataConversionUtilities.TryConvertItemMetadataToBool
                        (
                            referenceAssemblyFile,
                            ItemMetadataNames.embedInteropTypes
                        );
                }

                AddReference(assemblyName, reference);

                if (reference.ExternallyResolved)
                {
                    _externallyResolvedPrimaryReferences.Add(assemblyName.Name);
                }
            }
            catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
            {
                throw new InvalidParameterValueException("AssemblyFiles", referenceAssemblyFile.ItemSpec, e.Message);
            }
        }

        /// <summary>
        /// Find related files like .pdbs and .xmls
        /// </summary>
        /// <param name="reference">The reference to the parent assembly.</param>
        private void FindRelatedFiles
        (
            Reference reference
        )
        {
            string baseName = reference.FullPathWithoutExtension;

            // Look for companion files like pdbs and xmls that ride along with 
            // assemblies.
            foreach (string companionExtension in _relatedFileExtensions)
            {
                string companionFile = baseName + companionExtension;

                if (_fileExists(companionFile))
                {
                    reference.AddRelatedFileExtension(companionExtension);
                }
            }

            // Native Winmd files may have a companion dll beside it.
            if (reference.IsWinMDFile && !reference.IsManagedWinMDFile)
            {
                var companionFile = baseName + ".dll";

                if (reference.IsPrimary)
                {
                    var implementationFile = reference.PrimarySourceItem.GetMetadata(ItemMetadataNames.winmdImplmentationFile);

                    // Static library projects can produce a .winmd with an associated .lib, but that is not
                    // a real ImplementationAssembly--it would fail downstream when trying to read its PE header.
                    if (!String.IsNullOrEmpty(implementationFile) && Path.GetExtension(implementationFile) == ".dll")
                    {
                        companionFile = Path.Combine(Path.GetDirectoryName(baseName), implementationFile);
                    }
                }

                if (_fileExists(companionFile))
                {
                    reference.ImplementationAssembly = companionFile;
                }
            }
        }

        /// <summary>
        /// Find satellite assemblies.
        /// </summary>
        /// <param name="reference">The reference to the parent assembly.</param>
        private void FindSatellites
        (
            Reference reference
        )
        {
            try
            {
                // If the directory doesn't exist (which is possible in the situation
                // where we were passed in a pre-resolved reference from a P2P reference
                // that hasn't actually been built yet), then GetDirectories will throw.
                // Avoid that by just short-circuiting here.
                if (!_directoryExists(reference.DirectoryName))
                {
                    return;
                }

                string[] subDirectories = _getDirectories(reference.DirectoryName, "*");
                string sateliteFilename = subDirectories.Length > 0
                    ? reference.FileNameWithoutExtension + ".resources.dll"
                    : string.Empty;

                foreach (string subDirectory in subDirectories)
                {
                    // Is there a candidate satellite in that folder?
                    string cultureName = Path.GetFileName(subDirectory);

                    if (CultureInfoCache.IsValidCultureString(cultureName))
                    {
                        string satelliteAssembly = Path.Combine(subDirectory, sateliteFilename);
                        if (_fileExists(satelliteAssembly))
                        {
                            // This is valid satellite assembly. 
                            reference.AddSatelliteFile(Path.Combine(cultureName, sateliteFilename));
                        }
                    }
                }
            }
            catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
            {
                _log?.LogErrorFromResources("ResolveAssemblyReference.ProblemFindingSatelliteAssemblies", reference.FullPath, e.Message);
            }
        }

        /// <summary>
        /// Find serialization assemblies.
        /// </summary>
        /// <param name="reference">The reference to the parent assembly.</param>
        private void FindSerializationAssemblies
        (
            Reference reference
        )
        {
            string serializationAssemblyFilename = reference.FileNameWithoutExtension + ".XmlSerializers.dll";
            string serializationAssemblyPath = Path.Combine(reference.DirectoryName, serializationAssemblyFilename);
            if (_fileExists(serializationAssemblyPath))
            {
                // This is valid serialization assembly. 
                reference.AddSerializationAssemblyFile(serializationAssemblyFilename);
            }
        }

        /// <summary>
        /// Get unified dependencies and scatter files for a reference.
        /// </summary>
        private void GetUnifiedAssemblyMetadata
            (
                Reference reference,
                out IEnumerable<UnifiedAssemblyName> unifiedDependencies,
                out string[] scatterFiles
            )
        {
            // Shortcut if this is a prereq file--don't find dependencies.
            // We also don't want to look for dependencies if we already know
            // this assembly is a bad image.
            if (reference.IsPrerequisite || reference.IsBadImage)
            {
                unifiedDependencies = null;
                scatterFiles = null;
                return;
            }

            _getAssemblyMetadata
            (
                reference.FullPath,
                _assemblyMetadataCache,
                out AssemblyNameExtension[] dependentAssemblies,
                out scatterFiles,
                out FrameworkName frameworkName
            );

            reference.FrameworkNameAttribute = frameworkName;

            var dependencies = new List<AssemblyNameExtension>(dependentAssemblies?.Length ?? 0);

            if (dependentAssemblies?.Length > 0)
            {
                // Re-map immediately so that to the sytem we actually got the remapped version when reading the manifest.
                for (int i = 0; i < dependentAssemblies.Length; i++)
                {
                    // This will return a clone of the remapped assemblyNameExtension so its ok to party on it.
                    AssemblyNameExtension remappedExtension = _installedAssemblies?.RemapAssemblyExtension(dependentAssemblies[i]);
                    if (remappedExtension != null)
                    {
                        AssemblyNameExtension originalExtension = dependentAssemblies[i];
                        AssemblyNameExtension existingExtension = dependencies.Find(x => x.Equals(remappedExtension));
                        if (existingExtension != null)
                        {
                            existingExtension.AddRemappedAssemblyName(originalExtension.CloneImmutable());
                            continue;
                        }
                        else
                        {
                            dependentAssemblies[i] = remappedExtension;
                            dependentAssemblies[i].AddRemappedAssemblyName(originalExtension.CloneImmutable());
                        }
                    }

                    // Assemblies which reference WinMD files sometimes will have references to mscorlib version 255.255.255 which is invalid. For this reason
                    // We will remove the dependency to mscorlib from the list of dependencies so it is not used for resolution or unification.
                    bool isMscorlib = IsPseudoAssembly(dependentAssemblies[i].Name);

                    if (!isMscorlib || dependentAssemblies[i].Version.Major != 255)
                    {
                        dependencies.Add(dependentAssemblies[i]);
                    }
                }

                dependentAssemblies = dependencies.ToArray();
            }

            unifiedDependencies = GetUnifiedAssemblyNames(dependentAssemblies);
        }

        /// <summary>
        /// Given an enumerator of pre-unified assembly names, return an enumerator of unified
        /// assembly names.
        /// </summary>
        private IEnumerable<UnifiedAssemblyName> GetUnifiedAssemblyNames
        (
            IEnumerable<AssemblyNameExtension> preUnificationAssemblyNames
        )
        {
            foreach (AssemblyNameExtension preUnificationAssemblyName in preUnificationAssemblyNames)
            {
                // First, unify the assembly name so that we're dealing with the right version.
                // Not AssemblyNameExtension because we're going to write to it.
                var dependentAssembly = new AssemblyNameExtension(preUnificationAssemblyName.AssemblyName.CloneIfPossible());

                bool isUnified = UnifyAssemblyNameVersions(dependentAssembly, out Version unifiedVersion, out UnificationReason unificationReason, out bool isPrerequisite, out bool? isRedistRoot, out string redistName);
                dependentAssembly.ReplaceVersion(unifiedVersion);

                yield return new UnifiedAssemblyName(preUnificationAssemblyName, dependentAssembly, isUnified, unificationReason, isPrerequisite, isRedistRoot, redistName);
            }
        }

        /// <summary>
        /// Find references and scatter files defined for the given assembly.
        /// </summary>
        /// <param name="reference">The reference to the parent assembly.</param>
        /// <param name="newEntries">New references are added to this list.</param>
        private void FindDependenciesAndScatterFiles
        (
            Reference reference,
            List<KeyValuePair<AssemblyNameExtension, Reference>> newEntries
        )
        {
            // Before checking for dependencies check to see if the reference itself exists. 
            // Even though to get to this point the reference must be resolved
            // the reference may not exist on disk if the reference is a project to project reference.
            if (!_fileExists(reference.FullPath))
            {
                reference.AddError
                      (
                          new DependencyResolutionException(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("General.ExpectedFileMissing", reference.FullPath), null)
                      );

                return;
            }

            try
            {
                GetUnifiedAssemblyMetadata(reference, out IEnumerable<UnifiedAssemblyName> unifiedDependencies, out string[] scatterFiles);
                reference.AttachScatterFiles(scatterFiles);

                // If no dependencies then fall out.
                if (unifiedDependencies == null)
                {
                    return;
                }

                foreach (UnifiedAssemblyName unifiedDependency in unifiedDependencies)
                {
                    // Now, see if it has already been found.
                    Reference existingReference = GetReference(unifiedDependency.PostUnified);

                    if (existingReference == null)
                    {
                        // This is valid reference.
                        Reference newReference = new Reference(_isWinMDFile, _fileExists, _getRuntimeVersion);

                        newReference.MakeDependentAssemblyReference(reference);
                        if (unifiedDependency.IsUnified)
                        {
                            newReference.AddPreUnificationVersion(reference.FullPath, unifiedDependency.PreUnified.Version, unifiedDependency.UnificationReason);
                        }

                        foreach (AssemblyNameExtension remappedFromName in unifiedDependency.PreUnified.RemappedFromEnumerator)
                        {
                            newReference.AddRemapping(remappedFromName, unifiedDependency.PreUnified.CloneImmutable());
                        }

                        newReference.IsPrerequisite = unifiedDependency.IsPrerequisite;

                        var newEntry = new KeyValuePair<AssemblyNameExtension, Reference>(unifiedDependency.PostUnified, newReference);
                        newEntries.Add(newEntry);
                    }
                    else
                    {
                        // If it already existed then just append the source items.
                        if (existingReference == reference)
                        {
                            // This means the assembly depends on itself. This seems to be legal so we allow allow it.
                            // I don't think this rises to the level of a warning for the user because fusion handles
                            // this case gracefully.
                        }
                        else
                        {
                            // Now, add new information to the reference.
                            existingReference.AddSourceItems(reference.GetSourceItems());
                            existingReference.AddDependee(reference);

                            if (unifiedDependency.IsUnified)
                            {
                                existingReference.AddPreUnificationVersion(reference.FullPath, unifiedDependency.PreUnified.Version, unifiedDependency.UnificationReason);
                            }

                            existingReference.IsPrerequisite = unifiedDependency.IsPrerequisite;
                        }

                        foreach (AssemblyNameExtension remappedFromName in unifiedDependency.PreUnified.RemappedFromEnumerator)
                        {
                            existingReference.AddRemapping(remappedFromName, unifiedDependency.PreUnified.CloneImmutable());
                        }
                    }
                }
            }
            catch (FileNotFoundException e) // Why isn't this covered in NotExpectedException?
            {
                reference.AddError(new DependencyResolutionException(e.Message, e));
            }
            catch (FileLoadException e)
            {
                // Managed assembly was found but could not be loaded.
                reference.AddError(new DependencyResolutionException(e.Message, e));
            }
            catch (BadImageFormatException e)
            {
                reference.AddError(new DependencyResolutionException(e.Message, e));
            }
            catch (System.Runtime.InteropServices.COMException e)
            {
                reference.AddError(new DependencyResolutionException(e.Message, e));
            }
            catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
            {
                reference.AddError(new DependencyResolutionException(e.Message, e));
            }
        }

        /// <summary>
        /// Mscorlib is not a real managed assembly. It is seen both with and without metadata.
        /// We assume that the correct mscorlib is on the target platform.
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        private static bool IsPseudoAssembly(string name)
        {
            return string.Equals(name, "mscorlib", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Based on the set of parent assemblies we want to add their directories to the list of resolvers so that
        /// if the dependency is sitting beside the assembly which requires it then we will resolve the assembly from that location first.
        ///
        /// The only time we do not want to do this is if the parent assembly came from the GAC or AssemblyFoldersEx then we want the assembly
        /// to be found using those resolvers so that our GAC and AssemblyFolders checks later on will work on those assemblies.
        /// </summary>
        internal static void CalculateParentAssemblyDirectories(List<string> parentReferenceFolders, Reference parentReference)
        {
            string parentReferenceFolder = parentReference.DirectoryName;
            string parentReferenceResolvedSearchPath = parentReference.ResolvedSearchPath;
            var parentReferencesAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            bool parentReferenceResolvedFromGAC = false;
            bool parentReferenceResolvedFromAssemblyFolders = false;
            if (!String.IsNullOrEmpty(parentReferenceResolvedSearchPath))
            {
                parentReferenceResolvedFromGAC = parentReferenceResolvedSearchPath.Equals(AssemblyResolutionConstants.gacSentinel, StringComparison.OrdinalIgnoreCase);
                parentReferenceResolvedFromAssemblyFolders = parentReferenceResolvedSearchPath.Equals(AssemblyResolutionConstants.assemblyFoldersSentinel, StringComparison.OrdinalIgnoreCase);
            }

            // Only add the parent folder as a search location if we have not added it to the list yet and the parent reference has not been resolved from the GAC or AssemblyFolders
            // If the reference has been resolved from one of these locations we want the dependency to be found using the GAC or AssemblyFolder resolver rather than the directory resolver
            // This way the dependency is marked with the correct search path "GAC" or "AssemblyFolder"  rather than "c:\xxxxxx" which prevents our GAC/AssemblyFolder check from working
            if (!parentReferencesAdded.Contains(parentReferenceFolder) && !parentReferenceResolvedFromGAC && !parentReferenceResolvedFromAssemblyFolders)
            {
                parentReferencesAdded.Add(parentReferenceFolder);
                parentReferenceFolders.Add(parentReferenceFolder);
            }
        }

        /// <summary>
        /// Given an unresolved reference (one that we don't know the full name for yet), figure out the
        /// full name. Should only be called on references that haven't been resolved yet--otherwise, its
        /// a perf problem.
        /// </summary>
        /// <param name="assemblyName">The fusion name for this reference.</param>
        /// <param name="rawFileNameCandidate">The file name to match if {RawFileName} is seen. (May be null).</param>
        /// <param name="reference">The reference object.</param>
        private void ResolveReference
        (
            AssemblyNameExtension assemblyName,
            string rawFileNameCandidate,
            Reference reference
        )
        {
            // Now, resolve this reference.
            string resolvedPath = null;
            string resolvedSearchPath = String.Empty;
            bool userRequestedSpecificFile = false;

            // A list of assemblies that might have been matches but weren't
            var assembliesConsideredAndRejected = new List<ResolutionSearchLocation>();

            // First, look for the dependency in the parents' directories. Unless they are resolved from the GAC or assemblyFoldersEx then 
            // we should make sure we use the GAC and assemblyFolders resolvers themserves rather than a directory resolver to find the reference.
            // This way we dont get assemblies pulled from the GAC or AssemblyFolders but dont have the marking that they were pulled form there.
            var parentReferenceFolders = new List<string>();
            foreach (Reference parentReference in reference.GetDependees())
            {
                CalculateParentAssemblyDirectories(parentReferenceFolders, parentReference);
            }

            // Build the set of resolvers.
            var jaggedResolvers = new List<Resolver[]>();

            // If a reference has an SDK name on it then we must ONLY resolve it from the SDK which matches the SDKName on the refernce metadata
            // this is to support the case where a single reference assembly is selected from the SDK.
            // If a reference has the SDKName metadata on it then we will only search using a single resolver, that is the InstalledSDKResolver.
            if (reference.SDKName.Length > 0)
            {
                jaggedResolvers.Add(new Resolver[] { new InstalledSDKResolver(_resolvedSDKReferences, "SDKResolver", _getAssemblyName, _fileExists, _getRuntimeVersion, _targetedRuntimeVersion) });
            }
            else
            {
                // Do not probe near dependees if the reference is primary and resolved externally. If resolved externally, the search paths should have been specified in such a way to point to the assembly file.
                if (assemblyName == null || !_externallyResolvedPrimaryReferences.Contains(assemblyName.Name))
                {
                    jaggedResolvers.Add(AssemblyResolution.CompileDirectories(parentReferenceFolders, _fileExists, _getAssemblyName, _getRuntimeVersion, _targetedRuntimeVersion));
                }

                jaggedResolvers.Add(Resolvers);
            }

            // Resolve
            try
            {
                resolvedPath = AssemblyResolution.ResolveReference
                (
                    jaggedResolvers,
                    assemblyName,
                    reference.SDKName,
                    rawFileNameCandidate,
                    reference.IsPrimary,
                    reference.WantSpecificVersion,
                    reference.GetExecutableExtensions(_allowedAssemblyExtensions),
                    reference.HintPath,
                    reference.AssemblyFolderKey,
                    assembliesConsideredAndRejected,
                    out resolvedSearchPath,
                    out userRequestedSpecificFile
                );
            }
            catch (BadImageFormatException e)
            {
                reference.AddError(new DependencyResolutionException(e.Message, e));
            }

            // Update the list of assemblies considered and rejected.
            reference.AddAssembliesConsideredAndRejected(assembliesConsideredAndRejected);

            // If the path was resolved, then specify the full path on the reference.
            if (resolvedPath != null)
            {
                if (ChangeWaves.AreFeaturesEnabled(ChangeWaves.Wave17_0))
                {
                    resolvedPath = FileUtilities.NormalizePath(resolvedPath);
                }
                else if (!Path.IsPathRooted(resolvedPath))
                {
                    resolvedPath = Path.GetFullPath(resolvedPath);
                }

                reference.FullPath = resolvedPath;
                reference.ResolvedSearchPath = resolvedSearchPath;
                reference.UserRequestedSpecificFile = userRequestedSpecificFile;
            }
            else
            {
                if (assemblyName != null)
                {
                    reference.AddError
                    (
                        new ReferenceResolutionException
                        (
                            ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("General.CouldNotLocateAssembly", assemblyName.FullName),
                            null
                        )
                    );
                }
            }
        }

        /// <summary>
        /// This method will remove references from the reference table which are contained in the blacklist.
        /// References which are primary references but are in the black list will be placed in the invalidResolvedFiles list.
        /// References which are dependency references but are in the black list will be placed in the invalidResolvedDependencyFiles list.
        /// </summary>
        internal void RemoveReferencesMarkedForExclusion(bool removeOnlyNoWarning, string subsetName)
        {
            MSBuildEventSource.Log.RarRemoveReferencesMarkedForExclusionStart();
            {
                // Create a table which will contain the references which are not in the black list
                var goodReferences = new Dictionary<AssemblyNameExtension, Reference>(AssemblyNameComparer.GenericComparer);

                // List of references which were removed from the reference table, we will loop through these and make sure that we get rid of the dependent references also.
                var removedReferences = new List<Reference>();

                // For each reference, have a list of dependency references and their assembly names. (List<KeyValuePair<Reference, AssemblyNameExtension>>) == the dependent reference and the assembly name.
                var dependencyGraph = new Dictionary<Reference, List<ReferenceAssemblyExtensionPair>>();

                if (subsetName == null)
                {
                    subsetName = String.Empty;
                }

                // Go through each of the references, we go through this table because in general it will be considerably smaller than the blacklist. (10's of references vs 100's of black list items)
                foreach (AssemblyNameExtension assemblyName in References.Keys)
                {
                    Reference assemblyReference = References[assemblyName];

                    AddToDependencyGraph(dependencyGraph, assemblyName, assemblyReference);

                    // Is the assembly name not in the black list. This means the assembly could be allowed.
                    bool isMarkedForExclusion = assemblyReference.ExclusionListLoggingProperties.IsInExclusionList;
                    LogExclusionReason logExclusionReason = assemblyReference.ExclusionListLoggingProperties.ExclusionReasonLogDelegate;

                    // Case one, the assembly is a primary reference
                    if (assemblyReference.IsPrimary)
                    {
                        // The assembly is good if it is not in the black list or it has specific version set to true.
                        if (!isMarkedForExclusion || assemblyReference.WantSpecificVersion)
                        {
                            // Do not add the reference to the good list if it has been added to the removed references list, possibly because of us processing another reference.
                            if (!removedReferences.Contains(assemblyReference))
                            {
                                goodReferences[assemblyName] = assemblyReference;
                            }
                        }
                        else
                        {
                            RemovePrimaryReferenceMarkedForExclusion(logExclusionReason, removeOnlyNoWarning, subsetName, removedReferences, assemblyName, assemblyReference);
                        }
                    }

                    // A Primary reference can also be dependency of other references. This means there may be other primary reference which depend on 
                    // the current primary reference and they need to be removed.
                    ICollection<ITaskItem> dependees = assemblyReference.GetSourceItems();

                    // Need to deal with dependencies, this can also include primary references who are dependencies themselves and are in the black list
                    if (!assemblyReference.IsPrimary || (assemblyReference.IsPrimary && isMarkedForExclusion && (dependees?.Count > 1)))
                    {
                        // Does the assembly have specific version true, or does any of its primary parent references have specific version true.
                        // This is checked because, if an assembly is in the black list, the only way it can possibly be allowed is if
                        // ANY of the primary references which caused it have specific version set to true. To see if any primary references have the metadata we pass true to the method indicating 
                        // we want to know if any primary references have specific version set to true.
                        bool hasSpecificVersionTrue = assemblyReference.CheckForSpecificVersionMetadataOnParentsReference(true);

                        //A dependency is "good" if it is not in the black list or any of its parents have specific version set to true
                        if (!isMarkedForExclusion || hasSpecificVersionTrue)
                        {
                            // Do not add the reference to the good list if it has been added to the removed references list, possibly because of us processing another reference.
                            if (!removedReferences.Contains(assemblyReference))
                            {
                                goodReferences[assemblyName] = assemblyReference;
                            }
                        }

                        // If the dependency is in the black list we need to remove the primary references which depend on this refernce.
                        // note, a reference can both be in the good references list and in the black list. This can happen if a multiple primary references
                        // depend on a single dependency. The dependency can be good for one reference but not allowed for the other.
                        if (isMarkedForExclusion)
                        {
                            RemoveDependencyMarkedForExclusion(logExclusionReason, removeOnlyNoWarning, subsetName, goodReferences, removedReferences, assemblyName, assemblyReference);
                        }
                    }
                }

                // Go through each of the reference which were removed from the reference list and make sure that we get rid of all of the assemblies which were 
                // dependencies of them.
                foreach (Reference reference in removedReferences)
                {
                    RemoveDependencies(reference, goodReferences, dependencyGraph);
                }

                // Replace the references table with the list only containing good references.
                References = goodReferences;
                MSBuildEventSource.Log.RarRemoveReferencesMarkedForExclusionStop();
            }
        }

        /// <summary>
        /// References usually only contains who they depend on, they do not know who depends on them. Given a reference
        /// A we cannot inspect A to find out that B,C,D depend on it. This method will traverse the references and build up this other direction of the graph,
        /// therefore we will be able to know given reference A, that B,C,D depend on it.
        /// </summary>
        private static void AddToDependencyGraph(Dictionary<Reference, List<ReferenceAssemblyExtensionPair>> dependencyGraph, AssemblyNameExtension assemblyName, Reference assemblyReference)
        {
            // Find the references who the current reference is a dependency for 
            foreach (Reference dependee in assemblyReference.GetDependees())
            {
                // For a dependee see if we already have a list started
                // 'dependencies' will contain a list of key value pairs (K: Dependent reference V: assembly Name)
                if (!dependencyGraph.TryGetValue(dependee, out List<ReferenceAssemblyExtensionPair> dependencies))
                {
                    dependencies = new List<ReferenceAssemblyExtensionPair>();
                    dependencyGraph.Add(dependee, dependencies);
                }

                dependencies.Add(new ReferenceAssemblyExtensionPair(assemblyReference, assemblyName));
            }
        }

        /// <summary>
        /// We have determined the given assembly reference is in the black list, we now need to find the primary references which caused it and make sure those are removed from the list of references.
        /// </summary>
        private void RemoveDependencyMarkedForExclusion(LogExclusionReason logExclusionReason, bool removeOnlyNoWarning, string subsetName, Dictionary<AssemblyNameExtension, Reference> goodReferences, List<Reference> removedReferences, AssemblyNameExtension assemblyName, Reference assemblyReference)
        {
            // For a dependency we would like to remove the primary references which caused this dependency to be found.
            // Source Items is the list of primary itemspecs which lead to the current reference being discovered. 
            ICollection<ITaskItem> dependees = assemblyReference.GetSourceItems();
            foreach (ITaskItem dependee in dependees)
            {
                string dependeeItemSpec = dependee.ItemSpec;

                if (assemblyReference.IsPrimary)
                {
                    // Dont process yourself
                    if (string.Equals(dependeeItemSpec, assemblyReference.PrimarySourceItem.ItemSpec, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                // Get the primary reference assemblyName
                AssemblyNameExtension primaryAssemblyName = GetReferenceFromItemSpec(dependeeItemSpec);

                if (primaryAssemblyName != null)
                {
                    // Get the specific primary reference which caused this dependency
                    Reference primaryAssemblyReference = References[primaryAssemblyName];
                    bool hasSpecificVersionMetadata = primaryAssemblyReference.WantSpecificVersion;

                    if (!hasSpecificVersionMetadata)
                    {
                        // If the reference has not been removed we need to remove it and possibly remove it from the good reference list.
                        if (!removedReferences.Contains(primaryAssemblyReference))
                        {
                            removedReferences.Add(primaryAssemblyReference);
                            goodReferences.Remove(primaryAssemblyName);
                        }

                        if (!removeOnlyNoWarning)
                        {
                            logExclusionReason?.Invoke(false, assemblyName, assemblyReference, dependee, subsetName);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// A primary references has been determined to be in the black list, it needs to be removed from the list of references by not being added to the list of good references
        /// and added to the list of removed references.
        /// </summary>
        private static void RemovePrimaryReferenceMarkedForExclusion(LogExclusionReason logExclusionReason, bool removeOnlyNoWarning, string subsetName, List<Reference> removedReferences, AssemblyNameExtension assemblyName, Reference assemblyReference)
        {
            removedReferences.Add(assemblyReference);

            if (!removeOnlyNoWarning)
            {
                // Note a primary references will always have a PrimarySourceItem which is not null
                logExclusionReason?.Invoke(true, assemblyName, assemblyReference, assemblyReference.PrimarySourceItem, subsetName);
            }
        }

        /// <summary>
        /// Get the primary reference based on the Itemspec
        /// </summary>
        internal AssemblyNameExtension GetReferenceFromItemSpec(string itemSpec)
        {
            foreach (AssemblyNameExtension assemblyName in References.Keys)
            {
                Reference assemblyReference = References[assemblyName];
                if (assemblyReference.IsPrimary && assemblyReference.PrimarySourceItem.ItemSpec.Equals(itemSpec, StringComparison.OrdinalIgnoreCase))
                {
                    return assemblyName;
                }
            }

            return null;
        }

        /// <summary>
        /// Go through the dependency graph and make sure that for a reference to remove that we get rid of all dependency assemblies which are not referenced by any other
        /// assembly. The remove reference list should contain ALL primary references which should be removed because they, or one of their dependencies is in the black list.
        /// </summary>
        /// <param name="removedReference">Reference to remove dependencies for</param>
        /// <param name="referenceList">Reference list which contains reference to be used in unification and returned as resolved items</param>
        /// <param name="dependencyList"> A dictionary (Key: Reference Value: List of dependencies and their assembly name)</param>
        private static void RemoveDependencies(Reference removedReference, Dictionary<AssemblyNameExtension, Reference> referenceList, Dictionary<Reference, List<ReferenceAssemblyExtensionPair>> dependencyList)
        {
            // See if the reference has a list of dependencies
            if (!dependencyList.TryGetValue(removedReference, out List<ReferenceAssemblyExtensionPair> dependencies))
            {
                return;
            }

            // Go through each of the dependency assemblies and remove the removedReference from the 
            // dependee list.
            foreach (ReferenceAssemblyExtensionPair dependency in dependencies)
            {
                Reference reference = dependency.Key;

                // Remove the referenceToRemove from the dependee list, this will "unlink" them, in that the dependency reference will no longer know that 
                // referenceToRemove had a dependency on it
                reference.RemoveDependee(removedReference);

                // A primary reference is special because it is declared in the project file so even if no one else deppends on it, the reference is still needed.
                if (reference.IsPrimary)
                {
                    continue;
                }

                // If the referenceToRemove was the last dependee of the current dependency reference, remove the dependency reference from the reference list.
                if (reference.GetDependees().Count == 0)
                {
                    referenceList.Remove(dependency.Value);

                    // Recurse using the current refererence so that we remove the next set of dependencies.
                    RemoveDependencies(reference, referenceList, dependencyList);
                }
            }
        }

        /// <summary>
        /// Searches the table for references that haven't been resolved to their full file names and
        /// for dependencies that haven't yet been found.
        ///
        /// If any are found, they're resolved and then dependencies are found. Then the process is repeated
        /// until nothing is left unresolved.
        /// </summary>
        /// <param name="remappedAssembliesValue">The table of remapped assemblies.</param>
        /// <param name="referenceAssemblyFiles">The task items which contain file names to add.</param>
        /// <param name="referenceAssemblyNames">The task items which contain fusion names to add.</param>
        /// <param name="exceptions">Errors encountered while computing closure.</param>
        internal void ComputeClosure
        (
            IEnumerable<DependentAssembly> remappedAssembliesValue,
            ITaskItem[] referenceAssemblyFiles,
            ITaskItem[] referenceAssemblyNames,
            List<Exception> exceptions
        )
        {
            MSBuildEventSource.Log.RarComputeClosureStart();
            {
                References.Clear();
                _externallyResolvedPrimaryReferences.Clear();
                SkippedFindingExternallyResolvedDependencies = false;

                _remappedAssemblies = remappedAssembliesValue;
                SetPrimaryItems(referenceAssemblyFiles, referenceAssemblyNames, exceptions);

                ComputeClosure();
            }
            MSBuildEventSource.Log.RarComputeClosureStop();
        }

        /// <summary>
        /// Implementation of ComputeClosure.
        /// </summary>
        private void ComputeClosure()
        {
            bool moreResolvable;
            int moreResolvableIterations = 0;
            const int maxIterations = 100000; // Wait for a ridiculously large number of iterations before bailing out.

            do
            {
                bool moreDependencies;

                int dependencyIterations = 0;
                do
                {
                    // Resolve all references.
                    ResolveAssemblyFilenames();

                    // Find prerequisites.
                    moreDependencies = FindAssociatedFiles();

                    ++dependencyIterations;
                    ErrorUtilities.VerifyThrow(dependencyIterations < maxIterations, "Maximum iterations exceeded while looking for dependencies.");
                } while (moreDependencies);

                // If everything is either resolved or unresolvable, then we can quit.
                // Otherwise, loop again.
                moreResolvable = false;
                foreach (Reference reference in References.Values)
                {
                    if (!reference.IsResolved)
                    {
                        if (!reference.IsUnresolvable)
                        {
                            moreResolvable = true;
                            break;
                        }
                    }
                }

                ++moreResolvableIterations;
                ErrorUtilities.VerifyThrow(moreResolvableIterations < maxIterations, "Maximum iterations exceeded while looking for resolvable references.");
            } while (moreResolvable);
        }

        /// <summary>
        /// Find associates for references that we haven't found associates for before.
        /// Returns true if new dependent assemblies were found.
        /// </summary>
        private bool FindAssociatedFiles()
        {
            bool newDependencies = false;

            var newEntries = new List<KeyValuePair<AssemblyNameExtension, Reference>>();

            foreach (Reference reference in References.Values)
            {
                // If the reference is resolved, but dependencies haven't been found,
                // then find dependencies.
                if (reference.IsResolved && !reference.DependenciesFound)
                {
                    // Set this reference to 'resolved' so it won't be processed the next time.
                    reference.DependenciesFound = true;

                    try
                    {
                        // We don't look for associated files for FX assemblies.
                        bool hasFrameworkPath = false;
                        string referenceDirectoryName = FileUtilities.EnsureTrailingSlash(reference.DirectoryName);

                        foreach (string frameworkPath in _frameworkPaths)
                        {
                            // frameworkPath is guaranteed to have a trailing slash, because 
                            // ResolveAssemblyReference.Execute takes care of adding it.

                            if (string.Equals(referenceDirectoryName, frameworkPath, StringComparison.OrdinalIgnoreCase))
                            {
                                hasFrameworkPath = true;
                            }
                        }

                        // We do not want to find dependencies of framework assembles, embedded interoptypes or assemblies in sdks.
                        if (!hasFrameworkPath && !reference.EmbedInteropTypes && reference.SDKName.Length == 0)
                        {
                            if (!reference.ExternallyResolved)
                            {
                                // Look for companion files like pdbs and xmls that ride along with 
                                // assemblies.
                                if (_findRelatedFiles)
                                {
                                    FindRelatedFiles(reference);
                                }

                                // Satellite assemblies are named <CultureDir>\<AppBaseName>.resources.dll
                                // where <CultureDir> is like 'en', 'fr', etc.
                                if (_findSatellites)
                                {
                                    FindSatellites(reference);
                                }

                                // Look for serialization assemblies.
                                if (_findSerializationAssemblies)
                                {
                                    FindSerializationAssemblies(reference);
                                }
                            }

                            if (!reference.ExternallyResolved || FindDependenciesOfExternallyResolvedReferences)
                            {
                                // Look for dependent assemblies.
                                if (_findDependencies)
                                {
                                    FindDependenciesAndScatterFiles(reference, newEntries);
                                }
                            }
                            else
                            {
                                SkippedFindingExternallyResolvedDependencies = true;
                            }

                            // If something was found, then break out and start fresh.
                            if (newEntries.Count > 0)
                            {
                                break;
                            }
                        }
                    }
                    catch (PathTooLongException e)
                    {
                        // If the directory path is too long then record the error and move on.
                        reference.AddError(new DependencyResolutionException(e.Message, e));
                    }
                }
            }

            // Add each new dependency found.
            foreach (KeyValuePair<AssemblyNameExtension, Reference> newEntry in newEntries)
            {
                newDependencies = true;
                AddReference(newEntry.Key, newEntry.Value);
            }

            return newDependencies;
        }

        /// <summary>
        /// Resolve all references that have not been resolved yet to real files on disk.
        /// </summary>
        private void ResolveAssemblyFilenames()
        {
            foreach (AssemblyNameExtension assemblyName in References.Keys)
            {
                Reference reference = GetReference(assemblyName);

                // Has this reference been resolved to a file name?
                if (!reference.IsResolved && !reference.IsUnresolvable)
                {
                    ResolveReference(assemblyName, null, reference);
                }
            }
        }

        /// <summary>
        /// This methods looks for conflicts between assemblies and attempts to
        /// resolve them.
        /// </summary>
        private void ResolveConflictsBetweenReferences(Dictionary<string, List<AssemblyNameReference>> baseNameToReferences)
        {
            // Now we have references organized into groups that would conflict.
            foreach (List<AssemblyNameReference> assemblyReferences in baseNameToReferences.Values)
            {
                ResolveConflictsBetweenReferences(assemblyReferences);
            }
        }

        private void ResolveConflictsBetweenReferences(List<AssemblyNameReference> assemblyReferences)
        {
            // Sort to make it predictable. Choose to sort by ascending version number
            // since this is known to reveal bugs in at least one circumstance.
            assemblyReferences.Sort(AssemblyNameReferenceAscendingVersionComparer.comparer);

            int currentWinnerIndex = 0;
            int comparisonIndex = 1;

            while (comparisonIndex < assemblyReferences.Count)
            {
                bool isLeftVictim = ResolveAssemblyNameConflict
                (
                    assemblyReferences[currentWinnerIndex],
                    assemblyReferences[comparisonIndex]
                ) == 0;

                if (isLeftVictim)
                {
                    currentWinnerIndex = comparisonIndex;
                }

                comparisonIndex++;
            }
        }

        /// <summary>
        /// Based on the closure, get a table of ideal remappings needed to
        /// produce zero conflicts.
        /// </summary>
        internal void ResolveConflicts
        (
            out List<DependentAssembly> idealRemappings,
            out List<AssemblyNameReference> conflictingReferences
        )
        {
            idealRemappings = null;
            conflictingReferences = null;

            // Get a table of simple name mapped to (perhaps multiple) reference.
            Dictionary<string, List<AssemblyNameReference>> baseNameToReferences = BuildSimpleNameTable();
            RemoveReferencesWithoutConflicts(baseNameToReferences);

            // If there were no basename conflicts then there can be no version-to-version conflicts.
            // In this case, short-circuit now rather than building up all the tables below.
            if (baseNameToReferences.Count == 0)
            {
                return;
            }

            // First, resolve all conflicts between references.
            ResolveConflictsBetweenReferences(baseNameToReferences);

            // Build a set of assembly names with conflicts and a table with the corresponding references.
            // Dependencies which differ only by version number need a suggested redirect.
            var conflictingFullNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var fullNameToReference = new Dictionary<string, AssemblyNameReference>(References.Count, StringComparer.OrdinalIgnoreCase);

            foreach (List<AssemblyNameReference> references in baseNameToReferences.Values)
            {
                foreach (AssemblyNameReference assemblyNameReference in references)
                {
                    AssemblyNameExtension assemblyName = assemblyNameReference.assemblyName;
                    Reference reference = assemblyNameReference.reference;

                    // If the assembly has a parent which has specific version set to true then we need to see if it is framework assembly
                    // Try and find an entry in the redist list by comparing everything except the version.
                    bool isFrameworkAssembly = _installedAssemblies != null
                                               && reference.CheckForSpecificVersionMetadataOnParentsReference(true)
                                               && _installedAssemblies.FindHighestVersionInRedistList(assemblyName) != null;

                    if (isFrameworkAssembly)
                    {
                        // We have found an entry in the redist list that this assembly is a framework assembly of some version
                        // also one if its parent references has specific version set to true, therefore we need to make sure
                        // that we do not consider it for conflict resolution.
                        continue;
                    }

                    byte[] pkt = assemblyName.GetPublicKeyToken();
                    if (pkt?.Length > 0)
                    {
                        AssemblyName baseKey = assemblyName.AssemblyName.CloneIfPossible();
                        Version version = baseKey.Version;
                        baseKey.Version = null;
                        string key = baseKey.ToString();

                        if (fullNameToReference.TryGetValue(key, out AssemblyNameReference conflictingReference))
                        {
                            conflictingFullNames.Add(key);
                            Version lastVersion = conflictingReference.assemblyName.Version;

                            if (lastVersion == null || lastVersion < version)
                            {
                                fullNameToReference[key] = assemblyNameReference;
                            }
                        }
                        else
                        {
                            fullNameToReference[key] = assemblyNameReference;
                        }
                    }
                }
            }

            // Build the list of conflicted assemblies.
            var assemblyNamesList = new List<AssemblyNameReference>(conflictingFullNames.Count);
            foreach (string versionLessAssemblyName in conflictingFullNames)
            {
                assemblyNamesList.Add(fullNameToReference[versionLessAssemblyName]);
            }

            // Pass over the list of conflicting references and make a binding redirect for each.
            var idealRemappingsList = new List<DependentAssembly>(assemblyNamesList.Count);
            var zeroVersion = new Version(0, 0, 0, 0);

            foreach (AssemblyNameReference assemblyNameReference in assemblyNamesList)
            {
                var remapping = new DependentAssembly
                {
                    PartialAssemblyName = assemblyNameReference.assemblyName.AssemblyName
                };
                var bindingRedirect = new BindingRedirect
                {
                    OldVersionLow = zeroVersion,
                    OldVersionHigh = assemblyNameReference.assemblyName.AssemblyName.Version,
                    NewVersion = assemblyNameReference.assemblyName.AssemblyName.Version
                };
                remapping.BindingRedirects = new List<BindingRedirect> { bindingRedirect };

                idealRemappingsList.Add(remapping);
            }

            idealRemappings = idealRemappingsList;
            conflictingReferences = assemblyNamesList;
        }

        /// <summary>
        /// If a reference is a higher version than what exists in the redist list of the target framework then
        /// this reference needs to be marked as excluded so that it is not allowed to be referenced.
        /// 
        /// If the user needs this reference then they need to set specific version to true.
        /// </summary>
        internal bool MarkReferencesExcludedDueToOtherFramework(AssemblyNameExtension assemblyName, Reference reference)
        {
            bool haveMarkedReference = false;

            // If the reference was not resolved from the GAC or AssemblyFolders then 
            // we do not need to check it if came from another framework
            string resolvedSearchPath = reference.ResolvedSearchPath;
            bool resolvedFromGAC = resolvedSearchPath.Equals(AssemblyResolutionConstants.gacSentinel, StringComparison.OrdinalIgnoreCase);
            bool resolvedFromAssemblyFolders = resolvedSearchPath.Equals(AssemblyResolutionConstants.assemblyFoldersSentinel, StringComparison.OrdinalIgnoreCase);

            if (!resolvedFromGAC && !resolvedFromAssemblyFolders && reference.IsResolved)
            {
                return false;
            }

            // Check against target framework version if projectTargetFramework is null or less than 4.5, also when flag to force check is set to true
            if (_checkAssemblyVersionAgainstTargetFrameworkVersion)
            {
                // Did the assembly name get resolved from a GlobalLocation, GAC or AssemblyFolders and is it in the frameworkList.xml for the 
                // highest version of the currently targeted framework identifier.
                bool inLaterRedistListAndFromGlobalLocation = InLatestRedistList(assemblyName);

                if (inLaterRedistListAndFromGlobalLocation)
                {
                    LogExclusionReason reason = LogAnotherFrameworkUnResolve;
                    reference.ExclusionListLoggingProperties.ExclusionReasonLogDelegate = reason;
                    reference.ExclusionListLoggingProperties.IsInExclusionList = true;
                    haveMarkedReference = true;
                }
            }

            return haveMarkedReference;
        }

        /// <summary>
        /// Is the assembly in the latest framework redist list as either passed into RAR on the lastestFrameworkDirectories property or determined by inspecting the file system.
        /// </summary>
        private bool InLatestRedistList(AssemblyNameExtension assemblyName)
        {
            bool inLaterRedistList = false;

            Tuple<RedistList, string> redistListOtherFramework = GetHighestVersionFullFrameworkForTFM(_targetFrameworkMoniker);

            if (redistListOtherFramework?.Item1 != null && redistListOtherFramework.Item1.FrameworkAssemblyEntryInRedist(assemblyName))
            {
                inLaterRedistList = true;
            }

            return inLaterRedistList;
        }

        /// <summary>
        /// Get the redist list which corresponds to the highest target framework for a given target framework moniker.
        ///
        /// This is done in two ways:
        ///  First, if the latestTargetFrameworkDirectories parameter is passed into RAR those directories will be used to get the redist list
        ///  regardless of the target framework moniker.
        ///
        /// Second, if latest Target Framework Directories is not passed in then we ask the ToollocationHelper for the highest target framework which has
        /// a TargetFrameworkIdentifier which matches the passed in TargetFrameworkMoniker.
        /// </summary>
        private Tuple<RedistList, string> GetHighestVersionFullFrameworkForTFM(FrameworkNameVersioning targetFrameworkMoniker)
        {
            RedistList redistList = null;
            Tuple<RedistList, string> redistListAndOtherFrameworkName = null;
            if (targetFrameworkMoniker != null)
            {
                lock (s_monikerToHighestRedistList)
                {
                    if (!s_monikerToHighestRedistList.TryGetValue(targetFrameworkMoniker.Identifier, out redistListAndOtherFrameworkName))
                    {
                        IList<string> referenceAssemblyDirectories;

                        string otherFrameworkName = null;

                        // The latestTargetFrameworkDirectories can be passed into RAR, if they are then use those directories rather than 
                        // getting a list by looking at the file system.
                        if (_latestTargetFrameworkDirectories?.Length > 0)
                        {
                            referenceAssemblyDirectories = new List<string>(_latestTargetFrameworkDirectories);
                            otherFrameworkName = String.Join(";", _latestTargetFrameworkDirectories);
                        }
                        else
                        {
                            referenceAssemblyDirectories = GetHighestVersionReferenceAssemblyDirectories(targetFrameworkMoniker, out FrameworkName highestFrameworkName);
                            if (highestFrameworkName != null)
                            {
                                otherFrameworkName = highestFrameworkName.FullName;
                            }
                        }

                        if (referenceAssemblyDirectories?.Count > 0)
                        {
                            var seenFrameworkDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            var assemblyTableInfos = new List<AssemblyTableInfo>();
                            foreach (string path in referenceAssemblyDirectories)
                            {
                                string[] listPaths = RedistList.GetRedistListPathsFromDisk(path);
                                foreach (string listPath in listPaths)
                                {
                                    if (!seenFrameworkDirectories.Contains(listPath))
                                    {
                                        assemblyTableInfos.Add(new AssemblyTableInfo(listPath, path));
                                        seenFrameworkDirectories.Add(listPath);
                                    }
                                }
                            }

                            // If the same set of directories was passed in before then the redist list will already be cached.
                            redistList = RedistList.GetRedistList(assemblyTableInfos.ToArray());
                        }

                        redistListAndOtherFrameworkName = new Tuple<RedistList, string>(redistList, otherFrameworkName);
                        s_monikerToHighestRedistList.Add(targetFrameworkMoniker.Identifier, redistListAndOtherFrameworkName);
                    }
                }
            }

            return redistListAndOtherFrameworkName;
        }

        /// <summary>
        /// Based on a target framework moniker, get the set of reference assembly directories which
        /// correspond to the highest version of the target framework identifier property on the target framework moniker.
        /// </summary>
        private static IList<string> GetHighestVersionReferenceAssemblyDirectories(FrameworkNameVersioning targetFrameworkMoniker, out FrameworkNameVersioning highestVersionMoniker)
        {
            IList<string> referenceAssemblyDirectories;
            string targetFrameworkRootDirectory = ToolLocationHelper.GetProgramFilesReferenceAssemblyRoot();

            highestVersionMoniker = ToolLocationHelper.HighestVersionOfTargetFrameworkIdentifier(targetFrameworkRootDirectory, targetFrameworkMoniker.Identifier);
            if (highestVersionMoniker == null)
            {
                referenceAssemblyDirectories = new List<string>();
            }
            else
            {
                referenceAssemblyDirectories = ToolLocationHelper.GetPathToReferenceAssemblies(targetFrameworkRootDirectory, highestVersionMoniker);
            }
            return referenceAssemblyDirectories;
        }

        /// <summary>
        /// Is the assemblyName in the current redist list and does it have a version number which is higher than what is in the current redist list.
        /// This may happen if someone passes in a p2p reference whcih is a framework assembly which is a higher version than what is in the redist list.
        /// </summary>
        internal void MarkReferenceWithHighestVersionInCurrentRedistList(AssemblyNameExtension assemblyName, Reference reference)
        {
            if (_installedAssemblies != null)
            {
                // Find the highest version of the assembly in the current redist list
                AssemblyEntry highestInRedistList = _installedAssemblies.FindHighestVersionInRedistList(assemblyName);

                if (highestInRedistList != null)
                {
                    reference.ExclusionListLoggingProperties.HighestVersionInRedist = highestInRedistList.AssemblyNameExtension.Version;
                }
            }
        }

        /// <summary>
        /// Is the assemblyName in the current redist list and does it have a version number which is higher than what is in the current redist list.
        /// This may happen if someone passes in a p2p reference whcih is a framework assembly which is a higher version than what is in the redist list.
        /// </summary>
        internal bool MarkReferenceForExclusionDueToHigherThanCurrentFramework(AssemblyNameExtension assemblyName, Reference reference)
        {
            // In this method have we marked a reference as needing to be excluded
            bool haveMarkedReference = false;

            // Mark reference as excluded

            // Check against target framework version if projectTargetFramework is null or less than 4.5, also when flag to force check is set to true
            if (_checkAssemblyVersionAgainstTargetFrameworkVersion)
            {
                // Check assemblies versions when target framework version is less than 4.5

                // Make sure the version is higher than the version in the redist. 
                bool higherThanCurrentRedistList = reference.ReferenceVersion != null && reference.ExclusionListLoggingProperties.HighestVersionInRedist != null
                                                   && reference.ReferenceVersion.CompareTo(reference.ExclusionListLoggingProperties.HighestVersionInRedist) > 0;

                if (higherThanCurrentRedistList)
                {
                    reference.ExclusionListLoggingProperties.ExclusionReasonLogDelegate = LogHigherVersionUnresolve;
                    reference.ExclusionListLoggingProperties.IsInExclusionList = true;
                    haveMarkedReference = true;
                }
            }

            return haveMarkedReference;
        }

        /// <summary>
        /// Does the assembly have a targetFrameworkAttribute which has a higher framework version than what the project is currently targeting.
        /// This may happen for example if a p2p is done between two projects with built against different target frameworks.
        /// </summary>
        internal bool MarkReferenceForExclusionDueToHigherThanCurrentFrameworkAttribute(AssemblyNameExtension assemblyName, Reference reference)
        {
            // In this method have we marked a reference as needing to be excluded
            bool haveMarkedReference = false;

            if (!(reference.IsResolved && _fileExists(reference.FullPath)) || reference.IsPrerequisite || (_frameworkPaths != null && Reference.IsFrameworkFile(reference.FullPath, _frameworkPaths)))
            {
                return false;
            }

            // Make sure the version is higher than the version in the redist. 
            // If the identifier are not equal we do not check since we are not trying to catch cross framework incompatibilities.
            bool higherThanCurrentFramework = reference.FrameworkNameAttribute != null
                                              && _targetFrameworkMoniker != null
                                              && String.Equals(reference.FrameworkNameAttribute.Identifier, _targetFrameworkMoniker.Identifier, StringComparison.OrdinalIgnoreCase)
                                              && reference.FrameworkNameAttribute.Version > _targetFrameworkMoniker.Version;

            // Mark reference as excluded
            if (higherThanCurrentFramework)
            {
                reference.ExclusionListLoggingProperties.ExclusionReasonLogDelegate = LogHigherVersionUnresolveDueToAttribute;
                reference.ExclusionListLoggingProperties.IsInExclusionList = true;
                haveMarkedReference = true;
            }

            return haveMarkedReference;
        }

        /// <summary>
        /// Build a table of simple names mapped to assemblyname+reference.
        /// </summary>
        private Dictionary<string, List<AssemblyNameReference>> BuildSimpleNameTable()
        {
            // Build a list of base file names from references.
            // These would conflict with each other if copied to the output directory.
            var baseNameToReferences = new Dictionary<string, List<AssemblyNameReference>>(References.Count, StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<AssemblyNameExtension, Reference> assemblyNameWithReference in References)
            {
                AssemblyNameExtension assemblyName = assemblyNameWithReference.Key;
                Reference reference = assemblyNameWithReference.Value;
                AssemblyNameReference assemblyReference = AssemblyNameReference.Create(assemblyName, reference);

                // Notice that unresolved assemblies are still added to the table.
                // This is because an unresolved assembly may have a different version 
                // which would influence unification. We want to report this to the user.
                string baseName = assemblyName.Name;

                if (!baseNameToReferences.TryGetValue(baseName, out List<AssemblyNameReference> refs))
                {
                    refs = new List<AssemblyNameReference>();
                    baseNameToReferences[baseName] = refs;
                }

                refs.Add(assemblyReference);
            }

            return baseNameToReferences;
        }

        private static void RemoveReferencesWithoutConflicts
        (
            Dictionary<string, List<AssemblyNameReference>> baseNameToReferences
        )
        {
            string[] baseNames = new string[baseNameToReferences.Count];
            baseNameToReferences.Keys.CopyTo(baseNames, 0);

            foreach (string baseName in baseNames)
            {
                if (baseNameToReferences[baseName].Count == 1)
                {
                    baseNameToReferences.Remove(baseName);
                }
            }
        }

        // TODO: Verify correctness of this implementation and extend to more cases.
        // Should be consistent with CompareAssemblyIdentity from Fusion API:
        // The result should be TRUE if one (or more) of the following conditions is true:
        // a) The assembly identities are equivalent. For strongly-named assemblies this means full match on (name, version, pkt, culture); for simply-named assemblies this means a match on (name, culture)
        // b) The assemblies being compared are FX assemblies (even if the version numbers are not the same, these will compare as equivalent by way of unification)
        // c) The assemblies are not FX assemblies but are equivalent because fUnified1 and/or fUnified2 were set.
        // The fUnified flag is used to indicate that all versions up to the version number of the strongly-named assembly are considered equivalent to itself.
        // For example, if assemblyIdentity1 is "foo, version=5.0.0.0, culture=neutral, publicKeyToken=...." and fUnified1==TRUE, then this means to treat all versions of the assembly in the range 0.0.0.0-5.0.0.0 to be equivalent to "foo, version=5.0.0.0, culture=neutral, publicKeyToken=...".
        // If assemblyIdentity2 is the same as assemblyIdentity1, except has a lower version number (e.g.version range 0.0.0.0-5.0.0.0), then the function will return that the identities are equivalent.
        // If assemblyIdentity2 is the same as assemblyIdentity1, but has a greater version number than 5.0.0.0 then the two identities will only be equivalent if fUnified2 is set.
        /// <summary>
        /// Compares two assembly identities to determine whether or not they are equivalent.
        /// </summary>
        /// <param name="assemblyIdentity1"> Textual identity of the first assembly to be compared.</param>
        /// <param name="fUnified1">Flag to indicate user-specified unification for assemblyIdentity1.</param>
        /// <param name="assemblyIdentity2">Textual identity of the second assembly to be compared.</param>
        /// <param name="fUnified2">Flag to indicate user-specified unification for assemblyIdentity2.</param>
        /// <returns>
        /// Boolean indicating whether the identities are equivalent.
        /// </returns>
        private static bool AreAssembliesEquivalent(
            string assemblyIdentity1,
            bool fUnified1,
            string assemblyIdentity2,
            bool fUnified2)
        {
            AssemblyName an1 = new AssemblyName(assemblyIdentity1);
            AssemblyName an2 = new AssemblyName(assemblyIdentity2);

            if (RefMatchesDef(an1, an2))
            {
                return true;
            }

            if (!an1.Name.Equals(an2.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var versionCompare = an1.Version.CompareTo(an2.Version);

            if ((versionCompare < 0 && fUnified2) || (versionCompare > 0 && fUnified1))
            {
                return true;
            }

            if (versionCompare == 0)
            {
                return true;
            }

            return false;
        }

        //  Based on coreclr baseassemblyspec.cpp (https://github.com/dotnet/coreclr/blob/4cf8a6b082d9bb1789facd996d8265d3908757b2/src/vm/baseassemblyspec.cpp#L330)
        private static bool RefMatchesDef(AssemblyName @ref, AssemblyName def)
        {
            if (IsStrongNamed(@ref))
            {
                return IsStrongNamed(def) && CompareRefToDef(@ref, def);
            }
            else
            {
                return @ref.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Based on coreclr baseassemblyspec.inl (https://github.com/dotnet/coreclr/blob/32f0f9721afb584b4a14d69135bea7ddc129f755/src/vm/baseassemblyspec.inl#L679-L683)
        private static bool IsStrongNamed(AssemblyName assembly)
        {
            var refPkt = assembly.GetPublicKeyToken();
            return refPkt != null && refPkt.Length != 0;
        }

        //  Based on https://github.com/dotnet/coreclr/blob/4cf8a6b082d9bb1789facd996d8265d3908757b2/src/vm/baseassemblyspec.cpp#L241
        private static bool CompareRefToDef(AssemblyName @ref, AssemblyName def)
        {
            if (!@ref.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            byte[] rpkt = @ref.GetPublicKeyToken();
            byte[] dpkt = def.GetPublicKeyToken();

            if (rpkt.Length != dpkt.Length)
            {
                return false;
            }

            for (int i = 0; i < rpkt.Length; i++)
            {
                if (rpkt[i] != dpkt[i])
                {
                    return false;
                }
            }

            if (@ref.Version != def.Version)
            {
                return false;
            }

            if (@ref.CultureName != null &&
                @ref.CultureName != def.CultureName)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Given two references along with their fusion names, resolve the filename conflict that they
        /// would have if both assemblies need to be copied to the same directory.
        /// </summary>
        private static int ResolveAssemblyNameConflict(AssemblyNameReference assemblyReference0, AssemblyNameReference assemblyReference1)
        {
            // Extra checks for PInvoke-destined data.
            ErrorUtilities.VerifyThrow(assemblyReference0.assemblyName.FullName != null, "Got a null assembly name fullname. (0)");
            ErrorUtilities.VerifyThrow(assemblyReference1.assemblyName.FullName != null, "Got a null assembly name fullname. (1)");

            Reference leftConflictReference = assemblyReference0.reference;
            Reference rightConflictReference = assemblyReference1.reference;

            // Remove one and provide some information about why.
            var victim = 0;
            ConflictLossReason reason = ConflictLossReason.InsolubleConflict;

            // Pick the one with the highest version number.
            if (leftConflictReference.IsPrimary && !rightConflictReference.IsPrimary)
            {
                // Choose the primary version.
                victim = 1;
                reason = ConflictLossReason.WasNotPrimary;
            }
            else if (!leftConflictReference.IsPrimary && rightConflictReference.IsPrimary)
            {
                // Choose the primary version.
                victim = 0;
                reason = ConflictLossReason.WasNotPrimary;
            }
            else if (!leftConflictReference.IsPrimary && !rightConflictReference.IsPrimary)
            {
                string leftConflictFusionName = assemblyReference0.assemblyName.FullName;
                string rightConflictFusionName = assemblyReference1.assemblyName.FullName;

                //  If both assemblies being compared are primary references, the caller should pass in a zero-flag 
                // (non-unified) for both. (This conforms to the C# assumption that two direct references are meant to be 
                // SxS.)
                bool isNonUnified = leftConflictReference.IsPrimary && rightConflictReference.IsPrimary;
                bool leftConflictLegacyUnified = !isNonUnified && assemblyReference0.reference.IsPrimary;
                bool rightConflictLegacyUnified = !isNonUnified && assemblyReference1.reference.IsPrimary;

                // This is ok here because even if the method says two versions are equivalent the algorithm below will still pick the highest version.
                bool equivalent = AreAssembliesEquivalent
                (
                    leftConflictFusionName,
                    leftConflictLegacyUnified,
                    rightConflictFusionName,
                    rightConflictLegacyUnified
                );

                Version leftConflictVersion = assemblyReference0.assemblyName.Version;
                Version rightConflictVersion = assemblyReference1.assemblyName.Version;

                if
                (
                    // Version comparison only if there are two versions to compare.
                    // Null versions can occur when simply-named assemblies are unresolved.
                    leftConflictVersion != null && rightConflictVersion != null
                    && leftConflictVersion > rightConflictVersion
                )
                {
                    // Choose the higher version
                    victim = 1;
                    if (equivalent)
                    {
                        reason = ConflictLossReason.HadLowerVersion;
                    }
                }
                else if
                (
                    // Version comparison only if there are two versions to compare.
                    // Null versions can occur when simply-named assemblies are unresolved.
                    leftConflictVersion != null && rightConflictVersion != null
                    && leftConflictVersion < rightConflictVersion
                )
                {
                    // Choose the higher version
                    victim = 0;
                    if (equivalent)
                    {
                        reason = ConflictLossReason.HadLowerVersion;
                    }
                }
                else
                {
                    victim = 0;

                    if (equivalent)
                    {
                        // Fusion thinks they're interchangeable.
                        reason = ConflictLossReason.FusionEquivalentWithSameVersion;
                    }
                }
            }

            // Remove the one chosen.
            int victor = 1 - victim;

            AssemblyNameExtension leftAssemblyName = assemblyReference0.assemblyName;
            AssemblyNameExtension rightAssemblyName = assemblyReference1.assemblyName;

            Reference victimReference = victim == 0 ? leftConflictReference : rightConflictReference;
            Reference victorReference = victor == 0 ? leftConflictReference : rightConflictReference;
            AssemblyNameExtension victimAssemblyName = victim == 0 ? leftAssemblyName : rightAssemblyName;
            AssemblyNameExtension victorAssemblyName = victor == 0 ? leftAssemblyName : rightAssemblyName;

            victimReference.ConflictVictorName = victorAssemblyName;
            victimReference.ConflictLossExplanation = reason;
            victorReference.AddConflictVictim(victimAssemblyName);

            return victim;
        }

        /// <summary>
        /// Returns true if an assembly has been removed from the .NET framework
        /// </summary>
        private static bool IsAssemblyRemovedFromDotNetFramework(AssemblyNameExtension assemblyName, string fullPath, string[] frameworkPaths, InstalledAssemblies installedAssemblies)
        {
            if (installedAssemblies != null)
            {
                AssemblyEntry redistListEntry = installedAssemblies.FindHighestVersionInRedistList(assemblyName);
                if (redistListEntry != null)
                {
                    Version redistListVersion = redistListEntry.AssemblyNameExtension.Version;

                    if (redistListVersion != null && assemblyName.Version >= redistListVersion && !Reference.IsFrameworkFile(fullPath, frameworkPaths))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Get unification information for the given assembly name.
        /// </summary>
        /// <param name="assemblyName">The assembly name.</param>
        /// <param name="unifiedVersion">The new version of the assembly to use.</param>
        /// <param name="unificationReason">The reason this reference was unified.</param>
        /// <param name="isPrerequisite">True if this is a prereq assembly.</param>
        /// <param name="isRedistRoot">May be true, false or null. Null means there was no IsRedistRoot in the redist list.</param>
        /// <param name="redistName">Name of the corresponding Resist specified in the redist list.</param>
        /// <returns>True if there was a unification.</returns>
        private bool UnifyAssemblyNameVersions
        (
            AssemblyNameExtension assemblyName,
            out Version unifiedVersion,
            out UnificationReason unificationReason,
            out bool isPrerequisite,
            out bool? isRedistRoot,
            out string redistName
        )
        {
            unifiedVersion = assemblyName.Version;
            isPrerequisite = false;
            isRedistRoot = null;
            redistName = null;
            unificationReason = UnificationReason.DidntUnify;

            // If there's no version, for example in a simple name, then no remapping is possible.
            if (assemblyName.Version == null)
            {
                return false;
            }

            // Try for a remapped assemblies unification.
            if (_remappedAssemblies != null)
            {
                foreach (DependentAssembly remappedAssembly in _remappedAssemblies)
                {
                    AssemblyName comparisonAssembly = remappedAssembly.AssemblyNameReadOnly;

                    if (CompareAssembliesIgnoringVersion(assemblyName.AssemblyName, comparisonAssembly))
                    {
                        foreach (BindingRedirect bindingRedirect in remappedAssembly.BindingRedirects)
                        {
                            if (assemblyName.Version >= bindingRedirect.OldVersionLow && assemblyName.Version <= bindingRedirect.OldVersionHigh)
                            {
                                // If the new version is different than the old version, then there is a unification.
                                if (assemblyName.Version != bindingRedirect.NewVersion)
                                {
                                    unifiedVersion = bindingRedirect.NewVersion;
                                    unificationReason = UnificationReason.BecauseOfBindingRedirect;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            // Try for an installed assemblies unification.
            if (_installedAssemblies != null)
            {
                _installedAssemblies.GetInfo
                (
                    assemblyName,
                    out unifiedVersion,
                    out isPrerequisite,
                    out isRedistRoot,
                    out redistName
                );

                // Was there a unification?
                if (unifiedVersion != assemblyName.Version)
                {
                    unificationReason = UnificationReason.FrameworkRetarget;
                    return assemblyName.Version != unifiedVersion;
                }
            }

            return false;
        }

        /// <summary>
        /// Used to avoid extra allocations from cloning AssemblyNameExtension and AssemblyName
        /// </summary>
        private bool CompareAssembliesIgnoringVersion(AssemblyName a, AssemblyName b)
        {
            ErrorUtilities.VerifyThrowInternalNull(a, nameof(a));
            ErrorUtilities.VerifyThrowInternalNull(b, nameof(b));

            if (a == b)
            {
                return true;
            }

            if (!string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!AssemblyNameExtension.CompareCultures(a, b))
            {
                return false;
            }

            if (!AssemblyNameExtension.ComparePublicKeyTokens(a.GetPublicKeyToken(), b.GetPublicKeyToken()))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Return the resulting reference items, dependencies and other files.
        /// </summary>
        /// <param name="primaryFiles">Primary references fully resolved.</param>
        /// <param name="dependencyFiles">Dependent references fully resolved.</param>
        /// <param name="relatedFiles">Related files like .xmls and .pdbs.</param>
        /// <param name="satelliteFiles">Satellite files.</param>
        /// <param name="serializationAssemblyFiles">Serialization assembly files.</param>
        /// <param name="scatterFiles">Receives the list of associated scatter files.</param>
        /// <param name="copyLocalFiles">All copy-local files out of primaryFiles+dependencyFiles+relatedFiles+satelliteFiles.</param>
        internal void GetReferenceItems
        (
            out ITaskItem[] primaryFiles,
            out ITaskItem[] dependencyFiles,
            out ITaskItem[] relatedFiles,
            out ITaskItem[] satelliteFiles,
            out ITaskItem[] serializationAssemblyFiles,
            out ITaskItem[] scatterFiles,
            out ITaskItem[] copyLocalFiles
        )
        {
            var primaryItems = new List<ITaskItem>();
            var dependencyItems = new List<ITaskItem>();
            var relatedItems = new List<ITaskItem>();
            var satelliteItems = new List<ITaskItem>();
            var serializationAssemblyItems = new List<ITaskItem>();
            var scatterItems = new List<ITaskItem>();
            var copyLocalItems = new List<ITaskItem>();

            foreach (KeyValuePair<AssemblyNameExtension, Reference> kvp in References)
            {
                AssemblyNameExtension assemblyName = kvp.Key;
                Reference reference = kvp.Value;

                // Conflict victims and badimages are filtered out.
                if (!reference.IsBadImage)
                {
                    reference.SetFinalCopyLocalState
                    (
                        assemblyName,
                        _frameworkPaths,
                        _targetProcessorArchitecture,
                        _getRuntimeVersion,
                        _targetedRuntimeVersion,
                        _fileExists,
                        _getAssemblyPathInGac,
                        _copyLocalDependenciesWhenParentReferenceInGac,
                        _doNotCopyLocalIfInGac,
                        this
                    );

                    // If mscorlib was found as a dependency and not a primary reference we will assume that mscorlib on the target machine will be ok to use.
                    // If mscorlib was a primary reference then we may have resolved one which is a differnt version that is on the target
                    // machine and we should gather it along with the other references.
                    if (!reference.IsPrimary && IsPseudoAssembly(assemblyName.Name))
                    {
                        continue;
                    }

                    if (reference.IsResolved)
                    {
                        ITaskItem referenceItem = SetItemMetadata(relatedItems, satelliteItems, serializationAssemblyItems, scatterItems, assemblyName.FullName, reference, assemblyName);

                        if (reference.IsPrimary)
                        {
                            if (!reference.IsBadImage)
                            {
                                // Add a primary item.
                                primaryItems.Add(referenceItem);
                            }
                        }
                        else
                        {
                            // Add the reference item.
                            dependencyItems.Add(referenceItem);
                        }
                    }
                }
            }

            primaryFiles = primaryItems.ToArray();
            dependencyFiles = dependencyItems.ToArray();
            relatedFiles = relatedItems.ToArray();
            satelliteFiles = satelliteItems.ToArray();
            serializationAssemblyFiles = serializationAssemblyItems.ToArray();
            scatterFiles = scatterItems.ToArray();

             // Sort for stable outputs. (These came from a dictionary, which has undefined enumeration order.)
            Array.Sort(primaryFiles, TaskItemSpecFilenameComparer.GenericComparer);

            // Find the copy-local items.
            FindCopyLocalItems(primaryFiles, copyLocalItems);
            FindCopyLocalItems(dependencyFiles, copyLocalItems);
            FindCopyLocalItems(relatedFiles, copyLocalItems);
            FindCopyLocalItems(satelliteFiles, copyLocalItems);
            FindCopyLocalItems(serializationAssemblyFiles, copyLocalItems);
            FindCopyLocalItems(scatterFiles, copyLocalItems);
            copyLocalFiles = copyLocalItems.ToArray();
        }

        /// <summary>
        /// Set metadata on the items which will be output from RAR.
        /// </summary>
        private ITaskItem SetItemMetadata(List<ITaskItem> relatedItems, List<ITaskItem> satelliteItems, List<ITaskItem> serializationAssemblyItems, List<ITaskItem> scatterItems, string fusionName, Reference reference, AssemblyNameExtension assemblyName)
        {
            // Set up the main item.
            TaskItem referenceItem = new TaskItem();
            referenceItem.ItemSpec = reference.FullPath;
            referenceItem.SetMetadata(ItemMetadataNames.resolvedFrom, reference.ResolvedSearchPath);

            // Set the CopyLocal metadata.
            referenceItem.SetMetadata(ItemMetadataNames.copyLocal, reference.IsCopyLocal ? "true" : "false");

            // Set the Redist name metadata.
            if (!String.IsNullOrEmpty(reference.RedistName))
            {
                referenceItem.SetMetadata(ItemMetadataNames.redist, reference.RedistName);
            }

            if (Reference.IsFrameworkFile(reference.FullPath, _frameworkPaths) || (_installedAssemblies?.FrameworkAssemblyEntryInRedist(assemblyName) == true))
            {
                if (!IsAssemblyRemovedFromDotNetFramework(assemblyName, reference.FullPath, _frameworkPaths, _installedAssemblies))
                {
                    referenceItem.SetMetadata(ItemMetadataNames.frameworkFile, "true");
                }
            }

            if (!String.IsNullOrEmpty(reference.ImageRuntime))
            {
                referenceItem.SetMetadata(ItemMetadataNames.imageRuntime, reference.ImageRuntime);
            }

            // The redist root is "null" when there was no IsRedistRoot flag in the Redist XML
            // (or there was no redist XML at all for this item).
            if (reference.IsRedistRoot != null)
            {
                referenceItem.SetMetadata(ItemMetadataNames.isRedistRoot, (bool)reference.IsRedistRoot ? "true" : "false");
            }

            // If there was a primary source item, then forward metadata from it.
            // It's important that the metadata from the primary source item
            // win over the same metadata from other source items, so that's
            // why we put this first.  (CopyMetadataTo will never override an 
            // already existing metadata.)  For example, if this reference actually
            // came directly from an item declared in the project file, we'd
            // want to use the metadata from it, not some other random item in
            // the project file that happened to have this reference as a dependency.
            if (reference.PrimarySourceItem != null)
            {
                reference.PrimarySourceItem.CopyMetadataTo(referenceItem);
            }
            else
            {
                bool hasImplementationFile = referenceItem.GetMetadata(ItemMetadataNames.winmdImplmentationFile).Length > 0;
                bool hasImageRuntime = referenceItem.GetMetadata(ItemMetadataNames.imageRuntime).Length > 0;
                bool hasWinMDFile = referenceItem.GetMetadata(ItemMetadataNames.winMDFile).Length > 0;

                // If there were non-primary source items, then forward metadata from them.
                ICollection<ITaskItem> sourceItems = reference.GetSourceItems();
                foreach (ITaskItem sourceItem in sourceItems)
                {
                    sourceItem.CopyMetadataTo(referenceItem);
                }

                // If the item originally did not have the implementation file metadata then we do not want to get it from the set of primary source items
                // since the implementation file is something specific to the source item and not supposed to be propagated.
                if (!hasImplementationFile)
                {
                    referenceItem.RemoveMetadata(ItemMetadataNames.winmdImplmentationFile);
                }

                // If the item originally did not have the ImageRuntime metadata then we do not want to get it from the set of primary source items
                // since the ImageRuntime is something specific to the source item and not supposed to be propagated.
                if (!hasImageRuntime)
                {
                    referenceItem.RemoveMetadata(ItemMetadataNames.imageRuntime);
                }

                // If the item originally did not have the WinMDFile metadata then we do not want to get it from the set of primary source items
                // since the WinMDFile is something specific to the source item and not supposed to be propigated
                if (!hasWinMDFile)
                {
                    referenceItem.RemoveMetadata(ItemMetadataNames.winMDFile);
                }
            }

            referenceItem.SetMetadata(ItemMetadataNames.version, reference.ReferenceVersion == null ? string.Empty : reference.ReferenceVersion.ToString());

            // Unset fusionName so we don't have to unset it later.
            referenceItem.RemoveMetadata(ItemMetadataNames.fusionName);

            List<string> relatedFileExtensions = reference.GetRelatedFileExtensions();
            List<string> satellites = reference.GetSatelliteFiles();
            List<string> serializationAssemblyFiles = reference.GetSerializationAssemblyFiles();
            string[] scatterFiles = reference.GetScatterFiles();
            Dictionary<string, string> nonForwardableMetadata = null;
            if (relatedFileExtensions.Count > 0 || satellites.Count > 0 || serializationAssemblyFiles.Count > 0 || scatterFiles.Length > 0)
            {
                // Unset non-forwardable metadata now so we don't have to do it for individual items.
                nonForwardableMetadata = RemoveNonForwardableMetadata(referenceItem);
            }

            // Now clone all properties onto the related files.
            foreach (string relatedFileExtension in relatedFileExtensions)
            {
                ITaskItem item = new TaskItem(reference.FullPathWithoutExtension + relatedFileExtension);
                // Clone metadata.
                referenceItem.CopyMetadataTo(item);

                // Add the related item.
                relatedItems.Add(item);
            }

            // Set up the satellites.
            foreach (string satelliteFile in satellites)
            {
                ITaskItem item = new TaskItem(Path.Combine(reference.DirectoryName, satelliteFile));
                // Clone metadata.
                referenceItem.CopyMetadataTo(item);
                // Set the destination directory.
                item.SetMetadata(ItemMetadataNames.destinationSubDirectory, FileUtilities.EnsureTrailingSlash(Path.GetDirectoryName(satelliteFile)));

                // Add the satellite item.
                satelliteItems.Add(item);
            }

            // Set up the serialization assemblies
            foreach (string serializationAssemblyFile in serializationAssemblyFiles)
            {
                ITaskItem item = new TaskItem(Path.Combine(reference.DirectoryName, serializationAssemblyFile));
                // Clone metadata.
                referenceItem.CopyMetadataTo(item);

                // Add the serialization assembly item.
                serializationAssemblyItems.Add(item);
            }

            // Set up the scatter files.
            foreach (string scatterFile in scatterFiles)
            {
                ITaskItem item = new TaskItem(Path.Combine(reference.DirectoryName, scatterFile));
                // Clone metadata.
                referenceItem.CopyMetadataTo(item);

                // Add the satellite item.
                scatterItems.Add(item);
            }

            // As long as the item has not come from somewhere else say it came from rar (p2p's can come from somewhere else).
            if (referenceItem.GetMetadata(ItemMetadataNames.msbuildReferenceSourceTarget).Length == 0)
            {
                referenceItem.SetMetadata(ItemMetadataNames.msbuildReferenceSourceTarget, "ResolveAssemblyReference");
            }

            if (referenceItem.GetMetadata(ItemMetadataNames.msbuildReferenceSourceTarget).Equals("ProjectReference"))
            {
                if (reference.PrimarySourceItem != null)
                {
                    referenceItem.SetMetadata(ItemMetadataNames.projectReferenceOriginalItemSpec, reference.PrimarySourceItem.GetMetadata("OriginalItemSpec"));
                }
            }

            if (reference.IsWinMDFile)
            {
                // The ImplementationAssembly is only set if the implementation file exits on disk
                if (reference.ImplementationAssembly != null)
                {
                    if (VerifyArchitectureOfImplementationDll(reference.ImplementationAssembly, reference.FullPath))
                    {
                        // Add the implementation item as a related file
                        ITaskItem item = new TaskItem(reference.ImplementationAssembly);
                        // Clone metadata.
                        referenceItem.CopyMetadataTo(item);

                        // Add the related item.
                        relatedItems.Add(item);

                        referenceItem.SetMetadata(ItemMetadataNames.winmdImplmentationFile, Path.GetFileName(reference.ImplementationAssembly));
                        // This may have been set previously (before it was removed so we could more efficiently set metadata on the various related files).
                        // This version should take priority, so we remove it from nonForwardableMetadata if it's there to prevent the correct value from
                        // being overwritten.
                        nonForwardableMetadata?.Remove(ItemMetadataNames.winmdImplmentationFile);
                    }
                }

                // This may have been set previously (before it was removed so we could more efficiently set metadata on the various related files).
                // This version should take priority, so we remove it from nonForwardableMetadata if it's there to prevent the correct value from
                // being overwritten.
                nonForwardableMetadata?.Remove(ItemMetadataNames.winMDFileType);
                if (reference.IsManagedWinMDFile)
                {
                    referenceItem.SetMetadata(ItemMetadataNames.winMDFileType, "Managed");
                }
                else
                {
                    referenceItem.SetMetadata(ItemMetadataNames.winMDFileType, "Native");
                }

                // This may have been set previously (before it was removed so we could more efficiently set metadata on the various related files).
                // This version should take priority, so we remove it from nonForwardableMetadata if it's there to prevent the correct value from
                // being overwritten.
                nonForwardableMetadata?.Remove(ItemMetadataNames.winMDFile);
                referenceItem.SetMetadata(ItemMetadataNames.winMDFile, "true");
            }

            // Set the FusionName late, so we don't copy it to the derived items, but it's still available on referenceItem.
            referenceItem.SetMetadata(ItemMetadataNames.fusionName, fusionName);

            // nonForwardableMetadata should be null here if relatedFileExtensions, satellites, serializationAssemblyFiles, and scatterFiles were all empty.
            if (nonForwardableMetadata != null)
            {
                foreach (KeyValuePair<string, string> kvp in nonForwardableMetadata)
                {
                    referenceItem.SetMetadata(kvp.Key, kvp.Value);
                }
            }

            return referenceItem;
        }

        /// <summary>
        /// Verify that the implementation dll has a matching architecture to what the project is targeting.
        /// </summary>
        private bool VerifyArchitectureOfImplementationDll(string dllPath, string winmdFile)
        {
            try
            {
                UInt16 machineType = _readMachineTypeFromPEHeader(dllPath);
                SystemProcessorArchitecture dllArchitecture = SystemProcessorArchitecture.None;

                if (machineType == NativeMethods.IMAGE_FILE_MACHINE_INVALID)
                {
                    throw new BadImageFormatException(ResourceUtilities.GetResourceString("ResolveAssemblyReference.ImplementationDllHasInvalidPEHeader"));
                }

                switch (machineType)
                {
                    case NativeMethods.IMAGE_FILE_MACHINE_AMD64:
                        dllArchitecture = SystemProcessorArchitecture.Amd64;
                        break;
                    case NativeMethods.IMAGE_FILE_MACHINE_ARM:
                    case NativeMethods.IMAGE_FILE_MACHINE_ARMV7:
                        dllArchitecture = SystemProcessorArchitecture.Arm;
                        break;
                    case NativeMethods.IMAGE_FILE_MACHINE_ARM64:
                        dllArchitecture = (SystemProcessorArchitecture) 6; // There's no entry for ARM64 in SystemProcessorArchitecture, use the next available constant
                        break;
                    case NativeMethods.IMAGE_FILE_MACHINE_I386:
                        dllArchitecture = SystemProcessorArchitecture.X86;
                        break;
                    case NativeMethods.IMAGE_FILE_MACHINE_IA64:
                        dllArchitecture = SystemProcessorArchitecture.IA64;
                        break;
                    case NativeMethods.IMAGE_FILE_MACHINE_UNKNOWN:
                        dllArchitecture = SystemProcessorArchitecture.None;
                        break;
                    default:
                        if (_warnOrErrorOnTargetArchitectureMismatch == WarnOrErrorOnTargetArchitectureMismatchBehavior.Error)
                        {
                            _log.LogErrorWithCodeFromResources("ResolveAssemblyReference.UnknownProcessorArchitecture", dllPath, winmdFile, machineType.ToString("X", CultureInfo.InvariantCulture));
                            return false;
                        }
                        else if (_warnOrErrorOnTargetArchitectureMismatch == WarnOrErrorOnTargetArchitectureMismatchBehavior.Warning)
                        {
                            _log.LogWarningWithCodeFromResources("ResolveAssemblyReference.UnknownProcessorArchitecture", dllPath, winmdFile, machineType.ToString("X", CultureInfo.InvariantCulture));
                            return true;
                        }
                        break;
                }

                // If the assembly is MSIL or none it can work anywhere so there does not need to be any warning ect.
                if (dllArchitecture == SystemProcessorArchitecture.MSIL || dllArchitecture == SystemProcessorArchitecture.None)
                {
                    return true;
                }

                if (_targetProcessorArchitecture != dllArchitecture)
                {
                    if (_warnOrErrorOnTargetArchitectureMismatch == WarnOrErrorOnTargetArchitectureMismatchBehavior.Error)
                    {
                        _log.LogErrorWithCodeFromResources("ResolveAssemblyReference.MismatchBetweenTargetedAndReferencedArchOfImplementation", ResolveAssemblyReference.ProcessorArchitectureToString(_targetProcessorArchitecture), ResolveAssemblyReference.ProcessorArchitectureToString(dllArchitecture), dllPath, winmdFile);
                        return false;
                    }
                    else if (_warnOrErrorOnTargetArchitectureMismatch == WarnOrErrorOnTargetArchitectureMismatchBehavior.Warning)
                    {
                        _log.LogWarningWithCodeFromResources("ResolveAssemblyReference.MismatchBetweenTargetedAndReferencedArchOfImplementation", ResolveAssemblyReference.ProcessorArchitectureToString(_targetProcessorArchitecture), ResolveAssemblyReference.ProcessorArchitectureToString(dllArchitecture), dllPath, winmdFile);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                if (ExceptionHandling.IsCriticalException(e))
                {
                    throw;
                }

                _log.LogErrorWithCodeFromResources("ResolveAssemblyReference.ProblemReadingImplementationDll", dllPath, e.Message);
                return false;
            }
        }

        /// <summary>
        /// Read the PE header to get the machine type
        /// </summary>
        internal static UInt16 ReadMachineTypeFromPEHeader(string dllPath)
        {
            /*
             At location 0x3c, the stub has the file offset to the PE signature. This information enables Windows to properly execute the image file, even though it has an MS DOS stub. This file offset is placed at location 0x3c during linking.
            * After the MS DOS stub, at the file offset specified at offset 0x3c, is a 4-byte signature that identifies the file as a PE format image file. This signature is "PE\0\0" (the letters "P" and "E" followed by two null bytes).
            * At the beginning of an object file, or immediately after the signature of an image file, is a standard COFF file header in the following format. Note that the Windows loader limits the number of sections to 96.
            Offset
                    Size	Field	Description
                    0	2	Machine	The number that identifies the type of target machine. For more information, see section 3.3.1, "Machine Types."

                    IMAGE_FILE_MACHINE_UNKNOWN	0x0	The contents of this field are assumed to be applicable to any machine type
                    IMAGE_FILE_MACHINE_AMD64	0x8664	x64
                    IMAGE_FILE_MACHINE_ARM	0x1c0	ARM little endian
                    IMAGE_FILE_MACHINE_I386	0x14c	Intel 386 or later processors and compatible processors
                    IMAGE_FILE_MACHINE_IA64	0x200	Intel Itanium processor family
            * */

            UInt16 machineType = NativeMethods.IMAGE_FILE_MACHINE_INVALID;
            using (FileStream implementationStream = new FileStream(dllPath, FileMode.Open, FileAccess.Read))
            {
                // Seek to location that contains PE offset.
                implementationStream.Seek(PEOFFSET, SeekOrigin.Begin);

                using (BinaryReader reader = new BinaryReader(implementationStream))
                {
                    // Read the offset to the PE header
                    Int32 offSet = reader.ReadInt32();
                    implementationStream.Seek(offSet, SeekOrigin.Begin);

                    // Read the PE header should be PE\0\0
                    UInt32 peHeader = reader.ReadUInt32();
                    if (peHeader == PEHEADER)
                    {
                        machineType = reader.ReadUInt16();
                    }
                }
            }

            return machineType;
        }

        /// <summary>
        /// Some metadata should not be forwarded between the parent and child items.
        /// </summary>
        /// <returns>The metadata that were removed.</returns>
        private static Dictionary<string, string> RemoveNonForwardableMetadata(ITaskItem item)
        {
            Dictionary<string, string> removedMetadata = new Dictionary<string, string>();
            RemoveMetadatum(ItemMetadataNames.winmdImplmentationFile, item, removedMetadata);
            RemoveMetadatum(ItemMetadataNames.imageRuntime, item, removedMetadata);
            RemoveMetadatum(ItemMetadataNames.winMDFile, item, removedMetadata);
            if (!Traits.Instance.EscapeHatches.TargetPathForRelatedFiles)
            {
                RemoveMetadatum(ItemMetadataNames.targetPath, item, removedMetadata);
            }
            return removedMetadata;
        }

        private static void RemoveMetadatum(string key, ITaskItem item, Dictionary<string, string> removedMetadata)
        {
            string meta = item.GetMetadata(key);
            if (!String.IsNullOrEmpty(meta))
            {
                removedMetadata.Add(key, meta);
            }
            item.RemoveMetadata(key);
        }

        /// <summary>
        /// Given a list of items, find all that have CopyLocal==true and add it to the list.
        /// </summary>
        private static void FindCopyLocalItems(ITaskItem[] items, List<ITaskItem> copyLocalItems)
        {
            foreach (ITaskItem i in items)
            {
                bool copyLocal = MetadataConversionUtilities.TryConvertItemMetadataToBool
                    (
                        i,
                        ItemMetadataNames.copyLocal,
                        out bool found
                    );

                if (found && copyLocal)
                {
                    copyLocalItems.Add(i);
                }
            }
        }

        #region ExclusionList LoggingMessage helpers

        /// <summary>
        /// The reference was determined to have a version which is higher than what is in the currently targeted redist list.
        /// </summary>
        internal void LogHigherVersionUnresolve(bool displayPrimaryReferenceMessage, AssemblyNameExtension assemblyName, Reference reference, ITaskItem referenceItem, string targetedFramework)
        {
            if (displayPrimaryReferenceMessage)
            {
                _log.LogWarningWithCodeFromResources("ResolveAssemblyReference.PrimaryReferenceOutsideOfFramework", reference.PrimarySourceItem.ItemSpec /* primary item spec*/, reference.ReferenceVersion /*Version of dependent assemby*/, reference.ExclusionListLoggingProperties.HighestVersionInRedist /*Version found in redist*/);
            }
            else
            {
                _log.LogWarningWithCodeFromResources("ResolveAssemblyReference.DependencyReferenceOutsideOfFramework", referenceItem.ItemSpec /* primary item spec*/, assemblyName.FullName /*Dependent assemblyName*/, reference.ReferenceVersion /*Version of dependent assemby*/, reference.ExclusionListLoggingProperties.HighestVersionInRedist /*Version found in redist*/);
            }
        }

        /// <summary>
        /// The reference was determined to have a version which is higher than what is in the currently targeted using the framework attribute.
        /// </summary>
        internal void LogHigherVersionUnresolveDueToAttribute(bool displayPrimaryReferenceMessage, AssemblyNameExtension assemblyName, Reference reference, ITaskItem referenceItem, string targetedFramework)
        {
            if (displayPrimaryReferenceMessage)
            {
                _log.LogWarningWithCodeFromResources("ResolveAssemblyReference.PrimaryReferenceOutsideOfFrameworkUsingAttribute", reference.PrimarySourceItem.ItemSpec /* primary item spec*/, reference.FrameworkNameAttribute /*Version of dependent assemby*/, targetedFramework);
            }
            else
            {
                _log.LogWarningWithCodeFromResources("ResolveAssemblyReference.DependencyReferenceOutsideOfFrameworkUsingAttribute", referenceItem.ItemSpec /* primary item spec*/, assemblyName.FullName /*Dependent assemblyName*/, reference.FrameworkNameAttribute, targetedFramework);
            }
        }

        /// <summary>
        /// The reference was determined to not be in the current redist list but in fact are from another framework.
        /// </summary>
        internal void LogAnotherFrameworkUnResolve(bool displayPrimaryReferenceMessage, AssemblyNameExtension assemblyName, Reference reference, ITaskItem referenceItem, string targetedFramework)
        {
            if (displayPrimaryReferenceMessage)
            {
                _log.LogWarningWithCodeFromResources("ResolveAssemblyReference.PrimaryReferenceInAnotherFramework", reference.PrimarySourceItem.ItemSpec /* primary item spec*/, targetedFramework);
            }
            else
            {
                _log.LogWarningWithCodeFromResources("ResolveAssemblyReference.DependencyReferenceInAnotherFramework", referenceItem.ItemSpec /* primary item spec*/, assemblyName.FullName /*Dependent assemblyName*/, targetedFramework);
            }
        }

        /// <summary>
        /// The reference was found to be resolved from a full framework while we are actually targeting a profile.
        /// </summary>
        internal void LogProfileExclusionUnresolve(bool displayPrimaryReferenceMessage, AssemblyNameExtension assemblyName, Reference reference, ITaskItem referenceItem, string targetedFramework)
        {
            if (displayPrimaryReferenceMessage)
            {
                _log.LogWarningWithCodeFromResources("ResolveAssemblyReference.FailedToResolveReferenceBecausePrimaryAssemblyInExclusionList", reference.PrimarySourceItem.ItemSpec, targetedFramework);
            }
            else
            {
                _log.LogWarningWithCodeFromResources("ResolveAssemblyReference.FailBecauseDependentAssemblyInExclusionList", referenceItem.ItemSpec, assemblyName.FullName, targetedFramework);
            }
        }
        #endregion

        #region Helper structures

        /// <summary>
        ///  Provide a class which has a key value pair for references and their assemblyNameExtensions.
        ///  This is used to prevent JIT'ing when using a generic list.
        /// </summary>
        internal struct ReferenceAssemblyExtensionPair
        {
            internal ReferenceAssemblyExtensionPair(Reference key, AssemblyNameExtension value)
            {
                Key = key;
                Value = value;
            }

            internal Reference Key { get; }

            internal AssemblyNameExtension Value { get; }
        }

        #endregion

        /// <summary>
        /// Rather than have exclusion lists float around, we may as well just mark the reference themselves. This allows us to attach to a reference
        /// whether or not it is excluded and why.  This method will do a number of checks in a specific order and mark the reference as being excluded or not.
        /// </summary>
        internal bool MarkReferencesForExclusion(Dictionary<string, string> exclusionList)
        {
            bool anyMarkedReference = false;
            ListOfExcludedAssemblies = new List<string>();

            foreach (AssemblyNameExtension assemblyName in References.Keys)
            {
                string assemblyFullName = assemblyName.FullName;
                Reference reference = GetReference(assemblyName);
                reference.ReferenceVersion = assemblyName.Version;

                MarkReferenceWithHighestVersionInCurrentRedistList(assemblyName, reference);

                // If CheckForSpecificVersionMetadataOnParentsReference is passed true then we will return true if any parent primary reference has the specific 
                // version metadata set to true, 
                // If false is passed in we will return true ONLY if all parent primary references have the metadata set to true.
                if (!reference.CheckForSpecificVersionMetadataOnParentsReference(false))
                {
                    // Check to see if the reference is not in a profile or subset
                    if (exclusionList?.ContainsKey(assemblyFullName) == true)
                    {
                        anyMarkedReference = true;
                        reference.ExclusionListLoggingProperties.ExclusionReasonLogDelegate = LogProfileExclusionUnresolve;
                        reference.ExclusionListLoggingProperties.IsInExclusionList = true;
                        ListOfExcludedAssemblies.Add(assemblyFullName);
                    }

                    // Check to see if the reference is in the current target framework but has a higher version than what exists in the target framework
                    if (!reference.ExclusionListLoggingProperties.IsInExclusionList)
                    {
                        if (MarkReferenceForExclusionDueToHigherThanCurrentFramework(assemblyName, reference))
                        {
                            anyMarkedReference = true;
                            ListOfExcludedAssemblies.Add(assemblyFullName);
                        }
                    }

                    // Check to see if the reference came from the GAC or AssemblyFolders and is in the highest redist list on the machine for the targeted framework identifier.
                    if (!reference.ExclusionListLoggingProperties.IsInExclusionList)
                    {
                        if (MarkReferencesExcludedDueToOtherFramework(assemblyName, reference))
                        {
                            anyMarkedReference = true;
                            ListOfExcludedAssemblies.Add(assemblyFullName);
                        }
                    }

                    // Check to see if the reference is built against a compatible framework
                    if (!reference.ExclusionListLoggingProperties.IsInExclusionList)
                    {
                        if (!_ignoreFrameworkAttributeVersionMismatch && MarkReferenceForExclusionDueToHigherThanCurrentFrameworkAttribute(assemblyName, reference))
                        {
                            anyMarkedReference = true;
                            ListOfExcludedAssemblies.Add(assemblyFullName);
                        }
                    }
                }
            }

            return anyMarkedReference;
        }
    }
}
