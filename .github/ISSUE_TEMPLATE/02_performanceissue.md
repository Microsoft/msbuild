---
name: 📉 Performance Issue
about: Report a performance issue or regression.
title: ''
labels: performance, needs-triage
---

<!-- This is a template that helps us provide quicker feedback. Please use any relevant sections and delete anything you don't need. -->

### Issue Description
<!--
* Please include a clear and concise description of the problem.
-->

### Steps to Reproduce
<!--
Include as much of the following if possible:

* A minimal sample project that reproduces the issue.
* Your zipped project.
* IDE / CLI steps to create the project and reproduce the behaviour.
* Your command line invocation
-->

### Data
<!--
* Please include all information you've gathered about this performance issue. This includes:
    - Timing
    - Measurements
    - ETW Traces
    - Call stacks
    - Dumps
    - etc.
* If possible please include text as text rather than images (so it shows up in searches).
* If applicable please include before and after measurements.
-->

### Analysis
<!--
* If you have an idea where the problem might lie, let us know that here.
* Please include any pointers to code, relevant changes, or related issues you know of.
-->

### Versions & Configurations
<!--
* In a Visual Studio developer command prompt, run `msbuild -version` and paste the output here.
* If applicable, include the version of the tool that invokes MSBuild (Visual Studio, dotnet CLI, etc):

Post any other relevant configuration settings here.
* OS, architecture, etc.
-->

### Regression?
<!--
* Is this a regression from a previous build/release?
* Please provide details on:
*   What version of MSBuild or VS were you using before the regression?
*   What version of MSBuild or VS are you on now that you discovered the regression?
-->

### Attach a binlog
<!--
* If providing us a project that reproduces the issue proves difficult, consider including a binlog.
* Click [here](https://aka.ms/msbuild/binlog) for details on sharing binary logs.
* Click [here](https://github.com/microsoft/msbuild/blob/master/documentation/wiki/Binary-Log.md) for more information on binary logs.
    NOTE: Binlogs can contain sensitive information. Don't attach anything you don't want to be public.

*   To view the contents of the binlogs yourself, you may wish to use a tool like https://msbuildlog.com/.
-->