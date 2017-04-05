﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Resources;
using System.Reflection;
using System.Globalization;

namespace Microsoft.Build.Shared
{
    /// <summary>
    /// This class provides access to the assembly's resources.
    /// </summary>
    internal static class AssemblyResources
    {
        /// <summary>
        /// Loads the specified resource string, either from the assembly's primary resources, or its shared resources.
        /// </summary>
        /// <remarks>This method is thread-safe.</remarks>
        /// <param name="name"></param>
        /// <returns>The resource string, or null if not found.</returns>
        internal static string GetString(string name)
        {
            // NOTE: the ResourceManager.GetString() method is thread-safe
            string resource = GetStringFromEngineResources(name);

            if (resource == null)
            {
                resource = GetStringFromMSBuildExeResources(name);
            }

            return resource;
        }

        /// <summary>
        /// Loads the specified resource string, from the Engine or else Shared resources.
        /// </summary>
        /// <returns>The resource string, or null if not found.</returns>
        private static string GetStringFromEngineResources(string name)
        {
            string resource = s_resources.GetString(name, CultureInfo.CurrentUICulture);

            if (resource == null)
            {
                resource = s_sharedResources.GetString(name, CultureInfo.CurrentUICulture);
            }

            ErrorUtilities.VerifyThrow(resource != null, "Missing resource '{0}'", name);

            return resource;
        }

        /// <summary>
        /// Loads the specified resource string, from the MSBuild.exe resources.
        /// </summary>
        /// <returns>The resource string, or null if not found.</returns>
        private static string GetStringFromMSBuildExeResources(string name)
        {
            string resource = null;

            return resource;
        }

        internal static ResourceManager PrimaryResources
        {
            get { return s_resources; }
        }

        internal static ResourceManager SharedResources
        {
            get { return s_sharedResources; }
        }

        // assembly resources
        private static readonly ResourceManager s_resources = new ResourceManager("Microsoft.Build.Strings", typeof(AssemblyResources).GetTypeInfo().Assembly);
        // shared resources
        private static readonly ResourceManager s_sharedResources = new ResourceManager("Microsoft.Build.Strings.shared", typeof(AssemblyResources).GetTypeInfo().Assembly);
    }
}
