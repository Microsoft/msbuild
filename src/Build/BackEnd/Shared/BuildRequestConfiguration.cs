// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Build.Shared;
using Microsoft.Build.Execution;
using Microsoft.Build.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.BackEnd.SdkResolution;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Framework;
using Microsoft.Build.Globbing;
using Microsoft.Build.Shared.FileSystem;

namespace Microsoft.Build.BackEnd
{
    /// <summary>
    /// A build request configuration represents all of the data necessary to know which project to build
    /// and the environment in which it should be built.
    /// </summary>
    internal class BuildRequestConfiguration : IEquatable<BuildRequestConfiguration>,
                                               INodePacket
    {
        /// <summary>
        /// The invalid configuration id
        /// </summary>
        public const int InvalidConfigurationId = 0;

        #region Static State

        /// <summary>
        /// This is the ID of the configuration as set by the generator of the configuration.  When
        /// a node generates a configuration, this is set to a negative number.  The Build Manager will
        /// generate positive IDs
        /// </summary>
        private int _configId;

        /// <summary>
        /// The full path to the project to build.
        /// </summary>
        private string _projectFullPath;

        /// <summary>
        /// The tools version specified for the configuration.
        /// Always specified.
        /// May have originated from a /tv switch, or an MSBuild task,
        /// or a Project tag, or the default.
        /// </summary>
        private string _toolsVersion;

        /// <summary>
        /// Whether the tools version was set by the /tv switch or passed in through an msbuild callback
        /// directly or indirectly.
        /// </summary>
        private bool _explicitToolsVersionSpecified;

        /// <summary>
        /// The set of global properties which should be used when building this project.
        /// </summary>
        private PropertyDictionary<ProjectPropertyInstance> _globalProperties;

        /// <summary>
        /// Flag indicating if the project in this configuration is a traversal
        /// </summary>
        private bool? _isTraversalProject;

        /// <summary>
        /// Synchronization object.  Currently this just prevents us from caching and uncaching at the
        /// same time, causing a race condition.  This class is not made 100% threadsafe by the presence
        /// and current usage of this lock.
        /// </summary>
        private readonly Object _syncLock = new Object();

        #endregion

        #region Build State

        /// <summary>
        /// The project object, representing the project to be built.
        /// </summary>
        private ProjectInstance _project;

        /// <summary>
        /// The state of a project instance which has been transferred from one node to another.
        /// </summary>
        private ProjectInstance _transferredState;

        /// <summary>
        /// The project instance properties we should transfer.
        /// <see cref="_transferredState"/> and <see cref="_transferredProperties"/> are mutually exclud
        /// </summary>
        private List<ProjectPropertyInstance> _transferredProperties;

        /// <summary>
        /// The initial targets for the project
        /// </summary>
        private List<string> _projectInitialTargets;

        /// <summary>
        /// The default targets for the project
        /// </summary>
        private List<string> _projectDefaultTargets;

        /// <summary>
        /// This is the lookup representing the current project items and properties 'state'.
        /// </summary>
        private Lookup _baseLookup;

        /// <summary>
        /// This is the set of targets which are currently building but which have not yet completed.
        /// { targetName -> globalRequestId }
        /// </summary>
        private Dictionary<string, int> _activelyBuildingTargets;

        /// <summary>
        /// The node where this configuration's master results are stored.
        /// </summary>
        private int _resultsNodeId = Scheduler.InvalidNodeId;

        ///<summary>
        /// Holds a snapshot of the environment at the time we blocked.
        /// </summary>
        private Dictionary<string, string> _savedEnvironmentVariables;

        /// <summary>
        /// Holds a snapshot of the current working directory at the time we blocked.
        /// </summary>
        private string _savedCurrentDirectory;

        #endregion

        /// <summary>
        /// The target names that were requested to execute.
        /// </summary>
        internal IReadOnlyCollection<string> TargetNames { get; }

        /// <summary>
        /// Initializes a configuration from a BuildRequestData structure.  Used by the BuildManager.
        /// Figures out the correct tools version to use, falling back to the provided default if necessary.
        /// May throw InvalidProjectFileException.
        /// </summary>
        /// <param name="data">The data containing the configuration information.</param>
        /// <param name="defaultToolsVersion">The default ToolsVersion to use as a fallback</param>
        internal BuildRequestConfiguration(BuildRequestData data, string defaultToolsVersion)
            : this(0, data, defaultToolsVersion)
        {
        }

        /// <summary>
        /// Initializes a configuration from a BuildRequestData structure.  Used by the BuildManager.
        /// Figures out the correct tools version to use, falling back to the provided default if necessary.
        /// May throw InvalidProjectFileException.
        /// </summary>
        /// <param name="configId">The configuration ID to assign to this new configuration.</param>
        /// <param name="data">The data containing the configuration information.</param>
        /// <param name="defaultToolsVersion">The default ToolsVersion to use as a fallback</param>
        internal BuildRequestConfiguration(int configId, BuildRequestData data, string defaultToolsVersion)
        {
            ErrorUtilities.VerifyThrowArgumentNull(data, nameof(data));
            ErrorUtilities.VerifyThrowInternalLength(data.ProjectFullPath, "data.ProjectFullPath");

            _configId = configId;
            _projectFullPath = data.ProjectFullPath;
            _explicitToolsVersionSpecified = data.ExplicitToolsVersionSpecified;
            _toolsVersion = ResolveToolsVersion(data, defaultToolsVersion);
            _globalProperties = data.GlobalPropertiesDictionary;
            TargetNames = new List<string>(data.TargetNames);

            // The following information only exists when the request is populated with an existing project.
            if (data.ProjectInstance != null)
            {
                _project = data.ProjectInstance;
                _projectInitialTargets = data.ProjectInstance.InitialTargets;
                _projectDefaultTargets = data.ProjectInstance.DefaultTargets;

                if (data.PropertiesToTransfer != null)
                {
                    _transferredProperties = new List<ProjectPropertyInstance>();
                    foreach (var name in data.PropertiesToTransfer)
                    {
                        _transferredProperties.Add(data.ProjectInstance.GetProperty(name));
                    }
                }

                IsCacheable = false;
            }
            else
            {
                IsCacheable = true;
            }
        }

        /// <summary>
        /// Creates a new BuildRequestConfiguration based on an existing project instance.
        /// Used by the BuildManager to populate configurations from a solution.
        /// </summary>
        /// <param name="configId">The configuration id</param>
        /// <param name="instance">The project instance.</param>
        internal BuildRequestConfiguration(int configId, ProjectInstance instance)
        {
            ErrorUtilities.VerifyThrowArgumentNull(instance, nameof(instance));

            _configId = configId;
            _projectFullPath = instance.FullPath;
            _explicitToolsVersionSpecified = instance.ExplicitToolsVersionSpecified;
            _toolsVersion = instance.ToolsVersion;
            _globalProperties = instance.GlobalPropertiesDictionary;

            _project = instance;
            _projectInitialTargets = instance.InitialTargets;
            _projectDefaultTargets = instance.DefaultTargets;
            IsCacheable = false;
        }

        /// <summary>
        /// Creates a new configuration which is a clone of the old one but with a new id.
        /// </summary>
        private BuildRequestConfiguration(int configId, BuildRequestConfiguration other)
        {
            ErrorUtilities.VerifyThrow(configId != InvalidConfigurationId, "Configuration ID must not be invalid when using this constructor.");
            ErrorUtilities.VerifyThrowArgumentNull(other, nameof(other));
            ErrorUtilities.VerifyThrow(other._transferredState == null, "Unexpected transferred state still set on other configuration.");

            _project = other._project;
            _transferredProperties = other._transferredProperties;
            _projectDefaultTargets = other._projectDefaultTargets;
            _projectInitialTargets = other._projectInitialTargets;
            _projectFullPath = other._projectFullPath;
            _toolsVersion = other._toolsVersion;
            _explicitToolsVersionSpecified = other._explicitToolsVersionSpecified;
            _globalProperties = other._globalProperties;
            IsCacheable = other.IsCacheable;
            _configId = configId;
            TargetNames = other.TargetNames;
        }

        /// <summary>
        /// Private constructor for deserialization
        /// </summary>
        private BuildRequestConfiguration(ITranslator translator)
        {
            Translate(translator);
        }

        internal BuildRequestConfiguration()
        {
        }

        /// <summary>
        /// Flag indicating whether the configuration is allowed to cache.  This does not mean that the configuration will
        /// actually cache - there are several criteria which must for that.
        /// </summary>
        public bool IsCacheable { get; set; }

        /// <summary>
        /// When reset caches is false we need to only keep around the configurations which are being asked for during the design time build.
        /// Other configurations need to be cleared. If this configuration is marked as ExplicitlyLoadedConfiguration then it should not be cleared when
        /// Reset Caches is false.
        /// </summary>
        public bool ExplicitlyLoaded { get; set; }

        /// <summary>
        /// Flag indicating whether or not the configuration is actually building.
        /// </summary>
        public bool IsActivelyBuilding => _activelyBuildingTargets?.Count > 0;

        /// <summary>
        /// Flag indicating whether or not the configuration has been loaded before.
        /// </summary>
        public bool IsLoaded => _project?.IsLoaded == true;

        /// <summary>
        /// Flag indicating if the configuration is cached or not.
        /// </summary>
        public bool IsCached { get; private set; }

        /// <summary>
        /// Flag indicating if this configuration represents a traversal project.  Traversal projects
        /// are projects which typically do little or no work themselves, but have references to other
        /// projects (and thus are used to find more work.)  The scheduler can treat these differently
        /// in order to fill its work queue with other options for scheduling.
        /// </summary>
        public bool IsTraversal
        {
            get
            {
                if (!_isTraversalProject.HasValue)
                {
                    if (String.Equals(Path.GetFileName(ProjectFullPath), "dirs.proj", StringComparison.OrdinalIgnoreCase))
                    {
                        // dirs.proj are assumed to be traversals
                        _isTraversalProject = true;
                    }
                    else if (FileUtilities.IsMetaprojectFilename(ProjectFullPath))
                    {
                        // Metaprojects generated by the SolutionProjectGenerator are traversals.  They have no 
                        // on-disk representation - they are ProjectInstances which exist only in memory.
                        _isTraversalProject = true;
                    }
                    else if (FileUtilities.IsSolutionFilename(ProjectFullPath))
                    {
                        // Solution files are considered to be traversals.
                        _isTraversalProject = true;
                    }
                    else
                    {
                        _isTraversalProject = false;
                    }
                }

                return _isTraversalProject.Value;
            }
        }

        /// <summary>
        /// Returns true if this configuration was generated on a node and has not yet been resolved.
        /// </summary>
        public bool WasGeneratedByNode => _configId < InvalidConfigurationId;

        /// <summary>
        /// Sets or returns the configuration id
        /// </summary>
        public int ConfigurationId
        {
            [DebuggerStepThrough]
            get => _configId;

            [DebuggerStepThrough]
            set
            {
                ErrorUtilities.VerifyThrow((_configId == InvalidConfigurationId) || (WasGeneratedByNode && (value > InvalidConfigurationId)), "Configuration ID must be invalid, or it must be less than invalid and the new config must be greater than invalid.  It was {0}, the new value was {1}.", _configId, value);
                _configId = value;
            }
        }

        /// <summary>
        /// Returns the filename of the project to build.
        /// </summary>
        public string ProjectFullPath => _projectFullPath;

        /// <summary>
        /// The tools version specified for the configuration.
        /// Always specified.
        /// May have originated from a /tv switch, or an MSBuild task,
        /// or a Project tag, or the default.
        /// </summary>
        public string ToolsVersion => _toolsVersion;

        /// <summary>
        /// Returns the global properties to use to build this project.
        /// </summary>
        public PropertyDictionary<ProjectPropertyInstance> GlobalProperties => _globalProperties;

        /// <summary>
        /// Sets or returns the project to build.
        /// </summary>
        public ProjectInstance Project
        {
            [DebuggerStepThrough]
            get
            {
                ErrorUtilities.VerifyThrow(!IsCached, "We shouldn't be accessing the ProjectInstance when the configuration is cached.");
                return _project;
            }

            [DebuggerStepThrough]
            set
            {
                SetProjectBasedState(value);

                // If we have transferred the state of a project previously, then we need to assume its items and properties.
                if (_transferredState != null)
                {
                    ErrorUtilities.VerifyThrow(_transferredProperties == null, "Shouldn't be transferring entire state of ProjectInstance when transferredProperties is not null.");
                    _project.UpdateStateFrom(_transferredState);
                    _transferredState = null;
                }

                // If we have just requested a limited transfer of properties, do that.
                if (_transferredProperties != null)
                {
                    foreach (var property in _transferredProperties)
                    {
                        _project.SetProperty(property.Name, ((IProperty)property).EvaluatedValueEscaped);
                    }

                    _transferredProperties = null;
                }
            }
        }

        private void SetProjectBasedState(ProjectInstance project)
        {
            ErrorUtilities.VerifyThrow(project != null, "Cannot set null project.");
            _project = project;
            _baseLookup = null;

            // Clear these out so the other accessors don't complain.  We don't want to generally enable resetting these fields.
            _projectDefaultTargets = null;
            _projectInitialTargets = null;

            ProjectDefaultTargets = _project.DefaultTargets;
            ProjectInitialTargets = _project.InitialTargets;

            if (IsCached)
            {
                ClearCacheFile();
                IsCached = false;
            }
        }

        /// <summary>
        /// Loads the project specified by the configuration's parameters into the configuration block.
        /// </summary>
        internal void LoadProjectIntoConfiguration(
            IBuildComponentHost componentHost,
            BuildRequestDataFlags buildRequestDataFlags,
            int submissionId,
            int nodeId)
        {
            ErrorUtilities.VerifyThrow(!IsLoaded, "Already loaded the project for this configuration id {0}.", ConfigurationId);

            InitializeProject(componentHost.BuildParameters, () =>
            {
                if (componentHost.BuildParameters.SaveOperatingEnvironment)
                {
                    try
                    {
                        NativeMethodsShared.SetCurrentDirectory(BuildParameters.StartupDirectory);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // Somehow the startup directory vanished. This can happen if build was started from a USB Key and it was removed.
                        NativeMethodsShared.SetCurrentDirectory(
                            BuildEnvironmentHelper.Instance.CurrentMSBuildToolsDirectory);
                    }
                }

                Dictionary<string, string> globalProperties = new Dictionary<string, string>(MSBuildNameIgnoreCaseComparer.Default);

                foreach (ProjectPropertyInstance property in GlobalProperties)
                {
                    globalProperties.Add(property.Name, ((IProperty)property).EvaluatedValueEscaped);
                }

                string toolsVersionOverride = ExplicitToolsVersionSpecified ? ToolsVersion : null;

                // Get the hosted ISdkResolverService.  This returns either the MainNodeSdkResolverService or the OutOfProcNodeSdkResolverService depending on who created the current RequestBuilder
                ISdkResolverService sdkResolverService = componentHost.GetComponent(BuildComponentType.SdkResolverService) as ISdkResolverService;

                // Use different project load settings if the build request indicates to do so
                ProjectLoadSettings projectLoadSettings = componentHost.BuildParameters.ProjectLoadSettings;

                if (buildRequestDataFlags.HasFlag(BuildRequestDataFlags.IgnoreMissingEmptyAndInvalidImports))
                {
                    projectLoadSettings |= ProjectLoadSettings.IgnoreMissingImports | ProjectLoadSettings.IgnoreInvalidImports | ProjectLoadSettings.IgnoreEmptyImports;
                }

                if (buildRequestDataFlags.HasFlag(BuildRequestDataFlags.FailOnUnresolvedSdk))
                {
                    projectLoadSettings |= ProjectLoadSettings.FailOnUnresolvedSdk;
                }
                return new ProjectInstance(
                    ProjectFullPath,
                    globalProperties,
                    toolsVersionOverride,
                    componentHost.BuildParameters,
                    componentHost.LoggingService,
                    new BuildEventContext(
                        submissionId,
                        nodeId,
                        BuildEventContext.InvalidEvaluationId,
                        BuildEventContext.InvalidProjectInstanceId,
                        BuildEventContext.InvalidProjectContextId,
                        BuildEventContext.InvalidTargetId,
                        BuildEventContext.InvalidTaskId),
                    sdkResolverService,
                    submissionId,
                    projectLoadSettings);
            });
        }

        private void InitializeProject(BuildParameters buildParameters, Func<ProjectInstance> loadProjectFromFile)
        {
            if (_project == null || // building from file. Load project from file
                _transferredProperties != null // need to overwrite particular properties, so load project from file and overwrite properties
            )
            {
                Project = loadProjectFromFile.Invoke();
            }
            else if (_project.TranslateEntireState)
            {
                // projectInstance was serialized over. Finish initialization with node specific state

                _project.LateInitialize(buildParameters.ProjectRootElementCache, buildParameters.HostServices);
            }

            ErrorUtilities.VerifyThrow(IsLoaded, $"This {nameof(BuildRequestConfiguration)} must be loaded at the end of this method");
        }

        internal void CreateUniqueGlobalProperty()
        {
            // create a copy so the mutation does not leak into the ProjectInstance
            _globalProperties = new PropertyDictionary<ProjectPropertyInstance>(_globalProperties);

            var key = $"{MSBuildConstants.MSBuildDummyGlobalPropertyHeader}{Guid.NewGuid():N}";
            _globalProperties[key] = ProjectPropertyInstance.Create(key, "Forces unique project identity in the MSBuild engine");
        }

        /// <summary>
        /// Returns true if the default and initial targets have been resolved.
        /// </summary>
        public bool HasTargetsResolved => ProjectInitialTargets != null && ProjectDefaultTargets != null;

        /// <summary>
        /// Gets the initial targets for the project
        /// </summary>
        public List<string> ProjectInitialTargets
        {
            get => _projectInitialTargets;

            [DebuggerStepThrough]
            set
            {
                ErrorUtilities.VerifyThrow(_projectInitialTargets == null, "Initial targets cannot be reset once they have been set.");
                _projectInitialTargets = value;
            }
        }

        /// <summary>
        /// Gets the default targets for the project
        /// </summary>
        public List<string> ProjectDefaultTargets
        {
            [DebuggerStepThrough]
            get => _projectDefaultTargets;

            [DebuggerStepThrough]
            set
            {
                ErrorUtilities.VerifyThrow(_projectDefaultTargets == null, "Default targets cannot be reset once they have been set.");
                _projectDefaultTargets = value;
            }
        }

        /// <summary>
        /// Returns the node packet type
        /// </summary>
        public NodePacketType Type => NodePacketType.BuildRequestConfiguration;

        /// <summary>
        /// Returns the lookup which collects all items and properties during the run of this project.
        /// </summary>
        public Lookup BaseLookup
        {
            get
            {
                ErrorUtilities.VerifyThrow(!IsCached, "Configuration is cached, we shouldn't be accessing the lookup.");

                if (_baseLookup == null)
                {
                    _baseLookup = new Lookup(Project.ItemsToBuildWith, Project.PropertiesToBuildWith);
                }

                return _baseLookup;
            }
        }

        /// <summary>
        /// Retrieves the set of targets currently building, mapped to the request id building them.
        /// </summary>
        public Dictionary<string, int> ActivelyBuildingTargets => _activelyBuildingTargets ?? (_activelyBuildingTargets =
                                                                      new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// Holds a snapshot of the environment at the time we blocked.
        /// </summary>
        public Dictionary<string, string> SavedEnvironmentVariables
        {
            get => _savedEnvironmentVariables;

            set => _savedEnvironmentVariables = value;
        }

        /// <summary>
        /// Holds a snapshot of the current working directory at the time we blocked.
        /// </summary>
        public string SavedCurrentDirectory
        {
            get => _savedCurrentDirectory;

            set => _savedCurrentDirectory = value;
        }

        /// <summary>
        /// Whether the tools version was set by the /tv switch or passed in through an msbuild callback
        /// directly or indirectly.
        /// </summary>
        public bool ExplicitToolsVersionSpecified => _explicitToolsVersionSpecified;

        /// <summary>
        /// Gets or sets the node on which this configuration's results are stored.
        /// </summary>
        internal int ResultsNodeId
        {
            get => _resultsNodeId;

            set => _resultsNodeId = value;
        }

        /// <summary>
        /// Implementation of the equality operator.
        /// </summary>
        /// <param name="left">The left hand argument</param>
        /// <param name="right">The right hand argument</param>
        /// <returns>True if the objects are equivalent, false otherwise.</returns>
        public static bool operator ==(BuildRequestConfiguration left, BuildRequestConfiguration right)
        {
            if (left is null)
            {
                if (right is null)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (right is null)
                {
                    return false;
                }
                else
                {
                    return left.InternalEquals(right);
                }
            }
        }

        /// <summary>
        /// Implementation of the inequality operator.
        /// </summary>
        /// <param name="left">The left-hand argument</param>
        /// <param name="right">The right-hand argument</param>
        /// <returns>True if the objects are not equivalent, false otherwise.</returns>
        public static bool operator !=(BuildRequestConfiguration left, BuildRequestConfiguration right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Requests that the configuration be cached to disk.
        /// </summary>
        public void CacheIfPossible()
        {
            lock (_syncLock)
            {
                if (IsActivelyBuilding || IsCached || !IsLoaded || !IsCacheable)
                {
                    return;
                }

                lock (_project)
                {
                    if (IsCacheable)
                    {
                        using ITranslator translator = GetConfigurationTranslator(TranslationDirection.WriteToStream);

                        _project.Cache(translator);
                        _baseLookup = null;

                        IsCached = true;
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves the configuration data from the cache.
        /// </summary>
        public void RetrieveFromCache()
        {
            lock (_syncLock)
            {
                if (!IsLoaded)
                {
                    return;
                }

                if (!IsCached)
                {
                    return;
                }

                using ITranslator translator = GetConfigurationTranslator(TranslationDirection.ReadFromStream);

                _project.RetrieveFromCache(translator);

                IsCached = false;
            }
        }

        /// <summary>
        /// Gets the list of targets which are used to build the specified request, including all initial and applicable default targets
        /// </summary>
        /// <param name="request">The request </param>
        /// <returns>An array of t</returns>
        public List<string> GetTargetsUsedToBuildRequest(BuildRequest request)
        {
            ErrorUtilities.VerifyThrow(request.ConfigurationId == ConfigurationId, "Request does not match configuration.");
            ErrorUtilities.VerifyThrow(_projectInitialTargets != null, "Initial targets have not been set.");
            ErrorUtilities.VerifyThrow(_projectDefaultTargets != null, "Default targets have not been set.");

            if (request.ProxyTargets != null)
            {
                ErrorUtilities.VerifyThrow(
                    CollectionHelpers.SetEquivalent(request.Targets, request.ProxyTargets.ProxyTargetToRealTargetMap.Keys),
                    "Targets must be same as proxy targets");
            }

            List<string> initialTargets = _projectInitialTargets;
            List<string> nonInitialTargets = (request.Targets.Count == 0) ? _projectDefaultTargets : request.Targets;

            var allTargets = new List<string>(initialTargets.Count + nonInitialTargets.Count);

            allTargets.AddRange(initialTargets);
            allTargets.AddRange(nonInitialTargets);

            return allTargets;
        }

        private Func<string, bool> shouldSkipStaticGraphIsolationOnReference;

        public bool ShouldSkipIsolationConstraintsForReference(string referenceFullPath)
        {
            ErrorUtilities.VerifyThrowInternalNull(Project, nameof(Project));
            ErrorUtilities.VerifyThrowInternalLength(referenceFullPath, nameof(referenceFullPath));
            ErrorUtilities.VerifyThrow(Path.IsPathRooted(referenceFullPath), "Method does not treat path normalization cases");

            if (shouldSkipStaticGraphIsolationOnReference == null)
            {
                shouldSkipStaticGraphIsolationOnReference = GetReferenceFilter();
            }

            return shouldSkipStaticGraphIsolationOnReference(referenceFullPath);

            Func<string, bool> GetReferenceFilter()
            {
                lock (_syncLock)
                {
                    if (shouldSkipStaticGraphIsolationOnReference != null)
                    {
                        return shouldSkipStaticGraphIsolationOnReference;
                    }

                    var items = Project.GetItems(ItemTypeNames.GraphIsolationExemptReference);

                    if (items.Count == 0 || items.All(i => string.IsNullOrWhiteSpace(i.EvaluatedInclude)))
                    {
                        return _ => false;
                    }

                    var fragments = items.SelectMany(i => ExpressionShredder.SplitSemiColonSeparatedList(i.EvaluatedInclude));
                    var glob = new CompositeGlob(
                        fragments
                            .Select(s => MSBuildGlob.Parse(Project.Directory, s)));

                    return s => glob.IsMatch(s);
                }
            }
        }

        /// <summary>
        /// This override is used to provide a hash code for storage in dictionaries and the like.
        /// </summary>
        /// <remarks>
        /// If two objects are Equal, they must have the same hash code, for dictionaries to work correctly.
        /// Two configurations are Equal if their global properties are equivalent, not necessary reference equals.
        /// So only include filename and tools version in the hashcode.
        /// </remarks>
        /// <returns>A hash code</returns>
        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(_projectFullPath) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_toolsVersion);
        }

        /// <summary>
        /// Returns a string representation of the object
        /// </summary>
        /// <returns>String representation of the object</returns>
        public override string ToString()
        {
            return String.Format(CultureInfo.CurrentCulture, "{0} {1} {2} {3}", _configId, _projectFullPath, _toolsVersion, _globalProperties);
        }

        /// <summary>
        /// Determines object equality
        /// </summary>
        /// <param name="obj">The object to compare with</param>
        /// <returns>True if they contain the same data, false otherwise</returns>
        public override bool Equals(object obj)
        {
            if (obj is null)
            {
                return false;
            }

            if (GetType() != obj.GetType())
            {
                return false;
            }

            return InternalEquals((BuildRequestConfiguration)obj);
        }

        #region IEquatable<BuildRequestConfiguration> Members

        /// <summary>
        /// Equality of the configuration is the product of the equality of its members.
        /// </summary>
        /// <param name="other">The other configuration to which we will compare ourselves.</param>
        /// <returns>True if equal, false otherwise.</returns>
        public bool Equals(BuildRequestConfiguration other)
        {
            if (other is null)
            {
                return false;
            }

            return InternalEquals(other);
        }

        #endregion

        #region INodePacket Members

        /// <summary>
        /// Reads or writes the packet to the serializer.
        /// </summary>
        public void Translate(ITranslator translator)
        {
            if (translator.Mode == TranslationDirection.WriteToStream && _transferredProperties == null)
            {
                // When writing, we will transfer the state of any loaded project instance if we aren't transferring a limited subset.
                _transferredState = _project;
            }

            translator.Translate(ref _configId);
            translator.Translate(ref _projectFullPath);
            translator.Translate(ref _toolsVersion);
            translator.Translate(ref _explicitToolsVersionSpecified);
            translator.TranslateDictionary(ref _globalProperties, ProjectPropertyInstance.FactoryForDeserialization);
            translator.Translate(ref _transferredState, ProjectInstance.FactoryForDeserialization);
            translator.Translate(ref _transferredProperties, ProjectPropertyInstance.FactoryForDeserialization);
            translator.Translate(ref _resultsNodeId);
            translator.Translate(ref _savedCurrentDirectory);
            translator.TranslateDictionary(ref _savedEnvironmentVariables, StringComparer.OrdinalIgnoreCase);

            // if the  entire state is translated, then the transferred state represents the full evaluation data
            if (translator.Mode == TranslationDirection.ReadFromStream && _transferredState?.TranslateEntireState == true)
            {
                SetProjectBasedState(_transferredState);
            }
        }

        internal void TranslateForFutureUse(ITranslator translator)
        {
            translator.Translate(ref _configId);
            translator.Translate(ref _projectFullPath);
            translator.Translate(ref _toolsVersion);
            translator.Translate(ref _explicitToolsVersionSpecified);
            translator.Translate(ref _projectDefaultTargets);
            translator.Translate(ref _projectInitialTargets);
            translator.TranslateDictionary(ref _globalProperties, ProjectPropertyInstance.FactoryForDeserialization);
        }

        /// <summary>
        /// Factory for serialization.
        /// </summary>
        internal static BuildRequestConfiguration FactoryForDeserialization(ITranslator translator)
        {
            return new BuildRequestConfiguration(translator);
        }

        #endregion

        /// <summary>
        /// Applies the state from the specified instance to the loaded instance.  This overwrites the items and properties.
        /// </summary>
        /// <remarks>
        /// Used when we transfer results and state from a previous node to the current one.
        /// </remarks>
        internal void ApplyTransferredState(ProjectInstance instance)
        {
            if (instance != null)
            {
                _project.UpdateStateFrom(instance);
            }
        }

        /// <summary>
        /// Gets the name of the cache file for this configuration.
        /// </summary>
        internal string GetCacheFile()
        {
            string filename = Path.Combine(FileUtilities.GetCacheDirectory(), String.Format(CultureInfo.InvariantCulture, "Configuration{0}.cache", _configId));
            return filename;
        }

        /// <summary>
        /// Deletes the cache file
        /// </summary>
        internal void ClearCacheFile()
        {
            string cacheFile = GetCacheFile();
            if (FileSystems.Default.FileExists(cacheFile))
            {
                FileUtilities.DeleteNoThrow(cacheFile);
            }
        }

        /// <summary>
        /// Clones this BuildRequestConfiguration but sets a new configuration id.
        /// </summary>
        internal BuildRequestConfiguration ShallowCloneWithNewId(int newId)
        {
            return new BuildRequestConfiguration(newId, this);
        }

        /// <summary>
        /// Compares this object with another for equality
        /// </summary>
        /// <param name="other">The object with which to compare this one.</param>
        /// <returns>True if the objects contain the same data, false otherwise.</returns>
        private bool InternalEquals(BuildRequestConfiguration other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if ((other.WasGeneratedByNode == WasGeneratedByNode) &&
                (other._configId != InvalidConfigurationId) &&
                (_configId != InvalidConfigurationId))
            {
                return _configId == other._configId;
            }
            else
            {
                return _projectFullPath.Equals(other._projectFullPath, StringComparison.OrdinalIgnoreCase) &&
                       _toolsVersion.Equals(other._toolsVersion, StringComparison.OrdinalIgnoreCase) &&
                       _globalProperties.Equals(other._globalProperties);
            }
        }

        /// <summary>
        /// Determines what the real tools version is.
        /// </summary>
        private static string ResolveToolsVersion(BuildRequestData data, string defaultToolsVersion)
        {
            if (data.ExplicitToolsVersionSpecified)
            {
                return data.ExplicitlySpecifiedToolsVersion;
            }

            // None was specified by the call, fall back to the project's ToolsVersion attribute
            if (data.ProjectInstance != null)
            {
                return data.ProjectInstance.Toolset.ToolsVersion;
            }
            if (FileUtilities.IsVCProjFilename(data.ProjectFullPath))
            {
                ProjectFileErrorUtilities.ThrowInvalidProjectFile(new BuildEventFileInfo(data.ProjectFullPath), "ProjectUpgradeNeededToVcxProj", data.ProjectFullPath);
            }

            // We used to "sniff" the tools version from the project XML by opening it and reading the attribute.
            // This was causing unnecessary overhead since the ToolsVersion is never really used.  Instead we just
            // return the default tools version
            return defaultToolsVersion;
        }

        /// <summary>
        /// Gets the translator for this configuration.
        /// </summary>
        private ITranslator GetConfigurationTranslator(TranslationDirection direction)
        {
            string cacheFile = GetCacheFile();
            try
            {
                if (direction == TranslationDirection.WriteToStream)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                    return BinaryTranslator.GetWriteTranslator(File.Create(cacheFile));
                }
                else
                {
                    // Not using sharedReadBuffer because this is not a memory stream and so the buffer won't be used anyway.
                    return BinaryTranslator.GetReadTranslator(File.OpenRead(cacheFile), null);
                }
            }
            catch (Exception e)
            {
                if (e is DirectoryNotFoundException || e is UnauthorizedAccessException)
                {
                    ErrorUtilities.ThrowInvalidOperation("CacheFileInaccessible", cacheFile, e);
                }

                // UNREACHABLE
                throw;
            }
        }
    }
}
