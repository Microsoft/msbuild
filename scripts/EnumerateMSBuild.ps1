Function Write-Log
{
Param(
    [parameter(Mandatory=$false)]
    [string]$logString,
    [parameter(Mandatory=$false)]
    [bool]$LogToConsole = $True)

    if ($LogToConsole -eq $true)
    {
        Write-Host $logString
    }

    Add-Content $logFile -value $logString -Encoding Unicode
}

# Set log file (and get the full path) and delete if it exists.
$logFile = "msbuild_versions.txt"
If((Test-Path -Path $logFile))
{
    Remove-Item -Path $logFile
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
Write-Log "Using vswhere from $vswhere"

$vsInstances = & $vswhere -prerelease -all -format json | ConvertFrom-Json
$vsInstances | Out-File $logFile -Width 1000 -Append unicode

foreach ($instance in $vsInstances)
{
    $instanceName = $instance.installationName
    $instancePath = $instance.installationPath
    Write-Log "********************" -LogToConsole $False
    Write-Log "Found VS Instance: $instanceName"

    # Look at each dll/exe in the MSBuild bin folder and get their ProductVersion
    ls -File -Recurse -Include ('*.dll', '*.exe') -Path "$instancePath\MSBuild\15.0\Bin" | % VersionInfo | Format-Table -AutoSize InternalName, ProductVersion, FileName | Out-File $logFile -Width 1000 -Append unicode
    ls -File -Recurse -Include ('*.dll', '*.exe') -Path "$instancePath\MSBuild\Current\Bin" | % VersionInfo | Format-Table -AutoSize InternalName, ProductVersion, FileName | Out-File $logFile -Width 1000 -Append unicode
    Write-Log "********************" -LogToConsole $False
}

# Check in Program Files (x86)\MSBuild for old versions.
Write-Log
Write-Log "********************" -LogToConsole $False
$legacyPath = ${env:ProgramFiles(x86)} + "\MSBuild\"
Write-Log "Looking for legacy MSBuild versions: $legacyPath"
Get-ChildItem -File -Path "$legacyPath" -Recurse "Microsoft.Build*.dll" | % VersionInfo | Format-Table -AutoSize InternalName, ProductVersion, FileName | Out-File $logFile -Width 1000 -Append unicode
Write-Log "********************" -LogToConsole $False

# Check in the .NET 4.5+ GAC
Write-Log
Write-Log "********************" -LogToConsole $False
$gacPath = ${env:windir} + "\Microsoft.NET\assembly"
Write-Log "Looking for MSBuild in the GAC: $gacPath"
Get-ChildItem -File -Path "$gacPath" -Recurse "Microsoft.Build*.dll" -Exclude "*.ni.dll" | % VersionInfo | Format-Table -AutoSize InternalName, ProductVersion, FileName | Out-File $logFile -Width 1000 -Append unicode
Write-Log "********************" -LogToConsole $False

# Just for completeness look in c:\Windows\assembly as well.
Write-Log
Write-Log "********************" -LogToConsole $False
$gacPath = ${env:windir} + "\assembly"
Write-Log "Looking for MSBuild in the GAC: $gacPath"
Get-ChildItem -File -Path "$gacPath" -Recurse "Microsoft.Build*.dll" -Exclude "*.ni.dll" | % VersionInfo | Format-Table -AutoSize InternalName, ProductVersion, FileName | Out-File $logFile -Width 1000 -Append unicode
Write-Log "********************" -LogToConsole $False

# Expand full path for the output message
$logFile = Get-ChildItem -File -Path $logFile

Write-Host
Write-Host "Output saved to $logFile"
