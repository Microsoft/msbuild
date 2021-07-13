// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

using Microsoft.Build.BackEnd.SdkResolution;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

using Project = Microsoft.Build.Evaluation.Project;
using ProjectCollection = Microsoft.Build.Evaluation.ProjectCollection;
using ProjectItem = Microsoft.Build.Evaluation.ProjectItem;
using IProperty = Microsoft.Build.Evaluation.IProperty;

using Constants = Microsoft.Build.Internal.Constants;
using ILoggingService = Microsoft.Build.BackEnd.Logging.ILoggingService;

using FrameworkName = System.Runtime.Versioning.FrameworkName;
using Microsoft.Build.Execution;
using Microsoft.Build.Utilities;

using Microsoft.NET.StringTools;

namespace Microsoft.Build.Construction
{
    /// <summary>
    /// This class is used to generate an MSBuild wrapper project for a solution file.
    /// </summary>
    internal class SolutionProjectGenerator
    {
        /// <summary>
        /// Name of the property used to store the path to the solution being built.
        /// </summary>
        internal const string SolutionPathPropertyName = "SolutionPath";

#if FEATURE_ASPNET_COMPILER
        /// <summary>
        /// The path node to add in when the output directory for a website is overridden.
        /// </summary>
        private const string WebProjectOverrideFolder = "_PublishedWebsites";
#endif // FEATURE_ASPNET_COMPILER

        /// <summary>
        /// The set of properties all projects in the solution should be built with
        /// </summary>
        private const string SolutionProperties = "BuildingSolutionFile=true; CurrentSolutionConfigurationContents=$(CurrentSolutionConfigurationContents); SolutionDir=$(SolutionDir); SolutionExt=$(SolutionExt); SolutionFileName=$(SolutionFileName); SolutionName=$(SolutionName); SolutionPath=$(SolutionPath)";

        /// <summary>
        /// The set of properties which identify the configuration and platform to build a project with
        /// </summary>
        private const string SolutionConfigurationAndPlatformProperties = "Configuration=$(Configuration); Platform=$(Platform)";

        /// <summary>
        /// A known list of target names to create.  This is for backwards compatibility.
        /// </summary>
        internal static readonly ImmutableHashSet<string> _defaultTargetNames = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            "Build",
            "Clean",
            "Rebuild",
            "Publish",
            "ValidateSolutionConfiguration",
            "ValidateToolsVersions",
            "ValidateProjects",
            "GetSolutionConfigurationContents"
            );

#if FEATURE_ASPNET_COMPILER
        /// <summary>
        /// Version 2.0
        /// </summary>
        private readonly Version _version20 = new Version(2, 0);

        /// <summary>
        /// Version 4.0
        /// </summary>
        private readonly Version _version40 = new Version(4, 0);
#endif // FEATURE_ASPNET_COMPILER

        /// <summary>
        /// The list of global properties we set on each metaproject and which get passed to each project when building.
        /// </summary>
        private readonly Tuple<string, string>[] _metaprojectGlobalProperties =
        {
            new Tuple<string, string>("Configuration", null), // This is the solution configuration in a metaproject, and project configuration on an actual project
            new Tuple<string, string>("Platform", null), // This is the solution platform in a metaproject, and project platform on an actual project
            new Tuple<string, string>("BuildingSolutionFile", "true"),
            new Tuple<string, string>("CurrentSolutionConfigurationContents", null),
            new Tuple<string, string>("SolutionDir", null),
            new Tuple<string, string>("SolutionExt", null),
            new Tuple<string, string>("SolutionFileName", null),
            new Tuple<string, string>("SolutionName", null),
            new Tuple<string, string>(SolutionPathPropertyName, null)
        };

        /// <summary>
        /// The SolutionFile containing information about the solution we're generating a wrapper for.
        /// </summary>
        private readonly SolutionFile _solutionFile;

        /// <summary>
        /// The global properties passed under which the project should be opened.
        /// </summary>
        private readonly IDictionary<string, string> _globalProperties;

        /// <summary>
        /// The ToolsVersion passed on the commandline, if any.  May be null.
        /// </summary>
        private readonly string _toolsVersionOverride;

        /// <summary>
        /// The context of this build (used for logging purposes).
        /// </summary>
        private readonly BuildEventContext _projectBuildEventContext;

        /// <summary>
        /// The LoggingService used to log messages.
        /// </summary>
        private readonly ILoggingService _loggingService;

        /// <summary>
        /// The list of targets specified to use.
        /// </summary>
        private readonly IReadOnlyCollection<string> _targetNames = new Collection<string>();

        /// <summary>
        /// The solution configuration selected for this build.
        /// </summary>
        private string _selectedSolutionConfiguration;

        /// <summary>
        /// The <see cref="ISdkResolverService"/> to use.
        /// </summary>
        private readonly ISdkResolverService _sdkResolverService;

        /// <summary>
        /// The current build submission ID.
        /// </summary>
        private readonly int _submissionId;

        /// <summary>
        /// Constructor.
        /// </summary>
        private SolutionProjectGenerator(
            SolutionFile solution,
            IDictionary<string, string> globalProperties,
            string toolsVersionOverride,
            BuildEventContext projectBuildEventContext,
            ILoggingService loggingService,
            IReadOnlyCollection<string> targetNames,
            ISdkResolverService sdkResolverService,
            int submissionId)
        {
            _solutionFile = solution;
            _globalProperties = globalProperties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _toolsVersionOverride = toolsVersionOverride;
            _projectBuildEventContext = projectBuildEventContext;
            _loggingService = loggingService;
            _sdkResolverService = sdkResolverService ?? SdkResolverService.Instance;
            _submissionId = submissionId;

            if (targetNames != null)
            {
                _targetNames = targetNames.Select(i => i.Split(new char[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries).Last()).ToList();
            }
        }

        /// <summary>
        /// This method generates an MSBuild project file from the list of projects and project dependencies
        /// that have been collected from the solution file.
        /// </summary>
        /// <param name="solution">The parser which contains the solution file.</param>
        /// <param name="globalProperties">The global properties.</param>
        /// <param name="toolsVersionOverride">Tools Version override (may be null).  This should be any tools version explicitly passed to the command-line or from an MSBuild ToolsVersion parameter.</param>
        /// <param name="projectBuildEventContext">The logging context for this project.</param>
        /// <param name="loggingService">The logging service.</param>
        /// <param name="targetNames">A collection of target names the user requested to be built.</param>
        /// <param name="sdkResolverService">An <see cref="ISdkResolverService"/> to use.</param>
        /// <param name="submissionId">The current build submission ID.</param>
        /// <returns>An array of ProjectInstances.  The first instance is the traversal project, the remaining are the metaprojects for each project referenced in the solution.</returns>
        internal static ProjectInstance[] Generate
            (
            SolutionFile solution,
            IDictionary<string, string> globalProperties,
            string toolsVersionOverride,
            BuildEventContext projectBuildEventContext,
            ILoggingService loggingService,
            IReadOnlyCollection<string> targetNames = default(IReadOnlyCollection<string>),
            ISdkResolverService sdkResolverService = null,
            int submissionId = BuildEventContext.InvalidSubmissionId)
        {
            SolutionProjectGenerator projectGenerator = new SolutionProjectGenerator
                (
                solution,
                globalProperties,
                toolsVersionOverride,
                projectBuildEventContext,
                loggingService,
                targetNames,
                sdkResolverService,
                submissionId
                );

            return projectGenerator.Generate();
        }

        /// <summary>
        /// Adds a new property group with contents of the given solution configuration to the project
        /// Internal for unit-testing.
        /// </summary>
        internal static void AddPropertyGroupForSolutionConfiguration(ProjectRootElement msbuildProject, SolutionFile solutionFile, SolutionConfigurationInSolution solutionConfiguration)
        {
            ProjectPropertyGroupElement solutionConfigurationProperties = msbuildProject.CreatePropertyGroupElement();
            msbuildProject.AppendChild(solutionConfigurationProperties);
            solutionConfigurationProperties.Condition = GetConditionStringForConfiguration(solutionConfiguration);

            var solutionConfigurationContents = new StringBuilder(1024);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true
            };
            using (XmlWriter xw = XmlWriter.Create(solutionConfigurationContents, settings))
            {
                xw.WriteStartElement("SolutionConfiguration");

                // add a project configuration entry for each project in the solution
                foreach (ProjectInSolution project in solutionFile.ProjectsInOrder)
                {
                    if (project.ProjectConfigurations.TryGetValue(solutionConfiguration.FullName, out ProjectConfigurationInSolution projectConfiguration))
                    {
                        xw.WriteStartElement("ProjectConfiguration");
                        xw.WriteAttributeString("Project", project.ProjectGuid);
                        xw.WriteAttributeString("AbsolutePath", project.AbsolutePath);
                        xw.WriteAttributeString("BuildProjectInSolution", projectConfiguration.IncludeInBuild.ToString());
                        xw.WriteString(projectConfiguration.FullName);

                        foreach (string dependencyProjectGuid in project.Dependencies)
                        {
                            // This is a project that the current project depends *ON* (ie., it must build first)
                            if (!solutionFile.ProjectsByGuid.TryGetValue(dependencyProjectGuid, out ProjectInSolution dependencyProject))
                            {
                                // If it's not itself part of the solution, that's an invalid solution
                                ProjectFileErrorUtilities.VerifyThrowInvalidProjectFile(dependencyProject != null, "SubCategoryForSolutionParsingErrors", new BuildEventFileInfo(solutionFile.FullPath), "SolutionParseProjectDepNotFoundError", project.ProjectGuid, dependencyProjectGuid);
                            }

                            // Add it to the list of dependencies, but only if it should build in this solution configuration 
                            // (If a project is not selected for build in the solution configuration, it won't build even if it's depended on by something that IS selected for build)
                            // .. and only if it's known to be MSBuild format, as projects can't use the information otherwise 
                            if (dependencyProject.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
                            {
                                if (dependencyProject.ProjectConfigurations.TryGetValue(solutionConfiguration.FullName, out ProjectConfigurationInSolution dependencyProjectConfiguration) &&
                                    WouldProjectBuild(solutionFile, solutionConfiguration.FullName, dependencyProject, dependencyProjectConfiguration))
                                {
                                    xw.WriteStartElement("ProjectDependency");
                                    xw.WriteAttributeString("Project", dependencyProjectGuid);
                                    xw.WriteEndElement();
                                }
                            }
                        }

                        xw.WriteEndElement(); // </ProjectConfiguration>
                    }
                }

                xw.WriteEndElement(); // </SolutionConfiguration>
            }

            var escapedSolutionConfigurationContents = EscapingUtilities.Escape(solutionConfigurationContents.ToString());

            solutionConfigurationProperties.AddProperty("CurrentSolutionConfigurationContents", escapedSolutionConfigurationContents);

            msbuildProject.AddItem(
                "SolutionConfiguration",
                solutionConfiguration.FullName,
                new Dictionary<string, string>
                {
                    { "Configuration", solutionConfiguration.ConfigurationName },
                    { "Platform", solutionConfiguration.PlatformName },
                    { "Content", escapedSolutionConfigurationContents },
                });
        }

        /// <summary>
        /// Add a new error/warning/message tag into the given target
        /// </summary>
        internal static ProjectTaskElement AddErrorWarningMessageElement
            (
            ProjectTargetElement target,
            string elementType,
            bool treatAsLiteral,
            string textResourceName,
            params object[] args
            )
        {
            string text = ResourceUtilities.FormatResourceStringStripCodeAndKeyword(out string code, out string helpKeyword, textResourceName, args);

            if (treatAsLiteral)
            {
                text = EscapingUtilities.Escape(text);
            }

            ProjectTaskElement task = target.AddTask(elementType);
            task.SetParameter("Text", text);

            if ((elementType != XMakeElements.message) && (code != null))
            {
                task.SetParameter("Code", EscapingUtilities.Escape(code));
            }

            if ((elementType != XMakeElements.message) && (helpKeyword != null))
            {
                task.SetParameter("HelpKeyword", EscapingUtilities.Escape(helpKeyword));
            }

            return task;
        }

        /// <summary>
        /// Normally the active solution configuration/platform is determined when we build the solution
        /// wrapper project, not when we create it. However, we need to know them to scan project references
        /// for the right project configuration/platform. It's unlikely that references would be conditional,
        /// but still possible and we want to get that case right.
        /// </summary>
        internal static string PredictActiveSolutionConfigurationName(SolutionFile solutionFile, IDictionary<string, string> globalProperties)
        {
            string candidateFullSolutionConfigurationName = DetermineLikelyActiveSolutionConfiguration(solutionFile, globalProperties);

            // Now check if this solution configuration actually exists
            string fullSolutionConfigurationName = null;

            foreach (SolutionConfigurationInSolution solutionConfiguration in solutionFile.SolutionConfigurations)
            {
                if (String.Equals(solutionConfiguration.FullName, candidateFullSolutionConfigurationName, StringComparison.OrdinalIgnoreCase))
                {
                    fullSolutionConfigurationName = solutionConfiguration.FullName;
                    break;
                }
            }

            return fullSolutionConfigurationName;
        }

        /// <summary>
        /// Returns the name of the metaproject for an actual project.
        /// </summary>
        /// <param name="fullPathToProject">The full path to the actual project</param>
        /// <returns>The metaproject path name</returns>
        private static string GetMetaprojectName(string fullPathToProject)
        {
            return EscapingUtilities.Escape(fullPathToProject + ".metaproj");
        }

        /// <summary>
        /// Figure out what tools version to build the solution wrapper project with. If a /tv
        /// switch was passed in, use that; otherwise fall back to the default (12.0).
        /// </summary>
        private static string DetermineWrapperProjectToolsVersion(string toolsVersionOverride, out bool explicitToolsVersionSpecified)
        {
            string wrapperProjectToolsVersion = toolsVersionOverride;

            if (wrapperProjectToolsVersion == null)
            {
                explicitToolsVersionSpecified = false;
                wrapperProjectToolsVersion = Constants.defaultSolutionWrapperProjectToolsVersion;
            }
            else
            {
                explicitToolsVersionSpecified = true;
            }

            return wrapperProjectToolsVersion;
        }

#if FEATURE_ASPNET_COMPILER
        /// <summary>
        /// Add a call to the ResolveAssemblyReference task to crack the pre-resolved referenced
        /// assemblies for the complete list of dependencies, PDBs, satellites, etc.  The invoke
        /// the Copy task to copy all these files (or at least the ones that RAR determined should
        /// be copied local) into the web project's bin directory.
        /// </summary>
        private static void AddTasksToCopyAllDependenciesIntoBinDir
            (
            ProjectTargetInstance target,
            ProjectInSolution project,
            string referenceItemName,
            string conditionDescribingValidConfigurations
            )
        {
            string copyLocalFilesItemName = referenceItemName + "_CopyLocalFiles";
            string targetFrameworkDirectoriesName = GenerateSafePropertyName(project, "_TargetFrameworkDirectories");
            string fullFrameworkRefAssyPathName = GenerateSafePropertyName(project, "_FullFrameworkReferenceAssemblyPaths");
            string destinationFolder = String.Format(CultureInfo.InvariantCulture, @"$({0})\Bin\", GenerateSafePropertyName(project, "AspNetPhysicalPath"));

            // This is a bit of a hack.  We're actually calling the "Copy" task on all of 
            // the *non-existent* files.  Why?  Because we want to emit a warning in the 
            // log for each non-existent file, and the Copy task does that nicely for us.
            // I would have used the <Warning> task except for the fact that we are in 
            // string-resource lockdown.
            ProjectTaskInstance copyNonExistentReferencesTask = target.AddTask("Copy", String.Format(CultureInfo.InvariantCulture, "!Exists('%({0}.Identity)')", referenceItemName), "true");
            copyNonExistentReferencesTask.SetParameter("SourceFiles", "@(" + referenceItemName + "->'%(FullPath)')");
            copyNonExistentReferencesTask.SetParameter("DestinationFolder", destinationFolder);

            // We need to determine the appropriate TargetFrameworkMoniker to pass to GetReferenceAssemblyPaths,
            // so that we will pass the appropriate target framework directories to RAR.
            ProjectTaskInstance getRefAssembliesTask = target.AddTask("GetReferenceAssemblyPaths", null, null);
            getRefAssembliesTask.SetParameter("TargetFrameworkMoniker", project.TargetFrameworkMoniker);
            getRefAssembliesTask.SetParameter("RootPath", "$(TargetFrameworkRootPath)");
            getRefAssembliesTask.AddOutputProperty("ReferenceAssemblyPaths", targetFrameworkDirectoriesName, null);
            getRefAssembliesTask.AddOutputProperty("FullFrameworkReferenceAssemblyPaths", fullFrameworkRefAssyPathName, null);

            // Call ResolveAssemblyReference on each of the .DLL files that were found on 
            // disk from the .REFRESH files as well as the P2P references.  RAR will crack
            // the dependencies, find PDBs, satellite assemblies, etc., and determine which
            // files need to be copy-localed.
            ProjectTaskInstance rarTask = target.AddTask("ResolveAssemblyReference", String.Format(CultureInfo.InvariantCulture, "Exists('%({0}.Identity)')", referenceItemName), null);
            rarTask.SetParameter("Assemblies", "@(" + referenceItemName + "->'%(FullPath)')");
            rarTask.SetParameter("TargetFrameworkDirectories", "$(" + targetFrameworkDirectoriesName + ")");
            rarTask.SetParameter("FullFrameworkFolders", "$(" + fullFrameworkRefAssyPathName + ")");
            rarTask.SetParameter("SearchPaths", "{RawFileName};{TargetFrameworkDirectory};{GAC}");
            rarTask.SetParameter("FindDependencies", "true");
            rarTask.SetParameter("FindSatellites", "true");
            rarTask.SetParameter("FindSerializationAssemblies", "true");
            rarTask.SetParameter("FindRelatedFiles", "true");
            rarTask.SetParameter("TargetFrameworkMoniker", project.TargetFrameworkMoniker);
            rarTask.AddOutputItem("CopyLocalFiles", copyLocalFilesItemName, null);

            // Copy all the copy-local files (reported by RAR) to the web project's "bin"
            // directory.
            ProjectTaskInstance copyTask = target.AddTask("Copy", conditionDescribingValidConfigurations, null);
            copyTask.SetParameter("SourceFiles", "@(" + copyLocalFilesItemName + ")");
            copyTask.SetParameter
                (
                "DestinationFiles",
                String.Format(CultureInfo.InvariantCulture, @"@({0}->'{1}%(DestinationSubDirectory)%(Filename)%(Extension)')", copyLocalFilesItemName, destinationFolder)
                );
        }

        /// <summary>
        /// This code handles the *.REFRESH files that are in the "bin" subdirectory of
        /// a web project.  These .REFRESH files are just text files that contain absolute or
        /// relative paths to the referenced assemblies.  The goal of these tasks is to
        /// search all *.REFRESH files and extract fully-qualified absolute paths for
        /// each of the references.
        /// </summary>
        private static void AddTasksToResolveAutoRefreshFileReferences
            (
            ProjectTargetInstance target,
            ProjectInSolution project,
            string referenceItemName
            )
        {
            string webRoot = "$(" + GenerateSafePropertyName(project, "AspNetPhysicalPath") + ")";

            // Create an item list containing each of the .REFRESH files.
            ProjectTaskInstance createItemTask = target.AddTask("CreateItem", null, null);
            createItemTask.SetParameter("Include", webRoot + @"\Bin\*.refresh");
            createItemTask.AddOutputItem("Include", referenceItemName + "_RefreshFile", null);

            // Read the lines out of each .REFRESH file; they should be paths to .DLLs.  Put these paths
            // into an item list.
            ProjectTaskInstance readLinesTask = target.AddTask("ReadLinesFromFile", String.Format(CultureInfo.InvariantCulture, @" '%({0}_RefreshFile.Identity)' != '' ", referenceItemName), null);
            readLinesTask.SetParameter("File", String.Format(CultureInfo.InvariantCulture, @"%({0}_RefreshFile.Identity)", referenceItemName));
            readLinesTask.AddOutputItem("Lines", referenceItemName + "_ReferenceRelPath", null);

            // Take those paths and combine them with the root of the web project to form either
            // an absolute path or a path relative to the .SLN file.  These paths can be passed
            // directly to RAR later.
            ProjectTaskInstance combinePathTask = target.AddTask("CombinePath", null, null);
            combinePathTask.SetParameter("BasePath", webRoot);
            combinePathTask.SetParameter("Paths", String.Format(CultureInfo.InvariantCulture, @"@({0}_ReferenceRelPath)", referenceItemName));
            combinePathTask.AddOutputItem("CombinedPaths", referenceItemName, null);
        }

        /// <summary>
        /// Adds an MSBuild task to the specified target
        /// </summary>
        private static ProjectTaskInstance AddMSBuildTaskInstance
        (
            ProjectTargetInstance target,
            string projectPath,
            string msbuildTargetName,
            string configurationName,
            string platformName,
            bool specifyProjectToolsVersion
        )
        {
            ProjectTaskInstance msbuildTask = target.AddTask("MSBuild", null, null);
            msbuildTask.SetParameter("Projects", EscapingUtilities.Escape(projectPath));

            if (!string.IsNullOrEmpty(msbuildTargetName))
            {
                msbuildTask.SetParameter("Targets", msbuildTargetName);
            }

            string additionalProperties = string.Format(
                CultureInfo.InvariantCulture,
                "Configuration={0}; Platform={1}; BuildingSolutionFile=true; CurrentSolutionConfigurationContents=$(CurrentSolutionConfigurationContents); SolutionDir=$(SolutionDir); SolutionExt=$(SolutionExt); SolutionFileName=$(SolutionFileName); SolutionName=$(SolutionName); SolutionPath=$(SolutionPath)",
                EscapingUtilities.Escape(configurationName),
                EscapingUtilities.Escape(platformName)
            );

            msbuildTask.SetParameter("Properties", additionalProperties);
            if (specifyProjectToolsVersion)
            {
                msbuildTask.SetParameter("ToolsVersion", "$(ProjectToolsVersion)");
            }

            return msbuildTask;
        }

        /// <summary>
        /// Takes a project in the solution and a base property name, and creates a new property name
        /// that can safely be used as an XML element name, and is also unique to that project (by
        /// embedding the project's GUID into the property name.
        /// </summary>
        private static string GenerateSafePropertyName
            (
            ProjectInSolution proj,
            string propertyName
            )
        {
            // XML element names cannot contain curly braces, so get rid of them from the project guid.
            string projectGuid = proj.ProjectGuid.Substring(1, proj.ProjectGuid.Length - 2);
            return "Project_" + projectGuid + "_" + propertyName;
        }
#endif // FEATURE_ASPNET_COMPILER

        /// <summary>
        /// Makes a legal item name from a given string by replacing invalid characters with '_'
        /// </summary>
        private static string MakeIntoSafeItemName(string name)
        {
            var builder = new StringBuilder(name);

            if (name.Length > 0)
            {
                if (!XmlUtilities.IsValidInitialElementNameCharacter(name[0]))
                {
                    builder[0] = '_';
                }
            }

            for (int i = 1; i < builder.Length; i++)
            {
                if (!XmlUtilities.IsValidSubsequentElementNameCharacter(builder[i]))
                {
                    builder[i] = '_';
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Add a new error/warning/message tag into the given target
        /// </summary>
        private static ProjectTaskInstance AddErrorWarningMessageInstance
            (
            ProjectTargetInstance target,
            string condition,
            string elementType,
            bool treatAsLiteral,
            string textResourceName,
            params object[] args
            )
        {
            string text = ResourceUtilities.FormatResourceStringStripCodeAndKeyword(out string code, out string helpKeyword, textResourceName, args);

            if (treatAsLiteral)
            {
                text = EscapingUtilities.Escape(text);
            }

            ProjectTaskInstance task = target.AddTask(elementType, condition, null);
            task.SetParameter("Text", text);

            if ((elementType != XMakeElements.message) && (code != null))
            {
                task.SetParameter("Code", EscapingUtilities.Escape(code));
            }

            if ((elementType != XMakeElements.message) && (helpKeyword != null))
            {
                task.SetParameter("HelpKeyword", EscapingUtilities.Escape(helpKeyword));
            }

            return task;
        }

        /// <summary>
        /// A helper method for constructing conditions for a solution configuration
        /// </summary>
        /// <remarks>
        /// Sample configuration condition:
        /// '$(Configuration)' == 'Release' and '$(Platform)' == 'Any CPU'
        /// </remarks>
        private static string GetConditionStringForConfiguration(SolutionConfigurationInSolution configuration)
        {
            return string.Format
                (
                CultureInfo.InvariantCulture,
                " ('$(Configuration)' == '{0}') and ('$(Platform)' == '{1}') ",
                EscapingUtilities.Escape(configuration.ConfigurationName),
                EscapingUtilities.Escape(configuration.PlatformName)
                );
        }

        /// <summary>
        /// Figure out what solution configuration we are going to build, whether or not it actually exists in the solution
        /// file.
        /// </summary>
        private static string DetermineLikelyActiveSolutionConfiguration(SolutionFile solutionFile, IDictionary<string, string> globalProperties)
        {
            globalProperties.TryGetValue("Configuration", out string activeSolutionConfiguration);
            globalProperties.TryGetValue("Platform", out string activeSolutionPlatform);

            if (String.IsNullOrEmpty(activeSolutionConfiguration))
            {
                activeSolutionConfiguration = solutionFile.GetDefaultConfigurationName();
            }

            if (String.IsNullOrEmpty(activeSolutionPlatform))
            {
                activeSolutionPlatform = solutionFile.GetDefaultPlatformName();
            }

            var configurationInSolution = new SolutionConfigurationInSolution(activeSolutionConfiguration, activeSolutionPlatform);

            return configurationInSolution.FullName;
        }

        /// <summary>
        /// Returns true if the specified project will build in the currently selected solution configuration.
        /// </summary>
        private static bool WouldProjectBuild(SolutionFile solutionFile, string selectedSolutionConfiguration, ProjectInSolution project, ProjectConfigurationInSolution projectConfiguration)
        {
            // If the solution filter does not contain this project, do not build it.
            if (!solutionFile.ProjectShouldBuild(project.RelativePath))
            {
                return false;
            }

            if (projectConfiguration == null)
            {
                if (project.ProjectType == SolutionProjectType.WebProject)
                {
                    // Sometimes web projects won't have the configuration we need (Release typically.)  But they should still build if there is
                    // a solution configuration for it
                    foreach (SolutionConfigurationInSolution configuration in solutionFile.SolutionConfigurations)
                    {
                        if (String.Equals(configuration.FullName, selectedSolutionConfiguration, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                // No configuration, so it can't build.
                return false;
            }

            if (!projectConfiguration.IncludeInBuild)
            {
                // Not included in the build.
                return false;
            }

            return true;
        }

        /// <summary>
        /// Private method: generates an MSBuild wrapper project for the solution passed in; the MSBuild wrapper
        /// project to be generated is the private variable "msbuildProject" and the SolutionFile containing information
        /// about the solution is the private variable "solutionFile"
        /// </summary>
        private ProjectInstance[] Generate()
        {
            // Validate against our minimum for upgradable projects
            ProjectFileErrorUtilities.VerifyThrowInvalidProjectFile
                (
                _solutionFile.Version >= SolutionFile.slnFileMinVersion,
                "SubCategoryForSolutionParsingErrors",
                new BuildEventFileInfo(_solutionFile.FullPath),
                "SolutionParseUpgradeNeeded"
                );

            // This is needed in order to make decisions about tools versions such as whether to put a 
            // ToolsVersion parameter on <MSBuild> task tags and what MSBuildToolsPath to use when 
            // scanning child projects for dependency information.
            // The knowledge of whether it was explicitly specified is required because otherwise we 
            // don't know whether we need to pass the ToolsVersion on to the child projects or not.  
            string wrapperProjectToolsVersion = DetermineWrapperProjectToolsVersion(_toolsVersionOverride, out bool explicitToolsVersionSpecified);

            return CreateSolutionProject(wrapperProjectToolsVersion, explicitToolsVersionSpecified);
        }

        /// <summary>
        /// Given a parsed solution, generate a top level traversal project and the metaprojects representing the dependencies for each real project
        /// referenced in the solution.
        /// </summary>
        private ProjectInstance[] CreateSolutionProject(string wrapperProjectToolsVersion, bool explicitToolsVersionSpecified)
        {
            AddFakeReleaseSolutionConfigurationIfNecessary();

            if (_solutionFile.ContainsWebDeploymentProjects)
            {
                // If there are Web Deployment projects, we need to scan those project files
                // and specify the references explicitly.  
                // Other references are either ProjectReferences (taken care of by MSBuild) or 
                // explicit manual references in the solution file -- which get parsed out by 
                // the SolutionParser.
                string childProjectToolsVersion = DetermineChildProjectToolsVersion(wrapperProjectToolsVersion);
                string fullSolutionConfigurationName = PredictActiveSolutionConfigurationName();

                ScanProjectDependencies(childProjectToolsVersion, fullSolutionConfigurationName);
            }

            // Get a list of all actual projects in the solution
            var projectsInOrder = new List<ProjectInSolution>(_solutionFile.ProjectsInOrder.Count);
            foreach (ProjectInSolution project in _solutionFile.ProjectsInOrder)
            {
                if (SolutionFile.IsBuildableProject(project))
                {
                    projectsInOrder.Add(project);
                }
            }

            // Create the list of our generated projects.
            var projectInstances = new List<ProjectInstance>(projectsInOrder.Count + 1);

            // Create the project instance for the traversal project.
            ProjectInstance traversalInstance = CreateTraversalInstance(wrapperProjectToolsVersion, explicitToolsVersionSpecified, projectsInOrder);

            // Compute the solution configuration which will be used for this build.  We will use it later.
            _selectedSolutionConfiguration = String.Format(CultureInfo.InvariantCulture, "{0}|{1}", traversalInstance.GetProperty("Configuration").EvaluatedValue, traversalInstance.GetProperty("Platform").EvaluatedValue);
            projectInstances.Add(traversalInstance);

            // Now evaluate all of the projects in the solution and handle them appropriately.
            EvaluateAndAddProjects(projectsInOrder, projectInstances, traversalInstance, _selectedSolutionConfiguration);

            // Special environment variable to allow people to see the in-memory MSBuild project generated
            // to represent the SLN.
            foreach (ProjectInstance instance in projectInstances)
            {
                EmitMetaproject(instance.ToProjectRootElement(), instance.FullPath);
            }

            return projectInstances.ToArray();
        }

        /// <summary>
        /// Examine each project in the solution, add references and targets for it, and create metaprojects if necessary.
        /// </summary>
        private void EvaluateAndAddProjects(List<ProjectInSolution> projectsInOrder, List<ProjectInstance> projectInstances, ProjectInstance traversalInstance, string selectedSolutionConfiguration)
        {
            // Now add all of the per-project items, targets and metaprojects.
            foreach (ProjectInSolution project in projectsInOrder)
            {
                project.ProjectConfigurations.TryGetValue(selectedSolutionConfiguration, out ProjectConfigurationInSolution projectConfiguration);
                if (!WouldProjectBuild(_solutionFile, selectedSolutionConfiguration, project, projectConfiguration))
                {
                    // Project wouldn't build, so omit it from further processing.
                    continue;
                }

                bool canBuildDirectly = CanBuildDirectly(traversalInstance, project, projectConfiguration);

                // Add an entry to @(ProjectReference) for the project.  This will be either a reference directly to the project, or to the 
                // metaproject, as appropriate.
                AddProjectReference(traversalInstance, traversalInstance, project, projectConfiguration, canBuildDirectly);

                // Add the targets to the traversal project for each standard target.  These will either invoke the project directly or invoke the
                // metaproject, as appropriate
                AddTraversalTargetForProject(traversalInstance, project, projectConfiguration, null, "BuildOutput", canBuildDirectly);
                AddTraversalTargetForProject(traversalInstance, project, projectConfiguration, "Clean", null, canBuildDirectly);
                AddTraversalTargetForProject(traversalInstance, project, projectConfiguration, "Rebuild", "BuildOutput", canBuildDirectly);
                AddTraversalTargetForProject(traversalInstance, project, projectConfiguration, "Publish", null, canBuildDirectly);

                // Add any other targets specified by the user that were not already added. A target's presence or absence must be determined at the last
                // minute because whether traversalInstance.Targets.ContainsKey(i) is true or not can change during the enumeration.
                foreach (string targetName in _targetNames.Where(i => !traversalInstance.Targets.ContainsKey(i)))
                {
                    AddTraversalTargetForProject(traversalInstance, project, projectConfiguration, targetName, null, canBuildDirectly);
                }

                // If we cannot build the project directly, then we need to generate a metaproject for it.
                if (!canBuildDirectly)
                {
                    ProjectInstance metaproject = CreateMetaproject(traversalInstance, project, projectConfiguration);
                    projectInstances.Add(metaproject);
                }
            }

            // Add any other targets specified by the user that were not already added
            foreach (string targetName in _targetNames.Where(i => !traversalInstance.Targets.ContainsKey(i)))
            {
                AddTraversalReferencesTarget(traversalInstance, targetName, null);
            }
        }

        /// <summary>
        /// Adds the standard targets to the traversal project.
        /// </summary>
        private void AddStandardTraversalTargets(ProjectInstance traversalInstance, List<ProjectInSolution> projectsInOrder)
        {
            // Add the initial target with some solution configuration validation/information
            AddInitialTargets(traversalInstance, projectsInOrder);

            // Add the targets to traverse the metaprojects.
            AddTraversalReferencesTarget(traversalInstance, null, "CollectedBuildOutput");
            AddTraversalReferencesTarget(traversalInstance, "Clean", null);
            AddTraversalReferencesTarget(traversalInstance, "Rebuild", "CollectedBuildOutput");
            AddTraversalReferencesTarget(traversalInstance, "Publish", null);
        }

        /// <summary>
        /// Creates the traversal project instance.  This has all of the properties against which we can perform evaluations for the remainder of the process.
        /// </summary>
        private ProjectInstance CreateTraversalInstance(string wrapperProjectToolsVersion, bool explicitToolsVersionSpecified, List<ProjectInSolution> projectsInOrder)
        {
            // Create the traversal project's root element.  We will later instantiate this, and use it for evaluation of conditions on
            // the metaprojects.
            ProjectRootElement traversalProject = ProjectRootElement.Create();
            traversalProject.ToolsVersion = wrapperProjectToolsVersion;
            traversalProject.DefaultTargets = "Build";
            traversalProject.InitialTargets = "ValidateSolutionConfiguration;ValidateToolsVersions;ValidateProjects";
            traversalProject.FullPath = _solutionFile.FullPath + ".metaproj";

            // Add default solution configuration/platform names in case the user doesn't specify them on the command line
            AddConfigurationPlatformDefaults(traversalProject);

            // Add default Venus configuration names (for more details, see comments for this method)
            AddVenusConfigurationDefaults(traversalProject);

            // Add solution related macros
            AddGlobalProperties(traversalProject);

            // Add a property group for each solution configuration, each with one XML property containing the
            // project configurations in this solution configuration.
            foreach (SolutionConfigurationInSolution solutionConfiguration in _solutionFile.SolutionConfigurations)
            {
                AddPropertyGroupForSolutionConfiguration(traversalProject, solutionConfiguration);
            }

            // Add our global extensibility points to the project representing the solution:
            // Imported at the top:  $(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\SolutionFile\ImportBefore\* 
            // Imported at the bottom:  $(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\SolutionFile\ImportAfter\*             
            ProjectImportElement importBefore = traversalProject.CreateImportElement(@"$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\SolutionFile\ImportBefore\*");
            importBefore.Condition = @"'$(ImportByWildcardBeforeSolution)' != 'false' and exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\SolutionFile\ImportBefore')"; // Avoids wildcard perf problem

            ProjectImportElement importAfter = traversalProject.CreateImportElement(@"$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\SolutionFile\ImportAfter\*");
            importAfter.Condition = @"'$(ImportByWildcardBeforeSolution)' != 'false' and exists('$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\SolutionFile\ImportAfter')"; // Avoids wildcard perf problem

            /* The code below adds the following XML:

            - TOP -

                <PropertyGroup Condition="'$(ImportDirectorySolutionProps)' != 'false' and '$(DirectorySolutionPropsPath)' == ''">
                  <_DirectorySolutionPropsFile Condition="'$(_DirectorySolutionPropsFile)' == ''">Directory.Solution.props</_DirectorySolutionPropsFile>
                  <_DirectorySolutionPropsBasePath Condition="'$(_DirectorySolutionPropsBasePath)' == ''">$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildProjectDirectory), '$(_DirectorySolutionPropsFile)'))</_DirectorySolutionPropsBasePath>
                  <DirectorySolutionPropsPath Condition="'$(_DirectorySolutionPropsBasePath)' != '' and '$(_DirectorySolutionPropsFile)' != ''">$([System.IO.Path]::Combine('$(_DirectorySolutionPropsBasePath)', '$(_DirectorySolutionPropsFile)'))</DirectorySolutionPropsPath>
                </PropertyGroup>

                <Import Project="$(DirectorySolutionPropsPath)" Condition="'$(ImportDirectorySolutionProps)' != 'false' and exists('$(DirectorySolutionPropsPath)')"/>

            - BOTTOM -

                <PropertyGroup Condition="'$(ImportDirectorySolutionTargets)' != 'false' and '$(DirectorySolutionTargetsPath)' == ''">
                  <_DirectorySolutionTargetsFile Condition="'$(_DirectorySolutionTargetsFile)' == ''">Directory.Solution.targets</_DirectorySolutionTargetsFile>
                  <_DirectorySolutionTargetsBasePath Condition="'$(_DirectorySolutionTargetsBasePath)' == ''">$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildProjectDirectory), '$(_DirectorySolutionTargetsFile)'))</_DirectorySolutionTargetsBasePath>
                  <DirectorySolutionTargetsPath Condition="'$(_DirectorySolutionTargetsBasePath)' != '' and '$(_DirectorySolutionTargetsFile)' != ''">$([System.IO.Path]::Combine('$(_DirectorySolutionTargetsBasePath)', '$(_DirectorySolutionTargetsFile)'))</DirectorySolutionTargetsPath>
                </PropertyGroup>

                <Import Project="$(DirectorySolutionTargetsPath)" Condition="'$(ImportDirectorySolutionTargets)' != 'false' and exists('$(DirectorySolutionTargetsPath)')"/>
            */
            ProjectPropertyGroupElement directorySolutionPropsPropertyGroup = traversalProject.CreatePropertyGroupElement();
            directorySolutionPropsPropertyGroup.Condition = "'$(ImportDirectorySolutionProps)' != 'false' and '$(DirectorySolutionPropsPath)' == ''";

            ProjectPropertyElement directorySolutionPropsFileProperty = traversalProject.CreatePropertyElement("_DirectorySolutionPropsFile");
            directorySolutionPropsFileProperty.Value = "Directory.Solution.props";
            directorySolutionPropsFileProperty.Condition = "'$(_DirectorySolutionPropsFile)' == ''";

            ProjectPropertyElement directorySolutionPropsBasePathProperty = traversalProject.CreatePropertyElement("_DirectorySolutionPropsBasePath");
            directorySolutionPropsBasePathProperty.Value = "$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildProjectDirectory), '$(_DirectorySolutionPropsFile)'))";
            directorySolutionPropsBasePathProperty.Condition = "'$(_DirectorySolutionPropsBasePath)' == ''";

            ProjectPropertyElement directorySolutionPropsPathProperty = traversalProject.CreatePropertyElement("DirectorySolutionPropsPath");
            directorySolutionPropsPathProperty.Value = "$([System.IO.Path]::Combine('$(_DirectorySolutionPropsBasePath)', '$(_DirectorySolutionPropsFile)'))";
            directorySolutionPropsPathProperty.Condition = "'$(_DirectorySolutionPropsBasePath)' != '' and '$(_DirectorySolutionPropsFile)' != ''";

            ProjectImportElement directorySolutionPropsImport = traversalProject.CreateImportElement("$(DirectorySolutionPropsPath)");
            directorySolutionPropsImport.Condition = "'$(ImportDirectorySolutionProps)' != 'false' and exists('$(DirectorySolutionPropsPath)')";

            ProjectPropertyGroupElement directorySolutionTargetsPropertyGroup = traversalProject.CreatePropertyGroupElement();
            directorySolutionTargetsPropertyGroup.Condition = "'$(ImportDirectorySolutionTargets)' != 'false' and '$(DirectorySolutionTargetsPath)' == ''";

            ProjectPropertyElement directorySolutionTargetsFileProperty = traversalProject.CreatePropertyElement("_DirectorySolutionTargetsFile");
            directorySolutionTargetsFileProperty.Value = "Directory.Solution.targets";
            directorySolutionTargetsFileProperty.Condition = "'$(_DirectorySolutionTargetsFile)' == ''";

            ProjectPropertyElement directorySolutionTargetsBasePathProperty = traversalProject.CreatePropertyElement("_DirectorySolutionTargetsBasePath");
            directorySolutionTargetsBasePathProperty.Value = "$([MSBuild]::GetDirectoryNameOfFileAbove($(MSBuildProjectDirectory), '$(_DirectorySolutionTargetsFile)'))";
            directorySolutionTargetsBasePathProperty.Condition = "'$(_DirectorySolutionTargetsBasePath)' == ''";

            ProjectPropertyElement directorySolutionTargetsPathProperty = traversalProject.CreatePropertyElement("DirectorySolutionTargetsPath");
            directorySolutionTargetsPathProperty.Value = "$([System.IO.Path]::Combine('$(_DirectorySolutionTargetsBasePath)', '$(_DirectorySolutionTargetsFile)'))";
            directorySolutionTargetsPathProperty.Condition = "'$(_DirectorySolutionTargetsBasePath)' != '' and '$(_DirectorySolutionTargetsFile)' != ''";

            ProjectImportElement directorySolutionTargetsImport = traversalProject.CreateImportElement("$(DirectorySolutionTargetsPath)");
            directorySolutionTargetsImport.Condition = "'$(ImportDirectorySolutionTargets)' != 'false' and exists('$(DirectorySolutionTargetsPath)')";

            // Add our local extensibility points to the project representing the solution
            // Imported at the top: before.mysolution.sln.targets
            // Imported at the bottom: after.mysolution.sln.targets
            string escapedSolutionFile = EscapingUtilities.Escape(Path.GetFileName(_solutionFile.FullPath));
            string escapedSolutionDirectory = EscapingUtilities.Escape(_solutionFile.SolutionFileDirectory);
            string localFile = Path.Combine(escapedSolutionDirectory, "before." + escapedSolutionFile + ".targets");
            ProjectImportElement importBeforeLocal = traversalProject.CreateImportElement(localFile);
            importBeforeLocal.Condition = @"exists('" + localFile + "')";

            localFile = Path.Combine(escapedSolutionDirectory, "after." + escapedSolutionFile + ".targets");
            ProjectImportElement importAfterLocal = traversalProject.CreateImportElement(localFile);
            importAfterLocal.Condition = @"exists('" + localFile + "')";

            // Put locals second so they can override globals if they want
            traversalProject.PrependChild(importBeforeLocal);
            traversalProject.PrependChild(directorySolutionPropsImport);
            traversalProject.PrependChild(directorySolutionPropsPropertyGroup);
            traversalProject.PrependChild(importBefore);
            traversalProject.AppendChild(importAfter);
            traversalProject.AppendChild(directorySolutionTargetsPropertyGroup);
            traversalProject.AppendChild(directorySolutionTargetsImport);
            traversalProject.AppendChild(importAfterLocal);

            directorySolutionTargetsPropertyGroup.AppendChild(directorySolutionTargetsFileProperty);
            directorySolutionTargetsPropertyGroup.AppendChild(directorySolutionTargetsBasePathProperty);
            directorySolutionTargetsPropertyGroup.AppendChild(directorySolutionTargetsPathProperty);

            directorySolutionPropsPropertyGroup.AppendChild(directorySolutionPropsFileProperty);
            directorySolutionPropsPropertyGroup.AppendChild(directorySolutionPropsBasePathProperty);
            directorySolutionPropsPropertyGroup.AppendChild(directorySolutionPropsPathProperty);

            // These are just dummies necessary to make the evaluation into a project instance succeed when 
            // any custom imported targets have declarations like BeforeTargets="Build"
            // They'll be replaced momentarily with the real ones.
            string[] dummyTargetsForEvaluationTime = _defaultTargetNames.Union(_targetNames).ToArray();
            foreach (string targetName in dummyTargetsForEvaluationTime)
            {
                ProjectTargetElement target = traversalProject.CreateTargetElement(targetName);
                // Prepend so that any imported target overrides these default ones.
                traversalProject.PrependChild(target);
            }

            // For debugging purposes: some information is lost when evaluating into a project instance,
            // so make it possible to see what we have at this point.
            string path = traversalProject.FullPath;
            string metaprojectPath = _solutionFile.FullPath + ".metaproj.tmp";
            EmitMetaproject(traversalProject, metaprojectPath);
            traversalProject.FullPath = path;

            // Create the instance.  From this point forward we can evaluate conditions against the traversal project directly.
            var traversalInstance = new ProjectInstance
                (
                traversalProject,
                _globalProperties,
                explicitToolsVersionSpecified ? wrapperProjectToolsVersion : null,
                _solutionFile.VisualStudioVersion,
                new ProjectCollection(),
                _sdkResolverService,
                _submissionId
                );

            // Traversal meta project entire state has to be serialized as it was generated and hence
            // does not have disk representation to load project from.
            traversalInstance.TranslateEntireState = true;

            // Make way for the real ones
            foreach (string targetName in dummyTargetsForEvaluationTime)
            {
                // Remove targets only if they were the dummy ones (from the metaproj path),
                // but leave them if they're from another source (imported/overridden).
                if (traversalInstance.Targets[targetName].Location.File == traversalProject.FullPath)
                {
                    traversalInstance.RemoveTarget(targetName);
                }
            }

            AddStandardTraversalTargets(traversalInstance, projectsInOrder);

            return traversalInstance;
        }

        private void EmitMetaproject(ProjectRootElement metaproject, string path)
        {
            if (Traits.Instance.EmitSolutionMetaproj)
            {
                metaproject.Save(path);
            }
            if (_loggingService.IncludeEvaluationMetaprojects)
            {
                var xml = new StringBuilder();
                using (var writer = new StringWriter(xml))
                {
                    metaproject.Save(writer);
                }

                string message = ResourceUtilities.GetResourceString("MetaprojectGenerated");
                var eventArgs = new MetaprojectGeneratedEventArgs(xml.ToString(), path, message)
                {
                    BuildEventContext = _projectBuildEventContext,
                };
                _loggingService.LogBuildEvent(eventArgs);
            }
        }

        /// <summary>
        /// This method adds a new ProjectReference item to the specified instance.  The reference will either be to its metaproject (if the project
        /// is a web project or has reference of its own) or to the project itself (if it has no references and is a normal MSBuildable project.)
        /// </summary>
        private void AddProjectReference(ProjectInstance traversalProject, ProjectInstance projectInstance, ProjectInSolution projectToAdd, ProjectConfigurationInSolution projectConfiguration, bool direct)
        {
            ProjectItemInstance item;

            if (direct)
            {
                // We can build this project directly, so add its reference.
                item = projectInstance.AddItem("ProjectReference", EscapingUtilities.Escape(projectToAdd.AbsolutePath), null);
                item.SetMetadata("ToolsVersion", GetToolsVersionMetadataForDirectMSBuildTask(traversalProject));
                item.SetMetadata("SkipNonexistentProjects", "False"); // Skip if it doesn't exist on disk.
                item.SetMetadata("AdditionalProperties", GetPropertiesMetadataForProjectReference(traversalProject, GetConfigurationAndPlatformPropertiesString(projectConfiguration)));
            }
            else
            {
                // We cannot build directly, add the metaproject reference instead.
                item = projectInstance.AddItem("ProjectReference", GetMetaprojectName(projectToAdd), null);
                item.SetMetadata("ToolsVersion", traversalProject.ToolsVersion);
                item.SetMetadata("SkipNonexistentProjects", "Build"); // Instruct the MSBuild task to try to build even though the file doesn't exist on disk.
                item.SetMetadata("AdditionalProperties", GetPropertiesMetadataForProjectReference(traversalProject, SolutionConfigurationAndPlatformProperties));
            }

            // Set raw config and platform for custom build steps to use if they wish
            // Configuration is null for web projects
            if (projectConfiguration != null)
            {
                item.SetMetadata("Configuration", projectConfiguration.ConfigurationName);
                item.SetMetadata("Platform", projectConfiguration.PlatformName);
            }
        }

        /// <summary>
        /// The value to be passed to the ToolsVersion attribute of the MSBuild task used to directly build a project.
        /// </summary>
        private static string GetToolsVersionMetadataForDirectMSBuildTask(ProjectInstance traversalProject)
        {
            string directProjectToolsVersion = traversalProject.GetPropertyValue("ProjectToolsVersion");
            return directProjectToolsVersion;
        }

        /// <summary>
        /// The value to be passed to the ToolsVersion attribute of the MSBuild task used to directly build a project.
        /// </summary>
        private static string GetToolsVersionAttributeForDirectMSBuildTask()
        {
            return "$(ProjectToolsVersion)";
        }

        /// <summary>
        /// The value to be assigned to the metadata for a particular project reference.  Contains only configuration and platform specified in the project configuration, evaluated.
        /// </summary>
        private static string GetPropertiesMetadataForProjectReference(ProjectInstance traversalProject, string configurationAndPlatformProperties)
        {
            string directProjectProperties = traversalProject.ExpandString(configurationAndPlatformProperties);

            if (traversalProject.SubToolsetVersion != null)
            {
                // Note: it is enough below to compare traversalProject.SubToolsetVersion with 4.0 as a means to verify if 
                // traversalProject.SubToolsetVersion < 12.0 since this path isn't followed for traversalProject.SubToolsetVersion values of 2.0 and 3.5
                if (traversalProject.SubToolsetVersion.Equals("4.0", StringComparison.OrdinalIgnoreCase))
                {
                    directProjectProperties = String.Format(CultureInfo.InvariantCulture, "{0}; {1}={2}", directProjectProperties, Constants.SubToolsetVersionPropertyName, traversalProject.SubToolsetVersion);
                }
            }

            return directProjectProperties;
        }

        /// <summary>
        /// Gets the project configuration and platform values as an attribute string for an MSBuild task used to build the project.
        /// </summary>
        private static string GetConfigurationAndPlatformPropertiesString(ProjectConfigurationInSolution projectConfiguration)
        {
            string directProjectProperties = String.Format(CultureInfo.InvariantCulture, "Configuration={0}; Platform={1}", projectConfiguration.ConfigurationName, projectConfiguration.PlatformName);
            return directProjectProperties;
        }

        /// <summary>
        /// The value to be passed to the Properties attribute of the MSBuild task to build a specific project.  Contains reference to project configuration and
        /// platform as well as the solution configuration bits.
        /// </summary>
        private static string GetPropertiesAttributeForDirectMSBuildTask(ProjectConfigurationInSolution projectConfiguration)
        {
            string directProjectProperties = Strings.WeakIntern(String.Join(";", GetConfigurationAndPlatformPropertiesString(projectConfiguration), SolutionProperties));
            return directProjectProperties;
        }

        /// <summary>
        /// Returns true if the specified project can be built directly, without using a metaproject.
        /// </summary>
        private bool CanBuildDirectly(ProjectInstance traversalProject, ProjectInSolution projectToAdd, ProjectConfigurationInSolution projectConfiguration)
        {
            // Can we build this project directly, without a metaproject?  We can if it's MSBuild-able and has no references building in this configuration.
            bool canBuildDirectly = false;
            if ((projectToAdd.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat) ||
                (projectToAdd.CanBeMSBuildProjectFile(out _)))
            {
                canBuildDirectly = true;
                foreach (string dependencyProjectGuid in projectToAdd.Dependencies)
                {
                    if (!_solutionFile.ProjectsByGuid.TryGetValue(dependencyProjectGuid, out ProjectInSolution dependencyProject))
                    {
                        ProjectFileErrorUtilities.VerifyThrowInvalidProjectFile
                            (
                            false,
                            "SubCategoryForSolutionParsingErrors",
                            new BuildEventFileInfo(traversalProject.FullPath),
                            "SolutionParseProjectDepNotFoundError",
                            projectToAdd.ProjectGuid,
                            dependencyProjectGuid
                            );
                    }

                    if (WouldProjectBuild(_solutionFile, _selectedSolutionConfiguration, dependencyProject, projectConfiguration))
                    {
                        // This is a reference we would have to build, so we can't build the project directly.
                        canBuildDirectly = false;
                        break;
                    }
                }
            }

            return canBuildDirectly;
        }

        /// <summary>
        /// Produces a set of targets which allows the MSBuild scheduler to schedule projects in the order automatically by
        /// following their dependencies without enforcing build levels.
        /// </summary>
        /// <remarks>
        /// We want MSBuild to be able to parallelize the builds of these projects where possible and still honor references.
        /// Since the project files referenced by the solution do not (necessarily) themselves contain actual project references
        /// to the projects they depend on, we need to synthesize this relationship ourselves.  This is done by creating a target
        /// which first invokes the project's dependencies, then invokes the actual project itself.  However, invoking the
        /// dependencies must also invoke their dependencies and so on down the line.
        ///
        /// Additionally, we do not wish to create a separate MSBuild project to contain this target yet we want to parallelize
        /// calls to these targets.  The way to do this is to pass in different global properties to the same project in the same
        /// MSBuild call.  MSBuild easily allows this using the AdditionalProperties metadata which can be specified on an Item.
        ///
        /// Assuming the solution project we are generating is called "foo.proj", we can accomplish this parallelism as follows:
        /// <ItemGroup>
        ///     <ProjectReference Include="Project0"/>
        ///     <ProjectReference Include="Project1"/>
        ///     <ProjectReference Include="Project2"/>
        /// </ItemGroup>
        ///
        /// We now have expressed the top level reference to all projects as @(SolutionReference) and each project's
        /// set of references as @(PROJECTNAMEReference).  We construct our target as:
        ///
        /// <Target Name="Build">
        ///     <MSBuild Projects="@(ProjectReference)" Targets="Build" />
        ///     <MSBuild Projects="actualProjectName" Targets="Build" />
        /// </Target>
        ///
        /// The first MSBuild call re-invokes the solution project instructing it to build the reference projects for the
        /// current project.  The second MSBuild call invokes the actual project itself.  Because all reference projects have
        /// the same additional properties, MSBuild will only build the first one it comes across and the rest will be
        /// satisfied from the cache.
        /// </remarks>
        private ProjectInstance CreateMetaproject(ProjectInstance traversalProject, ProjectInSolution project, ProjectConfigurationInSolution projectConfiguration)
        {
            // Create a new project instance with global properties and tools version from the existing project
            ProjectInstance metaprojectInstance = new ProjectInstance(EscapingUtilities.UnescapeAll(GetMetaprojectName(project)), traversalProject, GetMetaprojectGlobalProperties(traversalProject));

            // Traversal meta project entire state has to be serialized as it was generated and hence
            // does not have disk representation to load project from.
            metaprojectInstance.TranslateEntireState = true;

            // Add the project references which must build before this one.
            AddMetaprojectReferenceItems(traversalProject, metaprojectInstance, project);

            if (project.ProjectType == SolutionProjectType.WebProject)
            {
#if !FEATURE_ASPNET_COMPILER
                ProjectFileErrorUtilities.VerifyThrowInvalidProjectFile
                    (
                    false,
                    "SubCategoryForSolutionParsingErrors",
                    new BuildEventFileInfo(_solutionFile.FullPath),
                    "AspNetCompiler.UnsupportedMSBuildVersion",
                    project.ProjectName
                    );
#else
                AddMetaprojectTargetForWebProject(traversalProject, metaprojectInstance, project, null);
                AddMetaprojectTargetForWebProject(traversalProject, metaprojectInstance, project, "Clean");
                AddMetaprojectTargetForWebProject(traversalProject, metaprojectInstance, project, "Rebuild");
                AddMetaprojectTargetForWebProject(traversalProject, metaprojectInstance, project, "Publish");

                foreach (string targetName in _targetNames.Where(i => !metaprojectInstance.Targets.ContainsKey(i)))
                {
                    AddMetaprojectTargetForWebProject(traversalProject, metaprojectInstance, project, targetName);
                }
#endif
            }
            else if ((project.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat) ||
                     (project.CanBeMSBuildProjectFile(out string unknownProjectTypeErrorMessage)))
            {
                // unknownProjectTypeErrorMessage holds the error message generated when we try to determine if a project is an MSBuild format
                // project but it is not.

                string safeItemNameFromProjectName = MakeIntoSafeItemName(project.ProjectName);
                string targetOutputItemName = string.Format(CultureInfo.InvariantCulture, "{0}BuildOutput", safeItemNameFromProjectName);

                AddMetaprojectTargetForManagedProject(traversalProject, metaprojectInstance, project, projectConfiguration, "Clean", null);
                AddMetaprojectTargetForManagedProject(traversalProject, metaprojectInstance, project, projectConfiguration, null, targetOutputItemName);
                AddMetaprojectTargetForManagedProject(traversalProject, metaprojectInstance, project, projectConfiguration, "Rebuild", targetOutputItemName);
                AddMetaprojectTargetForManagedProject(traversalProject, metaprojectInstance, project, projectConfiguration, "Publish", null);

                foreach (string targetName in _targetNames.Where(i => !metaprojectInstance.Targets.ContainsKey(i)))
                {
                    AddMetaprojectTargetForManagedProject(traversalProject, metaprojectInstance, project, projectConfiguration, targetName, null);
                }
            }
            else
            {
                AddMetaprojectTargetForUnknownProjectType(traversalProject, metaprojectInstance, project, null, unknownProjectTypeErrorMessage);
                AddMetaprojectTargetForUnknownProjectType(traversalProject, metaprojectInstance, project, "Clean", unknownProjectTypeErrorMessage);
                AddMetaprojectTargetForUnknownProjectType(traversalProject, metaprojectInstance, project, "Rebuild", unknownProjectTypeErrorMessage);
                AddMetaprojectTargetForUnknownProjectType(traversalProject, metaprojectInstance, project, "Publish", unknownProjectTypeErrorMessage);

                foreach (string targetName in _targetNames.Where(i => !metaprojectInstance.Targets.ContainsKey(i)))
                {
                    AddMetaprojectTargetForUnknownProjectType(traversalProject, metaprojectInstance, project, targetName, unknownProjectTypeErrorMessage);
                }
            }

            return metaprojectInstance;
        }

        /// <summary>
        /// Returns the metaproject name for a given project.
        /// </summary>
        private string GetMetaprojectName(ProjectInSolution project)
        {
            string baseName;
            if (project.ProjectType == SolutionProjectType.WebProject)
            {
                baseName = Path.Combine(_solutionFile.SolutionFileDirectory, MakeIntoSafeItemName(project.GetUniqueProjectName()));
            }
            else
            {
                baseName = project.AbsolutePath;
            }

            if (String.IsNullOrEmpty(baseName))
            {
                baseName = project.ProjectName;
            }

            baseName = FileUtilities.EnsureNoTrailingSlash(baseName);

            return GetMetaprojectName(baseName);
        }

        /// <summary>
        /// Adds a set of items which describe the references for this project.
        /// </summary>
        private void AddMetaprojectReferenceItems(ProjectInstance traversalProject, ProjectInstance metaprojectInstance, ProjectInSolution project)
        {
            foreach (string dependencyProjectGuid in project.Dependencies)
            {
                if (!_solutionFile.ProjectsByGuid.TryGetValue(dependencyProjectGuid, out ProjectInSolution dependencyProject))
                {
                    ProjectFileErrorUtilities.VerifyThrowInvalidProjectFile
                        (
                        false,
                        "SubCategoryForSolutionParsingErrors",
                        new BuildEventFileInfo(traversalProject.FullPath),
                        "SolutionParseProjectDepNotFoundError",
                        project.ProjectGuid,
                        dependencyProjectGuid
                        );
                }
                else
                {
                    if (dependencyProject.ProjectConfigurations.TryGetValue(_selectedSolutionConfiguration, out ProjectConfigurationInSolution dependencyProjectConfiguration) &&
                        WouldProjectBuild(_solutionFile, _selectedSolutionConfiguration, dependencyProject, dependencyProjectConfiguration))
                    {
                        bool canBuildDirectly = CanBuildDirectly(traversalProject, dependencyProject, dependencyProjectConfiguration);
                        AddProjectReference(traversalProject, metaprojectInstance, dependencyProject, dependencyProjectConfiguration, canBuildDirectly);
                    }
                }
            }
        }

        /// <summary>
        /// Adds the targets which build the dependencies and actual project for a metaproject.
        /// </summary>
        private static void AddMetaprojectTargetForManagedProject(ProjectInstance traversalProject, ProjectInstance metaprojectInstance, ProjectInSolution project, ProjectConfigurationInSolution projectConfiguration, string targetName, string outputItem)
        {
            string outputItemAsItem = null;
            if (!String.IsNullOrEmpty(outputItem))
            {
                outputItemAsItem = "@(" + outputItem + ")";
            }

            ProjectTargetInstance target = metaprojectInstance.AddTarget(targetName ?? "Build", String.Empty, String.Empty, outputItemAsItem, null, String.Empty, String.Empty, String.Empty, String.Empty, false /* legacy target returns behaviour */);

            AddReferencesBuildTask(target, targetName, null /* No need to capture output */);

            // Add the task to build the actual project.
            AddProjectBuildTask(traversalProject, projectConfiguration, target, targetName, EscapingUtilities.Escape(project.AbsolutePath), String.Empty, outputItem);
        }

        /// <summary>
        /// Adds an MSBuild task to a real project.
        /// </summary>
        private static void AddProjectBuildTask(ProjectInstance traversalProject, ProjectConfigurationInSolution projectConfiguration, ProjectTargetInstance target, string targetToBuild, string sourceItems, string condition, string outputItem)
        {
            ProjectTaskInstance task = target.AddTask("MSBuild", condition, String.Empty);
            task.SetParameter("Projects", sourceItems);
            if (targetToBuild != null)
            {
                task.SetParameter("Targets", targetToBuild);
            }

            task.SetParameter("BuildInParallel", "True");

            task.SetParameter("ToolsVersion", GetToolsVersionAttributeForDirectMSBuildTask());
            task.SetParameter("Properties", GetPropertiesAttributeForDirectMSBuildTask(projectConfiguration));

            if (outputItem != null)
            {
                task.AddOutputItem("TargetOutputs", outputItem, String.Empty);
            }
        }

        /// <summary>
        /// Adds an MSBuild task to a single metaproject.  This is used in the traversal project.
        /// </summary>
        private void AddMetaprojectBuildTask(ProjectInSolution project, ProjectTargetInstance target, string targetToBuild, string outputItem)
        {
            ProjectTaskInstance task = target.AddTask("MSBuild", Strings.WeakIntern("'%(ProjectReference.Identity)' == '" + GetMetaprojectName(project) + "'"), String.Empty);
            task.SetParameter("Projects", "@(ProjectReference)");

            if (targetToBuild != null)
            {
                task.SetParameter("Targets", targetToBuild);
            }

            task.SetParameter("BuildInParallel", "True");
            task.SetParameter("ToolsVersion", "Current");
            task.SetParameter("Properties", SolutionProperties);
            task.SetParameter("SkipNonexistentProjects", "%(ProjectReference.SkipNonexistentProjects)");

            if (outputItem != null)
            {
                task.AddOutputItem("TargetOutputs", outputItem, String.Empty);
            }
        }

#if FEATURE_ASPNET_COMPILER
        /// <summary>
        /// Add a target for a Venus project into the XML doc that's being generated.  This
        /// target will call the AspNetCompiler task.
        /// </summary>
        private void AddMetaprojectTargetForWebProject(ProjectInstance traversalProject, ProjectInstance metaprojectInstance, ProjectInSolution project, string targetName)
        {
            // Add a supporting target called "GetFrameworkPathAndRedistList".
            AddTargetForGetFrameworkPathAndRedistList(metaprojectInstance);

            ProjectTargetInstance newTarget = metaprojectInstance.AddTarget(targetName ?? "Build", ComputeTargetConditionForWebProject(project), null, null, null, null, "GetFrameworkPathAndRedistList", null, null, false /* legacy target returns behaviour */);

            // Build the references
            AddReferencesBuildTask(newTarget, targetName, null /* No need to capture output */);

            if (targetName == "Clean")
            {
                // Well, hmmm.  The AspNetCompiler task doesn't support any kind of 
                // a "Clean" operation.  The best we can really do is offer up a 
                // message saying so.
                AddErrorWarningMessageInstance(newTarget, null, XMakeElements.message, true, "SolutionVenusProjectNoClean");
            }
            else if (targetName == "Publish")
            {
                // Well, hmmm.  The AspNetCompiler task doesn't support any kind of 
                // a "Publish" operation.  The best we can really do is offer up a 
                // message saying so.
                AddErrorWarningMessageInstance(newTarget, null, XMakeElements.message, true, "SolutionVenusProjectNoPublish");
            }
            else
            {
                // For normal build and "Rebuild", just call the AspNetCompiler task with the
                // correct parameters.  But before calling the AspNetCompiler task, we need to
                // do a bunch of prep work regarding references.

                // We're going to build up an MSBuild condition string that represents the valid Configurations.
                // We do this by OR'ing together individual conditions, each of which compares $(Configuration)
                // with a valid configuration name.  We init our condition string to "false", so we can easily 
                // OR together more stuff as we go, and also easily take the negation of the condition by putting
                // a ! around the whole thing.
                var conditionDescribingValidConfigurations = new StringBuilder("(false)");

                // Loop through all the valid configurations and add a PropertyGroup for each one.
                foreach (DictionaryEntry aspNetConfiguration in project.AspNetConfigurations)
                {
                    string configurationName = (string)aspNetConfiguration.Key;
                    var aspNetCompilerParameters = (AspNetCompilerParameters)aspNetConfiguration.Value;

                    // We only add the PropertyGroup once per Venus project.  Without the following "if", we would add
                    // the same identical PropertyGroup twice, once when AddTargetForWebProject is called with 
                    // subTargetName=null and once when subTargetName="Rebuild".
                    if (targetName == null)
                    {
                        AddPropertyGroupForAspNetConfiguration(traversalProject, metaprojectInstance, project, configurationName, aspNetCompilerParameters, _solutionFile.FullPath);
                    }

                    // Update our big condition string to include this configuration.
                    conditionDescribingValidConfigurations.Append(" or ");
                    conditionDescribingValidConfigurations.AppendFormat(CultureInfo.InvariantCulture, "('$(AspNetConfiguration)' == '{0}')", EscapingUtilities.Escape(configurationName));
                }

                StringBuilder referenceItemName = new StringBuilder(GenerateSafePropertyName(project, "References"));
                if (!string.IsNullOrEmpty(targetName))
                {
                    referenceItemName.Append('_');
                    referenceItemName.Append(targetName);
                }

                // Add tasks to resolve project references of this web project, if any
                if (project.ProjectReferences.Count > 0)
                {
                    // This is a bit tricky. Even though web projects don't use solution configurations,
                    // we want to use the current solution configuration to build the proper configurations
                    // of referenced projects.
                    foreach (SolutionConfigurationInSolution solutionConfiguration in _solutionFile.SolutionConfigurations)
                    {
                        AddResolveProjectReferenceTasks
                            (
                            traversalProject,
                            newTarget,
                            project,
                            solutionConfiguration,
                            referenceItemName.ToString(),
                            out _
                            );
                    }
                }

                // Add tasks to capture the auto-refreshed file references (those .REFRESH files).
                AddTasksToResolveAutoRefreshFileReferences(newTarget, project, referenceItemName.ToString());

                // Add a call to RAR (ResolveAssemblyReference) and the Copy task to put the referenced 
                // project outputs in the right place
                AddTasksToCopyAllDependenciesIntoBinDir(newTarget, project, referenceItemName.ToString(), conditionDescribingValidConfigurations.ToString());

                // Add a call to the AspNetCompiler task, conditioned on having a valid Configuration.
                AddTaskForAspNetCompiler(newTarget, project, conditionDescribingValidConfigurations.ToString());

                // Add a call to the <Message> task, conditioned on having an *invalid* Configuration.  The
                // message says that we're skipping the Venus project because it's either not enabled
                // for precompilation, or doesn't support the given configuration.
                AddErrorWarningMessageInstance
                    (
                    newTarget,
                    "!(" + conditionDescribingValidConfigurations + ")",
                    XMakeElements.message,
                    false,
                    "SolutionVenusProjectSkipped"
                    );
            }
        }

        /// <summary>
        /// Helper method to add a call to the AspNetCompiler task into the given target.
        /// </summary>
        private void AddTaskForAspNetCompiler
            (
            ProjectTargetInstance target,
            ProjectInSolution project,
            string conditionDescribingValidConfigurations
            )
        {
            // Add a call to the AspNetCompiler task, conditioned on having a valid Configuration.
            ProjectTaskInstance newTask = target.AddTask("AspNetCompiler", conditionDescribingValidConfigurations, null);
            newTask.SetParameter("VirtualPath", "$(" + GenerateSafePropertyName(project, "AspNetVirtualPath") + ")");
            newTask.SetParameter("PhysicalPath", "$(" + GenerateSafePropertyName(project, "AspNetPhysicalPath") + ")");
            newTask.SetParameter("TargetPath", "$(" + GenerateSafePropertyName(project, "AspNetTargetPath") + ")");
            newTask.SetParameter("Force", "$(" + GenerateSafePropertyName(project, "AspNetForce") + ")");
            newTask.SetParameter("Updateable", "$(" + GenerateSafePropertyName(project, "AspNetUpdateable") + ")");
            newTask.SetParameter("Debug", "$(" + GenerateSafePropertyName(project, "AspNetDebug") + ")");
            newTask.SetParameter("KeyFile", "$(" + GenerateSafePropertyName(project, "AspNetKeyFile") + ")");
            newTask.SetParameter("KeyContainer", "$(" + GenerateSafePropertyName(project, "AspNetKeyContainer") + ")");
            newTask.SetParameter("DelaySign", "$(" + GenerateSafePropertyName(project, "AspNetDelaySign") + ")");
            newTask.SetParameter("AllowPartiallyTrustedCallers", "$(" + GenerateSafePropertyName(project, "AspNetAPTCA") + ")");
            newTask.SetParameter("FixedNames", "$(" + GenerateSafePropertyName(project, "AspNetFixedNames") + ")");

            ValidateTargetFrameworkForWebProject(project);

            try
            {
                SetToolPathForAspNetCompilerTask(project, newTask);
            }
            catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
            {
                ProjectFileErrorUtilities.ThrowInvalidProjectFile
                    (
                    new BuildEventFileInfo(_solutionFile.FullPath),
                    e,
                    "AspNetCompiler.InvalidTargetFrameworkMonikerFromException",
                    project.ProjectName,
                    project.TargetFrameworkMoniker,
                    e.Message
                    );
            }
        }

        private void ValidateTargetFrameworkForWebProject(ProjectInSolution project)
        {
            var targetFramework = new FrameworkName(project.TargetFrameworkMoniker);
            bool isDotNetFramework = String.Equals(targetFramework.Identifier, ".NETFramework", StringComparison.OrdinalIgnoreCase);

            if (targetFramework.Version > _version40)
            {
                _loggingService.LogComment
                    (
                    _projectBuildEventContext,
                    MessageImportance.Low,
                    "AspNetCompiler.TargetingHigherFrameworksDefaultsTo40",
                    project.ProjectName,
                    targetFramework.Version.ToString()
                    );
            }
            if (!isDotNetFramework)
            {
                ProjectFileErrorUtilities.VerifyThrowInvalidProjectFile
                    (
                    false,
                    "SubCategoryForSolutionParsingErrors",
                    new BuildEventFileInfo(_solutionFile.FullPath),
                    "AspNetCompiler.InvalidTargetFrameworkMonikerNotDotNET",
                    project.ProjectName,
                    project.TargetFrameworkMoniker
                    );
            }
        }

        // As of .NET Framework 4.0, there are only two versions of aspnet_compiler.exe: 2.0 and 4.0.  If 
        // the TargetFrameworkVersion is less than 4.0, use the 2.0 version.  Otherwise, just use the 4.0
        // version of the executable, so that if say FV 4.1 is passed in, we don't throw an error.
        private void SetToolPathForAspNetCompilerTask(ProjectInSolution project, ProjectTaskInstance task)
        {
            // generate the target .NET Framework version based on the passed in TargetFrameworkMoniker.
            var targetFramework = new FrameworkName(project.TargetFrameworkMoniker);
            bool shouldDefaultToVersion40 = targetFramework.Version.Major >= 4;
            Version aspnetCompilerVersion = shouldDefaultToVersion40 ? _version40 : _version20;
            string aspnetCompilerPath = FrameworkLocationHelper.GetPathToDotNetFramework(aspnetCompilerVersion);

            ProjectFileErrorUtilities.VerifyThrowInvalidProjectFile
                (
                aspnetCompilerPath != null,
                "SubCategoryForSolutionParsingErrors",
                new BuildEventFileInfo(_solutionFile.FullPath),
                "AspNetCompiler.20NotInstalled"
                );

            task.SetParameter("ToolPath", aspnetCompilerPath);
        }

        /// <summary>
        /// Adds MSBuild tasks to a project target to pre-resolve its project references
        /// </summary>
        private void AddResolveProjectReferenceTasks
        (
            ProjectInstance traversalProject,
            ProjectTargetInstance target,
            ProjectInSolution project,
            SolutionConfigurationInSolution solutionConfiguration,
            string outputReferenceItemName,
            out string addedReferenceGuids
        )
        {
            var referenceGuids = new StringBuilder();

            // Suffix for the reference item name. Since we need to attach additional (different) metadata to every
            // reference item, we need to have helper item lists each with only one item
            int outputReferenceItemNameSuffix = 0;

            // Pre-resolve the MSBuild project references
            foreach (string projectReferenceGuid in project.ProjectReferences)
            {
                ProjectInSolution referencedProject = _solutionFile.ProjectsByGuid[projectReferenceGuid];

                if ((referencedProject != null) &&
                    (referencedProject.ProjectConfigurations.TryGetValue(solutionConfiguration.FullName, out ProjectConfigurationInSolution referencedProjectConfiguration)) &&
                    (referencedProjectConfiguration != null))
                {
                    string outputReferenceItemNameWithSuffix = string.Format(CultureInfo.InvariantCulture, "{0}_{1}", outputReferenceItemName, outputReferenceItemNameSuffix);

                    if ((referencedProject.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat) ||
                        ((referencedProject.ProjectType == SolutionProjectType.Unknown) && (referencedProject.CanBeMSBuildProjectFile(out _))))
                    {
                        string condition = GetConditionStringForConfiguration(solutionConfiguration);
                        if (traversalProject.EvaluateCondition(condition))
                        {
                            bool specifyProjectToolsVersion =
                                !String.Equals(traversalProject.ToolsVersion, "2.0", StringComparison.OrdinalIgnoreCase);

                            ProjectTaskInstance msbuildTask = AddMSBuildTaskInstance
                                (
                                target,
                                referencedProject.RelativePath,
                                "GetTargetPath",
                                referencedProjectConfiguration.ConfigurationName,
                                referencedProjectConfiguration.PlatformName,
                                specifyProjectToolsVersion
                                );
                            msbuildTask.AddOutputItem("TargetOutputs", outputReferenceItemNameWithSuffix, null);
                        }

                        if (referenceGuids.Length > 0)
                        {
                            referenceGuids.Append(';');
                        }

                        referenceGuids.Append(projectReferenceGuid);

                        // This merges the one-item item list into the main list, adding the appropriate guid metadata
                        ProjectTaskInstance createItemTask = target.AddTask("CreateItem", null, null);
                        createItemTask.SetParameter("Include", "@(" + outputReferenceItemNameWithSuffix + ")");
                        createItemTask.SetParameter("AdditionalMetadata", "Guid=" + projectReferenceGuid);
                        createItemTask.AddOutputItem("Include", outputReferenceItemName, null);
                    }

                    outputReferenceItemNameSuffix++;
                }
            }

            addedReferenceGuids = referenceGuids.ToString();
        }

        /// <summary>
        /// Add a PropertyGroup to the project for a particular Asp.Net configuration.  This PropertyGroup
        /// will have the correct values for all the Asp.Net properties for this project and this configuration.
        /// </summary>
        private static void AddPropertyGroupForAspNetConfiguration
            (
            ProjectInstance traversalProject,
            ProjectInstance metaprojectInstance,
            ProjectInSolution project,
            string configurationName,
            AspNetCompilerParameters aspNetCompilerParameters,
            string solutionFile
            )
        {
            // If the configuration doesn't match, don't add the properties.
            if (!traversalProject.EvaluateCondition(String.Format(CultureInfo.InvariantCulture, " '$(AspNetConfiguration)' == '{0}' ", EscapingUtilities.Escape(configurationName))))
            {
                return;
            }

            // Add properties into the property group for each of the AspNetCompiler properties.
            // REVIEW: SetProperty takes an evaluated value.  Are we doing the right thing here?
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetVirtualPath"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetVirtualPath));
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetPhysicalPath"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetPhysicalPath));
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetTargetPath"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetTargetPath));
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetForce"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetForce));
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetUpdateable"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetUpdateable));
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetDebug"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetDebug));
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetKeyFile"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetKeyFile));
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetKeyContainer"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetKeyContainer));
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetDelaySign"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetDelaySign));
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetAPTCA"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetAPTCA));
            metaprojectInstance.SetProperty(GenerateSafePropertyName(project, "AspNetFixedNames"), EscapingUtilities.Escape(aspNetCompilerParameters.aspNetFixedNames));

            string aspNetPhysicalPath = aspNetCompilerParameters.aspNetPhysicalPath;
            if (!String.IsNullOrEmpty(aspNetPhysicalPath))
            {
                // Trim the trailing slash if one exists.
                if (
                        (aspNetPhysicalPath[aspNetPhysicalPath.Length - 1] == Path.AltDirectorySeparatorChar) ||
                        (aspNetPhysicalPath[aspNetPhysicalPath.Length - 1] == Path.DirectorySeparatorChar)
                    )
                {
                    aspNetPhysicalPath = aspNetPhysicalPath.Substring(0, aspNetPhysicalPath.Length - 1);
                }

                // This gets us the last folder in the physical path.
                string lastFolderInPhysicalPath = null;

                try
                {
                    lastFolderInPhysicalPath = Path.GetFileName(aspNetPhysicalPath);
                }
                catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
                {
                    ProjectFileErrorUtilities.VerifyThrowInvalidProjectFile
                        (
                        false,
                        "SubCategoryForSolutionParsingErrors",
                        new BuildEventFileInfo(solutionFile),
                        e,
                        "SolutionParseInvalidProjectFileName",
                        project.RelativePath,
                        e.Message
                        );
                }

                if (!String.IsNullOrEmpty(lastFolderInPhysicalPath))
                {
                    // If there is a global property called "OutDir" set, that means the caller is trying to 
                    // override the AspNetTargetPath.  What we want to do in this case is concatenate:
                    // $(OutDir) + "\_PublishedWebsites" + (the last portion of the folder in the AspNetPhysicalPath).
                    if (traversalProject.EvaluateCondition(" '$(OutDir)' != '' "))
                    {
                        string outDirValue = String.Empty;
                        ProjectPropertyInstance outdir = metaprojectInstance.GetProperty("OutDir");

                        if (outdir != null)
                        {
                            outDirValue = ProjectInstance.GetPropertyValueEscaped(outdir);
                        }

                        // Make sure the path we are appending to has no leading slash to prevent double slashes.
                        string publishWebsitePath = EscapingUtilities.Escape(WebProjectOverrideFolder) + Path.DirectorySeparatorChar + EscapingUtilities.Escape(lastFolderInPhysicalPath) + Path.DirectorySeparatorChar;

                        metaprojectInstance.SetProperty
                            (
                            GenerateSafePropertyName(project, "AspNetTargetPath"),
                            outDirValue + publishWebsitePath
                            );
                    }
                }
            }
        }

        /// <summary>
        /// When adding a target to build a web project, we want to put a Condition on the Target node that
        /// effectively says "Only build this target if the web project is active (marked for building) in the
        /// current solution configuration.
        /// </summary>
        private string ComputeTargetConditionForWebProject(ProjectInSolution project)
        {
            var condition = new StringBuilder(" ('$(CurrentSolutionConfigurationContents)' != '') and (false");

            // Loop through all the solution configurations.
            foreach (SolutionConfigurationInSolution solutionConfiguration in _solutionFile.SolutionConfigurations)
            {
                // Find out if the web project has a project configuration for this solution configuration.
                // (Actually, web projects only have one project configuration, so the TryGetValue should
                // pretty much always return "true".
                if (project.ProjectConfigurations.TryGetValue(solutionConfiguration.FullName, out ProjectConfigurationInSolution projectConfiguration))
                {
                    // See if the web project is marked as active for this solution configuration.  If so,
                    // we'll build the target.  Otherwise not.
                    if (projectConfiguration.IncludeInBuild)
                    {
                        condition.Append(" or (");
                        condition.Append(GetConditionStringForConfiguration(solutionConfiguration));
                        condition.Append(")");
                    }
                }
                else if (String.Equals(solutionConfiguration.ConfigurationName, "Release", StringComparison.OrdinalIgnoreCase) ||
                         String.Equals(solutionConfiguration.ConfigurationName, "Debug", StringComparison.OrdinalIgnoreCase))
                {
                    // we don't have a project configuration that matches the solution configuration but
                    // the solution configuration is called "Release" or "Debug" which are standard AspNetConfigurations
                    // so these should be available in the solution project
                    condition.Append(" or (");
                    condition.Append(GetConditionStringForConfiguration(solutionConfiguration));
                    condition.Append(")");
                }
            }

            condition.Append(") ");
            return condition.ToString();
        }

        /// <summary>
        /// Add a target to the project called "GetFrameworkPathAndRedistList".  This target calls the
        /// GetFrameworkPath task and then CreateItem to populate @(_CombinedTargetFrameworkDirectoriesItem) and
        /// @(InstalledAssemblyTables), so that we can pass these into the ResolveAssemblyReference task
        /// when building web projects.
        /// </summary>
        private static void AddTargetForGetFrameworkPathAndRedistList(ProjectInstance metaprojectInstance)
        {
            if (metaprojectInstance.Targets.ContainsKey("GetFrameworkPathAndRedistList"))
            {
                return;
            }

            ProjectTargetInstance frameworkPathAndRedistListTarget = metaprojectInstance.AddTarget("GetFrameworkPathAndRedistList", String.Empty, null, null, null, null, null, null, null, false /* legacy target returns behaviour */);

            ProjectTaskInstance getFrameworkPathTask = frameworkPathAndRedistListTarget.AddTask("GetFrameworkPath", String.Empty, null);

            // Follow the same logic we use in Microsoft.Common.targets to choose the target framework
            // directories (which are then used to find the set of redist lists).
            getFrameworkPathTask.AddOutputItem(
                "Path",
                "_CombinedTargetFrameworkDirectoriesItem",
                "'$(MSBuildToolsVersion)' == '2.0'");

            // TFV v4.0 supported by TV 4.0+
            getFrameworkPathTask.AddOutputItem(
                "FrameworkVersion40Path",
                "_CombinedTargetFrameworkDirectoriesItem",
                " '$(TargetFrameworkVersion)' == 'v4.0' and '$(MSBuildToolsVersion)' != '2.0' and '$(MSBuildToolsVersion)' != '3.5'");

            // TFV v3.5 supported by TV 3.5+
            getFrameworkPathTask.AddOutputItem(
                "FrameworkVersion35Path",
                "_CombinedTargetFrameworkDirectoriesItem",
                " ('$(TargetFrameworkVersion)' == 'v3.5' or '$(TargetFrameworkVersion)' == 'v4.0') and '$(MSBuildToolsVersion)' != '2.0'");

            // TFV v3.0 supported by TV 3.5+ (there was no TV 3.0)
            getFrameworkPathTask.AddOutputItem(
                "FrameworkVersion30Path",
                "_CombinedTargetFrameworkDirectoriesItem",
                " ('$(TargetFrameworkVersion)' == 'v3.0' or '$(TargetFrameworkVersion)' == 'v3.5' or '$(TargetFrameworkVersion)' == 'v4.0') and '$(MSBuildToolsVersion)' != '2.0'");

            // TFV v2.0 supported by TV 3.5+ (there was no TV 3.0). This property was not added until toolsversion 3.5 therefore it cannot be used for toolsversion 2.0
            getFrameworkPathTask.AddOutputItem(
                "FrameworkVersion20Path",
                "_CombinedTargetFrameworkDirectoriesItem",
                "'$(MSBuildToolsVersion)' != '2.0'");

            ProjectTaskInstance createItemTask = frameworkPathAndRedistListTarget.AddTask("CreateItem", null, null);
            createItemTask.SetParameter("Include", @"@(_CombinedTargetFrameworkDirectoriesItem->'%(Identity)\RedistList\*.xml')");
            createItemTask.AddOutputItem("Include", "InstalledAssemblyTables", null);
        }
#endif // FEATURE_ASPNET_COMPILER

        /// <summary>
        /// Adds a target for a project whose type is unknown and we cannot build.  We will emit an error or warning as appropriate.
        /// </summary>
        private void AddMetaprojectTargetForUnknownProjectType(ProjectInstance traversalProject, ProjectInstance metaprojectInstance, ProjectInSolution project, string targetName, string unknownProjectTypeErrorMessage)
        {
            ProjectTargetInstance newTarget = metaprojectInstance.AddTarget(targetName ?? "Build", "'$(CurrentSolutionConfigurationContents)' != ''", null, null, null, null, null, null, null, false /* legacy target returns behaviour */);

            foreach (SolutionConfigurationInSolution solutionConfiguration in _solutionFile.SolutionConfigurations)
            {
                if (project.ProjectConfigurations.TryGetValue(solutionConfiguration.FullName, out ProjectConfigurationInSolution projectConfiguration))
                {
                    if (projectConfiguration.IncludeInBuild)
                    {
                        // Only add the task if it would run in this configuration.
                        if (!traversalProject.EvaluateCondition(GetConditionStringForConfiguration(solutionConfiguration)))
                        {
                            continue;
                        }

                        if (unknownProjectTypeErrorMessage == null)
                        {
                            // we haven't encountered any problems accessing the project file in the past, but do not support
                            // building this project type
                            AddErrorWarningMessageInstance
                            (
                                newTarget,
                                null,
                                XMakeElements.warning,
                                true,
                                "SolutionParseUnknownProjectType",
                                project.RelativePath
                            );
                        }
                        else
                        {
                            // this project file may be of supported type, but we have encountered problems accessing it
                            AddErrorWarningMessageInstance
                            (
                                newTarget,
                                null,
                                XMakeElements.warning,
                                true,
                                "SolutionParseErrorReadingProject",
                                project.RelativePath,
                                unknownProjectTypeErrorMessage
                            );
                        }
                    }
                    else
                    {
                        AddErrorWarningMessageInstance
                        (
                            newTarget,
                            null,
                            XMakeElements.message,
                            true,
                            "SolutionProjectSkippedForBuilding",
                            project.ProjectName,
                            solutionConfiguration.FullName
                        );
                    }
                }
                else
                {
                    AddErrorWarningMessageInstance
                    (
                        newTarget,
                        null,
                        XMakeElements.warning,
                        true,
                        "SolutionProjectConfigurationMissing",
                        project.ProjectName,
                        solutionConfiguration.FullName
                    );
                }
            }
        }

        /// <summary>
        /// Adds a target which verifies that all of the project references and configurations are valid.
        /// </summary>
        private void AddValidateProjectsTarget(ProjectInstance traversalProject, List<ProjectInSolution> projects)
        {
            ProjectTargetInstance newTarget = traversalProject.AddTarget("ValidateProjects", null, null, null, null, null, null, null, null, false /* legacy target returns behaviour */);

            foreach (ProjectInSolution project in projects)
            {
                foreach (SolutionConfigurationInSolution solutionConfiguration in _solutionFile.SolutionConfigurations)
                {
                    string condition = GetConditionStringForConfiguration(solutionConfiguration);

                    if (project.ProjectConfigurations.TryGetValue(solutionConfiguration.FullName, out ProjectConfigurationInSolution projectConfiguration))
                    {
                        if (!projectConfiguration.IncludeInBuild)
                        {
                            AddErrorWarningMessageInstance
                                (
                                newTarget,
                                condition,
                                XMakeElements.message,
                                true,
                                "SolutionProjectSkippedForBuilding",
                                project.ProjectName,
                                solutionConfiguration.FullName
                                );
                        }
                    }
                    else
                    {
                        AddErrorWarningMessageInstance
                            (
                            newTarget,
                            condition,
                            XMakeElements.warning,
                            true,
                            "SolutionProjectConfigurationMissing",
                            project.ProjectName,
                            solutionConfiguration.FullName
                            );
                    }
                }
            }
        }

        ///<summary>
        /// Creates the target used to build all of the references in the traversal project.
        ///</summary>
        private static void AddTraversalReferencesTarget(ProjectInstance traversalProject, string targetName, string outputItem)
        {
            string outputItemAsItem = null;
            if (!String.IsNullOrEmpty(outputItem))
            {
                outputItemAsItem = "@(" + outputItem + ")";
            }

            string correctedTargetName = targetName ?? "Build";

            traversalProject.RemoveTarget(correctedTargetName);
            ProjectTargetInstance target = traversalProject.AddTarget(correctedTargetName, string.Empty, string.Empty, outputItemAsItem, null, string.Empty, string.Empty, string.Empty, string.Empty, false /* legacy target returns behaviour */);
            AddReferencesBuildTask(target, targetName, outputItem);
        }

        /// <summary>
        /// Adds a task which builds the @(ProjectReference) items.
        /// </summary>
        private static void AddReferencesBuildTask(ProjectTargetInstance target, string targetToBuild, string outputItem)
        {
            ProjectTaskInstance task = target.AddTask("MSBuild", String.Empty, String.Empty);
            if (String.Equals(targetToBuild, "Clean", StringComparison.OrdinalIgnoreCase))
            {
                task.SetParameter("Projects", "@(ProjectReference->Reverse())");
            }
            else
            {
                task.SetParameter("Projects", "@(ProjectReference)");  // The references already have the tools versions and properties set on them.
            }

            if (targetToBuild != null)
            {
                task.SetParameter("Targets", targetToBuild);
            }

            task.SetParameter("BuildInParallel", "True");
            task.SetParameter("Properties", SolutionProperties);

            // We only want to build "nonexistent" projects if we're building metaprojects, since they don't exist on disk.  Otherwise, 
            // we still want to error when the referenced project doesn't exist.  
            task.SetParameter("SkipNonexistentProjects", "%(ProjectReference.SkipNonexistentProjects)");

            if (outputItem != null)
            {
                task.AddOutputItem("TargetOutputs", outputItem, String.Empty);
            }
        }

        /// <summary>
        /// Adds a traversal target which invokes a specified target on a single project.  This creates targets called "Project", "Project:Rebuild", "Project:Clean", "Project:Publish" etc.
        /// </summary>
        private void AddTraversalTargetForProject(ProjectInstance traversalProject, ProjectInSolution project, ProjectConfigurationInSolution projectConfiguration, string targetToBuild, string outputItem, bool canBuildDirectly)
        {
            string baseProjectName = ProjectInSolution.DisambiguateProjectTargetName(project.GetUniqueProjectName());
            string actualTargetName = baseProjectName;
            if (targetToBuild != null)
            {
                actualTargetName += ":" + targetToBuild;
            }

            // Don't add the target again.  The user might have specified /t:Project:target which was already added but only this method knows about Project:Target so
            // after coming up with that target name, it can check if it has already been added.
            if (traversalProject.Targets.ContainsKey(actualTargetName))
            {
                return;
            }

            // The output item name is the concatenation of the project name with the specified outputItem.  In the typical case, if the
            // project name is MyProject, the outputItemName will be MyProjectBuildOutput, and the outputItemAsItem will be @(MyProjectBuildOutput).
            // In the case where the project contains characters not allowed as Xml element attribute values, those characters will
            // be replaced with underscores.  In the case where MyProject is actually unrepresentable in Xml, then the
            // outputItemName would be _________BuildOutput.
            string outputItemName = null;
            string outputItemAsItem = null;
            if (!String.IsNullOrEmpty(outputItem))
            {
                outputItemName = MakeIntoSafeItemName(baseProjectName) + outputItem;
                outputItemAsItem = "@(" + outputItemName + ")";
            }

            ProjectTargetInstance targetElement = traversalProject.AddTarget(actualTargetName, null, null, outputItemAsItem, null, null, null, null, null, false /* legacy target returns behaviour */);
            if (canBuildDirectly)
            {
                AddProjectBuildTask(traversalProject, projectConfiguration, targetElement, targetToBuild, "@(ProjectReference)", "'%(ProjectReference.Identity)' == '" + EscapingUtilities.Escape(project.AbsolutePath) + "'", outputItemName);
            }
            else
            {
                AddMetaprojectBuildTask(project, targetElement, targetToBuild, outputItemName);
            }
        }

        /// <summary>
        /// Retrieves a dictionary representing the global properties which should be transferred to a metaproject.
        /// </summary>
        /// <param name="traversalProject">The traversal from which the global properties should be obtained.</param>
        /// <returns>A dictionary of global properties.</returns>
        private IDictionary<string, string> GetMetaprojectGlobalProperties(ProjectInstance traversalProject)
        {
            var properties = new Dictionary<string, string>(_metaprojectGlobalProperties.Length, StringComparer.OrdinalIgnoreCase);
            foreach (Tuple<string, string> property in _metaprojectGlobalProperties)
            {
                if (property.Item2 == null)
                {
                    properties[property.Item1] = EscapingUtilities.Escape(traversalProject.GetPropertyValue(property.Item1));
                }
                else
                {
                    properties[property.Item1] = EscapingUtilities.Escape(property.Item2);
                }
            }

            // Now provide any which are explicitly set on the solution
            foreach (ProjectPropertyInstance globalProperty in traversalProject.GlobalPropertiesDictionary)
            {
                properties[globalProperty.Name] = ((IProperty)globalProperty).EvaluatedValueEscaped;
            }

            // If we have a sub-toolset version, it will be set on the P2P from the solution metaproj, so we need
            // to make sure it's set here, too, so the global properties will match.  
            if (traversalProject.SubToolsetVersion != null)
            {
                if (traversalProject.SubToolsetVersion.Equals("4.0", StringComparison.OrdinalIgnoreCase))
                {
                    properties[Constants.SubToolsetVersionPropertyName] = traversalProject.SubToolsetVersion;
                }
            }

            return properties;
        }

        /// <summary>
        /// Figures out what the ToolsVersion should be for child projects (used when scanning
        /// for dependencies)
        /// </summary>
        private string DetermineChildProjectToolsVersion(string wrapperProjectToolsVersion)
        {
            _globalProperties.TryGetValue("ProjectToolsVersion", out string childProjectToolsVersion);

            return childProjectToolsVersion ?? wrapperProjectToolsVersion;
        }

        /// <summary>
        /// Normally the active solution configuration/platform is determined when we build the solution
        /// wrapper project, not when we create it. However, we need to know them to scan project references
        /// for the right project configuration/platform. It's unlikely that references would be conditional,
        /// but still possible and we want to get that case right.
        /// </summary>
        private string PredictActiveSolutionConfigurationName()
        {
            return PredictActiveSolutionConfigurationName(_solutionFile, _globalProperties);
        }

        /// <summary>
        /// Loads each MSBuild project in this solution and looks for its project-to-project references so that
        /// we know what build order we should use when building the solution.
        /// </summary>
        private void ScanProjectDependencies(string childProjectToolsVersion, string fullSolutionConfigurationName)
        {
            // Don't bother with all this if the solution configuration doesn't even exist.
            if (fullSolutionConfigurationName == null)
            {
                return;
            }

            foreach (ProjectInSolution project in _solutionFile.ProjectsInOrder)
            {
                // We only need to scan .wdproj projects: Everything else is either MSBuildFormat or 
                // something we don't know how to do anything with anyway
                if (project.ProjectType == SolutionProjectType.WebDeploymentProject)
                {
                    // Skip the project if we don't have its configuration in this solution configuration
                    if (!project.ProjectConfigurations.ContainsKey(fullSolutionConfigurationName))
                    {
                        continue;
                    }

                    try
                    {
                        Project msbuildProject = new Project(project.AbsolutePath, _globalProperties, childProjectToolsVersion);

                        // ProjectDependency items work exactly like ProjectReference items from the point of 
                        // view of determining that project B depends on project A.  This item must cause
                        // project A to be built prior to project B.
                        //
                        // This has the format 
                        // <ProjectDependency Include="DependentProjectRelativePath">
                        //   <Project>{GUID}</Project>
                        // </Project>
                        IEnumerable<ProjectItem> references = msbuildProject.GetItems("ProjectDependency");

                        foreach (ProjectItem reference in references)
                        {
                            string referencedProjectGuid = reference.GetMetadataValue("Project");
                            AddDependencyByGuid(project, referencedProjectGuid);
                        }

                        // If this is a web deployment project, we have a reference specified as a property
                        // "SourceWebProject" rather than as a ProjectReference item.  This has the format
                        // {GUID}|PATH_TO_CSPROJ
                        // where
                        // GUID is the project guid for the "source" project
                        // PATH_TO_CSPROJ is the solution-relative path to the csproj file.
                        //
                        // NOTE: This is obsolete and is intended only for backward compatability with
                        // Whidbey-generated web deployment projects.  New projects should use the
                        // ProjectDependency item above.
                        string referencedWebProjectGuid = msbuildProject.GetPropertyValue("SourceWebProject");
                        if (!string.IsNullOrEmpty(referencedWebProjectGuid))
                        {
                            // Grab the guid with its curly braces...
                            referencedWebProjectGuid = referencedWebProjectGuid.Substring(0, 38);
                            AddDependencyByGuid(project, referencedWebProjectGuid);
                        }
                    }
                    catch (Exception e)
                    {
                        // We don't want any problems scanning the project file to result in aborting the build.
                        if (ExceptionHandling.IsCriticalException(e))
                        {
                            throw;
                        }

                        _loggingService.LogWarning
                            (
                            _projectBuildEventContext,
                            "SubCategoryForSolutionParsingErrors",
                            new BuildEventFileInfo(project.RelativePath),
                            "SolutionScanProjectDependenciesFailed",
                            project.RelativePath,
                            e.Message
                            );
                    }
                }
            }
        }

        /// <summary>
        /// Adds a dependency to the project based on the specified guid string.
        /// </summary>
        /// <remarks>
        /// If the string is null or empty, no dependency is added and this is not considered an error.
        /// </remarks>
        private void AddDependencyByGuid(ProjectInSolution project, string dependencyGuid)
        {
            if (!String.IsNullOrEmpty(dependencyGuid))
            {
                if (_solutionFile.ProjectsByGuid.ContainsKey(dependencyGuid))
                {
                    project.AddDependency(dependencyGuid);
                }
                else
                {
                    _loggingService.LogWarning
                        (
                        _projectBuildEventContext,
                        "SubCategoryForSolutionParsingErrors",
                        new BuildEventFileInfo(_solutionFile.FullPath),
                        "SolutionParseProjectDepNotFoundError",
                        project.ProjectGuid,
                        dependencyGuid
                        );
                }
            }
        }

        /// <summary>
        /// Creates default Configuration and Platform values based on solution configurations present in the solution
        /// </summary>
        private void AddConfigurationPlatformDefaults(ProjectRootElement traversalProject)
        {
            ProjectPropertyGroupElement configurationDefaultingPropertyGroup = traversalProject.CreatePropertyGroupElement();
            traversalProject.AppendChild(configurationDefaultingPropertyGroup);

            configurationDefaultingPropertyGroup.Condition = " '$(Configuration)' == '' ";
            configurationDefaultingPropertyGroup.AddProperty("Configuration", EscapingUtilities.Escape(_solutionFile.GetDefaultConfigurationName()));

            ProjectPropertyGroupElement platformDefaultingPropertyGroup = traversalProject.CreatePropertyGroupElement();
            traversalProject.AppendChild(platformDefaultingPropertyGroup);

            platformDefaultingPropertyGroup.Condition = " '$(Platform)' == '' ";
            platformDefaultingPropertyGroup.AddProperty("Platform", EscapingUtilities.Escape(_solutionFile.GetDefaultPlatformName()));
        }

        /// <summary>
        /// Adds a new property group with contents of the given solution configuration to the project.
        /// </summary>
        private void AddPropertyGroupForSolutionConfiguration(ProjectRootElement traversalProject, SolutionConfigurationInSolution solutionConfiguration)
        {
            AddPropertyGroupForSolutionConfiguration(traversalProject, _solutionFile, solutionConfiguration);
        }

        /// <summary>
        /// Creates the default Venus configuration property based on the selected solution configuration.
        /// Unfortunately, Venus projects only expose one project configuration in the IDE (Debug) although
        /// they allow building Debug and Release from command line. This means that if we wanted to use
        /// the project configuration from the active solution configuration for Venus projects, we'd always
        /// end up with Debug and there'd be no way to build the Release configuration. To work around this,
        /// we use a special mechanism for choosing ASP.NET project configuration: we set it to Release if
        /// we're building a Release solution configuration, and to Debug if we're building a Debug solution
        /// configuration. The property is also settable from the command line, in which case it takes
        /// precedence over this algorithm.
        /// </summary>
        private static void AddVenusConfigurationDefaults(ProjectRootElement traversalProject)
        {
            ProjectPropertyGroupElement venusConfiguration = traversalProject.CreatePropertyGroupElement();
            traversalProject.AppendChild(venusConfiguration);

            venusConfiguration.Condition = " ('$(AspNetConfiguration)' == '') ";
            venusConfiguration.AddProperty("AspNetConfiguration", "$(Configuration)");
        }

        /// <summary>
        /// Adds solution related build event macros and other global properties to the wrapper project
        /// </summary>
        private void AddGlobalProperties(ProjectRootElement traversalProject)
        {
            ProjectPropertyGroupElement globalProperties = traversalProject.CreatePropertyGroupElement();
            traversalProject.AppendChild(globalProperties);

            string directoryName = _solutionFile.SolutionFileDirectory;
            if (!directoryName.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                directoryName += Path.DirectorySeparatorChar;
            }

            globalProperties.AddProperty("SolutionDir", EscapingUtilities.Escape(directoryName));
            globalProperties.AddProperty("SolutionExt", EscapingUtilities.Escape(Path.GetExtension(_solutionFile.FullPath)));
            globalProperties.AddProperty("SolutionFileName", EscapingUtilities.Escape(Path.GetFileName(_solutionFile.FullPath)));
            globalProperties.AddProperty("SolutionName", EscapingUtilities.Escape(Path.GetFileNameWithoutExtension(_solutionFile.FullPath)));

            globalProperties.AddProperty(SolutionPathPropertyName, EscapingUtilities.Escape(Path.Combine(_solutionFile.SolutionFileDirectory, Path.GetFileName(_solutionFile.FullPath))));

            // Add other global properties
            ProjectPropertyGroupElement frameworkVersionProperties = traversalProject.CreatePropertyGroupElement();
            traversalProject.AppendChild(frameworkVersionProperties);

            // Set the property "TargetFrameworkVersion". This is needed for the GetFrameworkPath target.
            // If TargetFrameworkVersion is already set by the user, use that value.
            // Otherwise if MSBuildToolsVersion is 2.0, use "v2.0"
            // Otherwise if MSBuildToolsVersion is 3.5, use "v3.5"
            // Otherwise use "v4.0".
            ProjectPropertyElement tfv20Property = frameworkVersionProperties.AddProperty("TargetFrameworkVersion", "v2.0");
            ProjectPropertyElement tfv35Property = frameworkVersionProperties.AddProperty("TargetFrameworkVersion", "v3.5");
            ProjectPropertyElement tfv40Property = frameworkVersionProperties.AddProperty("TargetFrameworkVersion", "v4.0");
            tfv20Property.Condition = "'$(TargetFrameworkVersion)' == '' and '$(MSBuildToolsVersion)' == '2.0'";
            tfv35Property.Condition = "'$(TargetFrameworkVersion)' == '' and ('$(MSBuildToolsVersion)' == '3.5' or '$(MSBuildToolsVersion)' == '3.0')";
            tfv40Property.Condition = "'$(TargetFrameworkVersion)' == '' and !('$(MSBuildToolsVersion)' == '3.5' or '$(MSBuildToolsVersion)' == '3.0' or '$(MSBuildToolsVersion)' == '2.0')";
        }

        /// <summary>
        /// Special hack for web projects. It can happen that there is no Release configuration for solutions
        /// containing web projects, yet we still want to be able to build the Release configuration for
        /// those projects. Since the ASP.NET project configuration defaults to the solution configuration,
        /// we allow Release even if it doesn't actually exist in the solution.
        /// </summary>
        private void AddFakeReleaseSolutionConfigurationIfNecessary()
        {
            if (_solutionFile.ContainsWebProjects)
            {
                bool solutionHasReleaseConfiguration = false;
                foreach (SolutionConfigurationInSolution solutionConfiguration in _solutionFile.SolutionConfigurations)
                {
                    if (string.Equals(solutionConfiguration.ConfigurationName, "Release", StringComparison.OrdinalIgnoreCase))
                    {
                        solutionHasReleaseConfiguration = true;
                        break;
                    }
                }

                if ((!solutionHasReleaseConfiguration) && (_solutionFile.SolutionConfigurations.Count > 0))
                {
                    _solutionFile.AddSolutionConfiguration("Release", _solutionFile.GetDefaultPlatformName());
                }
            }
        }

        /// <summary>
        /// Adds the initial target to the solution wrapper project, necessary for a few message/error tags
        /// </summary>
        private void AddInitialTargets(ProjectInstance traversalProject, List<ProjectInSolution> projects)
        {
            AddValidateSolutionConfigurationTarget(traversalProject);
            AddValidateToolsVersionsTarget(traversalProject);
            AddValidateProjectsTarget(traversalProject, projects);
            AddGetSolutionConfigurationContentsTarget(traversalProject);
        }

        /// <summary>
        /// Adds the target which validates that the solution configuration specified by the user is supported.
        /// </summary>
        private void AddValidateSolutionConfigurationTarget(ProjectInstance traversalProject)
        {
            ProjectTargetInstance initialTarget = traversalProject.AddTarget("ValidateSolutionConfiguration", null, null, null, null, null, null, null, null, false /* legacy target returns behaviour */);

            if (_solutionFile.SolutionConfigurations.Count > 0)
            {
                AddErrorWarningMessageInstance
                    (
                    initialTarget,
                    "('$(CurrentSolutionConfigurationContents)' == '') and ('$(SkipInvalidConfigurations)' != 'true')",
                    XMakeElements.error,
                    false /* do not treat as literal */,
                    "SolutionInvalidSolutionConfiguration",
                    "$(Configuration)|$(Platform)"
                    );

                AddErrorWarningMessageInstance
                    (
                    initialTarget,
                    "('$(CurrentSolutionConfigurationContents)' == '') and ('$(SkipInvalidConfigurations)' == 'true')",
                    XMakeElements.warning,
                    false /* do not treat as literal */,
                    "SolutionInvalidSolutionConfiguration",
                    "$(Configuration)|$(Platform)"
                    );

                AddErrorWarningMessageInstance
                    (
                    initialTarget,
                    "'$(CurrentSolutionConfigurationContents)' != ''",
                    XMakeElements.message,
                    false /* do not treat as literal */,
                    "SolutionBuildingSolutionConfiguration",
                    "$(Configuration)|$(Platform)"
                    );
            }
        }

        /// <summary>
        /// Adds the target which validates that the tools version is supported.
        /// </summary>
        private static void AddValidateToolsVersionsTarget(ProjectInstance traversalProject)
        {
            ProjectTargetInstance validateToolsVersionsTarget = traversalProject.AddTarget("ValidateToolsVersions", null, null, null, null, null, null, null, null, false /* legacy target returns behaviour */);
            ProjectTaskInstance toolsVersionErrorTask = AddErrorWarningMessageInstance
                (
                validateToolsVersionsTarget,
                "'$(MSBuildToolsVersion)' == '2.0' and ('$(ProjectToolsVersion)' != '2.0' and '$(ProjectToolsVersion)' != '')",
                XMakeElements.error,
                false /* do not treat as literal */,
                "SolutionToolsVersionDoesNotSupportProjectToolsVersion",
                "$(MSBuildToolsVersion)"
                );
        }

        /// <summary> Adds the target to fetch solution configuration contents for given configuration|platform combo. </summary>
        private static void AddGetSolutionConfigurationContentsTarget(ProjectInstance traversalProject)
        {
            var initialTarget = traversalProject.AddTarget(
                targetName: "GetSolutionConfigurationContents",
                condition: null,
                inputs: null,
                outputs: "$(SolutionConfigurationContents)",
                returns: null,
                keepDuplicateOutputs: null,
                dependsOnTargets: null,
                beforeTargets: null,
                afterTargets: null,
                parentProjectSupportsReturnsAttribute: false);

            var property = new ProjectPropertyGroupTaskPropertyInstance(
                                                    "SolutionConfigurationContents",
                                                    "@(SolutionConfiguration->WithMetadataValue('Identity', '$(Configuration)|$(Platform)')->'%(Content)')",
                                                    string.Empty,
                                                    initialTarget.Location,
                                                    initialTarget.Location);

            initialTarget.AddProjectTargetInstanceChild(new ProjectPropertyGroupTaskInstance(
                                                            string.Empty,
                                                            initialTarget.Location,
                                                            initialTarget.Location,
                                                            new List<ProjectPropertyGroupTaskPropertyInstance> { property }));
        }
    }
}
