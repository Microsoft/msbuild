﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------
// </copyright>
// <summary>The public class representing the data for a build request.</summary>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

using Microsoft.Build.Collections;
using Microsoft.Build.Shared;

namespace Microsoft.Build.Execution
{
    /// <summary>
    /// Flags providing additional control over the build request
    /// </summary>
    [Flags]
    public enum BuildRequestDataFlags
    {
        /// <summary>
        /// No flags.
        /// </summary>
        None = 0,

        /// <summary>
        /// When this flag is present, the existing ProjectInstance in the build will be replaced by this one.
        /// </summary>
        ReplaceExistingProjectInstance = 0x1,

        /// <summary>
        /// When this flag is present, <see cref="BuildResult"/> issued in response to this request will
        /// include <see cref="BuildResult.ProjectStateAfterBuild"/>.
        /// </summary>
        ProvideProjectStateAfterBuild = 0x2,

        /// <summary>
        /// When this flag is present and the project has previously been built on a node whose affinity is
        /// incompatible with the affinity this request requires, we will ignore the project state (but not
        /// target results) that were previously generated.
        /// </summary>
        /// <remarks>
        /// This usually is not desired behavior.  It is only provided for those cases where the client
        /// knows that the new build request does not depend on project state generated by a previous request.  Setting
        /// this flag can provide a performance boost in the case of incompatible node affinities, as MSBuild would
        /// otherwise have to serialize the project state from one node to another, which may be 
        /// expensive depending on how much data the project previously generated.
        /// 
        /// This flag has no effect on target results, so if a previous request already built a target, the new 
        /// request will not re-build that target (nor will any of the project state mutations which previously
        /// occurred as a consequence of building that target be re-applied.)
        /// </remarks>
        IgnoreExistingProjectState = 0x4,
    }

    /// <summary>
    /// BuildRequestData encapsulates all of the data needed to submit a build request.
    /// </summary>
    public class BuildRequestData
    {
        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project instances.
        /// </summary>
        /// <param name="projectInstance">The instance to build.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        public BuildRequestData(ProjectInstance projectInstance, string[] targetsToBuild)
            : this(projectInstance, targetsToBuild, null, BuildRequestDataFlags.None)
        {
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project instances.
        /// </summary>
        /// <param name="projectInstance">The instance to build.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use, if any.  May be null.</param>
        public BuildRequestData(ProjectInstance projectInstance, string[] targetsToBuild, HostServices hostServices)
            : this(projectInstance, targetsToBuild, hostServices, BuildRequestDataFlags.None)
        {
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project instances.
        /// </summary>
        /// <param name="projectInstance">The instance to build.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use, if any.  May be null.</param>
        /// <param name="flags">Flags controlling this build request.</param>
        public BuildRequestData(ProjectInstance projectInstance, string[] targetsToBuild, HostServices hostServices, BuildRequestDataFlags flags)
            : this(projectInstance, targetsToBuild, hostServices, flags, null)
        {
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project instances.
        /// </summary>
        /// <param name="projectInstance">The instance to build.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use, if any.  May be null.</param>
        /// <param name="flags">Flags controlling this build request.</param>
        /// <param name="propertiesToTransfer">The list of properties whose values should be transferred from the project to any out-of-proc node.</param>
        public BuildRequestData(ProjectInstance projectInstance, string[] targetsToBuild, HostServices hostServices, BuildRequestDataFlags flags, IEnumerable<string> propertiesToTransfer)
            : this(targetsToBuild, hostServices, flags)
        {
            ErrorUtilities.VerifyThrowArgumentNull(projectInstance, "projectInstance");

            foreach (string targetName in targetsToBuild)
            {
                ErrorUtilities.VerifyThrowArgumentNull(targetName, "target");
            }

            ProjectInstance = projectInstance;

            ProjectFullPath = projectInstance.FullPath;
            GlobalPropertiesDictionary = projectInstance.GlobalPropertiesDictionary;
            ExplicitlySpecifiedToolsVersion = projectInstance.ExplicitToolsVersion;
            if (propertiesToTransfer != null)
            {
                this.PropertiesToTransfer = new List<string>(propertiesToTransfer);
            }
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project files.
        /// </summary>
        /// <param name="projectFullPath">The full path to the project file.</param>
        /// <param name="globalProperties">The global properties which should be used during evaluation of the project.  Cannot be null.</param>
        /// <param name="toolsVersion">The tools version to use for the build.  May be null.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use.  May be null.</param>
        public BuildRequestData(string projectFullPath, IDictionary<string, string> globalProperties, string toolsVersion, string[] targetsToBuild, HostServices hostServices)
            : this(projectFullPath, globalProperties, toolsVersion, targetsToBuild, hostServices, BuildRequestDataFlags.None)
        {
        }

        /// <summary>
        /// Constructs a BuildRequestData for build requests based on project files.
        /// </summary>
        /// <param name="projectFullPath">The full path to the project file.</param>
        /// <param name="globalProperties">The global properties which should be used during evaluation of the project.  Cannot be null.</param>
        /// <param name="toolsVersion">The tools version to use for the build.  May be null.</param>
        /// <param name="targetsToBuild">The targets to build.</param>
        /// <param name="hostServices">The host services to use.  May be null.</param>
        public BuildRequestData(string projectFullPath, IDictionary<string, string> globalProperties, string toolsVersion, string[] targetsToBuild, HostServices hostServices, BuildRequestDataFlags flags)
            : this(targetsToBuild, hostServices, flags)
        {
            ErrorUtilities.VerifyThrowArgumentLength(projectFullPath, "projectFullPath");
            ErrorUtilities.VerifyThrowArgumentNull(globalProperties, "globalProperties");

            this.ProjectFullPath = FileUtilities.NormalizePath(projectFullPath);
            TargetNames = (ICollection<string>)targetsToBuild.Clone();
            GlobalPropertiesDictionary = new PropertyDictionary<ProjectPropertyInstance>(globalProperties.Count);
            foreach (KeyValuePair<string, string> propertyPair in globalProperties)
            {
                GlobalPropertiesDictionary.Set(ProjectPropertyInstance.Create(propertyPair.Key, propertyPair.Value));
            }

            ExplicitlySpecifiedToolsVersion = toolsVersion;
        }

        /// <summary>
        /// Common constructor.
        /// </summary>
        private BuildRequestData(string[] targetsToBuild, HostServices hostServices, BuildRequestDataFlags flags)
        {
            ErrorUtilities.VerifyThrowArgumentNull(targetsToBuild, "targetsToBuild");

            HostServices = hostServices;
            TargetNames = new List<string>(targetsToBuild);
            Flags = flags;
        }

        /// <summary>
        /// The actual project, in the case where the project doesn't come from disk.
        /// May be null.
        /// </summary>
        /// <value>The project instance.</value>
        public ProjectInstance ProjectInstance
        {
            get;
            private set;
        }

        /// <summary>The project file.</summary>
        /// <value>The project file to be built.</value>
        public string ProjectFullPath
        {
            get;
            internal set;
        }

        /// <summary>
        /// The name of the targets to build.
        /// </summary>
        /// <value>An array of targets in the project to be built.</value>
        public ICollection<string> TargetNames
        {
            get;
            private set;
        }

        /// <summary>
        /// Extra flags for this BuildRequest.
        /// </summary>
        public BuildRequestDataFlags Flags
        {
            get;
            private set;
        }

        /// <summary>
        /// The global properties to use.
        /// </summary>
        /// <value>The set of global properties to be used to build this request.</value>
        public ICollection<ProjectPropertyInstance> GlobalProperties
        {
            get
            {
                return (GlobalPropertiesDictionary == null) ?
                    (ICollection<ProjectPropertyInstance>)ReadOnlyEmptyCollection<ProjectPropertyInstance>.Instance :
                    new ReadOnlyCollection<ProjectPropertyInstance>(GlobalPropertiesDictionary);
            }
        }

        /// <summary>
        /// The explicitly requested tools version to use.
        /// </summary>
        public string ExplicitlySpecifiedToolsVersion
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the HostServices object for this request.
        /// </summary>
        public HostServices HostServices
        {
            get;
            private set;
        }

        /// <summary>
        /// Returns a list of properties to transfer out of proc for the build.
        /// </summary>
        public IEnumerable<string> PropertiesToTransfer
        {
            get;
            private set;
        }

        /// <summary>
        /// Whether the tools version used originated from an explicit specification,
        /// for example from an MSBuild task or /tv switch.
        /// </summary>
        internal bool ExplicitToolsVersionSpecified
        {
            get { return (ExplicitlySpecifiedToolsVersion != null); }
        }

        /// <summary>
        /// Returns the global properties as a dictionary.
        /// </summary>
        internal PropertyDictionary<ProjectPropertyInstance> GlobalPropertiesDictionary
        {
            get;
            private set;
        }
    }
}
