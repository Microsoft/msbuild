using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Build.Shared;
using Shouldly;
using Xunit;

namespace Microsoft.Build.Engine.UnitTests
{
    public class BuildEnvironmentHelper_Tests
    {
#if USE_MSBUILD_DLL_EXTN
        private const string MSBuildExeName = "MSBuild.dll";
#else
        private const string MSBuildExeName = "MSBuild.exe";
#endif

        [Fact]
        public void GetExecutablePath()
        {
            var msbuildPath = Path.GetDirectoryName(FileUtilities.ExecutingAssemblyPath);
            string expectedMSBuildPath = Path.Combine(msbuildPath, MSBuildExeName).ToLowerInvariant();

            string configFilePath = BuildEnvironmentHelper.Instance.CurrentMSBuildConfigurationFile.ToLowerInvariant();
            string toolsDirectoryPath = BuildEnvironmentHelper.Instance.CurrentMSBuildToolsDirectory.ToLowerInvariant();
            string actualMSBuildPath = BuildEnvironmentHelper.Instance.CurrentMSBuildExePath.ToLowerInvariant();

            configFilePath.ShouldBe($"{actualMSBuildPath}.config");
            actualMSBuildPath.ShouldBe(expectedMSBuildPath);
            Path.GetDirectoryName(expectedMSBuildPath).ShouldBe(toolsDirectoryPath);
            BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.Standalone);
        }

        [Fact]
        public void FindBuildEnvironmentByEnvironmentVariable()
        {
            using (var env = new EmptyStandaloneEnviroment(MSBuildExeName))
            {
                var path = env.BuildDirectory;
                var msBuildPath = Path.Combine(path, MSBuildExeName);
                var msBuildConfig = Path.Combine(path, $"{MSBuildExeName}.config");

                env.WithEnvironment("MSBUILD_EXE_PATH", env.MSBuildExePath);
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.CurrentMSBuildToolsDirectory.ShouldBe(path);
                BuildEnvironmentHelper.Instance.CurrentMSBuildExePath.ShouldBe(msBuildPath);
                BuildEnvironmentHelper.Instance.CurrentMSBuildConfigurationFile.ShouldBe(msBuildConfig);
                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBeNull();
                BuildEnvironmentHelper.Instance.RunningTests.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.Standalone);
            }
        }

        /// <summary>
        /// If MSBUILD_EXE_PATH is explicitly set, we should detect it as a VisualStudio instance even in older scenarios
        /// (for example when the install path is under 15.0).
        /// </summary>
        /// <param name="is64BitMSbuild">When true, run the test pointing to amd64 msbuild.exe.</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void FindVisualStudioEnvironmentByEnvironmentVariable(bool is64BitMSbuild)
        {
            using (var env = new EmptyVSEnviroment())
            {
                var msbuildBinDirectory = is64BitMSbuild
                    ? Path.Combine(env.BuildDirectory, "amd64")
                    : env.BuildDirectory;

                var msBuildPath = Path.Combine(msbuildBinDirectory, MSBuildExeName);
                var msBuildConfig = Path.Combine(msbuildBinDirectory, $"{MSBuildExeName}.config");
                var vsMSBuildDirectory = Path.Combine(env.TempFolderRoot, "MSBuild");

                env.WithEnvironment("MSBUILD_EXE_PATH", msBuildPath);
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
                BuildEnvironmentHelper.Instance.MSBuildExtensionsPath.ShouldBe(vsMSBuildDirectory);
                BuildEnvironmentHelper.Instance.CurrentMSBuildToolsDirectory.ShouldBe(msbuildBinDirectory);
                BuildEnvironmentHelper.Instance.CurrentMSBuildExePath.ShouldBe(msBuildPath);
                BuildEnvironmentHelper.Instance.CurrentMSBuildConfigurationFile.ShouldBe(msBuildConfig);
                // This code is not running inside the Visual Studio devenv.exe process
                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                BuildEnvironmentHelper.Instance.RunningTests.ShouldBeFalse();
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void FindOlderVisualStudioEnvironmentByEnvironmentVariable(bool is64BitMSbuild)
        {
            using (var env = new EmptyVSEnviroment("15.0"))
            {
                var msbuildBinDirectory = is64BitMSbuild
                    ? Path.Combine(env.BuildDirectory, "amd64")
                    : env.BuildDirectory;

                var msBuildPath = Path.Combine(msbuildBinDirectory, MSBuildExeName);
                var msBuildConfig = Path.Combine(msbuildBinDirectory, $"{MSBuildExeName}.config");
                var vsMSBuildDirectory = Path.Combine(env.TempFolderRoot, "MSBuild");

                env.WithEnvironment("MSBUILD_EXE_PATH", msBuildPath);
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
                BuildEnvironmentHelper.Instance.MSBuildExtensionsPath.ShouldBe(vsMSBuildDirectory);
                BuildEnvironmentHelper.Instance.CurrentMSBuildToolsDirectory.ShouldBe(msbuildBinDirectory);
                BuildEnvironmentHelper.Instance.CurrentMSBuildExePath.ShouldBe(msBuildPath);
                BuildEnvironmentHelper.Instance.CurrentMSBuildConfigurationFile.ShouldBe(msBuildConfig);
                // This code is not running inside the Visual Studio devenv.exe process
                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                BuildEnvironmentHelper.Instance.RunningTests.ShouldBeFalse();
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void FindBuildEnvironmentFromCommandLineVisualStudio()
        {
            using (var env = new EmptyVSEnviroment())
            {
                // All we know about is path to msbuild.exe as the command-line arg[0]
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => env.MSBuildExePath, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory64);
                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.RunningTests.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        [Fact]
        public void FindBuildEnvironmentFromCommandLineStandalone()
        {
            // Path will not be under a Visual Studio install like path.
            using (var env = new EmptyStandaloneEnviroment(MSBuildExeName))
            {
                // All we know about is path to msbuild.exe as the command-line arg[0]
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => env.MSBuildExePath, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.RunningTests.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.Standalone);
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void FindBuildEnvironmentFromRunningProcessVisualStudio()
        {
            using (var env = new EmptyVSEnviroment())
            {
                // All we know about is path to msbuild.exe as the current process
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, () => env.MSBuildExePath, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory64);
                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.RunningTests.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        [Fact]
        public void FindBuildEnvironmentFromRunningProcessStandalone()
        {
            // Path will not be under a Visual Studio install like path.
            using (var env = new EmptyStandaloneEnviroment(MSBuildExeName))
            {
                // All we know about is path to msbuild.exe as the current process
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, () => env.MSBuildExePath, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.RunningTests.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.Standalone);
            }
        }

        [Fact]
        public void FindBuildEnvironmentFromExecutingAssemblyAsDll()
        {
            // Ensure the correct file is found (.dll not .exe)
            using (var env = new EmptyStandaloneEnviroment("MSBuild.dll"))
            {
                // All we know about is path to msbuild.exe as the current process
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, () => env.MSBuildExePath, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.RunningTests.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.Standalone);
            }
        }

        [Fact]
        public void FindBuildEnvironmentFromAppContextDirectory()
        {
            using (var env = new EmptyStandaloneEnviroment(MSBuildExeName))
            {
                // Only the app base directory will be available
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, ReturnNull, () => env.BuildDirectory, env.VsInstanceMock, env.EnvironmentMock, () => false);

                // Make sure we get the right MSBuild entry point. On .NET Core this will be MSBuild.dll, otherwise MSBuild.exe
                Path.GetFileName(BuildEnvironmentHelper.Instance.CurrentMSBuildExePath).ShouldBe(MSBuildExeName);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.RunningTests.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.Standalone);
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void FindBuildEnvironmentFromVisualStudioRoot()
        {
            using (var env = new EmptyVSEnviroment())
            {
                // All we know about is path to DevEnv.exe
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => env.DevEnvPath, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory64);
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeTrue();
                BuildEnvironmentHelper.Instance.RunningTests.ShouldBeFalse();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        [Theory]
        [InlineData(MSBuildConstants.CurrentVisualStudioVersion, true)]
        [InlineData("15.0", false)]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentDetectsVisualStudioByEnvironment(string visualStudioVersion, bool shouldBeValid)
        {
            using (var env = new EmptyVSEnviroment())
            {
                env.WithEnvironment("VSINSTALLDIR", env.TempFolderRoot);
                env.WithEnvironment("VisualStudioVersion", visualStudioVersion);
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                if (shouldBeValid)
                {
                    BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                    BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
                }
                else
                {
                    // Ensure that no VS instance was found (older versions that shouldn't be discovered).
                    BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBeNull();
                    BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.None);
                }
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentDetectsVisualStudioByMSBuildProcess()
        {
            using (var env = new EmptyVSEnviroment())
            {
                // We only know we're in msbuild.exe, we should still be able to attempt to find Visual Studio
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => env.MSBuildExePath, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentDetectsVisualStudioByMSBuildProcessAmd64()
        {
            using (var env = new EmptyVSEnviroment())
            {
                // We only know we're in amd64\msbuild.exe, we should still be able to attempt to find Visual Studio
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => env.MSBuildExePath64, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        [Theory]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        [InlineData("17.0", true)]
        [InlineData("17.3", true)]
        [InlineData("16.0", false)]
        public void BuildEnvironmentDetectsVisualStudioFromSetupInstance(string visualStudioVersion, bool shouldBeValid)
        {
            using (var env = new EmptyVSEnviroment())
            {
                env.WithVsInstance(new VisualStudioInstance("Invalid path", @"c:\_doesnotexist", new Version(visualStudioVersion)));
                env.WithVsInstance(new VisualStudioInstance("VS", env.TempFolderRoot, new Version(visualStudioVersion)));

                // This test has no context to find MSBuild other than Visual Studio root.
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                if (shouldBeValid)
                {
                    BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                    BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
                }
                else
                {
                    // Ensure that no VS instance was found (older versions that shouldn't be discovered).
                    BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBeNull();
                    BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.None);
                }
            }
        }

        [Fact]
        public void BuildEnvironmentVisualStudioNotFoundWhenVersionMismatch()
        {
            using (var env = new EmptyVSEnviroment())
            {
                env.WithVsInstance(new VisualStudioInstance("Invalid path", @"c:\_doesnotexist", new Version("15.0")));
                env.WithVsInstance(new VisualStudioInstance("VS", env.TempFolderRoot, new Version("14.0")));

                // This test has no context to find MSBuild other than Visual Studio root.
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBeNull();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.None);
            }
        }

        [Fact]
        public void BuildEnvironmentDetectsRunningTests()
        {
            BuildEnvironmentHelper.Instance.RunningTests.ShouldBeTrue();
            BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeFalse();
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentDetectsVisualStudioByProcessName()
        {
            using (var env = new EmptyVSEnviroment())
            {
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => env.DevEnvPath, () => env.MSBuildExePath, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeTrue();
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentDetectsVisualStudioByBlendProcess()
        {
            using (var env = new EmptyVSEnviroment())
            {
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => env.BlendPath, () => env.MSBuildExePath, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.RunningInVisualStudio.ShouldBeTrue();
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentFindsAmd64()
        {
            using (var env = new EmptyVSEnviroment())
            {
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => env.DevEnvPath, ReturnNull,
                    ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory64);
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentFindsAmd64RunningInAmd64NoVS()
        {
            using (var env = new EmptyStandaloneEnviroment(MSBuildExeName, writeFakeFiles:true, includeAmd64Folder:true))
            {
                var msBuild64Exe = Path.Combine(env.BuildDirectory, "amd64", MSBuildExeName);
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => msBuild64Exe, ReturnNull, ReturnNull,
                    env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(Path.Combine(env.BuildDirectory, "amd64"));
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBeNull();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.Standalone);
            }
        }

        [Fact]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentFindsAmd64NoVS()
        {
            using (var env = new EmptyStandaloneEnviroment(MSBuildExeName, writeFakeFiles: true, includeAmd64Folder: true))
            {
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => env.MSBuildExePath, ReturnNull,
                    ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(Path.Combine(env.BuildDirectory, "amd64"));
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.Standalone);
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentFindsAmd64RunningInAmd64()
        {
            using (var env = new EmptyVSEnviroment())
            {
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => env.MSBuildExePath64, ReturnNull, ReturnNull, env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory64);
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        [Fact]
        public void BuildEnvironmentNoneWhenNotAvailable()
        {
            using (var env = new EmptyStandaloneEnviroment(MSBuildExeName))
            {
                var entryProcess = Path.Combine(Path.GetTempPath(), "foo.exe");
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(() => entryProcess, ReturnNull, ReturnNull,
                    env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.CurrentMSBuildExePath.ShouldBe(entryProcess);
                BuildEnvironmentHelper.Instance.CurrentMSBuildToolsDirectory.ShouldBe(Path.GetDirectoryName(entryProcess));
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBeNull();
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.None);
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentVSFromMSBuildAssembly()
        {
            using (var env = new EmptyVSEnviroment())
            {
                var msBuildAssembly = Path.Combine(env.BuildDirectory, "Microsoft.Build.dll");

                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, () => msBuildAssembly, ReturnNull,
                    env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory64);
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        [Fact]
        [SkipOnTargetFramework(TargetFrameworkMonikers.Netcoreapp, "No Visual Studio install for netcore")]
        [PlatformSpecific(TestPlatforms.Windows)]
        public void BuildEnvironmentVSFromMSBuildAssemblyAmd64()
        {
            using (var env = new EmptyVSEnviroment())
            {
                var msBuildAssembly = Path.Combine(env.BuildDirectory64, "Microsoft.Build.dll");

                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(ReturnNull, () => msBuildAssembly, ReturnNull,
                    env.VsInstanceMock, env.EnvironmentMock, () => false);

                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory32.ShouldBe(env.BuildDirectory);
                BuildEnvironmentHelper.Instance.MSBuildToolsDirectory64.ShouldBe(env.BuildDirectory64);
                BuildEnvironmentHelper.Instance.VisualStudioInstallRootDirectory.ShouldBe(env.TempFolderRoot);
                BuildEnvironmentHelper.Instance.Mode.ShouldBe(BuildEnvironmentMode.VisualStudio);
            }
        }

        private static string ReturnNull()
        {
            return null;
        }

        private class EmptyVSEnviroment : EmptyStandaloneEnviroment
        {
            public string DevEnvPath { get; }

            public string BlendPath { get; }

            public string BuildDirectory64 { get; }

            public string MSBuildExePath64 => Path.Combine(BuildDirectory64, MSBuildExeName);

            public EmptyVSEnviroment(string toolsVersion = MSBuildConstants.CurrentToolsVersion) : base("MSBuild.exe", false)
            {
                try
                {
                    var files = new[] { "msbuild.exe", "msbuild.exe.config" };
                    BuildDirectory = Path.Combine(TempFolderRoot, "MSBuild", toolsVersion, "Bin");
                    BuildDirectory64 = Path.Combine(BuildDirectory, "amd64");
                    DevEnvPath = Path.Combine(TempFolderRoot, "Common7", "IDE", "devenv.exe");
                    BlendPath = Path.Combine(TempFolderRoot, "Common7", "IDE", "blend.exe");

                    Directory.CreateDirectory(BuildDirectory);
                    foreach (var file in files)
                    {
                        File.WriteAllText(Path.Combine(BuildDirectory, file), string.Empty);
                    }

                    Directory.CreateDirectory(BuildDirectory64);
                    foreach (var file in files)
                    {
                        File.WriteAllText(Path.Combine(BuildDirectory64, file), string.Empty);
                    }

                    Directory.CreateDirectory(Path.Combine(TempFolderRoot, "Common7", "IDE"));
                    File.WriteAllText(DevEnvPath, string.Empty);
                }
                catch (Exception)
                {
                    FileUtilities.DeleteDirectoryNoThrow(BuildDirectory, true);
                    throw;
                }
            }
        }

        private class EmptyStandaloneEnviroment : IDisposable
        {
            public string TempFolderRoot { get; }

            public string BuildDirectory { get; protected set; }

            public string MSBuildExeName { get; }

            public string MSBuildExePath => Path.Combine(BuildDirectory, MSBuildExeName);

            private readonly Dictionary<string, string> _mockEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            private readonly List<VisualStudioInstance> _mockInstances = new List<VisualStudioInstance>();

            public EmptyStandaloneEnviroment(string msBuildExeName, bool writeFakeFiles = true, bool includeAmd64Folder = false)
            {
                try
                {
                    MSBuildExeName = msBuildExeName;
                    TempFolderRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                    BuildDirectory = Path.Combine(TempFolderRoot, "MSBuild");

                    Directory.CreateDirectory(BuildDirectory);
                    if (writeFakeFiles)
                    {
                        File.WriteAllText(MSBuildExePath, string.Empty);
                        File.WriteAllText($"{MSBuildExePath}.config", string.Empty);

                        if (includeAmd64Folder)
                        {
                            Directory.CreateDirectory(Path.Combine(BuildDirectory, "amd64"));
                            File.WriteAllText(Path.Combine(BuildDirectory, "amd64", msBuildExeName), string.Empty);
                            File.WriteAllText(Path.Combine(BuildDirectory, "amd64", $"{MSBuildExePath}.config"), string.Empty);
                        }
                    }
                }
                catch (Exception)
                {
                    FileUtilities.DeleteDirectoryNoThrow(BuildDirectory, true);
                    throw;
                }
            }

            public void WithEnvironment(string variable, string value)
            {
                _mockEnvironment.Add(variable, value);
            }

            public void WithVsInstance(VisualStudioInstance instance)
            {
                _mockInstances.Add(instance);
            }

            public string EnvironmentMock(string variable)
            {
                return _mockEnvironment.ContainsKey(variable) ? _mockEnvironment[variable] : null;
            }

            public IEnumerable<VisualStudioInstance> VsInstanceMock()
            {
                return _mockInstances;
            }

            public void Dispose()
            {
                FileUtilities.DeleteDirectoryNoThrow(TempFolderRoot, true);
                BuildEnvironmentHelper.ResetInstance_ForUnitTestsOnly(runningTests: () => true);
            }
        }
    }
}
