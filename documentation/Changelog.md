# MSBuild Changelog

## MSBuild 16.11.0

This version of MSBuild will ship with Visual Studio 2019 version 16.11.0 and .NET SDK 5.0.400.

### What's new

### Detailed release notes

#### Added

* Additional properties documented and available for completion in Visual Studio (#6500, #6530).
* The `SignFile` task is now available in MSBuild on .NET 5.0 (#6509). Thanks, @Zastai!
* New version properties `MSBuildFileVersion` (4-part, matches file version) and `MSBuildSemanticVersion` (matches package versions) are now available for use (#6534).
#### Changed

* When using the experimental cache API, schedule proxy builds to the in-proc node for performance (#6386).
* Experimental cache queries are now executed in parallel (#6468).
* The ETW events generated in `ResolveAssemblyReference` now include an approximation of the "size" of the RAR request (#6410).

#### Fixed

* Fixed memory leak in `ProjectRootElement.Reload` (#6457).
* Added locking to avoid race conditions in `BuildManager` (#6412).
* Allow `ResolveAssemblyReferences` precomputed cache files to be in read-only locations (#6393).
* 64-bit `al.exe` is used when targeting 64-bit architectures (for real this time) (#6484).
* Builds with `ProduceOnlyReferenceAssembly` no longer expect debug symbols to be produced (#6511). Thanks, @Zastai!

#### Infrastructure

* Use a packaged C# compiler to avoid changes in reference assembly generation caused by compiler changes (#6431).
* Use more resilient test-result upload patterns (#6489).
* Conditional compilation for .NET Core within our repo now includes new .NET 5.0+ runtimes (#6538).
* Switched to OneLocBuild for localization PRs (#6561).
* Moved to latest Ubuntu image for PR test legs (#6573).

#### Documentation

## MSBuild 16.10.2

This version of MSBuild shipped with Visual Studio 2019 version 16.10.2 and will ship with .NET SDK 5.0.302.

#### Fixed

* Fixed a regression in the `MakeRelative` property function that dropped trailing slashes (#6513). Thanks, @dsparkplug and @pmisik!
* Fixed a regression in glob matching where files without extensions were erroneously not matched (#6531).
* Fixed a change in logging that caused crashes in Azure DevOps loggers (#6520).

## MSBuild 16.10.1

This version of MSBuild shipped with Visual Studio 2019 version 16.10.1 and .NET SDK 5.0.301.

#### Fixed

* Restore support for building individual project(s) within solutions by specifying `-t:Project` (#6465).

## MSBuild 16.9.2

This version of MSBuild shipped with Visual Studio 2019 version 16.9.7.

#### Fixed

* Fixed MSB0001 error when building large solutions (#6437).

## MSBuild 16.10.0

This version of MSBuild shipped with Visual Studio 2019 version 16.10.0 and .NET SDK 5.0.300.

### What's new

* MSBuild now targets .NET 5.0 and .NET Framework 4.7.2.
* MSBuild is faster and uses less memory.
* Binary logs are smaller and have less performance overhead.
* Tasks can now opt into resource management to improve parallelism in large builds.
* It's now possible to optionally embed arbitrary files in a binary log.

### Detailed release notes

#### Added

* Projects can now specify `AdditionalTargetFrameworkInfoProperty` items to indicate that referencing projects should get those properties exposed as `AdditionalPropertiesFromProject` metadata on resolved reference items. (#5994).
* The `Unzip` task now accepts `Include` and `Exclude` arguments to filter what is extracted from the zip file (#6018). Thanks, @IvanLieckens!
* The `-graph:noBuild` command line argument can be used to validate that a graph is buildable without actually building it (#6016).
* `TaskParameterEventArgs` allow logging task parameters and values in a compact, structured way (#6155). Thanks, @KirillOsenkov!
* ClickOnce publish now supports Ready To Run (#6244).
* .NET 5.0 applications may now specify a toolset configuration file (#6220).
* `ResolveAssemblyReferences` can now consume information about assemblies distributed as part of the SDK (#6017).
* Allow constructing a `ProjectInstance` from a `ProjectLink` (#6262).
* Introduce cross-process resource management for tasks (#5859).
* `ProjectEvaluationFinished` now has fields for properties and items (#6287). Thanks, @KirillOsenkov!
* `WriteCodeFragment` can now write assembly attributes of specified types, and infers some common types (#6285). Thanks, @reduckted!
* The `-detailedSummary` option now accepts a boolean argument, preventing dumping details to the console logger when building with `-bl -ds:false` (#6338). Thanks, @KirillOsenkov!
* Binary logs now include files listed in the item `EmbedInBinlog` as well as MSBuild projects (#6339). Thanks, @KirillOsenkov!
* The `FindInvalidProjectReferences` task is now available in .NET Core/5.0+ scenarios (#6365).

#### Changed

* String deduplication is now much more sophisticated, reducing memory usage (#5663).
* Refactoring and performance improvements in `ResolveAssemblyReferences` (#5929, #6094).
* Binary logs now store strings only once, dramatically reducing log size (#6017, #6326). Thanks, @KirillOsenkov!
* Refactoring and code cleanup (#6120, #6159, #6158, #6282). Thanks, @Nirmal4G!
* `Span<T>`-based methods are used on .NET Framework MSBuild as well as .NET 5.0 (#6130).
* Improved `MSB4064` error to include information about the loaded task that didn't have the argument (#5945). Thanks, @BartoszKlonowski!
* Performance improvements in inter-node communication (#6023). Thanks, @KirillOsenkov!
* Performance improvements in matching items based on metadata (#6035), property expansion (#6128), glob evaluation (#6151), enumerating files (#6227).
* When evaluated with `IgnoreInvalidImports`, _empty_ imports are also allowed (#6222).
* `Log.HasLoggedError` now respects `MSBuildWarningsAsErrors` (#6174).
* `TargetPath` metadata is now respected on items that copy to output directories, and takes precedence over `Link` (#6237).
* The `Restore` operation now fails when SDKs are unresolvable (#6312).
* `MSBuild.exe.config` now has explicit binding redirects for all assemblies in the MSBuild VSIX (#6334).

#### Fixed

* Inconsistencies between `XamlPreCompile` and the `CoreCompile` C## compiler invocation (#6093). Thanks, @huoyaoyuan!
* Wait for child nodes to exit before exiting the entry-point node in VSTest scenarios (#6053). Thanks, @tmds!
* Fix bad plugin EndBuild exception handling during graph builds (#6110).
* Allow specifying `UseUtf8Encoding` in `ToolTask`s (#6188).
* Failures on big-endian systems (#6204). Thanks, @uweigand!
* 64-bit `al.exe` is used when targeting 64-bit architectures (#6207).
* Improved error messages when encountering a `BadImageReferenceException` in `ResolveAssemblyReferences` (#6240, #6270). Thanks, @FiniteReality!
* Escape special characters in `Exec`’s generated batch files, allowing builds as users with some special characters in their Windows username (#6233).
* Permit comments and trailing commas in solution filter files (#6346).
* Exceptions thrown from experimental cache plugins are now handled and logged better (#6345, #6368).
* Source generators with configuration files can now be used in XamlPreCompile (#6438).
* Large builds no longer crash with an exception in `LogProjectStarted` (#6437).

#### Infrastructure

* Update to Arcade 5.0 and .NET 5.0 (#5836).
* The primary development branch is now named `main`.
* Test robustness improvements (#6055, #6336, #6337, #6332). Thanks, @tmds and @KirillOsenkov!
* Remove unnecessary NuGet package references (#6036). Thanks, @teo-tsirpanis!
* Correctly mark .NET Framework 3.5 reference assembly package dependency as private (#6214).
* Our own builds opt into text-based performance logging (#6274).
* Update to Arcade publishing v3 (#6349).
* Use OneLocBuild localization process (#6378).

#### Documentation

* Updates to static graph documentation (#6043).
* Short doc on the threading model (#6042).
* Update help text to indicate that `--` is a valid argument prefix (#6205). Thanks, @BartoszKlonowski!
* API documentation improvements (#6246, #6284).
* Details about interactions with the Global Assembly Cache (#6173).

## MSBuild 16.9.0.2116703

⚠ This release should have been versioned `16.9.1` but was erroneously released as 16.9.0.

This version of MSBuild shipped with Visual Studio 2019 version 16.9.3.

#### Fixed

* Restore support for building solutions with web site projects (#6238).

## MSBuild 16.9.0

This version of MSBuild shipped with Visual Studio 2019 version 16.9.0 and .NET SDK 5.0.200.

### What's new

* `MSB3277` warnings now include information about the assembly identities involved, instead of saying to rerun under higher verbosity.
* It's now possible to opt out of culture-name detection of `EmbeddedResource`s, for instance to have a resource named `a.cs.template`.
* Common targets now support `$(BaseOutputPath)`, with the default value `bin`.
* Item `Update`s are no longer case-sensitive, fixing a regression in MSBuild 16.6 (#5888).
* `ParentBuildEventContext` now includes a parent `MSBuild` task if relevant, enabling proper nesting in GUI viewers.
* Builds that fail because a warning was elevated to an error now report overall failure in the `MSBuild.exe` exit code.

### Detailed release notes

#### Added

* The `MSB4006` error has been enhanced to describe the cycle when possible (#5711). Thanks, @haiyuzhu!.
* More information is logged under `MSBUILDDEBUGCOMM` (#5759).
* The command line parser now accepts arguments with double hyphens (`--argument`) as well as single hyphens (`-argument`) and forward slashes (`/argument`) (#5786). Thanks, @BartoszKlonowski!
* MSBuild now participates in the .NET CLI text performance log system on an opt-in basis (#5861).
* Common targets now support `$(BaseOutputPath)`, with the default value `bin` (#5238). Thanks, @Nirmal4G!
* `Microsoft.Build.Exceptions.CircularDependencyException` is now public (#5988). Thanks, @tflynt91!
* `EvaluationId` is now preserved in the `ProjectStarted` event, allowing disambiguating related project start events (#5997). Thanks, @KirillOsenkov!
* The `ResolveAssemblyReference` task can now optionally emit items describing unresolved assembly conflicts (#5990).
* Experimental `ProjectCache` API to enable higher-order build systems (#5936).

#### Changed

* Warnings suppressed via `$(NoWarn)` (which formerly applied only to targets that opted in like the C## compiler) are now treated as `$(MSBuildWarningsAsMessages)` (#5671).
* Warnings elevated via `$(WarningsAsErrors )` (which formerly applied only to targets that opted in like the C## compiler) are now treated as `$(MSBuildWarningsAsErrors)` (#5774).
* Improved error message when using an old .NET (Core) SDK and targeting .NET 5.0 (#5826).
* Trailing spaces in property expressions inside conditionals now emit an error instead of silently expanding to the empty string (#5672, #5868). Thanks, @mfkl!
* `MSB3277` warnings now include information about the assembly identities involved, instead of saying to rerun under higher verbosity (#5798).
* `MSB5009` errors now indicate the project in the solution that is causing the nesting error (#5835). Thanks, @BartoszKlonowski!
* Avoid spawning a process to determine processor architecture (#5897). Thanks, @tmds!
* It's now possible to opt out of culture-name detection of `EmbeddedResource`s, for instance to have a resource named `a.cs.template` (#5824).
* `ProjectInSolution.AbsolutePath` returns a normalized full path when possible (#5949).
* Evaluation pass-stop events now include information about the "size" (number of properties/items/imports) of the project (#5978). Thanks, @arkalyanms!

#### Fixed

* `AllowFailureWithoutError` now does what it said it would do (#5743).
* The solution parser now no longer skips projects that are missing an EndProject line (#5808). Thanks, @BartoszKlonowski!
* `ProjectReference`s to `.vcxproj` projects from multi-targeted .NET projects no longer overbuild (#5838).
* Removed unused `InternalsVisibleTo` to obsolete test assemblies (#5914). Thanks, @SingleAccretion!
* Respect conditions when removing all items from an existing list at evaluation time (#5927).
* Common targets should no longer break if the environment variable `OS` is set (#5916).
* Some internal errors will now be reported as errors instead of hanging the build (#5917).
* Item `Update`s are no longer case-sensitive, fixing a regression in MSBuild 16.6 (#5888).
* Use lazy string formatting in more places (#5924).
* Redundant references to MSBuild assemblies no longer fail in 64 MSBuild inline tasks (#5975).
* The `Exec` task will now no longer emit the expanded `Command` to the log on failure (#5962). Thanks, @tmds!
* Tasks generated with `RoslynCodeTaskFactory` now no longer rebuild for every use, even with identical inputs (#5988). Thanks, @KirillOsenkov!
* `ParentBuildEventContext` now includes a parent `MSBuild` task if relevant (#5966). Thanks, @KirillOsenkov!
* Builds that fail because a warning was elevated to an error now report overall failure in the `MSBuild.exe` exit code (#6006).
* Performance of projects with large numbers of consecutive item updates without wildcards improved (#5853).
* Performance improvements in `ResolveAssemblyReferences` (#5973).
* PackageReferences that are marked as development dependencies are removed from the ClickOnce manifest (#6037).
* Stop overfiltering .NET Core assemblies from the ClickOnce manifest (#6080).

#### Infrastructure

* The MSBuild codebase now warns for unused `using` statements (#5761).
* The MSBuild codebase is now indexed for [Rich Code Navigation](https://visualstudio.microsoft.com/services/rich-code-navigation/) on CI build (#5790). Thanks, @jepetty!
* The 64-bit bootstrap directory is more usable (#5825).
* Test robustness improvements (#5827, #5944, #5995).
* Make non-shipping NuGet packages compliant (#5823).
* Use [Darc](https://github.com/dotnet/arcade/blob/main/Documentation/Darc.md) to keep bootstrap dependencies up to date (#5909).
* Replace MSBuild.Dev.sln and MSBuild.SourceBuild.sln with solution filters (#6010).
* Minimize and update NuGet feeds (#6019, #6136).

#### Documentation

* Improvements to MSBuild-internal Change Wave docs (#5770, #5851).
* High-level documentation for static graph functionality added (#5741).
* Instructions on testing private bits (#5818, #5831).
* XML doc comments updated to match public-ready API docs pages (#6028). Thanks, @ghogen!
