#! /bin/bash

configuration="Debug"
host_type="core"
build_stage1=true
properties=
extra_properties=

SOURCE="${BASH_SOURCE[0]}"
while [ -h "$SOURCE" ]; do # resolve $SOURCE until the file is no longer a symlink
  ScriptRoot="$( cd -P "$( dirname "$SOURCE" )" && pwd )"
  SOURCE="$(readlink "$SOURCE")"
  [[ $SOURCE != /* ]] && SOURCE="$ScriptRoot/$SOURCE" # if $SOURCE was a relative symlink, we need to resolve it relative to the path where the symlink file was located
done
ScriptRoot="$( cd -P "$( dirname "$SOURCE" )" && pwd )"

while [[ $# -gt 0 ]]; do
  lowerI="$(echo $1 | awk '{print tolower($0)}')"
  case "$lowerI" in
    --configuration)
      configuration=$2
      shift 2
      ;;
    --build_stage1)
      build_stage1=$2
      shift 2
      ;;
    --host_type)
      host_type=$2
      shift 2
      ;;
    *)
      properties="$properties $1"
      shift 1
      ;;
  esac
done

RepoRoot="$ScriptRoot/.."
ArtifactsDir="$RepoRoot/artifacts"
Stage1Dir="$RepoRoot/stage1"
PerfLogDir="$ArtifactsDir/log/$configuration/PerformanceLogs"

. "$ScriptRoot/common/tools.sh"
InitializeDotNetCli true

if [ $host_type = "mono" ] ; then
  export _InitializeBuildTool="msbuild"
  export _InitializeBuildToolCommand=""
  export _InitializeBuildToolFramework="net472"

  configuration="$configuration-MONO"
  extra_properties=" /p:DeterministicSourcePaths=false"
fi

if [[ $build_stage1 == true ]];
then
	/bin/bash "$ScriptRoot/common/build.sh" --restore --build --ci --configuration $configuration /p:CreateBootstrap=true $properties $extra_properties || exit $?
fi

bootstrapRoot="$Stage1Dir/bin/bootstrap"

if [ $host_type = "core" ]
then
  _InitializeBuildTool="$_InitializeDotNetCli/dotnet"
  _InitializeBuildToolCommand="$bootstrapRoot/net6.0/MSBuild/MSBuild.dll"
  _InitializeBuildToolFramework="netcoreapp3.1"
elif [ $host_type = "mono" ]
then
  export _InitializeBuildTool="mono"
  export _InitializeBuildToolCommand="$bootstrapRoot/net472/MSBuild/Current/Bin/MSBuild.dll"
  export _InitializeBuildToolFramework="net472"

  # FIXME: remove this once we move to a newer version of Arcade with a fix for $MonoTool
  # https://github.com/dotnet/arcade/commit/f6f14c169ba19cd851120e0d572cd1c5619205b3
  export MonoTool=`which mono`

  extn_path="$bootstrapRoot/net472/MSBuild/Current/Bin/Extensions"
  extra_properties=" /p:MSBuildExtensionsPath=$extn_path /p:MSBuildExtensionsPath32=$extn_path /p:MSBuildExtensionsPath64=$extn_path /p:DeterministicSourcePaths=false"
else
  echo "Unsupported hostType ($host_type)"
  exit 1
fi

mv $ArtifactsDir $Stage1Dir

# Ensure that debug bits fail fast, rather than hanging waiting for a debugger attach.
export MSBUILDDONOTLAUNCHDEBUGGER=true

# Opt into performance logging.
export DOTNET_PERFLOG_DIR=$PerfLogDir

# Prior to 3.0, the Csc task uses this environment variable to decide whether to run
# a CLI host or directly execute the compiler.
export DOTNET_HOST_PATH="$_InitializeDotNetCli/dotnet"

# When using bootstrapped MSBuild:
# - Turn off node reuse (so that bootstrapped MSBuild processes don't stay running and lock files)
# - Do run tests
# - Don't try to create a bootstrap deployment
. "$ScriptRoot/common/build.sh" --restore --build --test --ci --nodereuse false --configuration $configuration /p:CreateBootstrap=false $properties $extra_properties
