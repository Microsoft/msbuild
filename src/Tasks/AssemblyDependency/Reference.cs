// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Build.Shared;
using Microsoft.Build.Framework;

namespace Microsoft.Build.Tasks
{
    /// <summary>
    /// A reference to an assembly along with information about resolution.
    /// </summary>
    sealed internal class Reference
    {
        /// <summary>
        /// dictionary where ITaskItem.ItemSpec (a string) is the key and ITaskItem is the value.
        /// A hash table is used to remove duplicates.
        /// All source items that inspired this reference (possibly indirectly through a dependency chain).
        /// </summary>
        private Dictionary<string, ITaskItem> _sourceItems = new Dictionary<string, ITaskItem>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A list of unique dependencies.
        /// </summary>
        private HashSet<Reference> _dependees = new HashSet<Reference>();

        /// <summary>
        /// Hashset of Reference which depend on this reference
        /// A list of unique dependencies.
        /// </summary>
        private HashSet<Reference> _dependencies = new HashSet<Reference>();

        /// <summary>
        /// Scatter files associated with this reference.
        /// </summary>
        private string[] _scatterFiles = Array.Empty<string>();

        /// <summary>
        /// Any errors that occurred while resolving or finding dependencies on this item.
        /// </summary>
        private List<Exception> _errors = new List<Exception>();

        /// <summary>
        /// Contains any file extension that are related to this file. Pdbs and xmls are related.
        /// This is an extension string starting with "."
        /// </summary>
        private List<string> _relatedFileExtensions = new List<string>();

        /// <summary>
        /// Contains satellite files for this reference.
        /// This file path is relative to the location of the reference.
        /// </summary>
        private List<string> _satelliteFiles = new List<string>();

        /// <summary>
        /// Contains serialization assembly files for this reference.
        /// This file path is relative to the location of the reference.
        /// </summary>
        private List<string> _serializationAssemblyFiles = new List<string>();

        /// <summary>
        /// AssemblyNames of references that lost collision conflicts with this reference.
        /// </summary>
        private List<AssemblyNameExtension> _conflictVictims = new List<AssemblyNameExtension>();

        /// <summary>
        /// These are the versions (type UnificationVersion) that were unified from.
        /// </summary>
        private Dictionary<string, UnificationVersion> _preUnificationVersions = new Dictionary<string, UnificationVersion>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The original source item, as passed into the task that is directly associated
        /// with this reference.  This only applies to "primary" references.
        /// </summary>
        private ITaskItem _primarySourceItem;

        /// <summary>
        /// The full path to the assembly. If this is "", then that means that this reference
        /// has not been resolved.
        /// </summary>
        private string _fullPath = String.Empty;

        /// <summary>
        /// The directory that this reference lives in.
        /// </summary>
        private string _directoryName = String.Empty;

        /// <summary>
        /// The reference's filename without extension.
        /// </summary>
        private string _fileNameWithoutExtension = String.Empty;

        /// <summary>
        /// The full path to the file name but without the extension.
        /// </summary>
        private string _fullPathWithoutExtension = String.Empty;

        /// <summary>
        /// The list of expected extensions.
        /// </summary>
        private List<string> _expectedExtensions;

        /// <summary>
        ///  Is the file a managed winmd file. That means it has both windows runtime and CLR in the imageruntime string.
        /// </summary>
        private bool _isManagedWinMDFile;

        /// <summary>
        /// The imageruntime version for this reference. 
        /// </summary>
        private string _imageRuntimeVersion;

        /// <summary>
        /// Set containing the names the reference was remapped from
        /// </summary>
        private HashSet<AssemblyRemapping> _remappedAssemblyNames = new HashSet<AssemblyRemapping>();

        /// <summary>
        /// Delegate to determine if the file is a winmd file or not
        /// </summary>
        private IsWinMDFile _isWinMDFile;

        /// <summary>
        /// Delegate to check to see if the file exists on disk
        /// </summary>
        private FileExists _fileExists;

        /// <summary>
        /// Delegate to get the imageruntime version from a file.
        /// </summary>
        private GetAssemblyRuntimeVersion _getRuntimeVersion;

        internal Reference(IsWinMDFile isWinMDFile, FileExists fileExists, GetAssemblyRuntimeVersion getRuntimeVersion)
        {
            _isWinMDFile = isWinMDFile;
            _fileExists = fileExists;
            _getRuntimeVersion = getRuntimeVersion;
        }

        /// <summary>
        /// Add items that caused (possibly indirectly through a dependency chain) this Reference.
        /// </summary>
        internal void AddSourceItem(ITaskItem sourceItem)
        {
            string itemSpec = sourceItem.ItemSpec;
            if (!_sourceItems.ContainsKey(itemSpec))
            {
                _sourceItems[itemSpec] = sourceItem;
                PropagateSourceItems(sourceItem);
            }
        }

        /// <summary>
        /// Add items that caused (possibly indirectly through a dependency chain) this Reference.
        /// </summary>
        internal void AddSourceItems(IEnumerable<ITaskItem> sourceItemsToAdd)
        {
            foreach (ITaskItem sourceItem in sourceItemsToAdd)
            {
                AddSourceItem(sourceItem);
            }
        }

        /// <summary>
        /// We have had our source item list updated, we need to propagate this change to any of our dependencies so they have the new information.
        /// </summary>
        internal void PropagateSourceItems(ITaskItem sourceItem)
        {
            if (_dependencies != null)
            {
                foreach (Reference dependency in _dependencies)
                {
                    dependency.AddSourceItem(sourceItem);
                }
            }
        }

        /// <summary>
        /// Get the source items for this reference.
        ///  This is collection of ITaskItems.
        /// </summary>
        internal ICollection<ITaskItem> GetSourceItems()
        {
            return _sourceItems.Values;
        }

        /// <summary>
        /// Add a reference which this reference depends on
        /// </summary>
        internal void AddDependency(Reference dependency)
        {
            if (!_dependencies.Contains(dependency))
            {
                _dependencies.Add(dependency);
            }
        }

        /// <summary>
        /// Add a reference that caused (possibly indirectly through a dependency chain) this Reference.
        /// </summary>
        internal void AddDependee(Reference dependee)
        {
            Debug.Assert(dependee.FullPath.Length > 0, "Cannot add dependee that doesn't have a full name. This should have already been resolved.");

            dependee.AddDependency(this);

            if (!_dependees.Contains(dependee))
            {
                _dependees.Add(dependee);

                // When a new dependee is added, this is a new place where a reference might be resolved.
                // Reset this item so it will be re-resolved if possible.
                if (IsUnresolvable)
                {
                    _errors = new List<Exception>();
                    AssembliesConsideredAndRejected = new List<ResolutionSearchLocation>();
                }
            }
        }

        /// <summary>
        /// A dependee may be removed because it or its dependee's are in the black list
        /// </summary>
        internal void RemoveDependee(Reference dependeeToRemove)
        {
            _dependees.Remove(dependeeToRemove);
        }

        /// <summary>
        /// A dependency may be removed because it may not be referenced any more due this reference being in the black list or being removed due to it depending on something in the black list
        /// </summary>
        internal void RemoveDependency(Reference dependencyToRemove)
        {
            _dependencies.Remove(dependencyToRemove);
        }

        /// <summary>
        /// Get the dependee references for this reference.
        ///  This is collection of References.
        /// </summary>
        internal HashSet<Reference> GetDependees()
        {
            return _dependees;
        }

        /// <summary>
        /// Scatter files associated with this assembly.
        /// </summary>
        /// <value></value>
        internal void AttachScatterFiles(string[] scatterFilesToAttach)
        {
            if (scatterFilesToAttach == null || scatterFilesToAttach.Length == 0)
            {
                _scatterFiles = Array.Empty<string>();
            }
            else
            {
                _scatterFiles = scatterFilesToAttach;
            }
        }

        /// <summary>
        /// Scatter files associated with this assembly.
        /// </summary>
        /// <returns></returns>
        internal string[] GetScatterFiles()
        {
            return _scatterFiles;
        }

        /// <summary>
        /// Set one expected extension for this reference.
        /// </summary>
        internal void SetExecutableExtension(string extension)
        {
            if (_expectedExtensions == null)
            {
                _expectedExtensions = new List<string>();
            }
            else
            {
                _expectedExtensions.Clear();
            }
            if (extension.Length > 0 && extension[0] != '.')
            {
                extension = '.' + extension;
            }
            _expectedExtensions.Add(extension);
        }

        /// <summary>
        /// Get the list of expected extensions.
        /// </summary>
        internal string[] GetExecutableExtensions(string[] allowedAssemblyExtensions)
        {
            if (_expectedExtensions == null)
            {
                // Use the default.
                return allowedAssemblyExtensions;
            }
            return _expectedExtensions.ToArray();
        }

        /// <summary>
        /// Whether the name needs to match exactly or just the simple name part needs to match.
        /// </summary>
        /// <value></value>
        internal bool WantSpecificVersion { get; private set; } = true;

        /// <summary>
        /// Whether types need to be embedded into the target assembly
        /// </summary>
        /// <value></value>
        internal bool EmbedInteropTypes { get; set; } = false;

        /// <summary>
        /// This will be true if the user requested a specific file. We know this when the file was resolved
        /// by hintpath or if it was resolve as a raw file name for example.
        /// </summary>
        internal bool UserRequestedSpecificFile { get; set; } = false;

        /// <summary>
        /// The version number of this reference
        /// </summary>
        internal Version ReferenceVersion { get; set; } = null;

        /// <summary>
        /// True if the assembly was found to be in the GAC.
        /// </summary>
        internal bool? FoundInGac
        {
            get;
            private set;
        }

        /// <summary>
        /// True if the assembly was resolved through the GAC. Otherwise, false.
        /// </summary>
        internal bool ResolvedFromGac
        {
            get
            {
                return string.Equals(ResolvedSearchPath, AssemblyResolutionConstants.gacSentinel, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Set of properties for this reference used to log why this reference could not be resolved.
        /// </summary>
        internal ExclusionListProperties ExclusionListLoggingProperties { get; } = new ExclusionListProperties();

        /// <summary>
        /// Determines if a given reference or its parent primary references have specific version metadata set to true.
        /// If anyParentHasMetadata is set to true then we will return true if any parent primary reference has the specific version metadata set to true, 
        /// if the value is false we will return true ONLY if all parent primary references have the metadata set to true.
        /// </summary>
        internal bool CheckForSpecificVersionMetadataOnParentsReference(bool anyParentHasMetadata)
        {
            bool hasSpecificVersionMetadata = false;

            // We are our own parent, therefore the specific version metadata is what ever is passed into as wantspecificVersion for this reference.
            // this saves us from having to read the metadata from our item again.
            if (IsPrimary)
            {
                hasSpecificVersionMetadata = WantSpecificVersion;
            }
            else
            {
                // Go through all of the primary items which lead to this dependency, if they all have specificVersion set to true then 
                // hasSpecificVersionMetadata will be true. If any item has the metadata set to false or not set then the value will be false.
                foreach (ITaskItem item in GetSourceItems())
                {
                    hasSpecificVersionMetadata = MetadataConversionUtilities.TryConvertItemMetadataToBool(item, ItemMetadataNames.specificVersion);

                    // Break if one of the primary references has specific version false or not set
                    if (anyParentHasMetadata == hasSpecificVersionMetadata)
                    {
                        break;
                    }
                }
            }

            return hasSpecificVersionMetadata;
        }

        /// <summary>
        /// Add a dependency or resolution error to this reference's list of errors.
        /// </summary>
        /// <param name="e">The error.</param>
        internal void AddError(Exception e)
        {
            if (e is BadImageReferenceException)
            {
                IsBadImage = true;
            }
            _errors.Add(e);
        }

        /// <summary>
        /// Return the list of dependency or resolution errors for this item.
        /// </summary>
        /// <returns>The collection of resolution errors.</returns>
        internal List<Exception> GetErrors()
        {
            return _errors;
        }

        /// <summary>
        /// Add a new related file to this reference.
        /// Related files always live in the same directory as the reference.
        /// Examples include, MyAssembly.pdb and MyAssembly.xml
        /// </summary>
        /// <param name="filenameExtension">This is the filename extension.</param>
        internal void AddRelatedFileExtension(string filenameExtension)
        {
#if DEBUG
            Debug.Assert(filenameExtension[0]=='.', "Expected extension to start with '.'");
#endif
            _relatedFileExtensions.Add(filenameExtension);
        }

        /// <summary>
        /// Return the list of related files for this item.
        /// </summary>
        /// <returns>The collection of related file extensions.</returns>
        internal List<string> GetRelatedFileExtensions()
        {
            return _relatedFileExtensions;
        }

        /// <summary>
        /// Add a new satellite file
        /// </summary>
        /// <param name="filename">This is the filename relative the this reference.</param>
        internal void AddSatelliteFile(string filename)
        {
#if DEBUG
            Debug.Assert(!Path.IsPathRooted(filename), "Satellite path should be relative to the current reference.");
#endif
            _satelliteFiles.Add(filename);
        }

        /// <summary>
        /// Add a new serialization assembly file.
        /// </summary>
        /// <param name="filename">This is the filename relative the this reference.</param>
        internal void AddSerializationAssemblyFile(string filename)
        {
#if DEBUG
            Debug.Assert(!Path.IsPathRooted(filename), "Serialization assembly path should be relative to the current reference.");
#endif
            _serializationAssemblyFiles.Add(filename);
        }

        /// <summary>
        /// Return the list of satellite files for this item.
        /// </summary>
        /// <returns>The collection of satellit files.</returns>
        internal List<string> GetSatelliteFiles()
        {
            return _satelliteFiles;
        }

        /// <summary>
        /// Return the list of serialization assembly files for this item.
        /// </summary>
        /// <returns>The collection of serialization assembly files.</returns>
        internal List<string> GetSerializationAssemblyFiles()
        {
            return _serializationAssemblyFiles;
        }

        /// <summary>
        /// The full path to the assembly. If this is "", then that means that this reference
        /// has not been resolved.
        /// </summary>
        /// <value>The full path to this assembly.</value>
        internal string FullPath
        {
            get { return _fullPath; }
            set
            {
                if (_fullPath != value)
                {
                    _fullPath = value;
                    _fullPathWithoutExtension = null;
                    _fileNameWithoutExtension = null;
                    _directoryName = null;

                    if (string.IsNullOrEmpty(_fullPath))
                    {
                        _scatterFiles = Array.Empty<string>();
                        _satelliteFiles = new List<string>();
                        _serializationAssemblyFiles = new List<string>();
                        AssembliesConsideredAndRejected = new List<ResolutionSearchLocation>();
                        ResolvedSearchPath = String.Empty;
                        _preUnificationVersions = new Dictionary<string, UnificationVersion>(StringComparer.OrdinalIgnoreCase);
                        IsBadImage = false;
                        DependenciesFound = false;
                        UserRequestedSpecificFile = false;
                        IsWinMDFile = false;
                    }
                    else if (NativeMethodsShared.IsWindows)
                    {
                        IsWinMDFile = _isWinMDFile(_fullPath, _getRuntimeVersion, _fileExists, out _imageRuntimeVersion, out _isManagedWinMDFile);
                    }
                }
            }
        }

        internal void NormalizeFullPath()
        {
            _fullPath = FileUtilities.NormalizePath(_fullPath);
            _fullPathWithoutExtension = null;
            _directoryName = null;
        }

        /// <summary>
        /// The directory that this assembly lives in.
        /// </summary>
        internal string DirectoryName
        {
            get
            {
                if ((string.IsNullOrEmpty(_directoryName)) && (!string.IsNullOrEmpty(_fullPath)))
                {
                    _directoryName = Path.GetDirectoryName(_fullPath);
                    if (_directoryName.Length == 0)
                    {
                        _directoryName = ".";
                    }
                }
                return _directoryName;
            }
        }

        /// <summary>
        /// The file name without extension.
        /// </summary>
        /// <value></value>
        internal string FileNameWithoutExtension
        {
            get
            {
                if ((string.IsNullOrEmpty(_fileNameWithoutExtension)) && (!string.IsNullOrEmpty(_fullPath)))
                {
                    _fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_fullPath);
                }
                return _fileNameWithoutExtension;
            }
        }

        /// <summary>
        /// The full path to the assembly but without an extension on the file namee
        /// </summary>
        internal string FullPathWithoutExtension
        {
            get
            {
                if ((string.IsNullOrEmpty(_fullPathWithoutExtension)) && (!string.IsNullOrEmpty(_fullPath)))
                {
                    _fullPathWithoutExtension = Path.Combine(DirectoryName, FileNameWithoutExtension);
                }
                return _fullPathWithoutExtension;
            }
        }

        /// <summary>
        /// This is the HintPath from the source item. This is used to resolve the assembly.
        /// </summary>
        /// <value>The hint path to this assembly.</value>
        internal string HintPath { get; set; } = "";

        /// <summary>
        /// This is the key that was passed in to the reference through the &lt;AssemblyFolderKey&gt; metadata.
        /// </summary>
        /// <value>The &lt;AssemblyFolderKey&gt; value.</value>
        internal string AssemblyFolderKey { get; set; } = String.Empty;

        /// <summary>
        /// Whether this assembly came from the project. If 'false' then this reference was deduced 
        /// through the reference resolution process.
        /// </summary>
        /// <value>'true' if this reference is a primary assembly.</value>
        internal bool IsPrimary { get; private set; } = false;

        /// <summary>
        /// Whether or not this reference will be installed on the target machine.
        /// </summary>
        internal bool IsPrerequisite { set; get; } = false;

        /// <summary>
        /// Whether or not this reference is a redist root.
        /// </summary>
        internal bool? IsRedistRoot { set; get; } = null;

        /// <summary>
        /// The redist name for this reference (if any)
        /// </summary>
        internal string RedistName { set; get; } = null;

        /// <summary>
        /// The original source item, as passed into the task that is directly associated
        /// with this reference.  This only applies to "primary" references.
        /// </summary>
        internal ITaskItem PrimarySourceItem
        {
            get
            {
                ErrorUtilities.VerifyThrow(
                    !(IsPrimary && _primarySourceItem == null), "A primary reference must have a primary source item.");
                ErrorUtilities.VerifyThrow(
                    IsPrimary || _primarySourceItem == null, "Only a primary reference can have a primary source item.");

                return _primarySourceItem;
            }
        }

        /// <summary>
        /// If 'true' then the path that this item points to is known to be a bad image.
        /// This item shouldn't be passed to compilers and so forth. 
        /// </summary>
        /// <value>'true' if this reference points to a bad image.</value>
        internal bool IsBadImage { get; private set; } = false;

        /// <summary>
        ///  If true, then this item conflicted with another item and lost.
        /// </summary>
        internal bool IsConflictVictim
        {
            get
            {
                return ConflictVictorName != null;
            }
        }

        /// <summary>
        /// Add a conflict victim to this reference
        /// </summary>
        internal void AddConflictVictim(AssemblyNameExtension victim)
        {
            _conflictVictims.Add(victim);
        }

        /// <summary>
        /// Return the list of conflict victims.
        /// </summary>
        internal List<AssemblyNameExtension> GetConflictVictims()
        {
            return _conflictVictims;
        }

        /// <summary>
        ///  The name of the assembly that won over this reference.
        /// </summary>
        internal AssemblyNameExtension ConflictVictorName { get; set; } = null;

        /// <summary>
        ///  The reason why this reference lost to another reference.
        /// </summary>
        internal ConflictLossReason ConflictLossExplanation { get; set; } = ConflictLossReason.DidntLose;

        /// <summary>
        /// Is the file a WinMDFile.
        /// </summary>
        internal bool IsWinMDFile { get; set; }

        /// <summary>
        /// Is the file a Managed.
        /// </summary>
        internal bool IsManagedWinMDFile
        {
            get { return _isManagedWinMDFile; }
            set { _isManagedWinMDFile = value; }
        }

        /// <summary>
        /// For winmd files there may be an implementation file sitting beside the winmd called the assemblyName.dll
        /// We need to attach a piece of metadata to if this is the case.
        /// </summary>
        public string ImplementationAssembly
        {
            get;
            set;
        }

        /// <summary>
        /// ImageRuntime Information
        /// </summary>
        internal string ImageRuntime
        {
            get { return _imageRuntimeVersion; }
            set { _imageRuntimeVersion = value; }
        }

        /// <summary>
        /// Return the list of versions that this reference is unified from.
        /// </summary>
        internal List<UnificationVersion> GetPreUnificationVersions()
        {
            return new List<UnificationVersion>(_preUnificationVersions.Values);
        }

        /// <summary>
        /// Return the list of versions that this reference is unified from.
        /// </summary>
        internal HashSet<AssemblyRemapping> RemappedAssemblyNames()
        {
            return _remappedAssemblyNames;
        }

        /// <summary>
        /// Add a new version number for a version of this reference 
        /// </summary>
        internal void AddPreUnificationVersion(String referencePath, Version version, UnificationReason reason)
        {
            string key = referencePath + version.ToString() + reason.ToString();

            // Only add a reference, version, and reason once.
            UnificationVersion unificationVersion;
            if (!_preUnificationVersions.TryGetValue(key, out unificationVersion))
            {
                unificationVersion = new UnificationVersion();
                unificationVersion.referenceFullPath = referencePath;
                unificationVersion.version = version;
                unificationVersion.reason = reason;
                _preUnificationVersions[key] = unificationVersion;
            }
        }

        /// <summary>
        /// Add the AssemblyNames name we were remapped from
        /// </summary>
        internal void AddRemapping(AssemblyNameExtension remappedFrom, AssemblyNameExtension remappedTo)
        {
            ErrorUtilities.VerifyThrow(remappedFrom.Immutable, " Remapped from is NOT immutable");
            ErrorUtilities.VerifyThrow(remappedTo.Immutable, " Remapped to is NOT immutable");
            _remappedAssemblyNames.Add(new AssemblyRemapping(remappedFrom, remappedTo));
        }

        /// <summary>
        ///  Whether or not this reference is unified from a different version or versions.
        /// </summary>
        internal bool IsUnified
        {
            get { return _preUnificationVersions.Count != 0; }
        }

        /// <summary>
        /// Whether this reference should be copied to the local 'bin' dir or not and the reason this flag
        /// was set that way.
        /// </summary>
        /// <value>The current copy-local state.</value>
        internal CopyLocalState CopyLocal { get; private set; } = CopyLocalState.Undecided;

        /// <summary>
        /// Whether the reference should be CopyLocal. For the reason, see CopyLocalState.
        /// </summary>
        /// <value>'true' if this reference should be copied.</value>
        internal bool IsCopyLocal
        {
            get
            {
                return CopyLocalStateUtility.IsCopyLocal(CopyLocal);
            }
        }

        /// <summary>
        /// Whether this reference has already been resolved.
        /// Resolved means that the actual filename of the assembly has been found.
        /// </summary>
        /// <value>'true' if this reference has been resolved.</value>
        internal bool IsResolved
        {
            get { return _fullPath.Length > 0; }
        }

        /// <summary>
        /// Whether this reference can't be resolve.
        /// References are usually unresolvable because they weren't found anywhere in the defined search paths.
        /// </summary>
        /// <value>'true' if this reference is unresolvable.</value>
        internal bool IsUnresolvable
        {
            // If there are any resolution errors then this reference is unresolvable.
            get
            {
                return !IsResolved && _errors.Count > 0;
            }
        }

        /// <summary>
        /// Whether or not we still need to find dependencies for this reference.
        /// </summary>
        internal bool DependenciesFound { get; set; } = false;

        /// <summary>
        /// If the reference has an SDK name metadata this will contain that string.
        /// </summary>
        internal string SDKName { get; private set; } = String.Empty;

        /// <summary>
        /// Add some records to the table of assemblies that were considered and then rejected.
        /// </summary>
        internal void AddAssembliesConsideredAndRejected(List<ResolutionSearchLocation> assembliesConsideredAndRejectedToAdd)
        {
            AssembliesConsideredAndRejected.AddRange(assembliesConsideredAndRejectedToAdd);
        }

        /// <summary>
        /// Returns a collection of strings. Each string is the full path to an assembly that was 
        /// considered for resolution but then rejected because it wasn't a complete match.
        /// </summary>
        internal List<ResolutionSearchLocation> AssembliesConsideredAndRejected { get; private set; } = new List<ResolutionSearchLocation>();

        /// <summary>
        /// The searchpath location that the reference was found at.
        /// </summary>
        internal string ResolvedSearchPath { get; set; } = String.Empty;

        /// <summary>
        /// FrameworkName attribute on this reference
        /// </summary>
        internal FrameworkName FrameworkNameAttribute { get; set; }

        /// <summary>
        /// Indicates that the reference is primary and has ExternallyResolved=true metadata to denote that 
        /// it was resolved by an external system (commonly from nuget). Such a system has already provided a
        /// resolved closure as primary references and therefore we can skip the expensive closure walk.
        /// </summary>
        internal bool ExternallyResolved { get; private set; }

        /// <summary>
        /// Make this reference an assembly that is a dependency of 'sourceReference'
        ///
        /// For example, if 'sourceReference' is MyAssembly.dll then a dependent assembly file
        /// might be en\MyAssembly.resources.dll
        /// 
        /// Assembly references do not have their own dependencies, therefore they are
        /// </summary>
        /// <param name="sourceReference">The source reference that this reference will be dependent on</param>
        internal void MakeDependentAssemblyReference(Reference sourceReference)
        {
            CopyLocal = CopyLocalState.Undecided;

            // This is a true dependency, so its not primary.
            IsPrimary = false;

            // This is an assembly file, so we'll need to find dependencies later.
            DependenciesFound = false;

            // Dependencies must always be specific version.
            WantSpecificVersion = true;

            // Add source items from the original item.
            AddSourceItems(sourceReference.GetSourceItems());

            // Add dependees
            AddDependee(sourceReference);
        }

        /// <summary>
        /// Make this reference a primary assembly reference. 
        /// This is a refrence that is an assembly and is primary.
        /// </summary>
        /// <param name="sourceItem">The source item.</param>
        /// <param name="wantSpecificVersionValue">Whether the version needs to match exactly or loosely.</param>
        /// <param name="executableExtension">The filename extension that the resulting assembly must have.</param>
        internal void MakePrimaryAssemblyReference
        (
            ITaskItem sourceItem,
            bool wantSpecificVersionValue,
            string executableExtension
        )
        {
            CopyLocal = CopyLocalState.Undecided;

            // This is a primary reference.
            IsPrimary = true;

            // This is the source item (from the list passed into the task) that
            // originally created this reference.
            _primarySourceItem = sourceItem;
            SDKName = sourceItem.GetMetadata("SDKName");

            if (!string.IsNullOrEmpty(executableExtension))
            {
                // Set the expected extension.
                SetExecutableExtension(executableExtension);
            }

            // The specific version indicator.
            WantSpecificVersion = wantSpecificVersionValue;

            // This is an assembly file, so we'll need to find dependencies later.
            DependenciesFound = false;

            ExternallyResolved = MetadataConversionUtilities.TryConvertItemMetadataToBool(sourceItem, "ExternallyResolved");

            // Add source items from the original item.
            AddSourceItem(sourceItem);
        }

        /// <summary>
        /// Determine whether the given assembly is an FX assembly.
        /// </summary>
        /// <param name="fullPath">The full path to the assembly.</param>
        /// <param name="frameworkPaths">The path to the frameworks.</param>
        /// <returns>True if this is a frameworks assembly.</returns>
        internal static bool IsFrameworkFile(string fullPath, string[] frameworkPaths)
        {
            if (frameworkPaths != null)
            {
                foreach (string frameworkPath in frameworkPaths)
                {
                    if
                    (
                        String.Compare
                        (
                            frameworkPath, 0,
                            fullPath, 0,
                            frameworkPath.Length,
                            StringComparison.OrdinalIgnoreCase
                        ) == 0
                    )
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Figure out the what the CopyLocal state of given assembly should be.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <param name="frameworkPaths">The framework paths.</param>
        /// <param name="targetProcessorArchitecture">Like x86 or IA64\AMD64.</param>
        /// <param name="getRuntimeVersion">Delegate to get runtime version.</param>
        /// <param name="targetedRuntimeVersion">The targeted runtime version.</param>
        /// <param name="fileExists">Delegate to check if a file exists.</param>
        /// <param name="getAssemblyPathInGac">Delegate to get the path to an assembly in the system GAC.</param>
        /// <param name="copyLocalDependenciesWhenParentReferenceInGac">if set to true, copy local dependencies when only parent reference in gac.</param>
        /// <param name="doNotCopyLocalIfInGac">If set to true, do not copy local a reference that exists in the GAC (legacy behavior).</param>
        /// <param name="referenceTable">The reference table.</param>
        internal void SetFinalCopyLocalState
        (
            AssemblyNameExtension assemblyName,
            string[] frameworkPaths,
            ProcessorArchitecture targetProcessorArchitecture,
            GetAssemblyRuntimeVersion getRuntimeVersion,
            Version targetedRuntimeVersion,
            FileExists fileExists,
            GetAssemblyPathInGac getAssemblyPathInGac,
            bool copyLocalDependenciesWhenParentReferenceInGac,
            bool doNotCopyLocalIfInGac,
            ReferenceTable referenceTable
        )
        {
            // If this item was unresolvable, then copy-local is false.
            if (IsUnresolvable)
            {
                CopyLocal = CopyLocalState.NoBecauseUnresolved;
                return;
            }

            if (EmbedInteropTypes)
            {
                CopyLocal = CopyLocalState.NoBecauseEmbedded;
                return;
            }

            // If this item was a conflict victim, then it should not be copy-local.
            if (IsConflictVictim)
            {
                CopyLocal = CopyLocalState.NoBecauseConflictVictim;
                return;
            }

            // If this is a primary reference then see if there's a Private metadata on the source item
            if (IsPrimary)
            {
                bool found;
                bool result = MetadataConversionUtilities.TryConvertItemMetadataToBool
                    (
                        PrimarySourceItem,
                        ItemMetadataNames.privateMetadata,
                        out found
                    );

                if (found)
                {
                    CopyLocal = result
                        ? CopyLocalState.YesBecauseReferenceItemHadMetadata
                        : CopyLocalState.NoBecauseReferenceItemHadMetadata;
                    return;
                }
            }
            else
            {
                // This is a dependency. If any primary reference that lead to this dependency
                // has Private=false, then this dependency should false too.
                bool privateTrueFound = false;
                bool privateFalseFound = false;
                foreach (ITaskItem item in _sourceItems.Values)
                {
                    bool found;
                    bool result = MetadataConversionUtilities.TryConvertItemMetadataToBool
                        (
                            item,
                            ItemMetadataNames.privateMetadata,
                            out found
                        );

                    if (found)
                    {
                        if (result)
                        {
                            privateTrueFound = true;

                            // Once we hit this once we know there will be no modification to CopyLocal state.
                            // so we can immediately...
                            break;
                        }
                        else
                        {
                            privateFalseFound = true;
                        }
                    }
                }

                if (privateFalseFound && !privateTrueFound)
                {
                    CopyLocal = CopyLocalState.NoBecauseReferenceItemHadMetadata;
                    return;
                }
            }

            // If the item was determined to be an prereq assembly.
            if (IsPrerequisite && !UserRequestedSpecificFile)
            {
                CopyLocal = CopyLocalState.NoBecausePrerequisite;
                return;
            }

            // Items in the frameworks directory shouldn't be copy-local
            if (IsFrameworkFile(_fullPath, frameworkPaths))
            {
                CopyLocal = CopyLocalState.NoBecauseFrameworkFile;
                return;
            }

            // We are a dependency, check to see if all of our parent references have come from the GAC
            if (!IsPrimary && !copyLocalDependenciesWhenParentReferenceInGac)
            {
                // Did we discover a parent reference which was not found in the GAC
                bool foundSourceItemNotInGac = false;

                // Go through all of the parent source items and check to see if they were found in the GAC
                foreach (string key in _sourceItems.Keys)
                {
                    AssemblyNameExtension primaryAssemblyName = referenceTable.GetReferenceFromItemSpec(key);
                    Reference primaryReference = referenceTable.GetReference(primaryAssemblyName);

                    if (doNotCopyLocalIfInGac)
                    {
                        // Legacy behavior, don't copy local if the assembly is in the GAC at all
                        if (!primaryReference.FoundInGac.HasValue)
                        {
                            primaryReference.FoundInGac = !string.IsNullOrEmpty(getAssemblyPathInGac(primaryAssemblyName, targetProcessorArchitecture, getRuntimeVersion, targetedRuntimeVersion, fileExists, true, false));
                        }

                        if (!primaryReference.FoundInGac.Value)
                        {
                            foundSourceItemNotInGac = true;
                            break;
                        }
                    }
                    else
                    {
                        if (!primaryReference.ResolvedFromGac)
                        {
                            foundSourceItemNotInGac = true;
                            break;
                        }
                    }
                }

                // All parent source items were found in the GAC.
                if (!foundSourceItemNotInGac)
                {
                    CopyLocal = CopyLocalState.NoBecauseParentReferencesFoundInGAC;
                    return;
                }
            }

            if (doNotCopyLocalIfInGac)
            {
                // Legacy behavior, don't copy local if the assembly is in the GAC at all
                if (!FoundInGac.HasValue)
                {
                    FoundInGac = !string.IsNullOrEmpty(getAssemblyPathInGac(assemblyName, targetProcessorArchitecture, getRuntimeVersion, targetedRuntimeVersion, fileExists, true, false));
                }

                if (FoundInGac.Value)
                {
                    CopyLocal = CopyLocalState.NoBecauseReferenceFoundInGAC;
                    return;
                }
            }

            if (ResolvedFromGac)
            {
                CopyLocal = CopyLocalState.NoBecauseReferenceResolvedFromGAC;
                return;
            }

            //  It was resolved locally, so copy it.
            CopyLocal = CopyLocalState.YesBecauseOfHeuristic;
        }

        /// <summary>
        /// Produce a string representation.
        /// </summary>
        public override string ToString()
        {
            if (IsResolved)
            {
                return FullPath;
            }
            return "*Unresolved*";
        }

        /// <summary>
        /// There are a number of properties which are set when we generate exclusion lists and it is useful to have this information on the references so that 
        /// the correct reasons can be logged for these references being in the black list.
        /// </summary>
        internal class ExclusionListProperties
        {
            /// <summary>
            /// Is this reference in an exclusion list
            /// </summary>
            internal bool IsInExclusionList { get; set; }

            /// <summary>
            /// What is the highest version of this assembly in the current redist list
            /// </summary>
            internal Version HighestVersionInRedist { get; set; }

            /// <summary>
            /// What is the highest versioned redist list on the machine
            /// </summary>
            internal string HighestRedistListMonkier { get; set; }

            /// <summary>
            /// Delegate which logs the reason for not resolving a reference
            /// </summary>
            internal ReferenceTable.LogExclusionReason ExclusionReasonLogDelegate { get; set; }
        }
    }
}
