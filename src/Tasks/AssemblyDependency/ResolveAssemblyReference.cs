// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;

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
    /// Given a list of assemblyFiles, determine the closure of all assemblyFiles that
    /// depend on those assemblyFiles including second and nth-order dependencies too.
    /// </summary>
    public class ResolveAssemblyReference : TaskExtension
    {
        /// <summary>
        /// key assembly used to trigger inclusion of facade references.
        /// </summary>
        private const string SystemRuntimeAssemblyName = "System.Runtime";

        /// <summary>
        /// additional key assembly used to trigger inclusion of facade references.
        /// </summary>
        private const string NETStandardAssemblyName = "netstandard";

        /// <summary>
        /// Delegate to a method that takes a targetFrameworkDirectory and returns an array of redist or subset list paths
        /// </summary>
        /// <param name="targetFrameworkDirectory">TargetFramework directory to search for redist or subset list</param>
        /// <returns>String array of redist or subset lists</returns>
        private delegate string[] GetListPath(string targetFrameworkDirectory);

        /// <summary>
        /// Cache of system state information, used to optimize performance.
        /// </summary>
        internal SystemState _cache = null;

        /// <summary>
        /// Construct
        /// </summary>
        public ResolveAssemblyReference()
        {
            Strings.Initialize(Log);
        }

        private static class Strings
        {
            public const string FourSpaces = "    ";
            public const string EightSpaces = "        ";
            public const string TenSpaces = "          ";
            public const string TwelveSpaces = "            ";

            public static string ConsideredAndRejectedBecauseFusionNamesDidntMatch;
            public static string ConsideredAndRejectedBecauseNoFile;
            public static string ConsideredAndRejectedBecauseNotAFileNameOnDisk;
            public static string ConsideredAndRejectedBecauseNotInGac;
            public static string ConsideredAndRejectedBecauseTargetDidntHaveFusionName;
            public static string Dependency;
            public static string FormattedAssemblyInfo;
            public static string FoundRelatedFile;
            public static string FoundSatelliteFile;
            public static string FoundScatterFile;
            public static string ImageRuntimeVersion;
            public static string IsAWinMdFile;
            public static string LogAttributeFormat;
            public static string LogTaskPropertyFormat;
            public static string NoBecauseParentReferencesFoundInGac;
            public static string NotCopyLocalBecauseConflictVictim;
            public static string NotCopyLocalBecauseEmbedded;
            public static string NotCopyLocalBecauseFrameworksFiles;
            public static string NotCopyLocalBecauseIncomingItemAttributeOverrode;
            public static string NotCopyLocalBecausePrerequisite;
            public static string NotCopyLocalBecauseReferenceFoundInGAC;
            public static string PrimaryReference;
            public static string RemappedReference;
            public static string RequiredBy;
            public static string Resolved;
            public static string ResolvedFrom;
            public static string SearchedAssemblyFoldersEx;
            public static string SearchPath;
            public static string TargetedProcessorArchitectureDoesNotMatch;
            public static string UnificationByAppConfig;
            public static string UnificationByAutoUnify;
            public static string UnificationByFrameworkRetarget;
            public static string UnifiedDependency;
            public static string UnifiedPrimaryReference;

            private static bool initialized = false;

            internal static void Initialize(TaskLoggingHelper log)
            {
                if (initialized)
                {
                    return;
                }

                initialized = true;

                string GetResource(string name) => log.GetResourceMessage(name);
                string GetResourceFourSpaces(string name) => FourSpaces + log.GetResourceMessage(name);
                string GetResourceEightSpaces(string name) => EightSpaces + log.GetResourceMessage(name);

                ConsideredAndRejectedBecauseFusionNamesDidntMatch = GetResourceEightSpaces("ResolveAssemblyReference.ConsideredAndRejectedBecauseFusionNamesDidntMatch");
                ConsideredAndRejectedBecauseNoFile = GetResourceEightSpaces("ResolveAssemblyReference.ConsideredAndRejectedBecauseNoFile");
                ConsideredAndRejectedBecauseNotAFileNameOnDisk = GetResourceEightSpaces("ResolveAssemblyReference.ConsideredAndRejectedBecauseNotAFileNameOnDisk");
                ConsideredAndRejectedBecauseNotInGac = GetResourceEightSpaces("ResolveAssemblyReference.ConsideredAndRejectedBecauseNotInGac");
                ConsideredAndRejectedBecauseTargetDidntHaveFusionName = GetResourceEightSpaces("ResolveAssemblyReference.ConsideredAndRejectedBecauseTargetDidntHaveFusionName");
                Dependency = GetResource("ResolveAssemblyReference.Dependency");
                FormattedAssemblyInfo = GetResourceFourSpaces("ResolveAssemblyReference.FormattedAssemblyInfo");
                FoundRelatedFile = GetResourceFourSpaces("ResolveAssemblyReference.FoundRelatedFile");
                FoundSatelliteFile = GetResourceFourSpaces("ResolveAssemblyReference.FoundSatelliteFile");
                FoundScatterFile = GetResourceFourSpaces("ResolveAssemblyReference.FoundScatterFile");
                ImageRuntimeVersion = GetResourceFourSpaces("ResolveAssemblyReference.ImageRuntimeVersion");
                IsAWinMdFile = GetResourceFourSpaces("ResolveAssemblyReference.IsAWinMdFile");
                LogAttributeFormat = GetResourceEightSpaces("ResolveAssemblyReference.LogAttributeFormat");
                LogTaskPropertyFormat = GetResource("ResolveAssemblyReference.LogTaskPropertyFormat");
                NoBecauseParentReferencesFoundInGac = GetResourceFourSpaces("ResolveAssemblyReference.NoBecauseParentReferencesFoundInGac");
                NotCopyLocalBecauseConflictVictim = GetResourceFourSpaces("ResolveAssemblyReference.NotCopyLocalBecauseConflictVictim");
                NotCopyLocalBecauseEmbedded = GetResourceFourSpaces("ResolveAssemblyReference.NotCopyLocalBecauseEmbedded");
                NotCopyLocalBecauseFrameworksFiles = GetResourceFourSpaces("ResolveAssemblyReference.NotCopyLocalBecauseFrameworksFiles");
                NotCopyLocalBecauseIncomingItemAttributeOverrode = GetResourceFourSpaces("ResolveAssemblyReference.NotCopyLocalBecauseIncomingItemAttributeOverrode");
                NotCopyLocalBecausePrerequisite = GetResourceFourSpaces("ResolveAssemblyReference.NotCopyLocalBecausePrerequisite");
                NotCopyLocalBecauseReferenceFoundInGAC = GetResourceFourSpaces("ResolveAssemblyReference.NotCopyLocalBecauseReferenceFoundInGAC");
                PrimaryReference = GetResource("ResolveAssemblyReference.PrimaryReference");
                RemappedReference = GetResourceFourSpaces("ResolveAssemblyReference.RemappedReference");
                RequiredBy = GetResourceFourSpaces("ResolveAssemblyReference.RequiredBy");
                Resolved = GetResourceFourSpaces("ResolveAssemblyReference.Resolved");
                ResolvedFrom = GetResourceFourSpaces("ResolveAssemblyReference.ResolvedFrom");
                SearchedAssemblyFoldersEx = GetResourceEightSpaces("ResolveAssemblyReference.SearchedAssemblyFoldersEx");
                SearchPath = EightSpaces + GetResource("ResolveAssemblyReference.SearchPath");
                TargetedProcessorArchitectureDoesNotMatch = GetResourceEightSpaces("ResolveAssemblyReference.TargetedProcessorArchitectureDoesNotMatch");
                UnificationByAppConfig = GetResourceFourSpaces("ResolveAssemblyReference.UnificationByAppConfig");
                UnificationByAutoUnify = GetResourceFourSpaces("ResolveAssemblyReference.UnificationByAutoUnify");
                UnificationByFrameworkRetarget = GetResourceFourSpaces("ResolveAssemblyReference.UnificationByFrameworkRetarget");
                UnifiedDependency = GetResource("ResolveAssemblyReference.UnifiedDependency");
                UnifiedPrimaryReference = GetResource("ResolveAssemblyReference.UnifiedPrimaryReference");
            }
        }

        #region Properties

        private ITaskItem[] _assemblyFiles = Array.Empty<TaskItem>();
        private ITaskItem[] _assemblyNames = Array.Empty<TaskItem>();
        private ITaskItem[] _installedAssemblyTables = Array.Empty<TaskItem>();
        private ITaskItem[] _installedAssemblySubsetTables = Array.Empty<TaskItem>();
        private ITaskItem[] _fullFrameworkAssemblyTables = Array.Empty<TaskItem>();
        private ITaskItem[] _resolvedSDKReferences = Array.Empty<TaskItem>();
        private bool _ignoreDefaultInstalledAssemblyTables = false;
        private bool _ignoreDefaultInstalledAssemblySubsetTables = false;
        private string[] _candidateAssemblyFiles = Array.Empty<string>();
        private string[] _targetFrameworkDirectories = Array.Empty<string>();
        private string[] _searchPaths = Array.Empty<string>();
        private string[] _allowedAssemblyExtensions = new string[] { ".winmd", ".dll", ".exe" };
        private string[] _relatedFileExtensions = new string[] { ".pdb", ".xml", ".pri" };
        private string _appConfigFile = null;
        private bool _supportsBindingRedirectGeneration;
        private bool _autoUnify = false;
        private bool _ignoreVersionForFrameworkReferences = false;
        private bool _ignoreTargetFrameworkAttributeVersionMismatch = false;
        private ITaskItem[] _resolvedFiles = Array.Empty<TaskItem>();
        private ITaskItem[] _resolvedDependencyFiles = Array.Empty<TaskItem>();
        private ITaskItem[] _relatedFiles = Array.Empty<TaskItem>();
        private ITaskItem[] _satelliteFiles = Array.Empty<TaskItem>();
        private ITaskItem[] _serializationAssemblyFiles = Array.Empty<TaskItem>();
        private ITaskItem[] _scatterFiles = Array.Empty<TaskItem>();
        private ITaskItem[] _copyLocalFiles = Array.Empty<TaskItem>();
        private ITaskItem[] _suggestedRedirects = Array.Empty<TaskItem>();
        private List<ITaskItem> _unresolvedConflicts = new List<ITaskItem>();
        private string[] _targetFrameworkSubsets = Array.Empty<string>();
        private string[] _fullTargetFrameworkSubsetNames = Array.Empty<string>();
        private string _targetedFrameworkMoniker = String.Empty;

        private bool _findDependencies = true;
        private bool _findSatellites = true;
        private bool _findSerializationAssemblies = true;
        private bool _findRelatedFiles = true;
        private bool _silent = false;
        private string _projectTargetFrameworkAsString = String.Empty;
        private string _targetedRuntimeVersionRawValue = String.Empty;
        private Version _projectTargetFramework;

        private string _stateFile = null;
        private string _targetProcessorArchitecture = null;

        private string _profileName = String.Empty;
        private string[] _fullFrameworkFolders = Array.Empty<string>();
        private string[] _latestTargetFrameworkDirectories = Array.Empty<string>();
        private bool _copyLocalDependenciesWhenParentReferenceInGac = true;
        private Dictionary<string, MessageImportance> _showAssemblyFoldersExLocations = new Dictionary<string, MessageImportance>(StringComparer.OrdinalIgnoreCase);
        private bool _logVerboseSearchResults = false;
        private WarnOrErrorOnTargetArchitectureMismatchBehavior _warnOrErrorOnTargetArchitectureMismatch = WarnOrErrorOnTargetArchitectureMismatchBehavior.Warning;
        private bool _unresolveFrameworkAssembliesFromHigherFrameworks = false;

        /// <summary>
        /// If set to true, it forces to unresolve framework assemblies with versions higher or equal the version of the target framework, regardless of the target framework
        /// </summary>
        public bool UnresolveFrameworkAssembliesFromHigherFrameworks
        {
            get
            {
                return _unresolveFrameworkAssembliesFromHigherFrameworks;
            }
            set
            {
                _unresolveFrameworkAssembliesFromHigherFrameworks = value;
            }
        }

        /// <summary>
        /// If there is a mismatch between the targetprocessor architecture and the architecture of a primary reference.
        ///
        /// When this is error,  an error will be logged.
        ///
        /// When this is warn, if there is a mismatch between the targetprocessor architecture and the architecture of a primary reference a warning will be logged.
        ///
        /// When this is none, no error or warning will be logged.
        /// </summary>
        public string WarnOrErrorOnTargetArchitectureMismatch
        {
            get
            {
                return _warnOrErrorOnTargetArchitectureMismatch.ToString();
            }

            set
            {
                if (!Enum.TryParse<WarnOrErrorOnTargetArchitectureMismatchBehavior>(value, /*ignoreCase*/true, out _warnOrErrorOnTargetArchitectureMismatch))
                {
                    _warnOrErrorOnTargetArchitectureMismatch = WarnOrErrorOnTargetArchitectureMismatchBehavior.Warning;
                }
            }
        }
        /// <summary>
        /// A list of fully qualified paths-to-assemblyFiles to find dependencies for.
        ///
        /// Optional attributes are:
        ///     bool Private [default=true] -- means 'CopyLocal'
        ///     string FusionName -- the simple or strong fusion name for this item. If this
        ///         attribute is present it can save time since the assembly file won't need
        ///         to be opened to get the fusion name.
        ///     bool ExternallyResolved [default=false] -- indicates that the reference and its
        ///        dependencies are resolved by an external system (commonly from nuget assets) and
        ///        so several steps can be skipped as an optimization: finding dependencies,
        ///        satellite assemblies, etc.
        /// </summary>
        public ITaskItem[] AssemblyFiles
        {
            get { return _assemblyFiles; }
            set { _assemblyFiles = value; }
        }

        /// <summary>
        /// The list of directories which contain the redist lists for the most current
        /// framework which can be targeted on the machine. If this is not set
        /// Then we will looks for the highest framework installed on the machine
        /// for a given target framework identifier and use that.
        /// </summary>
        public string[] LatestTargetFrameworkDirectories
        {
            get
            {
                return _latestTargetFrameworkDirectories;
            }

            set
            {
                _latestTargetFrameworkDirectories = value;
            }
        }

        /// <summary>
        /// Should the framework attribute be ignored when checking to see if an assembly is compatible with the targeted framework.
        /// </summary>
        public bool IgnoreTargetFrameworkAttributeVersionMismatch
        {
            get
            {
                return _ignoreTargetFrameworkAttributeVersionMismatch;
            }

            set
            {
                _ignoreTargetFrameworkAttributeVersionMismatch = value;
            }
        }

        /// <summary>
        /// Force dependencies to be walked even when a reference is marked with ExternallyResolved=true
        /// metadata.
        /// </summary>
        /// <remarks>
        /// This is used to ensure that we suggest appropriate binding redirects for assembly version
        /// conflicts within an externally resolved graph.
        /// </remarks>
        public bool FindDependenciesOfExternallyResolvedReferences { get; set; }

        /// <summary>
        /// If true, outputs any unresolved assembly conflicts (MSB3277) in UnresolvedAssemblyConflicts.
        /// </summary>
        public bool OutputUnresolvedAssemblyConflicts { get; set; }

        /// <summary>
        /// List of target framework subset names which will be searched for in the target framework directories
        /// </summary>
        public string[] TargetFrameworkSubsets
        {
            get { return _targetFrameworkSubsets; }
            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "TargetFrameworkSubsets");
                _targetFrameworkSubsets = value;
            }
        }

        /// <summary>
        /// These can either be simple fusion names like:
        ///
        ///      System
        ///
        /// or strong names like
        ///
        ///     System, Version=2.0.3500.0, Culture=neutral, PublicKeyToken=b77a5c561934e089
        ///
        /// These names will be resolved into full paths and all dependencies will be found.
        ///
        /// Optional attributes are:
        ///     bool Private [default=true] -- means 'CopyLocal'
        ///     string HintPath [default=''] -- location of file name to consider as a reference,
        ///         used when {HintPathFromItem} is one of the paths in SearchPaths.
        ///     bool SpecificVersion [default=absent] --
        ///         when true, the exact fusionname in the Include must be matched.
        ///         when false, any assembly with the same simple name will be a match.
        ///         when absent, then look at the value in Include.
        ///           If its a simple name then behave as if specific version=false.
        ///           If its a strong name then behave as if specific version=true.
        ///     string ExecutableExtension [default=absent] --
        ///         when present, the resolved assembly must have this extension.
        ///         when absent, .dll is considered and then .exe for each directory looked at.
        ///     string SubType -- only items with empty SubTypes will be considered. Items
        ///         with non-empty subtypes will be ignored.
        ///     string AssemblyFolderKey [default=absent] -- supported for legacy AssemblyFolder
        ///         resolution. This key can have a value like 'hklm\vendor folder'. When set, only
        ///         this particular assembly folder key will be used.
        ///            This is to support the scenario in VSWhidey#357946 in which there are multiple
        ///            side-by-side libraries installed and the user wants to pick an exact version.
        ///     bool EmbedInteropTyeps [default=absent] --
        ///         when true, we should treat this assembly as if it has no dependencies and should
        ///         be completely embedded into the target assembly.
        /// </summary>
        public ITaskItem[] Assemblies
        {
            get { return _assemblyNames; }
            set { _assemblyNames = value; }
        }

        /// <summary>
        /// A list of assembly files that can be part of the search and resolution process.
        /// These must be absolute filenames, or project-relative filenames.
        ///
        /// Assembly files in this list will be considered when SearchPaths contains
        /// {CandidateAssemblyFiles} as one of the paths to consider.
        /// </summary>
        public string[] CandidateAssemblyFiles
        {
            get { return _candidateAssemblyFiles; }
            set { _candidateAssemblyFiles = value; }
        }

        /// <summary>
        /// A list of resolved SDK references which contain the sdk name, sdk location and the targeted configuration.
        /// These locations will only be searched if the reference has the SDKName metadata attached to it.
        /// </summary>
        public ITaskItem[] ResolvedSDKReferences
        {
            get { return _resolvedSDKReferences; }
            set { _resolvedSDKReferences = value; }
        }

        /// <summary>
        /// Path to the target frameworks directory. Required to figure out CopyLocal status
        /// for resulting items.
        /// If not present, then no resulting items will be deemed CopyLocal='true' unless they explicity
        /// have a Private='true' attribute on their source item.
        /// </summary>
        public string[] TargetFrameworkDirectories
        {
            get { return _targetFrameworkDirectories; }
            set { _targetFrameworkDirectories = value; }
        }

        /// <summary>
        /// A list of XML files that contain assemblies that are expected to be installed on the target machine.
        ///
        /// Format of the file is like:
        ///
        ///     <FileList Redist="Microsoft-Windows-CLRCoreComp" >
        ///         <File AssemblyName="System" Version="2.0.0.0" PublicKeyToken="b77a5c561934e089" Culture="neutral" ProcessorArchitecture="MSIL" FileVersion="2.0.40824.0" InGAC="true" />
        ///         etc.
        ///     </FileList>
        ///
        /// When present, assemblies from this list will be candidates to automatically "unify" from prior versions up to
        /// the version listed in the XML. Also, assemblies with InGAC='true' will be considered prerequisites and will be CopyLocal='false'
        /// unless explicitly overridden.
        /// Items in this list may optionally specify the "FrameworkDirectory" metadata to associate an InstalledAssemblyTable
        /// with a particular framework directory.  However, this setting will be ignored unless the Redist name begins with
        /// "Microsoft-Windows-CLRCoreComp".
        /// If there is only a single TargetFrameworkDirectories element, then any items in this list missing the
        /// "FrameworkDirectory" metadata will be treated as though this metadata is set to the lone (unique) value passed
        /// to TargetFrameworkDirectories.
        /// </summary>
        public ITaskItem[] InstalledAssemblyTables
        {
            get { return _installedAssemblyTables; }
            set { _installedAssemblyTables = value; }
        }

        /// <summary>
        /// A list of XML files that contain assemblies that are expected to be in the target subset
        ///
        /// Format of the file is like:
        ///
        ///     <FileList Redist="ClientSubset" >
        ///         <File AssemblyName="System" Version="2.0.0.0" PublicKeyToken="b77a5c561934e089" Culture="neutral" ProcessorArchitecture="MSIL" FileVersion="2.0.40824.0" InGAC="true" />
        ///         etc.
        ///     </FileList>
        ///
        /// Items in this list may optionally specify the "FrameworkDirectory" metadata to associate an InstalledAssemblySubsetTable
        /// with a particular framework directory.
        /// If there is only a single TargetFrameworkDirectories element, then any items in this list missing the
        /// "FrameworkDirectory" metadata will be treated as though this metadata is set to the lone (unique) value passed
        /// to TargetFrameworkDirectories.
        /// </summary>
        public ITaskItem[] InstalledAssemblySubsetTables
        {
            get { return _installedAssemblySubsetTables; }
            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "InstalledAssemblySubsetTables");
                _installedAssemblySubsetTables = value;
            }
        }

        /// <summary>
        /// A list of XML files that contain the full framework for the profile.
        ///
        /// Normally nothing is passed in here, this is for the cases where the location of the xml file for the full framework
        /// is not under a RedistList folder.
        ///
        /// Format of the file is like:
        ///
        ///     <FileList Redist="MatchingRedistListName" >
        ///         <File AssemblyName="System" Version="2.0.0.0" PublicKeyToken="b77a5c561934e089" Culture="neutral" ProcessorArchitecture="MSIL" FileVersion="2.0.40824.0" InGAC="true" />
        ///         etc.
        ///     </FileList>
        ///
        /// Items in this list must specify the "FrameworkDirectory" metadata to associate an redist list
        /// with a particular framework directory. If the association is not made an error will be logged. The reason is,
        /// The logic in rar assumes if a FrameworkDirectory is not set it will use the target framework directory.
        /// </summary>
        public ITaskItem[] FullFrameworkAssemblyTables
        {
            get { return _fullFrameworkAssemblyTables; }
            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "FullFrameworkAssemblyTables");
                _fullFrameworkAssemblyTables = value;
            }
        }

        /// <summary>
        /// [default=false]
        /// Boolean property to control whether or not the task should look for and use additional installed
        /// assembly tables (a.k.a Redist Lists) found in the RedistList directory underneath the provided
        /// TargetFrameworkDirectories.
        /// </summary>
        public bool IgnoreDefaultInstalledAssemblyTables
        {
            get { return _ignoreDefaultInstalledAssemblyTables; }
            set { _ignoreDefaultInstalledAssemblyTables = value; }
        }

        /// <summary>
        /// [default=false]
        /// Boolean property to control whether or not the task should look for and use additional installed
        /// assembly subset tables (a.k.a Subset Lists) found in the SubsetList directory underneath the provided
        /// TargetFrameworkDirectories.
        /// </summary>
        public bool IgnoreDefaultInstalledAssemblySubsetTables
        {
            get { return _ignoreDefaultInstalledAssemblySubsetTables; }
            set { _ignoreDefaultInstalledAssemblySubsetTables = value; }
        }

        /// <summary>
        /// If the primary reference is a framework assembly ignore its version information and actually resolve the framework assembly from the currently targeted framework.
        /// </summary>
        public bool IgnoreVersionForFrameworkReferences
        {
            get { return _ignoreVersionForFrameworkReferences; }
            set { _ignoreVersionForFrameworkReferences = value; }
        }

        /// <summary>
        /// The preferred target processor architecture. Used for resolving {GAC} references.
        /// Should be like x86, IA64 or AMD64.
        ///
        /// This is the order of preference:
        /// (1) Assemblies in the GAC that match the supplied ProcessorArchitecture.
        /// (2) Assemblies in the GAC that have ProcessorArchitecture=MSIL
        /// (3) Assemblies in the GAC that have no ProcessorArchitecture.
        ///
        /// If absent, then only consider assemblies in the GAC that have ProcessorArchitecture==MSIL or
        /// no ProcessorArchitecture (these are pre-Whidbey assemblies).
        /// </summary>
        public string TargetProcessorArchitecture
        {
            get { return _targetProcessorArchitecture; }
            set { _targetProcessorArchitecture = value; }
        }

        /// <summary>
        /// What is the runtime we are targeting, is it 2.0.57027 or anotherone, It can have a v or not prefixed onto it.
        /// </summary>
        public string TargetedRuntimeVersion
        {
            get { return _targetedRuntimeVersionRawValue; }
            set { _targetedRuntimeVersionRawValue = value; }
        }

        /// <summary>
        /// If not null, serializes information about <see cref="AssemblyFiles" /> inputs to the named file.
        /// This overrides the usual outputs, so do not use this unless you are building an SDK with many references.
        /// </summary>
        public string AssemblyInformationCacheOutputPath { get; set; }

        /// <summary>
        /// If not null, uses this set of caches as inputs if RAR cannot find the usual cache in the obj folder. Typically
        /// used for demos and first-run scenarios.
        /// </summary>
        public ITaskItem[] AssemblyInformationCachePaths { get; set; }

        /// <summary>
        /// List of locations to search for assemblyFiles when resolving dependencies.
        /// The following types of things can be passed in here:
        /// (1) A plain old directory path.
        /// (2) {HintPathFromItem} -- Look at the HintPath attribute from the base item.
        ///     This attribute must be a file name *not* a directory name.
        /// (3) {CandidateAssemblyFiles} -- Look at the files passed in through the CandidateAssemblyFiles
        ///     parameter.
        /// (4) {Registry:_AssemblyFoldersBase_,_RuntimeVersion_,_AssemblyFoldersSuffix_}
        ///      Where:
        ///
        ///         _AssemblyFoldersBase_ = Software\Microsoft\[.NetFramework | .NetCompactFramework]
        ///         _RuntimeVersion_ = the runtime version property from the project file
        ///         _AssemblyFoldersSuffix_ = [ PocketPC | SmartPhone | WindowsCE]\AssemblyFoldersEx
        ///
        ///      Then look in the registry for keys with the following schema:
        ///
        ///         [HKLM | HKCU]\SOFTWARE\MICROSOFT\.NetFramework\
        ///           v1.0.3705
        ///             AssemblyFoldersEx
        ///                 ControlVendor.GridControl.1.0:
        ///                     @Default = c:\program files\ControlVendor\grid control\1.0\bin
        ///                     @Description = Grid Control for .NET version 1.0
        ///                     9466
        ///                         @Default = c:\program files\ControlVendor\grid control\1.0sp1\bin
        ///                         @Description = SP1 for Grid Control for .NET version 1.0
        ///
        ///      The based registry key is composed as:
        ///
        ///          [HKLM | HKCU]\_AssemblyFoldersBase_\_RuntimeVersion_\_AssemblyFoldersSuffix_
        ///
        /// (5) {AssemblyFolders} -- Use the VisualStudion 2003 .NET finding-assemblies-from-registry scheme.
        /// (6) {GAC} -- Look in the GAC.
        /// (7) {RawFileName} -- Consider the Include value to be an exact path and file name.
        ///
        ///
        /// </summary>
        /// <value></value>
        [Required]
        public string[] SearchPaths
        {
            get { return _searchPaths; }
            set { _searchPaths = value; }
        }

        /// <summary>
        /// [default=.exe;.dll]
        /// These are the assembly extensions that will be considered during references resolution.
        /// </summary>
        public string[] AllowedAssemblyExtensions
        {
            get { return _allowedAssemblyExtensions; }
            set { _allowedAssemblyExtensions = value; }
        }

        /// <summary>
        /// [default=.pdb;.xml]
        /// These are the extensions that will be considered when looking for related files.
        /// </summary>
        public string[] AllowedRelatedFileExtensions
        {
            get { return _relatedFileExtensions; }
            set { _relatedFileExtensions = value; }
        }

        /// <summary>
        /// If this file name is passed in, then we parse it as an app.config file and extract bindingRedirect mappings. These mappings are used in the dependency
        /// calculation process to remap versions of assemblies.
        ///
        /// If this parameter is passed in, then AutoUnify must be false, otherwise error.
        /// </summary>
        /// <value></value>
        public string AppConfigFile
        {
            get { return _appConfigFile; }
            set { _appConfigFile = value; }
        }

        /// <summary>
        /// This is true if the project type supports "AutoGenerateBindingRedirects" (currently only for EXE projects).
        /// </summary>
        /// <value></value>
        public bool SupportsBindingRedirectGeneration
        {
            get { return _supportsBindingRedirectGeneration; }
            set { _supportsBindingRedirectGeneration = value; }
        }

        /// <summary>
        /// [default=false]
        /// This parameter is used for building assemblies, such as DLLs, which cannot have a normal
        /// App.Config file.
        ///
        /// When true, the resulting dependency graph is automatically treated as if there were an
        /// App.Config file passed in to the AppConfigFile parameter. This virtual
        /// App.Config file has a bindingRedirect entry for each conflicting set of assemblies such
        /// that the highest version assembly is chosen. A consequence of this is that there will never
        /// be a warning about conflicting assemblies because every conflict will have been resolved.
        ///
        /// When true, each distinct remapping will result in a high priority comment indicating the
        /// old and new versions and the fact that this was done automatically because AutoUnify was true.
        ///
        /// When true, the AppConfigFile parameter should be empty. Otherwise, it's an
        /// error.
        ///
        /// When false, no assembly version remapping will occur automatically. When two versions of an
        /// assembly are present, there will be a warning.
        ///
        /// When false, each distinct conflict between different versions of the same assembly will
        /// result in a high priority comment. After all of these comments are displayed, there will be
        /// a single warning with a unique error code and text that reads "Found conflicts between
        /// different versions of reference and dependent assemblies".
        /// </summary>
        /// <value></value>
        public bool AutoUnify
        {
            get { return _autoUnify; }
            set { _autoUnify = value; }
        }

        /// <summary>
        ///  When determining if a dependency should be copied locally one of the checks done is to see if the
        ///  parent reference in the project file has the Private metadata set or not. If that metadata is set then
        ///  We will use that for the dependency as well.
        ///
        /// However, if the metadata is not set then the dependency will go through the same checks as the parent reference.
        /// One of these checks is to see if the reference is in the GAC. If a reference is in the GAC then we will not copy it locally
        /// as it is assumed it will be in the gac on the target machine as well. However this only applies to that specific reference and not its dependencies.
        ///
        /// This means a reference in the project file may be copy local false due to it being in the GAC but the dependencies may still be copied locally because they are not in the GAC.
        /// This is the default behavior for RAR and causes the default value for this property to be true.
        ///
        /// When this property is false we will still check project file references to see if they are in the GAC and set their copy local state as appropriate.
        /// However for dependencies we will not only check to see if they are in the GAC but we will also check to see if the parent reference from the project file is in the GAC.
        /// If the parent reference from the project file is in the GAC then we will not copy the dependency locally.
        ///
        /// NOTE: If there are multiple parent reference and ANY of them does not come from the GAC then we will set copy local to true.
        /// </summary>
        public bool CopyLocalDependenciesWhenParentReferenceInGac
        {
            get { return _copyLocalDependenciesWhenParentReferenceInGac; }
            set { _copyLocalDependenciesWhenParentReferenceInGac = value; }
        }

        /// <summary>
        /// [default=false]
        /// Enables legacy mode for CopyLocal determination. If true, referenced assemblies will not be copied locally if they
        /// are found in the GAC. If false, assemblies will be copied locally unless they were found only in the GAC.
        /// </summary>
        public bool DoNotCopyLocalIfInGac
        {
            get;
            set;
        }

        /// <summary>
        /// An optional file name that indicates where to save intermediate build state
        /// for this task. If not specified, then no inter-build caching will occur.
        /// </summary>
        /// <value></value>
        public string StateFile
        {
            get { return _stateFile; }
            set { _stateFile = value; }
        }

        /// <summary>
        /// If set, then dependencies will be found. Otherwise, only Primary references will be
        /// resolved.
        ///
        /// Default is true.
        /// </summary>
        /// <value></value>
        public bool FindDependencies
        {
            get { return _findDependencies; }
            set { _findDependencies = value; }
        }

        /// <summary>
        /// If set, then satellites will be found.
        ///
        /// Default is true.
        /// </summary>
        /// <value></value>
        public bool FindSatellites
        {
            get { return _findSatellites; }
            set { _findSatellites = value; }
        }

        /// <summary>
        /// If set, then serialization assemblies will be found.
        ///
        /// Default is true.
        /// </summary>
        /// <value></value>
        public bool FindSerializationAssemblies
        {
            get { return _findSerializationAssemblies; }
            set { _findSerializationAssemblies = value; }
        }

        /// <summary>
        /// If set, then related files (.pdbs and .xmls) will be found.
        ///
        /// Default is true.
        /// </summary>
        /// <value></value>
        public bool FindRelatedFiles
        {
            get { return _findRelatedFiles; }
            set { _findRelatedFiles = value; }
        }

        /// <summary>
        /// If set, then don't log any messages to the screen.
        ///
        /// Default is false.
        /// </summary>
        /// <value></value>
        public bool Silent
        {
            get { return _silent; }
            set { _silent = value; }
        }

        /// <summary>
        /// The project target framework version.
        ///
        /// Default is empty. which means there will be no filtering for the reference based on their target framework.
        /// </summary>
        /// <value></value>
        public string TargetFrameworkVersion
        {
            get { return _projectTargetFrameworkAsString; }
            set { _projectTargetFrameworkAsString = value; }
        }

        /// <summary>
        /// The target framework moniker we are targeting if any. This is used for logging purposes.
        ///
        /// Default is empty.
        /// </summary>
        /// <value></value>
        public string TargetFrameworkMoniker
        {
            get { return _targetedFrameworkMoniker; }
            set { _targetedFrameworkMoniker = value; }
        }

        /// <summary>
        /// The display name of the target framework moniker, if any. This is only for logging.
        /// </summary>
        public string TargetFrameworkMonikerDisplayName
        {
            get;
            set;
        }

        /// <summary>
        /// Provide a set of names which if seen in the TargetFrameworkSubset list will cause the ignoring
        /// of TargetFrameworkSubsets.
        ///
        /// Full, Complete
        /// </summary>
        public string[] FullTargetFrameworkSubsetNames
        {
            get
            {
                return _fullTargetFrameworkSubsetNames;
            }

            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "FullTargetFrameworkSubsetNames");
                _fullTargetFrameworkSubsetNames = value;
            }
        }

        /// <summary>
        /// Name of the target framework profile we are targeting.
        /// Eg. Client, Web, or Network
        /// </summary>
        public string ProfileName
        {
            get
            {
                return _profileName;
            }

            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "profileName");
                _profileName = value;
            }
        }

        /// <summary>
        /// Set of folders which containd a RedistList directory which represent the full framework for a given client profile.
        /// An example would be
        /// %programfiles%\reference assemblies\microsoft\framework\v4.0
        /// </summary>
        public string[] FullFrameworkFolders
        {
            get
            {
                return _fullFrameworkFolders;
            }

            set
            {
                ErrorUtilities.VerifyThrowArgumentNull(value, "FullFrameworkFolders");
                _fullFrameworkFolders = value;
            }
        }

        /// <summary>
        /// This is a list of all primary references resolved to full paths.
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        ///     string FusionName - the fusion name for this dependency.
        ///     string ResolvedFrom - the literal search path that this file was resolved from.
        ///     bool IsRedistRoot - Whether or not this assembly is the representative for an entire redist.
        ///         'true' means the assembly is representative of an entire redist and should be indicated as
        ///         an application dependency in an application manifest.
        ///         'false' means the assembly is internal to a redist and should not be part of the
        ///         application manifest.
        ///     string Redist - The name (if any) of the redist that contains this assembly.
        /// </summary>
        [Output]
        public ITaskItem[] ResolvedFiles
        {
            get { return _resolvedFiles; }
        }

        /// <summary>
        /// A list of all n-th order paths-to-dependencies with the following attributes:
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        ///     string FusionName - the fusion name for this dependency.
        ///     string ResolvedFrom - the literal search path that this file was resolved from.
        ///     bool IsRedistRoot - Whether or not this assembly is the representative for an entire redist.
        ///         'true' means the assembly is representative of an entire redist and should be indicated as
        ///         an application dependency in an application manifest.
        ///         'false' means the assembly is internal to a redist and should not be part of the
        ///         application manifest.
        ///     string Redist - The name (if any) of the redist that contains this assembly.
        /// Does not include first order primary references--this list is in ResolvedFiles.
        /// </summary>
        [Output]
        public ITaskItem[] ResolvedDependencyFiles
        {
            get { return _resolvedDependencyFiles; }
        }

        /// <summary>
        /// Related files are files like intellidoc (.XML) and symbols (.PDB) that have the same base
        /// name as a reference.
        ///     bool Primary [always false] - true if this assembly was passed in with Assemblies.
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        /// </summary>
        [Output]
        public ITaskItem[] RelatedFiles
        {
            get { return _relatedFiles; }
        }

        /// <summary>
        /// Any satellite files found. These will be CopyLocal=true iff the reference or dependency
        /// that caused this item to exist is CopyLocal=true.
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        ///     string DestinationSubDirectory - the relative destination directory that this file
        ///       should be copied to. This is mainly for satellites.
        /// </summary>
        [Output]
        public ITaskItem[] SatelliteFiles
        {
            get { return _satelliteFiles; }
        }

        /// <summary>
        /// Any XML serialization assemblies found. These will be CopyLocal=true iff the reference or dependency
        /// that caused this item to exist is CopyLocal=true.
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        /// </summary>
        [Output]
        public ITaskItem[] SerializationAssemblyFiles
        {
            get { return _serializationAssemblyFiles; }
        }

        /// <summary>
        /// Scatter files associated with one of the given assemblies.
        ///     bool CopyLocal - whether the given reference should be copied to the output directory.
        /// </summary>
        [Output]
        public ITaskItem[] ScatterFiles
        {
            get { return _scatterFiles; }
        }

        /// <summary>
        /// Returns every file in ResolvedFiles+ResolvedDependencyFiles+RelatedFiles+SatelliteFiles+ScatterFiles+SatelliteAssemblyFiles
        /// that have CopyLocal flags set to 'true'.
        /// </summary>
        /// <value></value>
        [Output]
        public ITaskItem[] CopyLocalFiles
        {
            get { return _copyLocalFiles; }
        }

        /// <summary>
        /// Regardless of the value of AutoUnify, returns one item for every distinct conflicting assembly
        /// identity--including culture and PKT--that was found that did not have a suitable bindingRedirect
        /// entry in the ApplicationConfigurationFile.
        ///
        /// Each returned ITaskItem will have the following values:
        ///  ItemSpec - the full fusion name of the assembly family with empty version=0.0.0.0
        ///  MaxVersion - the maximum version number.
        /// </summary>
        [Output]
        public ITaskItem[] SuggestedRedirects
        {
            get { return _suggestedRedirects; }
        }

        /// <summary>
        /// Storage for names of all files writen to disk.
        /// </summary>
        private List<ITaskItem> _filesWritten = new List<ITaskItem>();

        /// <summary>
        /// The names of all files written to disk.
        /// </summary>
        [Output]
        public ITaskItem[] FilesWritten
        {
            set { /*Do Nothing, Inputs not Allowed*/ }
            get { return _filesWritten.ToArray(); }
        }

        /// <summary>
        /// Whether the assembly or any of its primary references depends on system.runtime. (Aka needs Facade references to resolve duplicate types)
        /// </summary>
        [Output]
        public String DependsOnSystemRuntime
        {
            get;
            private set;
        }

        /// <summary>
        /// Whether the assembly or any of its primary references depends on netstandard
        /// </summary>
        [Output]
        public String DependsOnNETStandard
        {
            get;
            private set;
        }

        /// <summary>
        /// If OutputUnresolvedAssemblyConflicts then a list of information about unresolved conflicts that normally would have
        /// been outputted in MSB3277. Otherwise empty.
        /// </summary>
        [Output]
        public ITaskItem[] UnresolvedAssemblyConflicts => _unresolvedConflicts.ToArray();

        #endregion
        #region Logging

        /// <summary>
        /// Log the results.
        /// </summary>
        /// <param name="dependencyTable">Reference table.</param>
        /// <param name="idealAssemblyRemappings">Array of ideal assembly remappings.</param>
        /// <param name="idealAssemblyRemappingsIdentities">Array of identities of ideal assembly remappings.</param>
        /// <param name="generalResolutionExceptions">List of exceptions that were not attributable to a particular fusion name.</param>
        /// <returns></returns>
        private bool LogResults
        (
            ReferenceTable dependencyTable,
            List<DependentAssembly> idealAssemblyRemappings,
            List<AssemblyNameReference> idealAssemblyRemappingsIdentities,
            List<Exception> generalResolutionExceptions
        )
        {
            bool success = true;
            MSBuildEventSource.Log.RarLogResultsStart();
            {
                /*
                PERF NOTE: The Silent flag turns off logging completely from the task side. This means
                we avoid the String.Formats that would normally occur even if the verbosity was set to 
                quiet at the engine level.
                */
                if (!Silent)
                {
                    // First, loop over primaries and display information.
                    foreach (AssemblyNameExtension assemblyName in dependencyTable.References.Keys)
                    {
                        string fusionName = assemblyName.FullName;
                        Reference primaryCandidate = dependencyTable.GetReference(assemblyName);

                        if (primaryCandidate.IsPrimary && !(primaryCandidate.IsConflictVictim && primaryCandidate.IsCopyLocal))
                        {
                            LogReference(primaryCandidate, fusionName);
                        }
                    }

                    // Second, loop over dependencies and display information.
                    foreach (AssemblyNameExtension assemblyName in dependencyTable.References.Keys)
                    {
                        string fusionName = assemblyName.FullName;
                        Reference dependencyCandidate = dependencyTable.GetReference(assemblyName);

                        if (!dependencyCandidate.IsPrimary && !(dependencyCandidate.IsConflictVictim && dependencyCandidate.IsCopyLocal))
                        {
                            LogReference(dependencyCandidate, fusionName);
                        }
                    }

                    // Third, show conflicts and their resolution.
                    foreach (AssemblyNameExtension assemblyName in dependencyTable.References.Keys)
                    {
                        string fusionName = assemblyName.FullName;
                        Reference conflictCandidate = dependencyTable.GetReference(assemblyName);

                        if (conflictCandidate.IsConflictVictim)
                        {
                            bool logWarning = idealAssemblyRemappingsIdentities.Any(i => i.assemblyName.FullName.Equals(fusionName) && i.reference.GetConflictVictims().Count == 0);
                            StringBuilder logConflict = StringBuilderCache.Acquire();
                            LogConflict(conflictCandidate, fusionName, logConflict);
                            StringBuilder logDependencies = logWarning ? logConflict.AppendLine() : StringBuilderCache.Acquire();

                            // Log the assemblies and primary source items which are related to the conflict which was just logged.
                            Reference victor = dependencyTable.GetReference(conflictCandidate.ConflictVictorName);

                            // Log the winner of the conflict resolution, the source items and dependencies which caused it
                            LogReferenceDependenciesAndSourceItemsToStringBuilder(conflictCandidate.ConflictVictorName.FullName, victor, logDependencies);

                            // Log the reference which lost the conflict and the dependencies and source items which caused it.
                            LogReferenceDependenciesAndSourceItemsToStringBuilder(fusionName, conflictCandidate, logDependencies.AppendLine());

                            string output = StringBuilderCache.GetStringAndRelease(logConflict);
                            string details = string.Empty;
                            if (logWarning)
                            {
                                // This warning is logged regardless of AutoUnify since it means a conflict existed where the reference	
                                // chosen was not the conflict victor in a version comparison. In other words, the victor was older.
                                Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.FoundConflicts", assemblyName.Name, output);
                            }
                            else
                            {
                                details = StringBuilderCache.GetStringAndRelease(logDependencies);
                                Log.LogMessage(ChooseReferenceLoggingImportance(conflictCandidate), output);
                                Log.LogMessage(MessageImportance.Low, details);
                            }

                            if (OutputUnresolvedAssemblyConflicts)
                            {
                                _unresolvedConflicts.Add(new TaskItem(assemblyName.Name, new Dictionary<string, string>()
                                {
                                    { "logMessage", output },
                                    { "logMessageDetails", details },
                                    { "victorVersionNumber", victor.ReferenceVersion?.ToString() },
                                    { "victimVersionNumber", conflictCandidate.ReferenceVersion?.ToString() }
                                }));
                            }
                        }
                    }

                    // Fourth, if there were any suggested redirects. Show one message per redirect and a single warning.
                    if (idealAssemblyRemappings != null)
                    {
                        bool foundAtLeastOneValidBindingRedirect = false;

                        var buffer = new StringBuilder();
                        var ns = XNamespace.Get("urn:schemas-microsoft-com:asm.v1");

                        // A high-priority message for each individual redirect.
                        for (int i = 0; i < idealAssemblyRemappings.Count; i++)
                        {
                            DependentAssembly idealRemapping = idealAssemblyRemappings[i];
                            AssemblyName idealRemappingPartialAssemblyName = idealRemapping.PartialAssemblyName;
                            Reference reference = idealAssemblyRemappingsIdentities[i].reference;

                            List<AssemblyNameExtension> conflictVictims = reference.GetConflictVictims();

                            for (int j = 0; j < idealRemapping.BindingRedirects.Count; j++)
                            {
                                foreach (AssemblyNameExtension conflictVictim in conflictVictims)
                                {
                                    // Make note we only output a conflict suggestion if the reference has at 
                                    // least one conflict victim - that way we don't suggest redirects to 
                                    // assemblies that don't exist at runtime. For example, this avoids us suggesting
                                    // a redirect from Foo 1.0.0.0 -> 2.0.0.0 in the following:
                                    //
                                    //      Project -> Foo, 1.0.0.0
                                    //      Project -> Bar -> Foo, 2.0.0.0
                                    //
                                    // Above, Foo, 1.0.0.0 wins out and is copied to the output directory because 
                                    // it is a primary reference.
                                    foundAtLeastOneValidBindingRedirect = true;

                                    Reference victimReference = dependencyTable.GetReference(conflictVictim);
                                    var newVerStr = idealRemapping.BindingRedirects[j].NewVersion.ToString();
                                    Log.LogMessageFromResources
                                    (
                                        MessageImportance.High,
                                        "ResolveAssemblyReference.ConflictRedirectSuggestion",
                                        idealRemappingPartialAssemblyName,
                                        conflictVictim.Version,
                                        victimReference.FullPath,
                                        newVerStr,
                                        reference.FullPath
                                    );

                                    if (!SupportsBindingRedirectGeneration && !AutoUnify)
                                    {
                                        // When running against projects types (such as Web Projects) where we can't auto-generate
                                        // binding redirects during the build, populate a buffer (to be output below) with the
                                        // binding redirect syntax that users need to add manually to the App.Config.

                                        var assemblyIdentityAttributes = new List<XAttribute>(4);

                                        assemblyIdentityAttributes.Add(new XAttribute("name", idealRemappingPartialAssemblyName.Name));

                                        // We use "neutral" for "Invariant Language (Invariant Country)" in assembly names.
                                        var cultureString = idealRemappingPartialAssemblyName.CultureName;
                                        assemblyIdentityAttributes.Add(new XAttribute("culture", String.IsNullOrEmpty(idealRemappingPartialAssemblyName.CultureName) ? "neutral" : idealRemappingPartialAssemblyName.CultureName));

                                        var publicKeyToken = idealRemappingPartialAssemblyName.GetPublicKeyToken();
                                        assemblyIdentityAttributes.Add(new XAttribute("publicKeyToken", ResolveAssemblyReference.ByteArrayToString(publicKeyToken)));

                                        var node = new XElement(
                                            ns + "assemblyBinding",
                                            new XElement(
                                                ns + "dependentAssembly",
                                                new XElement(
                                                    ns + "assemblyIdentity",
                                                    assemblyIdentityAttributes),
                                                new XElement(
                                                    ns + "bindingRedirect",
                                                    new XAttribute("oldVersion", "0.0.0.0-" + newVerStr),
                                                    new XAttribute("newVersion", newVerStr))));

                                        buffer.Append(node.ToString(SaveOptions.DisableFormatting));
                                    }
                                }
                            }
                        }

                        // Log the warning
                        if (idealAssemblyRemappings.Count > 0 && foundAtLeastOneValidBindingRedirect)
                        {
                            if (SupportsBindingRedirectGeneration)
                            {
                                if (!AutoUnify)
                                {
                                    Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.TurnOnAutoGenerateBindingRedirects");
                                }
                                // else we'll generate bindingRedirects to address the remappings
                            }
                            else if (!AutoUnify)
                            {
                                Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.SuggestedRedirects", buffer.ToString());
                            }
                            // else AutoUnify is on and bindingRedirect generation is not supported
                            // we don't warn in this case since the binder will automatically unify these remappings
                        }
                    }

                    // Fifth, log general resolution problems.

                    // Log general resolution exceptions.
                    foreach (Exception error in generalResolutionExceptions)
                    {
                        if (error is InvalidReferenceAssemblyNameException)
                        {
                            InvalidReferenceAssemblyNameException e = (InvalidReferenceAssemblyNameException)error;
                            Log.LogWarningWithCodeFromResources("General.MalformedAssemblyName", e.SourceItemSpec);
                        }
                        else
                        {
                            // An unknown Exception type was returned. Just throw.
                            throw error;
                        }
                    }
                }
            }

#if FEATURE_WIN32_REGISTRY
            if (dependencyTable.Resolvers != null)
            {
                foreach (Resolver r in dependencyTable.Resolvers)
                {
                    if (r is AssemblyFoldersExResolver)
                    {
                        AssemblyFoldersEx assemblyFoldersEx = ((AssemblyFoldersExResolver)r).AssemblyFoldersExLocations;

                        MessageImportance messageImportance = MessageImportance.Low;
                        if (assemblyFoldersEx != null && _showAssemblyFoldersExLocations.TryGetValue(r.SearchPath, out messageImportance))
                        {
                            Log.LogMessageFromResources(messageImportance, "ResolveAssemblyReference.AssemblyFoldersExSearchLocations", r.SearchPath);
                            foreach (var path in assemblyFoldersEx.UniqueDirectoryPaths)
                            {
                                Log.LogMessageFromResources(messageImportance, "ResolveAssemblyReference.EightSpaceIndent", path);
                            }
                        }
                    }
                }
            }
#endif

            MSBuildEventSource.Log.RarLogResultsStop();

            return success;
        }

        /// <summary>
        /// Used to generate the string representation of a public key token.
        /// </summary>
        internal static string ByteArrayToString(byte[] a)
        {
            if (a == null)
            {
                return null;
            }

            var buffer = new StringBuilder(a.Length * 2);
            for (int i = 0; i < a.Length; ++i)
                buffer.Append(a[i].ToString("x2", CultureInfo.InvariantCulture));

            return buffer.ToString();
        }

        /// <summary>
        /// Log the source items and dependencies which lead to a given item.
        /// </summary>
        private void LogReferenceDependenciesAndSourceItemsToStringBuilder(string fusionName, Reference conflictCandidate, StringBuilder log)
        {
            ErrorUtilities.VerifyThrowInternalNull(conflictCandidate, nameof(conflictCandidate));
            log.Append(Strings.FourSpaces);
            log.Append(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("ResolveAssemblyReference.ReferenceDependsOn", fusionName, conflictCandidate.FullPath));

            if (conflictCandidate.IsPrimary)
            {
                if (conflictCandidate.IsResolved)
                {
                    LogDependeeReferenceToStringBuilder(conflictCandidate, log);
                }
                else
                {
                    log
                        .AppendLine()
                        .Append(Strings.EightSpaces)
                        .Append(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("ResolveAssemblyReference.UnResolvedPrimaryItemSpec", conflictCandidate.PrimarySourceItem));
                }
            }

            // Log the references for the conflict victim
            foreach (Reference dependeeReference in conflictCandidate.GetDependees())
            {
                LogDependeeReferenceToStringBuilder(dependeeReference, log);
            }
        }

        /// <summary>
        /// Log the dependee and the item specs which caused the dependee reference to be resolved.
        /// </summary>
        /// <param name="dependeeReference"></param>
        /// <param name="log">The means by which messages should be logged.</param>
        private void LogDependeeReferenceToStringBuilder(Reference dependeeReference, StringBuilder log)
        {
            log.AppendLine().Append(Strings.EightSpaces).AppendLine(dependeeReference.FullPath);

            log.Append(Strings.TenSpaces).Append(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("ResolveAssemblyReference.PrimarySourceItemsForReference", dependeeReference.FullPath));
            foreach (ITaskItem sourceItem in dependeeReference.GetSourceItems())
            {
                log.AppendLine().Append(Strings.TwelveSpaces).Append(sourceItem.ItemSpec);
            }
        }

        /// <summary>
        /// Display the information about how a reference was resolved.
        /// </summary>
        /// <param name="reference">The reference information</param>
        /// <param name="fusionName">The fusion name of the reference.</param>
        private void LogReference(Reference reference, string fusionName)
        {
            // Set an importance level to be used for secondary messages.
            MessageImportance importance = ChooseReferenceLoggingImportance(reference);

            // Log the fusion name and whether this is a primary or a dependency.
            LogPrimaryOrDependency(reference, fusionName, importance);

            // Are there errors to report for this item?
            LogReferenceErrors(reference, importance);

            // Show the full name.
            LogFullName(reference, importance);

            // If there is a list of assemblyFiles that was considered but then rejected,
            // show information about them.
            LogAssembliesConsideredAndRejected(reference, fusionName, importance);

            if (!reference.IsBadImage)
            {
                // Show the files that made this dependency necessary.
                LogDependees(reference, importance);

                // If there were any related files (like pdbs and xmls) then show them here.
                LogRelatedFiles(reference, importance);

                // If there were any satellite files then show them here.
                LogSatellites(reference, importance);

                // If there were any scatter files then show them.
                LogScatterFiles(reference, importance);

                // Show the CopyLocal state
                LogCopyLocalState(reference, importance);

                // Show the CopyLocal state
                LogImageRuntime(reference, importance);
            }
        }

        /// <summary>
        /// Choose an importance level for reporting information about this reference.
        /// </summary>
        /// <param name="reference">The reference.</param>
        private MessageImportance ChooseReferenceLoggingImportance(Reference reference)
        {
            MessageImportance importance = MessageImportance.Low;

            bool hadProblems = reference.GetErrors().Count > 0;

            // No problems means low importance.
            if (hadProblems)
            {
                if (reference.IsPrimary || reference.IsCopyLocal)
                {
                    // The user cares more about Primary files and CopyLocal files.
                    // Accordingly, we show messages about these files only in the higher verbosity levels
                    // but only if there were errors during the resolution process.
                    importance = MessageImportance.Normal;
                }
            }

            return importance;
        }

        /// <summary>
        /// Log all task inputs.
        /// </summary>
        private void LogInputs()
        {
            if (Traits.Instance.EscapeHatches.LogTaskInputs || Silent)
            {
                // the inputs will be logged automatically anyway, avoid duplication in the logs
                return;
            }

            string indent = Strings.FourSpaces;
            string property = Strings.LogTaskPropertyFormat;
            MessageImportance importance = MessageImportance.Low;

            Log.LogMessage(importance, property, "TargetFrameworkMoniker");
            Log.LogMessage(importance, indent + _targetedFrameworkMoniker);

            Log.LogMessage(importance, property, "TargetFrameworkMonikerDisplayName");
            Log.LogMessage(importance, indent + TargetFrameworkMonikerDisplayName);

            Log.LogMessage(importance, property, "TargetedRuntimeVersion");
            Log.LogMessage(importance, indent + _targetedRuntimeVersionRawValue);

            Log.LogMessage(importance, property, "Assemblies");
            foreach (ITaskItem item in Assemblies)
            {
                Log.LogMessage(importance, indent + item.ItemSpec);
                LogAttribute(item, ItemMetadataNames.privateMetadata);
                LogAttribute(item, ItemMetadataNames.hintPath);
                LogAttribute(item, ItemMetadataNames.specificVersion);
                LogAttribute(item, ItemMetadataNames.embedInteropTypes);
                LogAttribute(item, ItemMetadataNames.executableExtension);
                LogAttribute(item, ItemMetadataNames.subType);
            }

            Log.LogMessage(importance, property, "AssemblyFiles");
            foreach (ITaskItem item in AssemblyFiles)
            {
                Log.LogMessage(importance, indent + item.ItemSpec);
                LogAttribute(item, ItemMetadataNames.privateMetadata);
                LogAttribute(item, ItemMetadataNames.fusionName);
            }

            Log.LogMessage(importance, property, "CandidateAssemblyFiles");
            foreach (string file in CandidateAssemblyFiles)
            {
                try
                {
                    if (FileUtilities.HasExtension(file, _allowedAssemblyExtensions))
                    {
                        Log.LogMessage(importance, indent + file);
                    }
                }
                catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
                {
                    throw new InvalidParameterValueException("CandidateAssemblyFiles", file, e.Message);
                }
            }

            Log.LogMessage(importance, property, "TargetFrameworkDirectories");
            Log.LogMessage(importance, indent + String.Join(",", TargetFrameworkDirectories));

            Log.LogMessage(importance, property, "InstalledAssemblyTables");
            foreach (ITaskItem installedAssemblyTable in InstalledAssemblyTables)
            {
                Log.LogMessage(importance, indent + installedAssemblyTable);
                LogAttribute(installedAssemblyTable, ItemMetadataNames.frameworkDirectory);
            }

            Log.LogMessage(importance, property, "IgnoreInstalledAssemblyTable");
            Log.LogMessage(importance, indent + _ignoreDefaultInstalledAssemblyTables);

            Log.LogMessage(importance, property, "SearchPaths");
            foreach (string path in SearchPaths)
            {
                Log.LogMessage(importance, indent + path);
            }

            Log.LogMessage(importance, property, "AllowedAssemblyExtensions");
            foreach (string allowedAssemblyExtension in _allowedAssemblyExtensions)
            {
                Log.LogMessage(importance, indent + allowedAssemblyExtension);
            }

            Log.LogMessage(importance, property, "AllowedRelatedFileExtensions");
            foreach (string allowedRelatedFileExtension in _relatedFileExtensions)
            {
                Log.LogMessage(importance, indent + allowedRelatedFileExtension);
            }

            Log.LogMessage(importance, property, "AppConfigFile");
            Log.LogMessage(importance, indent + AppConfigFile);

            Log.LogMessage(importance, property, "AutoUnify");
            Log.LogMessage(importance, indent + AutoUnify.ToString());

            Log.LogMessage(importance, property, "CopyLocalDependenciesWhenParentReferenceInGac");
            Log.LogMessage(importance, indent + _copyLocalDependenciesWhenParentReferenceInGac);

            Log.LogMessage(importance, property, "FindDependencies");
            Log.LogMessage(importance, indent + _findDependencies);

            Log.LogMessage(importance, property, "TargetProcessorArchitecture");
            Log.LogMessage(importance, indent + TargetProcessorArchitecture);

            Log.LogMessage(importance, property, "StateFile");
            Log.LogMessage(importance, indent + StateFile);

            Log.LogMessage(importance, property, "InstalledAssemblySubsetTables");
            foreach (ITaskItem installedAssemblySubsetTable in InstalledAssemblySubsetTables)
            {
                Log.LogMessage(importance, indent + installedAssemblySubsetTable);
                LogAttribute(installedAssemblySubsetTable, ItemMetadataNames.frameworkDirectory);
            }

            Log.LogMessage(importance, property, "IgnoreInstalledAssemblySubsetTable");
            Log.LogMessage(importance, indent + _ignoreDefaultInstalledAssemblySubsetTables);

            Log.LogMessage(importance, property, "TargetFrameworkSubsets");
            foreach (string subset in _targetFrameworkSubsets)
            {
                Log.LogMessage(importance, indent + subset);
            }

            Log.LogMessage(importance, property, "FullTargetFrameworkSubsetNames");
            foreach (string subset in FullTargetFrameworkSubsetNames)
            {
                Log.LogMessage(importance, indent + subset);
            }

            Log.LogMessage(importance, property, "ProfileName");
            Log.LogMessage(importance, indent + ProfileName);

            Log.LogMessage(importance, property, "FullFrameworkFolders");
            foreach (string fullFolder in FullFrameworkFolders)
            {
                Log.LogMessage(importance, indent + fullFolder);
            }

            Log.LogMessage(importance, property, "LatestTargetFrameworkDirectories");
            foreach (string latestFolder in _latestTargetFrameworkDirectories)
            {
                Log.LogMessage(importance, indent + latestFolder);
            }

            Log.LogMessage(importance, property, "ProfileTablesLocation");
            foreach (ITaskItem profileTable in FullFrameworkAssemblyTables)
            {
                Log.LogMessage(importance, indent + profileTable);
                LogAttribute(profileTable, ItemMetadataNames.frameworkDirectory);
            }
        }

        /// <summary>
        /// Log a specific item metadata.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="metadataName"></param>
        private void LogAttribute(ITaskItem item, string metadataName)
        {
            string metadataValue = item.GetMetadata(metadataName);
            if (!string.IsNullOrEmpty(metadataValue))
            {
                Log.LogMessage(MessageImportance.Low, Strings.LogAttributeFormat, metadataName, metadataValue);
            }
        }

        /// <summary>
        /// Describes whether this reference is primary or not
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="fusionName">The fusion name for this reference.</param>
        /// <param name="importance">The importance of the message.</param>
        private void LogPrimaryOrDependency(Reference reference, string fusionName, MessageImportance importance)
        {
            if (reference.IsPrimary)
            {
                if (reference.IsUnified)
                {
                    Log.LogMessage(importance, Strings.UnifiedPrimaryReference, fusionName);
                }
                else
                {
                    Log.LogMessage(importance, Strings.PrimaryReference, fusionName);
                }
            }
            else
            {
                if (reference.IsUnified)
                {
                    Log.LogMessage(importance, Strings.UnifiedDependency, fusionName);
                }
                else
                {
                    Log.LogMessage(importance, Strings.Dependency, fusionName);
                }
            }

            foreach (UnificationVersion unificationVersion in reference.GetPreUnificationVersions())
            {
                switch (unificationVersion.reason)
                {
                    case UnificationReason.BecauseOfBindingRedirect:
                        if (AutoUnify)
                        {
                            Log.LogMessage(importance, Strings.UnificationByAutoUnify, unificationVersion.version, unificationVersion.referenceFullPath);
                        }
                        else
                        {
                            Log.LogMessage(importance, Strings.UnificationByAppConfig, unificationVersion.version, _appConfigFile, unificationVersion.referenceFullPath);
                        }
                        break;

                    case UnificationReason.FrameworkRetarget:
                        Log.LogMessage(importance, Strings.UnificationByFrameworkRetarget, unificationVersion.version, unificationVersion.referenceFullPath);
                        break;

                    case UnificationReason.DidntUnify:
                        break;

                    default:
                        Debug.Assert(false, "Should have handled this case.");
                        break;
                }
            }

            foreach (AssemblyRemapping remapping in reference.RemappedAssemblyNames())
            {
                Log.LogMessage(importance, Strings.RemappedReference, remapping.From.FullName, remapping.To.FullName);
            }
        }

        /// <summary>
        /// Log any errors for a reference.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="importance">The importance of the message.</param>
        private void LogReferenceErrors(Reference reference, MessageImportance importance)
        {
            List<Exception> itemErrors = reference.GetErrors();
            foreach (Exception itemError in itemErrors)
            {
                string message = String.Empty;
                string helpKeyword = null;
                bool dependencyProblem = false;

                if (itemError is ReferenceResolutionException)
                {
                    message = Log.FormatResourceString("ResolveAssemblyReference.FailedToResolveReference", itemError.Message);
                    helpKeyword = "MSBuild.ResolveAssemblyReference.FailedToResolveReference";
                    dependencyProblem = false;
                }
                else if (itemError is DependencyResolutionException)
                {
                    message = Log.FormatResourceString("ResolveAssemblyReference.FailedToFindDependentFiles", itemError.Message);
                    helpKeyword = "MSBuild.ResolveAssemblyReference.FailedToFindDependentFiles";
                    dependencyProblem = true;
                }
                else if (itemError is BadImageReferenceException)
                {
                    message = Log.FormatResourceString("ResolveAssemblyReference.FailedWithException", itemError.InnerException?.ToString() ?? itemError.ToString());
                    helpKeyword = "MSBuild.ResolveAssemblyReference.FailedWithException";
                    dependencyProblem = false;
                }
                else
                {
                    Debug.Assert(false, "Unexpected exception type.");
                }

                string warningCode = Log.ExtractMessageCode(message, out string messageOnly);

                // Treat as warning if this is primary and the problem wasn't with a dependency, otherwise, make it a comment.
                if (reference.IsPrimary && !dependencyProblem)
                {
                    // Treat it as a warning
                    Log.LogWarning(
                        subcategory: null,
                        warningCode,
                        helpKeyword,
                        file: null,
                        lineNumber: 0,
                        columnNumber: 0,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message: messageOnly);
                }
                else
                {
                    // Just show the the message as a comment.
                    Log.LogMessage(importance, Strings.FourSpaces + messageOnly);
                }
            }
        }

        /// <summary>
        /// Show the full name of a reference.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="importance">The importance of the message.</param>
        private void LogFullName(Reference reference, MessageImportance importance)
        {
            ErrorUtilities.VerifyThrowArgumentNull(reference, nameof(reference));

            if (reference.IsResolved)
            {
                Log.LogMessage(importance, Strings.Resolved, reference.FullPath);
                Log.LogMessage(importance, Strings.ResolvedFrom, reference.ResolvedSearchPath);
            }
        }

        /// <summary>
        /// If there is a list of assemblyFiles that was considered but then rejected,
        /// show information about them.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="fusionName">The fusion name.</param>
        /// <param name="importance">The importance of the message.</param>
        private void LogAssembliesConsideredAndRejected(Reference reference, string fusionName, MessageImportance importance)
        {
            if (reference.AssembliesConsideredAndRejected != null)
            {
                string lastSearchPath = null;

                foreach (ResolutionSearchLocation location in reference.AssembliesConsideredAndRejected)
                {
                    // We need to keep track if whether or not we need to log the assemblyfoldersex folder structure at the end of RAR.
                    // We only need to do so if we logged a message indicating we looked in the assemblyfoldersex location
                    bool containsAssemblyFoldersExSentinel = String.Compare(location.SearchPath, 0, AssemblyResolutionConstants.assemblyFoldersExSentinel, 0, AssemblyResolutionConstants.assemblyFoldersExSentinel.Length, StringComparison.OrdinalIgnoreCase) == 0;
                    bool logAssemblyFoldersMinimal = containsAssemblyFoldersExSentinel && !_logVerboseSearchResults;
                    if (logAssemblyFoldersMinimal)
                    {
                        // We not only need to track if we logged a message but also what importance. We want the logging of the assemblyfoldersex folder structure to match the same importance.
                        MessageImportance messageImportance = MessageImportance.Low;
                        if (!_showAssemblyFoldersExLocations.TryGetValue(location.SearchPath, out messageImportance))
                        {
                            _showAssemblyFoldersExLocations.Add(location.SearchPath, importance);
                        }

                        if ((messageImportance == MessageImportance.Low && (importance == MessageImportance.Normal || importance == MessageImportance.High)) ||
                            (messageImportance == MessageImportance.Normal && importance == MessageImportance.High)
                           )
                        {
                            _showAssemblyFoldersExLocations[location.SearchPath] = importance;
                        }
                    }

                    // If this is a new search location, then show the message.
                    if (lastSearchPath != location.SearchPath)
                    {
                        lastSearchPath = location.SearchPath;
                        Log.LogMessage(importance, Strings.SearchPath, lastSearchPath);
                        if (logAssemblyFoldersMinimal)
                        {
                            Log.LogMessage(importance, Strings.SearchedAssemblyFoldersEx);
                        }
                    }

                    // Show a message based on the reason.
                    switch (location.Reason)
                    {
                        case NoMatchReason.FileNotFound:
                            {
                                if (!logAssemblyFoldersMinimal)
                                {
                                    Log.LogMessage(importance, Strings.ConsideredAndRejectedBecauseNoFile, location.FileNameAttempted);
                                }
                                break;
                            }
                        case NoMatchReason.FusionNamesDidNotMatch:
                            Log.LogMessage(importance, Strings.ConsideredAndRejectedBecauseFusionNamesDidntMatch, location.FileNameAttempted, location.AssemblyName.FullName, fusionName);
                            break;

                        case NoMatchReason.TargetHadNoFusionName:
                            Log.LogMessage(importance, Strings.ConsideredAndRejectedBecauseTargetDidntHaveFusionName, location.FileNameAttempted);
                            break;

                        case NoMatchReason.NotInGac:
                            Log.LogMessage(importance, Strings.ConsideredAndRejectedBecauseNotInGac, location.FileNameAttempted);
                            break;

                        case NoMatchReason.NotAFileNameOnDisk:
                            {
                                if (!logAssemblyFoldersMinimal)
                                {
                                    Log.LogMessage(importance, Strings.ConsideredAndRejectedBecauseNotAFileNameOnDisk, location.FileNameAttempted);
                                }

                                break;
                            }
                        case NoMatchReason.ProcessorArchitectureDoesNotMatch:
                            Log.LogMessage(
                                importance,
                                Strings.TargetedProcessorArchitectureDoesNotMatch,
                                location.FileNameAttempted,
                                location.AssemblyName.AssemblyName.ProcessorArchitecture.ToString(),
                                _targetProcessorArchitecture);
                            break;
                        default:
                            Debug.Assert(false, "Should have handled this case.");
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Show the files that made this dependency necessary.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="importance">The importance of the message.</param>
        private void LogDependees(Reference reference, MessageImportance importance)
        {
            if (!reference.IsPrimary)
            {
                ICollection<ITaskItem> dependees = reference.GetSourceItems();
                foreach (ITaskItem dependee in dependees)
                {
                    Log.LogMessage(importance, Strings.RequiredBy, dependee.ItemSpec);
                }
            }
        }

        /// <summary>
        /// Log related files.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="importance">The importance of the message.</param>
        private void LogRelatedFiles(Reference reference, MessageImportance importance)
        {
            if (reference.IsResolved)
            {
                if (reference.FullPath.Length > 0)
                {
                    foreach (string relatedFileExtension in reference.GetRelatedFileExtensions())
                    {
                        Log.LogMessage(importance, Strings.FoundRelatedFile, reference.FullPathWithoutExtension + relatedFileExtension);
                    }
                }
            }
        }

        /// <summary>
        /// Log the satellite files.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="importance">The importance of the message.</param>
        private void LogSatellites(Reference reference, MessageImportance importance)
        {
            foreach (string satelliteFile in reference.GetSatelliteFiles())
            {
                Log.LogMessage(importance, Strings.FoundSatelliteFile, satelliteFile);
            }
        }

        /// <summary>
        /// Log scatter files.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="importance">The importance of the message.</param>
        private void LogScatterFiles(Reference reference, MessageImportance importance)
        {
            foreach (string scatterFile in reference.GetScatterFiles())
            {
                Log.LogMessage(importance, Strings.FoundScatterFile, scatterFile);
            }
        }

        /// <summary>
        /// Log a message about the CopyLocal state of the reference.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="importance">The importance of the message.</param>
        private void LogCopyLocalState(Reference reference, MessageImportance importance)
        {
            if (!reference.IsUnresolvable && !reference.IsBadImage)
            {
                switch (reference.CopyLocal)
                {
                    case CopyLocalState.YesBecauseOfHeuristic:
                    case CopyLocalState.YesBecauseReferenceItemHadMetadata:
                        break;

                    case CopyLocalState.NoBecausePrerequisite:
                        Log.LogMessage(importance, Strings.NotCopyLocalBecausePrerequisite);
                        break;

                    case CopyLocalState.NoBecauseReferenceItemHadMetadata:
                        Log.LogMessage(importance, Strings.NotCopyLocalBecauseIncomingItemAttributeOverrode);
                        break;

                    case CopyLocalState.NoBecauseFrameworkFile:
                        Log.LogMessage(importance, Strings.NotCopyLocalBecauseFrameworksFiles);
                        break;

                    case CopyLocalState.NoBecauseReferenceResolvedFromGAC:
                    case CopyLocalState.NoBecauseReferenceFoundInGAC:
                        Log.LogMessage(importance, Strings.NotCopyLocalBecauseReferenceFoundInGAC);
                        break;

                    case CopyLocalState.NoBecauseConflictVictim:
                        Log.LogMessage(importance, Strings.NotCopyLocalBecauseConflictVictim);
                        break;

                    case CopyLocalState.NoBecauseEmbedded:
                        Log.LogMessage(importance, Strings.NotCopyLocalBecauseEmbedded);
                        break;

                    case CopyLocalState.NoBecauseParentReferencesFoundInGAC:
                        Log.LogMessage(importance, Strings.NoBecauseParentReferencesFoundInGac);
                        break;

                    default:
                        Debug.Assert(false, "Should have handled this case.");
                        break;
                }
            }
        }

        /// <summary>
        /// Log a message about the imageruntime information.
        /// </summary>
        private void LogImageRuntime(Reference reference, MessageImportance importance)
        {
            if (!reference.IsUnresolvable && !reference.IsBadImage)
            {
                // Don't log the overwhelming default as it just pollutes the logs.
                if (reference.ImageRuntime != "v4.0.30319")
                {
                    Log.LogMessage(importance, Strings.ImageRuntimeVersion, reference.ImageRuntime);
                }

                if (reference.IsWinMDFile)
                {
                    Log.LogMessage(importance, Strings.IsAWinMdFile);
                }
            }
        }

        /// <summary>
        /// Log a conflict.
        /// </summary>
        /// <param name="reference">The reference.</param>
        /// <param name="fusionName">The fusion name of the reference.</param>
        /// <param name="log">StringBuilder holding information to be logged.</param>
        private void LogConflict(Reference reference, string fusionName, StringBuilder log)
        {
            log.Append(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("ResolveAssemblyReference.ConflictFound", reference.ConflictVictorName, fusionName));
            switch (reference.ConflictLossExplanation)
            {
                case ConflictLossReason.HadLowerVersion:
                    {
                        Debug.Assert(!reference.IsPrimary, "A primary reference should never lose a conflict because of version. This is an insoluble conflict instead.");
                        string message = Log.FormatResourceString("ResolveAssemblyReference.ConflictHigherVersionChosen", reference.ConflictVictorName);
                        log.AppendLine().Append(Strings.FourSpaces).Append(message);
                        break;
                    }

                case ConflictLossReason.WasNotPrimary:
                    {
                        string message = Log.FormatResourceString("ResolveAssemblyReference.ConflictPrimaryChosen", reference.ConflictVictorName, fusionName);
                        log.AppendLine().Append(Strings.FourSpaces).Append(message);
                        break;
                    }

                case ConflictLossReason.InsolubleConflict:
                    // For primary references, there's no way an app.config binding redirect could help
                    // so log a warning.
                    if (reference.IsPrimary)
                    {
                        Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.ConflictUnsolvable", reference.ConflictVictorName, fusionName);
                    }
                    else
                    {
                        // For dependencies, adding an app.config entry could help. Log a comment, there will be
                        // a summary warning later on.
                        log.AppendLine().Append(ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("ResolveAssemblyReference.ConflictUnsolvable", reference.ConflictVictorName, fusionName));
                    }
                    break;
                // Can happen if one of the references has a dependency with the same simplename, and version but no publickeytoken and the other does.
                case ConflictLossReason.FusionEquivalentWithSameVersion:
                    break;
                default:
                    Debug.Assert(false, "Should have handled this case.");
                    break;
            }
        }
        #endregion

        #region StateFile
        /// <summary>
        /// Reads the state file (if present) into the cache.
        /// </summary>
        internal void ReadStateFile(FileExists fileExists)
        {
            _cache = SystemState.DeserializeCache(_stateFile, Log, typeof(SystemState)) as SystemState;

            // Construct the cache only if we can't find any caches.
            if (_cache == null && AssemblyInformationCachePaths != null && AssemblyInformationCachePaths.Length > 0)
            {
                _cache = SystemState.DeserializePrecomputedCaches(AssemblyInformationCachePaths, Log, fileExists);
            }

            if (_cache == null)
            {
                _cache = new SystemState();
            }
        }

        /// <summary>
        /// Write out the state file if a state name was supplied and the cache is dirty.
        /// </summary>
        internal void WriteStateFile()
        {
            if (!String.IsNullOrEmpty(AssemblyInformationCacheOutputPath))
            {
                _cache.SerializePrecomputedCache(AssemblyInformationCacheOutputPath, Log);
            }
            else if (!String.IsNullOrEmpty(_stateFile) && _cache.IsDirty)
            {
                _cache.SerializeCache(_stateFile, Log);
            }
        }
        #endregion

        #region App.config
        /// <summary>
        /// Read the app.config and get any assembly remappings from it.
        /// </summary>
        private List<DependentAssembly> GetAssemblyRemappingsFromAppConfig()
        {
            if (_appConfigFile != null)
            {
                AppConfig appConfig = new AppConfig();
                appConfig.Load(_appConfigFile);

                return appConfig.Runtime.DependentAssemblies;
            }

            return null;
        }

        #endregion
        #region ITask Members

#if FEATURE_WIN32_REGISTRY
        /// <summary>
        /// Execute the task.
        /// </summary>
        /// <param name="fileExists">Delegate used for checking for the existence of a file.</param>
        /// <param name="directoryExists">Delegate used for checking for the existence of a directory.</param>
        /// <param name="getDirectories">Delegate used for finding directories.</param>
        /// <param name="getAssemblyName">Delegate used for finding fusion names of assemblyFiles.</param>
        /// <param name="getAssemblyMetadata">Delegate used for finding dependencies of a file.</param>
        /// <param name="getRegistrySubKeyNames">Used to get registry subkey names.</param>
        /// <param name="getRegistrySubKeyDefaultValue">Used to get registry default values.</param>
        /// <param name="getLastWriteTime">Delegate used to get the last write time.</param>
        /// <param name="getRuntimeVersion">Delegate used to get the runtime version.</param>
        /// <param name="openBaseKey">Key object to open.</param>
        /// <param name="getAssemblyPathInGac">Delegate to get assembly path in the GAC.</param>
        /// <param name="isWinMDFile">Delegate used for checking whether it is a WinMD file.</param>
        /// <param name="readMachineTypeFromPEHeader">Delegate use to read machine type from PE Header</param>
        /// <returns>True if there was success.</returns>
#else
        /// <summary>
        /// Execute the task.
        /// </summary>
        /// <param name="fileExists">Delegate used for checking for the existence of a file.</param>
        /// <param name="directoryExists">Delegate used for checking for the existence of a directory.</param>
        /// <param name="getDirectories">Delegate used for finding directories.</param>
        /// <param name="getAssemblyName">Delegate used for finding fusion names of assemblyFiles.</param>
        /// <param name="getAssemblyMetadata">Delegate used for finding dependencies of a file.</param>
        /// <param name="getLastWriteTime">Delegate used to get the last write time.</param>
        /// <param name="getRuntimeVersion">Delegate used to get the runtime version.</param>
        /// <param name="getAssemblyPathInGac">Delegate to get assembly path in the GAC.</param>
        /// <param name="isWinMDFile">Delegate used for checking whether it is a WinMD file.</param>
        /// <param name="readMachineTypeFromPEHeader">Delegate use to read machine type from PE Header</param>
        /// <returns>True if there was success.</returns>
#endif
        internal bool Execute
        (
            FileExists fileExists,
            DirectoryExists directoryExists,
            GetDirectories getDirectories,
            GetAssemblyName getAssemblyName,
            GetAssemblyMetadata getAssemblyMetadata,
#if FEATURE_WIN32_REGISTRY
            GetRegistrySubKeyNames getRegistrySubKeyNames,
            GetRegistrySubKeyDefaultValue getRegistrySubKeyDefaultValue,
#endif
            GetLastWriteTime getLastWriteTime,
            GetAssemblyRuntimeVersion getRuntimeVersion,
#if FEATURE_WIN32_REGISTRY
            OpenBaseKey openBaseKey,
#endif
            GetAssemblyPathInGac getAssemblyPathInGac,
            IsWinMDFile isWinMDFile,
            ReadMachineTypeFromPEHeader readMachineTypeFromPEHeader
        )
        {
            bool success = true;
            MSBuildEventSource.Log.RarOverallStart();
            {
                try
                {
                    FrameworkNameVersioning frameworkMoniker = null;
                    if (!String.IsNullOrEmpty(_targetedFrameworkMoniker))
                    {
                        try
                        {
                            frameworkMoniker = new FrameworkNameVersioning(_targetedFrameworkMoniker);
                        }
                        catch (ArgumentException)
                        {
                            // The exception doesn't contain the bad value, so log it ourselves
                            Log.LogErrorWithCodeFromResources("ResolveAssemblyReference.InvalidParameter", "TargetFrameworkMoniker", _targetedFrameworkMoniker, String.Empty);
                            return false;
                        }
                    }

                    Version targetedRuntimeVersion = SetTargetedRuntimeVersion(_targetedRuntimeVersionRawValue);

                    // Log task inputs.
                    LogInputs();

                    if (!VerifyInputConditions())
                    {
                        return false;
                    }

                    _logVerboseSearchResults = Environment.GetEnvironmentVariable("MSBUILDLOGVERBOSERARSEARCHRESULTS") != null;

                    // Loop through all the target framework directories that were passed in,
                    // and ensure that they all have a trailing slash.  This is necessary
                    // for the string comparisons we will do later on.
                    if (_targetFrameworkDirectories != null)
                    {
                        for (int i = 0; i < _targetFrameworkDirectories.Length; i++)
                        {
                            _targetFrameworkDirectories[i] = FileUtilities.EnsureTrailingSlash(_targetFrameworkDirectories[i]);
                        }
                    }

                    // Validate the contents of the InstalledAssemblyTables parameter.
                    AssemblyTableInfo[] installedAssemblyTableInfo = GetInstalledAssemblyTableInfo(_ignoreDefaultInstalledAssemblyTables, _installedAssemblyTables, new GetListPath(RedistList.GetRedistListPathsFromDisk), TargetFrameworkDirectories);
                    AssemblyTableInfo[] inclusionListSubsetTableInfo = null;

                    InstalledAssemblies installedAssemblies = null;
                    RedistList redistList = null;

                    if (installedAssemblyTableInfo?.Length > 0)
                    {
                        redistList = RedistList.GetRedistList(installedAssemblyTableInfo);
                    }

                    Dictionary<string, string> exclusionList = null;

                    // The name of the subset if it is generated or the name of the profile. This will be used for error messages and logging.
                    string subsetOrProfileName = null;

                    // Are we targeting a profile
                    bool targetingProfile = !String.IsNullOrEmpty(ProfileName) && ((FullFrameworkFolders.Length > 0) || (FullFrameworkAssemblyTables.Length > 0));
                    bool targetingSubset = false;
                    List<Exception> inclusionListErrors = new List<Exception>();
                    List<string> inclusionListErrorFilesNames = new List<string>();

                    // Check for partial success in GetRedistList and log any tolerated exceptions.
                    if (redistList?.Count > 0 || targetingProfile || ShouldUseSubsetExclusionList())
                    {
                        // If we are not targeting a dev 10 profile and we have the required components to generate a orcas style subset, do so
                        if (!targetingProfile && ShouldUseSubsetExclusionList())
                        {
                            // Based in the target framework subset names find the paths to the files
                            SubsetListFinder inclusionList = new SubsetListFinder(_targetFrameworkSubsets);
                            inclusionListSubsetTableInfo = GetInstalledAssemblyTableInfo(IgnoreDefaultInstalledAssemblySubsetTables, InstalledAssemblySubsetTables, new GetListPath(inclusionList.GetSubsetListPathsFromDisk), TargetFrameworkDirectories);
                            if (inclusionListSubsetTableInfo.Length > 0 && (redistList?.Count > 0))
                            {
                                exclusionList = redistList.GenerateBlackList(inclusionListSubsetTableInfo, inclusionListErrors, inclusionListErrorFilesNames);
                            }
                            else
                            {
                                Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.NoSubsetsFound");
                            }

                            // Could get into this situation if the redist list files were full of junk and no assemblies were read in.
                            if (exclusionList == null)
                            {
                                Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.NoRedistAssembliesToGenerateExclusionList");
                            }

                            subsetOrProfileName = GenerateSubSetName(_targetFrameworkSubsets, _installedAssemblySubsetTables);
                            targetingSubset = true;
                        }
                        else
                        {
                            // We are targeting a profile
                            if (targetingProfile)
                            {
                                // When targeting a profile we want the redist list to be the full framework redist list, since this is what should be used
                                // when unifying assemblies ect. 
                                AssemblyTableInfo[] fullRedistAssemblyTableInfo = null;
                                RedistList fullFrameworkRedistList = null;

                                HandleProfile(installedAssemblyTableInfo /*This is the table info related to the profile*/, out fullRedistAssemblyTableInfo, out exclusionList, out fullFrameworkRedistList);

                                // Make sure the redist list and the installedAsemblyTableInfo structures point to the full framework, we replace the installedAssemblyTableInfo
                                // which contained the information about the profile redist files with the one from the full framework because when doing anything with the RAR cache
                                // we want to use the full frameworks redist list. Essentailly after generating the exclusion list the job of the profile redist list is done.
                                redistList = fullFrameworkRedistList;

                                // Save the profile redist list file locations as the inclusionList
                                inclusionListSubsetTableInfo = installedAssemblyTableInfo;

                                // Set the installed assembly table to the full redist list values
                                installedAssemblyTableInfo = fullRedistAssemblyTableInfo;
                                subsetOrProfileName = _profileName;
                            }
                        }

                        if (redistList?.Count > 0)
                        {
                            installedAssemblies = new InstalledAssemblies(redistList);
                        }
                    }

                    // Print out any errors reading the redist list.
                    if (redistList != null)
                    {
                        // Some files may have been skipped. Log warnings for these.
                        for (int i = 0; i < redistList.Errors.Length; ++i)
                        {
                            Exception e = redistList.Errors[i];
                            string filename = redistList.ErrorFileNames[i];

                            // Give the user a warning about the bad file (or files).
                            Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.InvalidInstalledAssemblyTablesFile", filename, RedistList.RedistListFolder, e.Message);
                        }

                        // Some files may have been skipped. Log warnings for these.
                        for (int i = 0; i < inclusionListErrors.Count; ++i)
                        {
                            Exception e = inclusionListErrors[i];
                            string filename = inclusionListErrorFilesNames[i];

                            // Give the user a warning about the bad file (or files).
                            Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.InvalidInstalledAssemblySubsetTablesFile", filename, SubsetListFinder.SubsetListFolder, e.Message);
                        }
                    }

                    // Load any prior saved state.
                    ReadStateFile(fileExists);
                    _cache.SetGetLastWriteTime(getLastWriteTime);
                    _cache.SetInstalledAssemblyInformation(installedAssemblyTableInfo);

                    // Cache delegates.
                    getAssemblyName = _cache.CacheDelegate(getAssemblyName);
                    getAssemblyMetadata = _cache.CacheDelegate(getAssemblyMetadata);
                    fileExists = _cache.CacheDelegate();
                    directoryExists = _cache.CacheDelegate(directoryExists);
                    getDirectories = _cache.CacheDelegate(getDirectories);
                    getRuntimeVersion = _cache.CacheDelegate(getRuntimeVersion);

                    _projectTargetFramework = FrameworkVersionFromString(_projectTargetFrameworkAsString);

                    // Filter out all Assemblies that have SubType!='', or higher framework
                    FilterBySubtypeAndTargetFramework();

                    // Compute the set of bindingRedirect remappings.
                    List<DependentAssembly> appConfigRemappedAssemblies = null;
                    if (FindDependencies)
                    {
                        try
                        {
                            appConfigRemappedAssemblies = GetAssemblyRemappingsFromAppConfig();
                        }
                        catch (AppConfigException e)
                        {
                            Log.LogErrorWithCodeFromResources(null, e.FileName, e.Line, e.Column, 0, 0, "ResolveAssemblyReference.InvalidAppConfig", AppConfigFile, e.Message);
                            return false;
                        }
                    }

                    SystemProcessorArchitecture processorArchitecture = TargetProcessorArchitectureToEnumeration(_targetProcessorArchitecture);

                    ConcurrentDictionary<string, AssemblyMetadata> assemblyMetadataCache =
                        Traits.Instance.EscapeHatches.CacheAssemblyInformation
                            ? new ConcurrentDictionary<string, AssemblyMetadata>()
                            : null;

                    // Start the table of dependencies with all of the primary references.
                    ReferenceTable dependencyTable = new ReferenceTable
                    (
                        BuildEngine,
                        _findDependencies,
                        _findSatellites,
                        _findSerializationAssemblies,
                        _findRelatedFiles,
                        _searchPaths,
                        _allowedAssemblyExtensions,
                        _relatedFileExtensions,
                        _candidateAssemblyFiles,
                        _resolvedSDKReferences,
                        _targetFrameworkDirectories,
                        installedAssemblies,
                        processorArchitecture,
                        fileExists,
                        directoryExists,
                        getDirectories,
                        getAssemblyName,
                        getAssemblyMetadata,
#if FEATURE_WIN32_REGISTRY
                        getRegistrySubKeyNames,
                        getRegistrySubKeyDefaultValue,
                        openBaseKey,
#endif
                        getRuntimeVersion,
                        targetedRuntimeVersion,
                        _projectTargetFramework,
                        frameworkMoniker,
                        Log,
                        _latestTargetFrameworkDirectories,
                        _copyLocalDependenciesWhenParentReferenceInGac,
                        DoNotCopyLocalIfInGac,
                        getAssemblyPathInGac,
                        isWinMDFile,
                        _ignoreVersionForFrameworkReferences,
                        readMachineTypeFromPEHeader,
                        _warnOrErrorOnTargetArchitectureMismatch,
                        _ignoreTargetFrameworkAttributeVersionMismatch,
                        _unresolveFrameworkAssembliesFromHigherFrameworks,
                        assemblyMetadataCache
                        );

                    dependencyTable.FindDependenciesOfExternallyResolvedReferences = FindDependenciesOfExternallyResolvedReferences;

                    // If AutoUnify, then compute the set of assembly remappings.
                    var generalResolutionExceptions = new List<Exception>();

                    subsetOrProfileName = targetingSubset && String.IsNullOrEmpty(_targetedFrameworkMoniker) ? subsetOrProfileName : _targetedFrameworkMoniker;
                    bool excludedReferencesExist = false;

                    List<DependentAssembly> autoUnifiedRemappedAssemblies = null;
                    List<AssemblyNameReference> autoUnifiedRemappedAssemblyReferences = null;
                    if (AutoUnify && FindDependencies)
                    {
                        // Compute all dependencies.
                        dependencyTable.ComputeClosure
                        (
                            // Use any app.config specified binding redirects so that later when we output suggested redirects
                            // for the GenerateBindingRedirects target, we don't suggest ones that the user already wrote
                            appConfigRemappedAssemblies,
                            _assemblyFiles,
                            _assemblyNames,
                            generalResolutionExceptions
                        );

                        try
                        {
                            excludedReferencesExist = false;
                            if (redistList?.Count > 0)
                            {
                                excludedReferencesExist = dependencyTable.MarkReferencesForExclusion(exclusionList);
                            }
                        }
                        catch (InvalidOperationException e)
                        {
                            Log.LogErrorWithCodeFromResources("ResolveAssemblyReference.ProblemDeterminingFrameworkMembership", e.Message);
                            return false;
                        }

                        if (excludedReferencesExist)
                        {
                            dependencyTable.RemoveReferencesMarkedForExclusion(true /* Remove the reference and do not warn*/, subsetOrProfileName);
                        }

                        // Based on the closure, get a table of ideal remappings needed to 
                        // produce zero conflicts.
                        dependencyTable.ResolveConflicts
                        (
                            out autoUnifiedRemappedAssemblies,
                            out autoUnifiedRemappedAssemblyReferences
                        );
                    }

                    IReadOnlyCollection<DependentAssembly> allRemappedAssemblies = CombineRemappedAssemblies(appConfigRemappedAssemblies, autoUnifiedRemappedAssemblies);
                    List<DependentAssembly> idealAssemblyRemappings = autoUnifiedRemappedAssemblies;
                    List<AssemblyNameReference> idealAssemblyRemappingsIdentities = autoUnifiedRemappedAssemblyReferences;
                    bool shouldRerunClosure = autoUnifiedRemappedAssemblies?.Count > 0  || excludedReferencesExist;

                    if (!AutoUnify || !FindDependencies || shouldRerunClosure)
                    {
                        // Compute all dependencies.
                        dependencyTable.ComputeClosure(allRemappedAssemblies, _assemblyFiles, _assemblyNames, generalResolutionExceptions);

                        try
                        {
                            excludedReferencesExist = false;
                            if (redistList?.Count > 0)
                            {
                                excludedReferencesExist = dependencyTable.MarkReferencesForExclusion(exclusionList);
                            }
                        }
                        catch (InvalidOperationException e)
                        {
                            Log.LogErrorWithCodeFromResources("ResolveAssemblyReference.ProblemDeterminingFrameworkMembership", e.Message);
                            return false;
                        }

                        if (excludedReferencesExist)
                        {
                            dependencyTable.RemoveReferencesMarkedForExclusion(false /* Remove the reference and warn*/, subsetOrProfileName);
                        }

                        // Resolve any conflicts.
                        dependencyTable.ResolveConflicts
                        (
                            out idealAssemblyRemappings,
                            out idealAssemblyRemappingsIdentities
                        );
                    }

                    // Build the output tables.
                    dependencyTable.GetReferenceItems
                    (
                        out _resolvedFiles,
                        out _resolvedDependencyFiles,
                        out _relatedFiles,
                        out _satelliteFiles,
                        out _serializationAssemblyFiles,
                        out _scatterFiles,
                        out _copyLocalFiles
                    );

                    // If we're not finding dependencies, then don't suggest redirects (they're only about dependencies).
                    if (FindDependencies)
                    {
                        // Build the table of suggested redirects. If we're auto-unifying, we want to output all the 
                        // assemblies that we auto-unified so that GenerateBindingRedirects can consume them, 
                        // not just the required ones for build to succeed
                        List<DependentAssembly> remappings = AutoUnify ? autoUnifiedRemappedAssemblies : idealAssemblyRemappings;
                        List<AssemblyNameReference> remappedReferences = AutoUnify ? autoUnifiedRemappedAssemblyReferences : idealAssemblyRemappingsIdentities;
                        PopulateSuggestedRedirects(remappings, remappedReferences);
                    }

                    bool useSystemRuntime = false;
                    bool useNetStandard = false;
                    foreach (var reference in dependencyTable.References.Keys)
                    {
                        if (string.Equals(SystemRuntimeAssemblyName, reference.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            useSystemRuntime = true;
                        }
                        if (string.Equals(NETStandardAssemblyName, reference.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            useNetStandard = true;
                        }
                        if (useSystemRuntime && useNetStandard)
                        {
                            break;
                        }
                    }

                    if ((!useSystemRuntime || !useNetStandard) && (!FindDependencies || dependencyTable.SkippedFindingExternallyResolvedDependencies))
                    {
                        // when we are not producing the (full) dependency graph look for direct dependencies of primary references
                        foreach (var resolvedReference in dependencyTable.References.Values)
                        {
                            if (FindDependencies && !resolvedReference.ExternallyResolved)
                            {
                                // if we're finding dependencies and a given reference was not marked as ExternallyResolved
                                // then its use of System.Runtime/.netstandard would already have been identified above.
                                continue;
                            }

                            var rawDependencies = GetDependencies(resolvedReference, fileExists, getAssemblyMetadata, assemblyMetadataCache);
                            if (rawDependencies != null)
                            {
                                foreach (var dependentReference in rawDependencies)
                                {
                                    if (string.Equals(SystemRuntimeAssemblyName, dependentReference.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        useSystemRuntime = true;
                                        break;
                                    }
                                    if (string.Equals(NETStandardAssemblyName, dependentReference.Name, StringComparison.OrdinalIgnoreCase))
                                    {
                                        useNetStandard = true;
                                        break;
                                    }
                                }
                            }

                            if (useSystemRuntime && useNetStandard)
                            {
                                break;
                            }
                        }
                    }

                    this.DependsOnSystemRuntime = useSystemRuntime.ToString();
                    this.DependsOnNETStandard = useNetStandard.ToString();

                    WriteStateFile();

                    // Save the new state out and put into the file exists if it is actually on disk.
                    if (_stateFile != null && fileExists(_stateFile))
                    {
                        _filesWritten.Add(new TaskItem(_stateFile));
                    }

                    // Log the results.
                    success = LogResults(dependencyTable, idealAssemblyRemappings, idealAssemblyRemappingsIdentities, generalResolutionExceptions);

                    DumpTargetProfileLists(installedAssemblyTableInfo, inclusionListSubsetTableInfo, dependencyTable);

                    if (processorArchitecture != SystemProcessorArchitecture.None && _warnOrErrorOnTargetArchitectureMismatch != WarnOrErrorOnTargetArchitectureMismatchBehavior.None)
                    {
                        foreach (ITaskItem item in _resolvedFiles)
                        {
                            AssemblyNameExtension assemblyName = null;

                            if (fileExists(item.ItemSpec) && !Reference.IsFrameworkFile(item.ItemSpec, _targetFrameworkDirectories))
                            {
                                try
                                {
                                    assemblyName = getAssemblyName(item.ItemSpec);
                                }
                                catch (System.IO.FileLoadException)
                                {
                                    // Its pretty hard to get here, you need an assembly that contains a valid reference
                                    // to a dependent assembly that, in turn, throws a FileLoadException during GetAssemblyName.
                                    // Still it happened once, with an older version of the CLR. 

                                    // ...falling through and relying on the targetAssemblyName==null behavior below...
                                }
                                catch (System.IO.FileNotFoundException)
                                {
                                    // Its pretty hard to get here, also since we do a file existence check right before calling this method so it can only happen if the file got deleted between that check and this call.
                                }
                                catch (UnauthorizedAccessException)
                                {
                                }
                                catch (BadImageFormatException)
                                {
                                }
                            }

                            if (assemblyName != null)
                            {
                                SystemProcessorArchitecture assemblyArch = assemblyName.ProcessorArchitecture;

                                // If the assembly is MSIL or none it can work anywhere so there does not need to be any warning ect.
                                if (assemblyArch == SystemProcessorArchitecture.MSIL || assemblyArch == SystemProcessorArchitecture.None)
                                {
                                    continue;
                                }

                                if (processorArchitecture != assemblyArch)
                                {
                                    if (_warnOrErrorOnTargetArchitectureMismatch == WarnOrErrorOnTargetArchitectureMismatchBehavior.Error)
                                    {
                                        Log.LogErrorWithCodeFromResources("ResolveAssemblyReference.MismatchBetweenTargetedAndReferencedArch", ProcessorArchitectureToString(processorArchitecture), item.GetMetadata("OriginalItemSpec"), ProcessorArchitectureToString(assemblyArch));
                                    }
                                    else
                                    {
                                        Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.MismatchBetweenTargetedAndReferencedArch", ProcessorArchitectureToString(processorArchitecture), item.GetMetadata("OriginalItemSpec"), ProcessorArchitectureToString(assemblyArch));
                                    }
                                }
                            }
                        }
                    }
                    MSBuildEventSource.Log.RarOverallStop(_assemblyNames?.Length ?? -1, _assemblyFiles?.Length ?? -1, _resolvedFiles?.Length ?? -1, _resolvedDependencyFiles?.Length ?? -1, _copyLocalFiles?.Length ?? -1, _findDependencies);
                    return success && !Log.HasLoggedErrors;
                }
                catch (ArgumentException e)
                {
                    Log.LogErrorWithCodeFromResources("General.InvalidArgument", e.Message);
                }

                // InvalidParameterValueException is thrown inside RAR when we find a specific parameter
                // has an invalid value. It's then caught up here so that we can abort the task.
                catch (InvalidParameterValueException e)
                {
                    Log.LogErrorWithCodeFromResources(null, "", 0, 0, 0, 0,
                        "ResolveAssemblyReference.InvalidParameter", e.ParamName, e.ActualValue, e.Message);
                }
            }

            MSBuildEventSource.Log.RarOverallStop(_assemblyNames?.Length ?? -1, _assemblyFiles?.Length ?? -1, _resolvedFiles?.Length ?? -1, _resolvedDependencyFiles?.Length ?? -1, _copyLocalFiles?.Length ?? -1, _findDependencies);

            return success && !Log.HasLoggedErrors;
        }

        /// <summary>
        /// Returns the raw list of direct dependent assemblies from assembly's metadata.
        /// </summary>
        /// <param name="resolvedReference">reference we are interested</param>
        /// <param name="fileExists">the delegate to check for the existence of a file.</param>
        /// <param name="getAssemblyMetadata">the delegate to access assembly metadata</param>
        /// <param name="assemblyMetadataCache">Cache of pre-extracted assembly metadata.</param>
        /// <returns>list of dependencies</returns>
        private AssemblyNameExtension[] GetDependencies(Reference resolvedReference, FileExists fileExists, GetAssemblyMetadata getAssemblyMetadata, ConcurrentDictionary<string, AssemblyMetadata> assemblyMetadataCache)
        {
            AssemblyNameExtension[] result = null;
            if (resolvedReference?.IsPrimary == true && !resolvedReference.IsBadImage)
            {
                try
                {
                    // in case of P2P that have not build the reference can be resolved but file does not exist on disk. 
                    if (fileExists(resolvedReference.FullPath))
                    {
                        FrameworkNameVersioning frameworkName;
                        string[] scatterFiles;
                        getAssemblyMetadata(resolvedReference.FullPath, assemblyMetadataCache, out result, out scatterFiles, out frameworkName);
                    }
                }
                catch (Exception e)
                {
                    if (ExceptionHandling.IsCriticalException(e))
                    {
                        throw;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Combines two DependentAssembly arrays into one.
        /// </summary>
        private static IReadOnlyCollection<DependentAssembly> CombineRemappedAssemblies(IReadOnlyCollection<DependentAssembly> first, IReadOnlyCollection<DependentAssembly> second)
        {
            if (first == null)
                return second;

            if (second == null)
                return first;

            var combined = new List<DependentAssembly>(first.Count + second.Count);
            combined.AddRange(first);
            combined.AddRange(second);

            return combined;
        }

        /// <summary>
        /// If a targeted runtime is passed in use that, if none is passed in then we need to use v2.0.50727
        /// since the common way this would be empty is if we were using RAR as an override task.
        /// </summary>
        /// <returns>The targered runtime</returns>
        internal static Version SetTargetedRuntimeVersion(string targetedRuntimeVersionRawValue)
        {
            Version versionToReturn = null;
            if (targetedRuntimeVersionRawValue != null)
            {
                versionToReturn = VersionUtilities.ConvertToVersion(targetedRuntimeVersionRawValue);
            }

            // Either the version passed in did not parse or none was passed in, lets default to 2.0 so that we can be used as an override task for tv 3.5
            if (versionToReturn == null)
            {
                versionToReturn = new Version(2, 0, 50727);
            }

            return versionToReturn;
        }

        /// <summary>
        /// For a given profile generate the exclusion list and return the list of redist list files read in so they can be logged at the end of the task execution.
        /// </summary>
        /// <param name="installedAssemblyTableInfo">Installed assembly info of the profile redist lists</param>
        /// <param name="fullRedistAssemblyTableInfo">Installed assemblyInfo for the full framework redist lists</param>
        /// <param name="exclusionList">Generated exclusion list</param>
        /// <param name="fullFrameworkRedistList">Redist list which will contain the full framework redist list.</param>
        private void HandleProfile(AssemblyTableInfo[] installedAssemblyTableInfo, out AssemblyTableInfo[] fullRedistAssemblyTableInfo, out Dictionary<string, string> exclusionList, out RedistList fullFrameworkRedistList)
        {
            // Redist list which will contain the full framework redist list.
            fullFrameworkRedistList = null;
            exclusionList = null;
            fullRedistAssemblyTableInfo = null;

            // Make sure the framework directory is on the FullFrameworkTablesLocation if it is being used.
            foreach (ITaskItem item in FullFrameworkAssemblyTables)
            {
                // Cannot be missing the FrameworkDirectory if we are using this property
                if (String.IsNullOrEmpty(item.GetMetadata("FrameworkDirectory")))
                {
                    Log.LogErrorWithCodeFromResources("ResolveAssemblyReference.FrameworkDirectoryOnProfiles", item.ItemSpec);
                    return;
                }
            }

            fullRedistAssemblyTableInfo = GetInstalledAssemblyTableInfo(false, FullFrameworkAssemblyTables, new GetListPath(RedistList.GetRedistListPathsFromDisk), FullFrameworkFolders);
            if (fullRedistAssemblyTableInfo.Length > 0)
            {
                // Get the redist list which represents the Full framework, we need this so that we can generate the exclusion list
                fullFrameworkRedistList = RedistList.GetRedistList(fullRedistAssemblyTableInfo);
                if (fullFrameworkRedistList != null)
                {
                    // Generate the exclusion list by determining what assemblies are in the full framework but not in the profile.
                    // The installedAssemblyTableInfo is the list of xml files for the Client Profile redist, these are the inclusionList xml files.
                    Log.LogMessageFromResources("ResolveAssemblyReference.ProfileExclusionListWillBeGenerated");

                    // Any errors reading the profile redist list will already be logged, we do not need to re-log the errors here.
                    List<Exception> inclusionListErrors = new List<Exception>();
                    List<string> inclusionListErrorFilesNames = new List<string>();
                    exclusionList = fullFrameworkRedistList.GenerateBlackList(installedAssemblyTableInfo, inclusionListErrors, inclusionListErrorFilesNames);
                }

                // Could get into this situation if the redist list files were full of junk and no assemblies were read in.
                if (exclusionList == null)
                {
                    Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.NoRedistAssembliesToGenerateExclusionList");
                }
            }
            else
            {
                Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.NoProfilesFound");
            }

            if (fullFrameworkRedistList != null)
            {
                // Any errors logged for the client profile redist list will have been logged after this method returns.
                // Some files may have been skipped. Log warnings for these.
                for (int i = 0; i < fullFrameworkRedistList.Errors.Length; ++i)
                {
                    Exception e = fullFrameworkRedistList.Errors[i];
                    string filename = fullFrameworkRedistList.ErrorFileNames[i];

                    // Give the user a warning about the bad file (or files).
                    Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.InvalidProfileRedistLocation", filename, RedistList.RedistListFolder, e.Message);
                }
            }
        }

        /// <summary>
        /// Given the names of the targetFrameworkSubset lists passed in generate a single name which can be used for logging.
        /// </summary>
        internal static string GenerateSubSetName(string[] frameworkSubSetNames, ITaskItem[] installedSubSetNames)
        {
            List<string> subsetNames = new List<string>();
            if (frameworkSubSetNames != null)
            {
                foreach (string subset in frameworkSubSetNames)
                {
                    if (!String.IsNullOrEmpty(subset))
                    {
                        subsetNames.Add(subset);
                    }
                }
            }

            if (installedSubSetNames != null)
            {
                foreach (ITaskItem subsetItems in installedSubSetNames)
                {
                    string fileName = subsetItems.ItemSpec;
                    if (!String.IsNullOrEmpty(fileName))
                    {
                        string fileNameNoExtension = Path.GetFileNameWithoutExtension(fileName);
                        if (!String.IsNullOrEmpty(fileNameNoExtension))
                        {
                            subsetNames.Add(fileNameNoExtension);
                        }
                    }
                }
            }

            return String.Join(", ", subsetNames.ToArray());
        }

        /// <summary>
        /// Make sure certain combinations of properties are validated before continuing with the execution of rar.
        /// </summary>
        /// <returns></returns>
        private bool VerifyInputConditions()
        {
            bool targetFrameworkSubsetIsSet = TargetFrameworkSubsets.Length != 0 || InstalledAssemblySubsetTables.Length != 0;

            // Make sure the inputs for profiles are correct
            bool profileNameIsSet = !String.IsNullOrEmpty(ProfileName);
            bool fullFrameworkFoldersIsSet = FullFrameworkFolders.Length > 0;
            bool fullFrameworkTableLocationsIsSet = FullFrameworkAssemblyTables.Length > 0;
            bool profileIsSet = profileNameIsSet && (fullFrameworkFoldersIsSet || fullFrameworkTableLocationsIsSet);

            // Cannot target a subset and a profile at the same time
            if (targetFrameworkSubsetIsSet && profileIsSet)
            {
                Log.LogErrorWithCodeFromResources("ResolveAssemblyReference.CannotSetProfileAndSubSet");
                return false;
            }

            // A profile name and either a FullFrameworkFolders or ProfileTableLocation must be set is a profile is being used
            if (profileNameIsSet && (!fullFrameworkFoldersIsSet && !fullFrameworkTableLocationsIsSet))
            {
                Log.LogErrorWithCodeFromResources("ResolveAssemblyReference.MustSetProfileNameAndFolderLocations");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Log the target framework subset information.
        /// </summary>
        private void DumpTargetProfileLists(AssemblyTableInfo[] installedAssemblyTableInfo, AssemblyTableInfo[] inclusionListSubsetTableInfo, ReferenceTable referenceTable)
        {
            if (installedAssemblyTableInfo == null)
            {
                return;
            }

            string dumpFrameworkSubsetList = Environment.GetEnvironmentVariable("MSBUILDDUMPFRAMEWORKSUBSETLIST");
            if (dumpFrameworkSubsetList == null)
            {
                return;
            }

            Log.LogMessageFromResources(MessageImportance.Low, "ResolveAssemblyReference.TargetFrameworkSubsetLogHeader");

            Log.LogMessageFromResources(MessageImportance.Low, "ResolveAssemblyReference.TargetFrameworkRedistLogHeader");
            foreach (AssemblyTableInfo redistInfo in installedAssemblyTableInfo)
            {
                if (redistInfo != null)
                {
                    Log.LogMessage(MessageImportance.Low, Strings.FormattedAssemblyInfo, redistInfo.Path);
                }
            }

            Log.LogMessageFromResources(MessageImportance.Low, "ResolveAssemblyReference.TargetFrameworkWhiteListLogHeader");
            if (inclusionListSubsetTableInfo != null)
            {
                foreach (AssemblyTableInfo inclusionListInfo in inclusionListSubsetTableInfo)
                {
                    if (inclusionListInfo != null)
                    {
                        Log.LogMessage(MessageImportance.Low, Strings.FormattedAssemblyInfo, inclusionListInfo.Path);
                    }
                }
            }

            if (referenceTable.ListOfExcludedAssemblies != null)
            {
                Log.LogMessageFromResources(MessageImportance.Low, "ResolveAssemblyReference.TargetFrameworkExclusionListLogHeader");
                foreach (string assemblyFullName in referenceTable.ListOfExcludedAssemblies)
                {
                    Log.LogMessageFromResources(MessageImportance.Low, "ResolveAssemblyReference.FourSpaceIndent", assemblyFullName);
                }
            }
        }

        /// <summary>
        /// Determine if an exclusion list should be used or not
        ///
        /// The exclusion list should only be used if there are TargetFrameworkSubsets to use or TargetFrameworkProfiles.
        ///
        /// 1) If we find a Full or equivalent marker in the list of subsets passed in we do not want to generate an exclusion list even if installedAssemblySubsets are passed in
        /// 2) If we are ignoring the default installed subset tables and we have not passed in any additional subset tables, we do not want to generate an exclusion list
        /// 3) If no targetframework subsets were passed in and no additional subset tables were passed in, we do not want to generate an exclusion list
        /// </summary>
        /// <returns>True if we should generate an exclusion list</returns>
        private bool ShouldUseSubsetExclusionList()
        {
            // Check for full subset names in the passed in list of subsets to search for
            foreach (string fullSubsetName in _fullTargetFrameworkSubsetNames)
            {
                foreach (string subsetName in _targetFrameworkSubsets)
                {
                    if (String.Equals(fullSubsetName, subsetName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!_silent)
                        {
                            Log.LogMessageFromResources(MessageImportance.Low, "ResolveAssemblyReference.NoExclusionListBecauseofFullClientName", subsetName);
                        }
                        return false;
                    }
                }
            }

            // We are going to ignore the default installed subsets and there are no additional installedAssemblySubsets passed in, we should not make the list
            if (IgnoreDefaultInstalledAssemblySubsetTables && _installedAssemblySubsetTables.Length == 0)
            {
                return false;
            }

            // No subset names were passed in to search for in the targetframework directories and no installed subset tables were provided, we have nothing to use to 
            // generate the exclusion list with, so do not continue.
            if (_targetFrameworkSubsets.Length == 0 && _installedAssemblySubsetTables.Length == 0)
            {
                return false;
            }

            if (!_silent)
            {
                Log.LogMessageFromResources(MessageImportance.Low, "ResolveAssemblyReference.UsingExclusionList");
            }
            return true;
        }

        /// <summary>
        /// Populates the suggested redirects output parameter.
        /// </summary>
        /// <param name="idealAssemblyRemappings">The list of ideal remappings.</param>
        /// <param name="idealAssemblyRemappedReferences">The list of references to ideal assembly remappings.</param>
        private void PopulateSuggestedRedirects(List<DependentAssembly> idealAssemblyRemappings, List<AssemblyNameReference> idealAssemblyRemappedReferences)
        {
            var holdSuggestedRedirects = new List<ITaskItem>();
            if (idealAssemblyRemappings != null)
            {
                for (int i = 0; i < idealAssemblyRemappings.Count; i++)
                {
                    DependentAssembly idealRemapping = idealAssemblyRemappings[i];
                    string itemSpec = idealRemapping.PartialAssemblyName.ToString();

                    Reference reference = idealAssemblyRemappedReferences[i].reference;
                    List<AssemblyNameExtension> conflictVictims = reference.GetConflictVictims();

                    // Skip any remapping that has no conflict victims since a redirect will not help.
                    if (conflictVictims == null || 0 == conflictVictims.Count)
                    {
                        continue;
                    }

                    for (int j = 0; j < idealRemapping.BindingRedirects.Count; j++)
                    {
                        ITaskItem suggestedRedirect = new TaskItem();
                        suggestedRedirect.ItemSpec = itemSpec;
                        suggestedRedirect.SetMetadata("MaxVersion", idealRemapping.BindingRedirects[j].NewVersion.ToString());
                        holdSuggestedRedirects.Add(suggestedRedirect);
                    }
                }
            }
            _suggestedRedirects = holdSuggestedRedirects.ToArray();
        }

        /// <summary>
        /// Process TargetFrameworkDirectories and an array of InstalledAssemblyTables.
        /// The goal is this:  for each installed assembly table (whether found on disk
        /// or given as an input), we wish to determine the target framework directory
        /// it is associated with.
        /// </summary>
        /// <returns>Array of AssemblyTableInfo objects (Describe the path and framework directory of a redist or subset list xml file) </returns>
        private AssemblyTableInfo[] GetInstalledAssemblyTableInfo(bool ignoreInstalledAssemblyTables, ITaskItem[] assemblyTables, GetListPath GetAssemblyListPaths, string[] targetFrameworkDirectories)
        {
            Dictionary<string, AssemblyTableInfo> tableMap = new Dictionary<string, AssemblyTableInfo>(StringComparer.OrdinalIgnoreCase);

            if (!ignoreInstalledAssemblyTables)
            {
                // first, find redist or subset files underneath the TargetFrameworkDirectories
                foreach (string targetFrameworkDirectory in targetFrameworkDirectories)
                {
                    string[] listPaths = GetAssemblyListPaths(targetFrameworkDirectory);
                    foreach (string listPath in listPaths)
                    {
                        tableMap[listPath] = new AssemblyTableInfo(listPath, targetFrameworkDirectory);
                    }
                }
            }

            // now process those provided as inputs from the project file
            foreach (ITaskItem installedAssemblyTable in assemblyTables)
            {
                string frameworkDirectory = installedAssemblyTable.GetMetadata(ItemMetadataNames.frameworkDirectory);

                // Whidbey behavior was to accept a single TargetFrameworkDirectory, and multiple
                // InstalledAssemblyTables, under the assumption that all of the InstalledAssemblyTables
                // were related to the single TargetFrameworkDirectory.  If inputs look like the Whidbey
                // case, let's make sure we behave the same way.

                if (String.IsNullOrEmpty(frameworkDirectory))
                {
                    if (TargetFrameworkDirectories?.Length == 1)
                    {
                        // Exactly one TargetFrameworkDirectory, so assume it's related to this
                        // InstalledAssemblyTable.

                        frameworkDirectory = TargetFrameworkDirectories[0];
                    }
                }
                else
                {
                    // The metadata on the item was non-empty, so use it.
                    frameworkDirectory = FileUtilities.EnsureTrailingSlash(frameworkDirectory);
                }

                tableMap[installedAssemblyTable.ItemSpec] = new AssemblyTableInfo(installedAssemblyTable.ItemSpec, frameworkDirectory);
            }

            AssemblyTableInfo[] extensions = new AssemblyTableInfo[tableMap.Count];
            tableMap.Values.CopyTo(extensions, 0);

            return extensions;
        }

        /// <summary>
        /// Converts the string target framework value to a number.
        /// Accepts both "v" prefixed and no "v" prefixed formats
        /// if format is bad will log a message and return 0.
        /// </summary>
        /// <returns>Target framework version value</returns>
        private Version FrameworkVersionFromString(string version)
        {
            Version parsedVersion = null;

            if (!String.IsNullOrEmpty(version))
            {
                parsedVersion = VersionUtilities.ConvertToVersion(version);

                if (parsedVersion == null)
                {
                    Log.LogMessageFromResources(MessageImportance.Normal, "ResolveAssemblyReference.BadTargetFrameworkFormat", version);
                }
            }

            return parsedVersion;
        }

        /// <summary>
        /// Check if the assembly is available for on project's target framework.
        /// - Assuming the russian doll model. It will be available if the projects target framework is higher or equal than the assembly target framework
        /// </summary>
        /// <returns>True if the assembly is available for the project's target framework.</returns>
        private bool IsAvailableForTargetFramework(string assemblyFXVersionAsString)
        {
            Version assemblyFXVersion = FrameworkVersionFromString(assemblyFXVersionAsString);
            return (assemblyFXVersion == null) || (_projectTargetFramework == null) || (_projectTargetFramework >= assemblyFXVersion);
        }

        /// <summary>
        /// Validate and filter the Assemblies that were passed in.
        /// - Check for assemblies that look like file names.
        /// - Check for assemblies where subtype!=''. These are removed.
        /// - Check for assemblies that have target framework higher than the project. These are removed.
        /// </summary>
        private void FilterBySubtypeAndTargetFramework()
        {
            var assembliesLeft = new List<ITaskItem>();
            foreach (ITaskItem assembly in Assemblies)
            {
                string subType = assembly.GetMetadata(ItemMetadataNames.subType);
                if (!string.IsNullOrEmpty(subType))
                {
                    Log.LogMessageFromResources(MessageImportance.Normal, "ResolveAssemblyReference.IgnoringBecauseNonEmptySubtype", assembly.ItemSpec, subType);
                }
                else if (!IsAvailableForTargetFramework(assembly.GetMetadata(ItemMetadataNames.targetFramework)))
                {
                    Log.LogWarningWithCodeFromResources("ResolveAssemblyReference.FailedToResolveReferenceBecauseHigherTargetFramework", assembly.ItemSpec, assembly.GetMetadata(ItemMetadataNames.targetFramework));
                }
                else
                {
                    assembliesLeft.Add(assembly);
                }
            }

            // Save the array of assemblies filtered by SubType==''.
            _assemblyNames = assembliesLeft.ToArray();
        }

        /// <summary>
        /// Take a processor architecure and get the string representation back.
        /// </summary>
        internal static string ProcessorArchitectureToString(SystemProcessorArchitecture processorArchitecture)
        {
            if (SystemProcessorArchitecture.Amd64 == processorArchitecture)
            {
                return Microsoft.Build.Utilities.ProcessorArchitecture.AMD64;
            }
            else if (SystemProcessorArchitecture.IA64 == processorArchitecture)
            {
                return Microsoft.Build.Utilities.ProcessorArchitecture.IA64;
            }
            else if (SystemProcessorArchitecture.MSIL == processorArchitecture)
            {
                return Microsoft.Build.Utilities.ProcessorArchitecture.MSIL;
            }
            else if (SystemProcessorArchitecture.X86 == processorArchitecture)
            {
                return Microsoft.Build.Utilities.ProcessorArchitecture.X86;
            }
            else if (SystemProcessorArchitecture.Arm == processorArchitecture)
            {
                return Microsoft.Build.Utilities.ProcessorArchitecture.ARM;
            }
            return String.Empty;
        }

        // Convert the string passed into rar to a processor architecture enum so that we can properly compare it with the AssemblyName objects we find in assemblyFoldersEx
        internal static SystemProcessorArchitecture TargetProcessorArchitectureToEnumeration(string targetedProcessorArchitecture)
        {
            if (targetedProcessorArchitecture != null)
            {
                if (targetedProcessorArchitecture.Equals(Microsoft.Build.Utilities.ProcessorArchitecture.AMD64, StringComparison.OrdinalIgnoreCase))
                {
                    return SystemProcessorArchitecture.Amd64;
                }
                else if (targetedProcessorArchitecture.Equals(Microsoft.Build.Utilities.ProcessorArchitecture.IA64, StringComparison.OrdinalIgnoreCase))
                {
                    return SystemProcessorArchitecture.IA64;
                }
                else if (targetedProcessorArchitecture.Equals(Microsoft.Build.Utilities.ProcessorArchitecture.MSIL, StringComparison.OrdinalIgnoreCase))
                {
                    return SystemProcessorArchitecture.MSIL;
                }
                else if (targetedProcessorArchitecture.Equals(Microsoft.Build.Utilities.ProcessorArchitecture.X86, StringComparison.OrdinalIgnoreCase))
                {
                    return SystemProcessorArchitecture.X86;
                }
                else if (targetedProcessorArchitecture.Equals(Microsoft.Build.Utilities.ProcessorArchitecture.ARM, StringComparison.OrdinalIgnoreCase))
                {
                    return SystemProcessorArchitecture.Arm;
                }
                else if (targetedProcessorArchitecture.Equals(Microsoft.Build.Utilities.ProcessorArchitecture.ARM64, StringComparison.OrdinalIgnoreCase))
                {
                    return (SystemProcessorArchitecture)6;
                }
            }

            return SystemProcessorArchitecture.MSIL;
        }

        /// <summary>
        ///  Checks to see if the assemblyName passed in is in the GAC.
        /// </summary>
        private string GetAssemblyPathInGac(AssemblyNameExtension assemblyName, SystemProcessorArchitecture targetProcessorArchitecture, GetAssemblyRuntimeVersion getRuntimeVersion, Version targetedRuntimeVersion, FileExists fileExists, bool fullFusionName, bool specificVersion)
        {
#if FEATURE_GAC
            return GlobalAssemblyCache.GetLocation(BuildEngine as IBuildEngine4, assemblyName, targetProcessorArchitecture, getRuntimeVersion, targetedRuntimeVersion, fullFusionName, fileExists, null, null, specificVersion /* this value does not matter if we are passing a full fusion name*/);
#else
            return string.Empty;
#endif
        }

        /// <summary>
        /// Execute the task.
        /// </summary>
        /// <returns>True if there was success.</returns>
        public override bool Execute()
        {
            return Execute
            (
                new FileExists(p => FileUtilities.FileExistsNoThrow(p)),
                new DirectoryExists(p => FileUtilities.DirectoryExistsNoThrow(p)),
                new GetDirectories(Directory.GetDirectories),
                new GetAssemblyName(AssemblyNameExtension.GetAssemblyNameEx),
                new GetAssemblyMetadata(AssemblyInformation.GetAssemblyMetadata),
#if FEATURE_WIN32_REGISTRY
                new GetRegistrySubKeyNames(RegistryHelper.GetSubKeyNames),
                new GetRegistrySubKeyDefaultValue(RegistryHelper.GetDefaultValue),
#endif
                new GetLastWriteTime(NativeMethodsShared.GetLastWriteFileUtcTime),
                new GetAssemblyRuntimeVersion(AssemblyInformation.GetRuntimeVersion),
#if FEATURE_WIN32_REGISTRY
                new OpenBaseKey(RegistryHelper.OpenBaseKey),
#endif
                new GetAssemblyPathInGac(GetAssemblyPathInGac),
                new IsWinMDFile(AssemblyInformation.IsWinMDFile),
                new ReadMachineTypeFromPEHeader(ReferenceTable.ReadMachineTypeFromPEHeader)
            );
        }

        #endregion
    }
}
