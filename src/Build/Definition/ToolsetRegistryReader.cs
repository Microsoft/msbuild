// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if FEATURE_WIN32_REGISTRY

using System;
using System.Collections.Generic;

using Microsoft.Build.Shared;
using error = Microsoft.Build.Shared.ErrorUtilities;
using RegistryKeyWrapper = Microsoft.Build.Internal.RegistryKeyWrapper;
using RegistryException = Microsoft.Build.Exceptions.RegistryException;
using InvalidToolsetDefinitionException = Microsoft.Build.Exceptions.InvalidToolsetDefinitionException;
using Constants = Microsoft.Build.Internal.Constants;
using Microsoft.Build.Construction;
using Microsoft.Build.Collections;
using Microsoft.Build.Execution;
using Microsoft.Build.Internal;

namespace Microsoft.Build.Evaluation
{
    /// <summary>
    /// Reads registry at the base key and returns a Dictionary keyed on ToolsVersion.
    /// Dictionary contains another dictionary of (property name, property value) pairs.
    /// If a registry value is not a string, this will throw a InvalidToolsetDefinitionException.
    /// An example of how the registry will look (note that the DefaultToolsVersion is per-MSBuild-version)
    /// [HKLM]\SOFTWARE\Microsoft
    ///   msbuild
    ///     3.5
    ///       @DefaultToolsVersion = 2.0
    ///     ToolsVersions
    ///       2.0
    ///         @MSBuildToolsPath = D:\SomeFolder
    ///       3.5
    ///         @MSBuildToolsPath = D:\SomeOtherFolder
    ///         @MSBuildBinPath = D:\SomeOtherFolder
    ///         @SomePropertyName = PropertyOtherValue
    /// </summary>
    internal class ToolsetRegistryReader : ToolsetReader
    {
        /// <summary>
        /// Registry location for storing tools version dependent data for msbuild
        /// </summary>
        private const string MSBuildRegistryPath = @"SOFTWARE\Microsoft\MSBuild";

        /// <summary>
        /// Cached registry wrapper at root of the msbuild entries
        /// </summary>
        private RegistryKeyWrapper _msbuildRegistryWrapper;

        /// <summary>
        /// Default constructor
        /// </summary>
        internal ToolsetRegistryReader(PropertyDictionary<ProjectPropertyInstance> environmentProperties, PropertyDictionary<ProjectPropertyInstance> globalProperties)
            : this(environmentProperties, globalProperties, new RegistryKeyWrapper(MSBuildRegistryPath))
        {
        }

        /// <summary>
        /// Constructor overload accepting a registry wrapper for unit testing purposes only
        /// </summary>
        internal ToolsetRegistryReader(PropertyDictionary<ProjectPropertyInstance> environmentProperties, PropertyDictionary<ProjectPropertyInstance> globalProperties, RegistryKeyWrapper msbuildRegistryWrapper)
            : base(environmentProperties, globalProperties)
        {
            error.VerifyThrowArgumentNull(msbuildRegistryWrapper, nameof(msbuildRegistryWrapper));

            _msbuildRegistryWrapper = msbuildRegistryWrapper;
        }

        /// <summary>
        /// Returns the list of tools versions
        /// </summary>
        protected override IEnumerable<ToolsetPropertyDefinition> ToolsVersions
        {
            get
            {
                string[] toolsVersionNames = Array.Empty<string>();
                try
                {
                    RegistryKeyWrapper subKey = null;
                    using (subKey = _msbuildRegistryWrapper.OpenSubKey("ToolsVersions"))
                    {
                        toolsVersionNames = subKey.GetSubKeyNames();
                    }
                }
                catch (RegistryException ex)
                {
                    InvalidToolsetDefinitionException.Throw(ex, "RegistryReadError", ex.Source, ex.Message);
                }

                foreach (string toolsVersionName in toolsVersionNames)
                {
                    // For the purposes of error location, use the registry path instead of a file name
                    IElementLocation location = new RegistryLocation(_msbuildRegistryWrapper.Name + "\\ToolsVersions\\" + toolsVersionName);

                    yield return new ToolsetPropertyDefinition(toolsVersionName, string.Empty, location);
                }
            }
        }

        /// <summary>
        /// Returns the default tools version, or null if none was specified
        /// </summary>
        protected override string DefaultToolsVersion
        {
            get
            {
                string defaultToolsVersion = null;

                // We expect to find the DefaultToolsVersion value under a registry key named for our
                // version, e.g., "3.5"
                using (RegistryKeyWrapper defaultToolsVersionKey = _msbuildRegistryWrapper.OpenSubKey(Constants.AssemblyVersion))
                {
                    if (defaultToolsVersionKey != null)
                    {
                        defaultToolsVersion = GetValue(defaultToolsVersionKey, "DefaultToolsVersion");
                    }
                }

                return defaultToolsVersion;
            }
        }

        /// <summary>
        /// Returns the path to find override tasks, or null if none was specified
        /// </summary>
        protected override string MSBuildOverrideTasksPath
        {
            get
            {
                string defaultToolsVersion = null;

                // We expect to find the MsBuildOverrideTasksPath value under a registry key named for our
                // version, e.g., "4.0"
                using (RegistryKeyWrapper defaultToolsVersionKey = _msbuildRegistryWrapper.OpenSubKey(Constants.AssemblyVersion))
                {
                    if (defaultToolsVersionKey != null)
                    {
                        defaultToolsVersion = GetValue(defaultToolsVersionKey, ReservedPropertyNames.overrideTasksPath);
                    }
                }

                return defaultToolsVersion;
            }
        }

        /// <summary>
        /// ToolsVersion to use as the default ToolsVersion for this version of MSBuild
        /// </summary>
        protected override string DefaultOverrideToolsVersion
        {
            get
            {
                string defaultOverrideToolsVersion = null;

                // We expect to find the MsBuildOverrideTasksPath value under a registry key named for our
                // version, e.g., "12.0"
                using (RegistryKeyWrapper defaultOverrideToolsVersionKey = _msbuildRegistryWrapper.OpenSubKey(Constants.AssemblyVersion))
                {
                    if (defaultOverrideToolsVersionKey != null)
                    {
                        defaultOverrideToolsVersion = GetValue(defaultOverrideToolsVersionKey, ReservedPropertyNames.defaultOverrideToolsVersion);
                    }
                }

                return defaultOverrideToolsVersion;
            }
        }

        /// <summary>
        /// Provides an enumerator over property definitions for a specified tools version
        /// </summary>
        /// <param name="toolsVersion">The tools version</param>
        /// <returns>An enumeration of property definitions</returns>
        protected override IEnumerable<ToolsetPropertyDefinition> GetPropertyDefinitions(string toolsVersion)
        {
            RegistryKeyWrapper toolsVersionWrapper = null;
            try
            {
                try
                {
                    toolsVersionWrapper = _msbuildRegistryWrapper.OpenSubKey("ToolsVersions\\" + toolsVersion);
                }
                catch (RegistryException ex)
                {
                    InvalidToolsetDefinitionException.Throw(ex, "RegistryReadError", ex.Source, ex.Message);
                }

                foreach (string propertyName in toolsVersionWrapper.GetValueNames())
                {
                    yield return CreatePropertyFromRegistry(toolsVersionWrapper, propertyName);
                }
            }
            finally
            {
                toolsVersionWrapper?.Dispose();
            }
        }

        /// <summary>
        /// Provides an enumerator over the set of sub-toolset names available to a particular
        /// toolsversion
        /// </summary>
        /// <param name="toolsVersion">The tools version.</param>
        /// <returns>An enumeration of the sub-toolsets that belong to that toolsversion.</returns>
        protected override IEnumerable<string> GetSubToolsetVersions(string toolsVersion)
        {
            RegistryKeyWrapper toolsVersionWrapper = null;
            try
            {
                try
                {
                    toolsVersionWrapper = _msbuildRegistryWrapper.OpenSubKey("ToolsVersions\\" + toolsVersion);
                }
                catch (RegistryException ex)
                {
                    InvalidToolsetDefinitionException.Throw(ex, "RegistryReadError", ex.Source, ex.Message);
                }

                return toolsVersionWrapper.GetSubKeyNames();
            }
            finally
            {
                toolsVersionWrapper?.Dispose();
            }
        }

        /// <summary>
        /// Provides an enumerator over property definitions for a specified sub-toolset version
        /// under a specified toolset version.
        /// </summary>
        /// <param name="toolsVersion">The tools version.</param>
        /// <param name="subToolsetVersion">The sub-toolset version.</param>
        /// <returns>An enumeration of property definitions.</returns>
        protected override IEnumerable<ToolsetPropertyDefinition> GetSubToolsetPropertyDefinitions(string toolsVersion, string subToolsetVersion)
        {
            ErrorUtilities.VerifyThrowArgumentLength(subToolsetVersion, nameof(subToolsetVersion));

            RegistryKeyWrapper toolsVersionWrapper = null;
            RegistryKeyWrapper subToolsetWrapper = null;

            try
            {
                try
                {
                    toolsVersionWrapper = _msbuildRegistryWrapper.OpenSubKey("ToolsVersions\\" + toolsVersion);
                }
                catch (RegistryException ex)
                {
                    InvalidToolsetDefinitionException.Throw(ex, "RegistryReadError", ex.Source, ex.Message);
                }

                try
                {
                    subToolsetWrapper = toolsVersionWrapper.OpenSubKey(subToolsetVersion);
                }
                catch (RegistryException ex)
                {
                    InvalidToolsetDefinitionException.Throw(ex, "RegistryReadError", ex.Source, ex.Message);
                }

                foreach (string propertyName in subToolsetWrapper.GetValueNames())
                {
                    yield return CreatePropertyFromRegistry(subToolsetWrapper, propertyName);
                }
            }
            finally
            {
                toolsVersionWrapper?.Dispose();

                subToolsetWrapper?.Dispose();
            }
        }

        /// <summary>
        /// Returns a map of MSBuildExtensionsPath* property names/kind to list of search paths
        /// </summary>
        protected override Dictionary<string, ProjectImportPathMatch> GetProjectImportSearchPathsTable(string toolsVersion, string os)
        {
            return new Dictionary<string, ProjectImportPathMatch>();
        }

        /// <summary>
        /// Given a registry location containing a property name and value, create the ToolsetPropertyDefinition that maps to it
        /// </summary>
        /// <param name="toolsetWrapper">Wrapper for the key that we're getting values from</param>
        /// <param name="propertyName">The name of the property whose value we wish to generate a ToolsetPropertyDefinition for.</param>
        /// <returns>A ToolsetPropertyDefinition instance corresponding to the property name requested.</returns>
        private static ToolsetPropertyDefinition CreatePropertyFromRegistry(RegistryKeyWrapper toolsetWrapper, string propertyName)
        {
            string propertyValue = null;

            if (propertyName?.Length == 0)
            {
                InvalidToolsetDefinitionException.Throw("PropertyNameInRegistryHasZeroLength", toolsetWrapper.Name);
            }

            try
            {
                propertyValue = GetValue(toolsetWrapper, propertyName);
            }
            catch (RegistryException ex)
            {
                InvalidToolsetDefinitionException.Throw(ex, "RegistryReadError", ex.Source, ex.Message);
            }

            // For the purposes of error location, use the registry path instead of a file name
            IElementLocation location = new RegistryLocation(toolsetWrapper.Name + "@" + propertyName);

            return new ToolsetPropertyDefinition(propertyName, propertyValue, location);
        }

        /// <summary>
        /// Reads a string value from the specified registry key
        /// </summary>
        /// <param name="wrapper">wrapper around key</param>
        /// <param name="valueName">name of the value</param>
        /// <returns>string data in the value</returns>
        private static string GetValue(RegistryKeyWrapper wrapper, string valueName)
        {
            if (wrapper.Exists())
            {
                object result = wrapper.GetValue(valueName);

                // RegistryKey.GetValue returns null if the value is not present
                // and String.Empty if the value is present and no data is defined.
                // We preserve this distinction, because a string property in the registry with
                // no value really has an empty string for a value (which is a valid property value)
                // rather than null for a value (which is an invalid property value)
                if (result != null)
                {
                    // Must be a value of string type
                    if (!(result is string))
                    {
                        InvalidToolsetDefinitionException.Throw("NonStringDataInRegistry", wrapper.Name + "@" + valueName);
                    }

                    return result.ToString();
                }
            }

            return null;
        }
    }
}
#endif
