// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;

namespace Microsoft.Build.BuildEngine.Shared
{
    /// <summary>
    /// FrameworkLocationHelper provides utility methods for locating .NET Framework and .NET Framework SDK directories and files
    /// </summary>
    internal static class FrameworkLocationHelper
    {
        #region Private and internal members

        /// <summary>
        /// By default when a root path is not specified we would like to use the program files directory \ reference assemblies\framework as the root location
        /// to generate the reference assembly paths from.
        /// </summary>
        internal static readonly string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        internal static readonly string programFiles32 = GenerateProgramFiles32();
        internal static readonly string programFilesReferenceAssemblyLocation = GenerateProgramFilesReferenceAssemblyRoot();

        private const string dotNetFrameworkRegistryPath = "SOFTWARE\\Microsoft\\.NETFramework";
        private const string dotNetFrameworkSetupRegistryPath = "SOFTWARE\\Microsoft\\NET Framework Setup\\NDP";
        private const string dotNetFrameworkSetupRegistryInstalledName = "Install";

        internal const string fullDotNetFrameworkRegistryKey = "HKEY_LOCAL_MACHINE\\" + dotNetFrameworkRegistryPath;
        private const string dotNetFrameworkAssemblyFoldersRegistryPath = dotNetFrameworkRegistryPath + "\\AssemblyFolders";
        private const string referenceAssembliesRegistryValueName = "All Assemblies In";

        internal const string dotNetFrameworkSdkInstallKeyValueV11 = "SDKInstallRootv1.1";
        internal const string dotNetFrameworkVersionFolderPrefixV11 = "v1.1"; // v1.1 is for Everett.
        private const string dotNetFrameworkVersionV11 = "v1.1.4322"; // full Everett version to pass to NativeMethodsShared.GetRequestedRuntimeInfo().
        private const string dotNetFrameworkRegistryKeyV11 = dotNetFrameworkSetupRegistryPath + "\\" + dotNetFrameworkVersionV11;

        internal const string dotNetFrameworkSdkInstallKeyValueV20 = "SDKInstallRootv2.0";
        internal const string dotNetFrameworkVersionFolderPrefixV20 = "v2.0"; // v2.0 is for Whidbey.
        private const string dotNetFrameworkVersionV20 = "v2.0.50727"; // full Whidbey version to pass to NativeMethodsShared.GetRequestedRuntimeInfo().
        private const string dotNetFrameworkRegistryKeyV20 = dotNetFrameworkSetupRegistryPath + "\\" + dotNetFrameworkVersionV20;

        internal const string dotNetFrameworkVersionFolderPrefixV30 = "v3.0"; // v3.0 is for WinFx.
        private const string dotNetFrameworkVersionV30 = "v3.0"; // full WinFx version to pass to NativeMethodsShared.GetRequestedRuntimeInfo().
        private const string dotNetFrameworkAssemblyFoldersRegistryKeyV30 = dotNetFrameworkAssemblyFoldersRegistryPath + "\\" + dotNetFrameworkVersionFolderPrefixV30;
        private const string dotNetFrameworkRegistryKeyV30 = dotNetFrameworkSetupRegistryPath + "\\" + dotNetFrameworkVersionFolderPrefixV30 +"\\Setup";

        private const string dotNetFrameworkSdkRegistryPathV35 = "SOFTWARE\\Microsoft\\Microsoft SDKs\\Windows\\v7.0A";
        internal const string fullDotNetFrameworkSdkRegistryKeyV35 = "HKEY_LOCAL_MACHINE\\" + dotNetFrameworkSdkRegistryPathV35;
        private const string dotNetFrameworkRegistryKeyV35 = dotNetFrameworkSetupRegistryPath + "\\" + dotNetFrameworkVersionFolderPrefixV35;
        internal const string dotNetFrameworkSdkInstallKeyValueV35 = "InstallationFolder";

        internal const string dotNetFrameworkVersionFolderPrefixV35 = "v3.5"; // v3.5 is for Orcas.

        private const string dotNetFrameworkAssemblyFoldersRegistryKeyV35 = dotNetFrameworkAssemblyFoldersRegistryPath + "\\" + dotNetFrameworkVersionFolderPrefixV35;
        private const string secondaryDotNetFrameworkSdkRegistryPathV35 = "SOFTWARE\\Microsoft\\Microsoft SDKs\\Windows";
        internal const string secondaryDotNetFrameworkSdkInstallKeyValueV35 = "CurrentInstallFolder";

        internal const string dotNetFrameworkVersionFolderPrefixV40 = "v4.0";
        private const string dotNetFrameworkVersionV40 = dotNetFrameworkVersionFolderPrefixV40; // full Dev10 version to pass to NativeMethodsShared.GetRequestedRuntimeInfo().
        private const string dotNetFrameworkSdkRegistryPathV40 = "SOFTWARE\\Microsoft\\Microsoft SDKs\\Windows\\v7.0A";
        internal const string fullDotNetFrameworkSdkRegistryKeyV40 = "HKEY_LOCAL_MACHINE\\" + dotNetFrameworkSdkRegistryPathV40;
        internal const string dotNetFrameworkSdkInstallKeyValueV40 = "InstallationFolder";

        private const string dotNetFrameworkAssemblyFoldersRegistryKeyV40 = dotNetFrameworkAssemblyFoldersRegistryPath + "\\" + dotNetFrameworkVersionFolderPrefixV40;
        private const string secondaryDotNetFrameworkSdkRegistryPathV40 = "SOFTWARE\\Microsoft\\Microsoft SDKs\\Windows";
        internal const string secondaryDotNetFrameworkSdkInstallKeyValueV40 = "CurrentInstallFolder";
        private const string dotNetFrameworkRegistryKeyV40 = dotNetFrameworkSetupRegistryPath + "\\v4\\Full";
        private static readonly GetDirectories getDirectories = new GetDirectories(Directory.GetDirectories);

        private static string pathToDotNetFrameworkV11;
        internal static string PathToDotNetFrameworkV11
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkV11 == null)
                {
                    if (!CheckForFrameworkInstallation(dotNetFrameworkRegistryKeyV11, dotNetFrameworkSetupRegistryInstalledName))
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkV11 = null;
                    }
                    else
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkV11 =
                            FindDotNetFrameworkPath(
                                Path.GetDirectoryName(typeof(object).Module.FullyQualifiedName),
                                dotNetFrameworkVersionFolderPrefixV11,
                                getDirectories
                                );
                    }
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkV11;
            }
        }

        private static string pathToDotNetFrameworkV20;

        internal static string PathToDotNetFrameworkV20
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkV20 == null)
                {
                    if (!CheckForFrameworkInstallation(dotNetFrameworkRegistryKeyV20, dotNetFrameworkSetupRegistryInstalledName))
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkV20 = null;
                    }
                    else
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkV20 =
                            FindDotNetFrameworkPath(
                                Path.GetDirectoryName(typeof(object).Module.FullyQualifiedName),
                                dotNetFrameworkVersionFolderPrefixV20,
                                getDirectories
                                );
                    }
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkV20;
            }
        }

        private static string pathToDotNetFrameworkV30;

        internal static string PathToDotNetFrameworkV30
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkV30 == null)
                {
                    if (!CheckForFrameworkInstallation(dotNetFrameworkRegistryKeyV30, "InstallSuccess"))
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkV30 = null;
                    }
                    else
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkV30 =
                            FindDotNetFrameworkPath(
                                Path.GetDirectoryName(typeof(object).Module.FullyQualifiedName),
                                dotNetFrameworkVersionFolderPrefixV30,
                                getDirectories
                                );
                    }
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkV30;
            }
        }

        private static string pathToDotNetFrameworkV35;

        internal static string PathToDotNetFrameworkV35
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkV35 == null)
                {
                    if (!CheckForFrameworkInstallation(dotNetFrameworkRegistryKeyV35, dotNetFrameworkSetupRegistryInstalledName))
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkV35 = null;
                    }
                    else
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkV35 =
                            FindDotNetFrameworkPath(
                                Path.GetDirectoryName(typeof(object).Module.FullyQualifiedName),
                                dotNetFrameworkVersionFolderPrefixV35,
                                getDirectories
                                );
                    }
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkV35;
            }
        }

        private static string pathToDotNetFrameworkV40;

        internal static string PathToDotNetFrameworkV40
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkV40 == null)
                {
                    if (!CheckForFrameworkInstallation(dotNetFrameworkRegistryKeyV40, dotNetFrameworkSetupRegistryInstalledName))
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkV40 = null;
                    }
                    else
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkV40 =
                            FindDotNetFrameworkPath(
                                Path.GetDirectoryName(typeof(object).Module.FullyQualifiedName),
                                dotNetFrameworkVersionFolderPrefixV40,
                                getDirectories
                                );
                    }
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkV40;
            }
        }

        private static string pathToDotNetFrameworkSdkV11;

        internal static string PathToDotNetFrameworkSdkV11
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkSdkV11 == null)
                {
                    FrameworkLocationHelper.pathToDotNetFrameworkSdkV11 = FindRegistryValueUnderKey(
                        dotNetFrameworkRegistryPath,
                        dotNetFrameworkSdkInstallKeyValueV11);
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkSdkV11;
            }
        }

        private static string pathToDotNetFrameworkSdkV20;

        internal static string PathToDotNetFrameworkSdkV20
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkSdkV20 == null)
                {
                    FrameworkLocationHelper.pathToDotNetFrameworkSdkV20 = FindRegistryValueUnderKey(
                        dotNetFrameworkRegistryPath,
                        dotNetFrameworkSdkInstallKeyValueV20);
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkSdkV20;
            }
        }

        private static string pathToDotNetFrameworkSdkV35;

        internal static string PathToDotNetFrameworkSdkV35
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkSdkV35 == null)
                {
                    FrameworkLocationHelper.pathToDotNetFrameworkSdkV35 = FindRegistryValueUnderKey(
                        dotNetFrameworkSdkRegistryPathV35,
                        dotNetFrameworkSdkInstallKeyValueV35);

                    // Because there is no longer a strong 1:1 mapping between FX versions and SDK
                    // versions, if we're unable to locate the desired SDK version, we will try to
                    // use whichever SDK version is installed by looking at the key pointing to the
                    // "latest" version.
                    //
                    // This isn't ideal, but it will allow our tasks to function on any of several
                    // related SDKs even if they don't have exactly the same versions.

                    if (String.IsNullOrEmpty(FrameworkLocationHelper.pathToDotNetFrameworkSdkV35))
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkSdkV35 = FindRegistryValueUnderKey(
                            secondaryDotNetFrameworkSdkRegistryPathV35,
                            secondaryDotNetFrameworkSdkInstallKeyValueV35);
                    }
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkSdkV35;
            }
        }

        private static string pathToDotNetFrameworkSdkV40;

        internal static string PathToDotNetFrameworkSdkV40
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkSdkV40 == null)
                {
                    FrameworkLocationHelper.pathToDotNetFrameworkSdkV40 = FindRegistryValueUnderKey(
                        dotNetFrameworkSdkRegistryPathV40,
                        dotNetFrameworkSdkInstallKeyValueV40);

                    // Because there is no longer a strong 1:1 mapping between FX versions and SDK
                    // versions, if we're unable to locate the desired SDK version, we will try to
                    // use whichever SDK version is installed by looking at the key pointing to the
                    // "latest" version. For example, instead of 6.0A, we might fall back to 6.0B.
                    //
                    // This isn't ideal, but it will allow our tasks to function on any of several
                    // related SDKs even if they don't have exactly the same versions.

                    if (String.IsNullOrEmpty(FrameworkLocationHelper.pathToDotNetFrameworkSdkV40))
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkSdkV40 = FindRegistryValueUnderKey(
                            secondaryDotNetFrameworkSdkRegistryPathV40,
                            secondaryDotNetFrameworkSdkInstallKeyValueV40);
                    }
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkSdkV40;
            }
        }

        private static string pathToDotNetFrameworkReferenceAssembliesV30;

        internal static string PathToDotNetFrameworkReferenceAssembliesV30
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV30 == null)
                {
                    FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV30 = FindRegistryValueUnderKey(
                        dotNetFrameworkAssemblyFoldersRegistryKeyV30,
                        referenceAssembliesRegistryValueName);

                    if (FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV30 == null)
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV30 = GenerateReferenceAssemblyDirectory(dotNetFrameworkVersionFolderPrefixV30);
                    }
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV30;
            }
        }

        private static string pathToDotNetFrameworkReferenceAssembliesV35;

        internal static string PathToDotNetFrameworkReferenceAssembliesV35
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV35 == null)
                {
                    FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV35 = FindRegistryValueUnderKey(
                        dotNetFrameworkAssemblyFoldersRegistryKeyV35,
                        referenceAssembliesRegistryValueName);

                    if (FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV35 == null)
                    {
                        FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV35 = GenerateReferenceAssemblyDirectory(dotNetFrameworkVersionFolderPrefixV35);
                    }
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV35;
            }
        }

        private static string pathToDotNetFrameworkReferenceAssembliesV40;

        internal static string PathToDotNetFrameworkReferenceAssembliesV40
        {
            get
            {
                if (FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV40 == null)
                {
                    FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV40 = FindRegistryValueUnderKey(
                        dotNetFrameworkAssemblyFoldersRegistryKeyV40,
                        referenceAssembliesRegistryValueName);

                    if (FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV40 == null)
                    {
                       FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV40 = GenerateReferenceAssemblyDirectory(dotNetFrameworkVersionFolderPrefixV40);
                    }
                }

                return FrameworkLocationHelper.pathToDotNetFrameworkReferenceAssembliesV40;
            }
        }

        internal static string GetPathToDotNetFramework(Version version)
        {
            string frameworkVersion = version.Major + "." + version.Minor;

            switch (frameworkVersion)
            {
                case "1.1":
                    return FrameworkLocationHelper.PathToDotNetFrameworkV11;

                case "2.0":
                    return FrameworkLocationHelper.PathToDotNetFrameworkV20;

                case "3.0":
                    return FrameworkLocationHelper.PathToDotNetFrameworkV30;

                case "3.5":
                    return FrameworkLocationHelper.PathToDotNetFrameworkV35;

                case "4.0":
                    return FrameworkLocationHelper.PathToDotNetFrameworkV40;

                default:
                    ErrorUtilities.VerifyThrowArgument(false, "FrameworkLocationHelper.UnsupportedFrameworkVersion", frameworkVersion);
                    return null;
            }
        }

        /// <summary>
        /// Will return the path to the dot net framework reference assemblies if they exist under the program files\reference assembies\microsoft\framework directory
        /// or null if the directory does not exist.
        /// </summary>
        private static string GenerateReferenceAssemblyDirectory(string versionPrefix)
        {
            string programFilesReferenceAssemblyDirectory = Path.Combine(programFilesReferenceAssemblyLocation, versionPrefix);
            string referenceAssemblyDirectory = null;

            if(Directory.Exists(programFilesReferenceAssemblyDirectory))
            {
                referenceAssemblyDirectory = programFilesReferenceAssemblyDirectory;
            }

            return referenceAssemblyDirectory;
        }

        /// <summary>
        /// Look for the given registry value under the given key.
        /// </summary>
        /// <owner>JomoF,LukaszG</owner>
        /// <param name="registryBaseKeyName"></param>
        /// <param name="registryKeyName"></param>
        private static string FindRegistryValueUnderKey
        (
            string registryBaseKeyName,
            string registryKeyName
        )
        {
            Microsoft.Win32.RegistryKey baseKey = Microsoft.Win32.Registry
                .LocalMachine
                .OpenSubKey(registryBaseKeyName);

            if (baseKey == null)
            {
                return null;
            }

            object keyValue = baseKey.GetValue(registryKeyName);

            if (keyValue == null)
            {
                return null;
            }

            return keyValue.ToString();
        }

        /// <summary>
        /// Check the registry key and value to see if the .net Framework is installed on the machine.
        /// </summary>
        /// <param name="registryEntryToCheckInstall">Registry path to look for the value</param>
        /// <param name="registryValueToCheckInstall">Key to retreive the value from</param>
        /// <returns>True if the registry key is 1 false if it is not there. This method also return true if the complus enviornment variables are set.</returns>
        internal static bool CheckForFrameworkInstallation(string registryEntryToCheckInstall, string registryValueToCheckInstall)
        {
             // Get the complus install root and version
            string complusInstallRoot = Environment.GetEnvironmentVariable("COMPLUS_INSTALLROOT");
            string complusVersion = Environment.GetEnvironmentVariable("COMPLUS_VERSION");

            // Complus is not set we need to make sure the framework we are targeting is installed. Check the registry key before trying to find the directory.
            // If complus is set then we will return that directory as the framework directory, there is no need to check the registry value for the framework and it may not even be installed.
            if (String.IsNullOrEmpty(complusInstallRoot) && String.IsNullOrEmpty(complusVersion))
            {
                // If the registry entry is 1 then the framework is installed. Go ahead and find the directory. If it is not 1 then the framework is not installed, return null.
                return String.Equals("1", FindRegistryValueUnderKey(registryEntryToCheckInstall, registryValueToCheckInstall), StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        /// <summary>
        /// Heuristic that first considers the current runtime path and then searches the base of that path for the given
        /// frameworks version.
        /// </summary>
        /// <owner>JomoF</owner>
        /// <param name="currentRuntimePath">The path to the runtime that is currently executing.</param>
        /// <param name="prefix">Should be something like 'v1.2' that indicates the runtime version we want.</param>
        /// <param name="frameworkVersion">Should be the full version number of the runtime version we want.</param>
        /// <param name="getDirectories">Delegate to method that can return filesystem entries.</param>
        /// <param name="useHeuristic">Whether we should fall back to a search heuristic if other searches fail.</param>
        /// <returns>Will return 'null' if there is no target frameworks on this machine.</returns>
        internal static string FindDotNetFrameworkPath
        (
            string currentRuntimePath,
            string prefix,
            GetDirectories getDirectories
        )
        {
            string leaf = Path.GetFileName(currentRuntimePath);
            if (leaf.StartsWith(prefix, StringComparison.Ordinal))
            {
                // If the current runtime starts with correct prefix, then this is the
                // runtime we want to use.
                return currentRuntimePath;
            }

            // We haven't managed to use exact methods to locate the FX, so
            // search for the correct path with a heuristic.
            string baseLocation = Path.GetDirectoryName(currentRuntimePath);
            string searchPattern = prefix + "*";
            string[] directories = getDirectories(baseLocation, searchPattern);

            if (directories.Length == 0)
            {
                // Couldn't find the path, return a null.
                return null;
            }

            // We don't care which one we choose, but we want to be predictible.
            // The intention here is to choose the alphabetical maximum.
            string max = directories[0];

            // the max.EndsWith condition: pre beta 2 versions of v3.5 have build number like v3.5.20111.
            // This was removed in beta2
            // We should favor \v3.5 over \v3.5.xxxxx
            // versions previous to 2.0 have .xxxx version numbers.  3.0 and 3.5 do not.
            if (!max.EndsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 1; i < directories.Length; ++i)
                {
                    if (directories[i].EndsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        max = directories[i];
                        break;
                    }
                    else if (String.Compare(directories[i], max, StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        max = directories[i];
                    }
                }
            }

            return max;
        }

        #endregion

        /// <summary>
        /// Determine the 32 bit program files directory, this is used for finding where the reference assemblies live.
        /// </summary>
        internal static string GenerateProgramFiles32()
        {
            // On a 64 bit machine we always want to use the program files x86.  If we are running as a 64 bit process then this variable will be set correctly
            // If we are on a 32 bit machine or running as a 32 bit process then this variable will be null and the programFiles variable will be correct.
            string programFilesX86 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (String.IsNullOrEmpty(programFilesX86))
            {
                // 32 bit box
                programFilesX86 = programFiles;
            }

            return programFilesX86;
        }

        /// <summary>
        /// Generate the path to the program files reference assembly location by taking in the program files special folder and then
        /// using that path to generate the path to the reference assemblies location.
        /// </summary>
        internal static string GenerateProgramFilesReferenceAssemblyRoot()
        {
            string combinedPath = Path.Combine(programFiles32, "Reference Assemblies\\Microsoft\\Framework");
            return Path.GetFullPath(combinedPath);
        }
    }
}
