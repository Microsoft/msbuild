// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.Build.Collections;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

using ElementLocation = Microsoft.Build.Construction.ElementLocation;
using ProjectItemInstanceFactory = Microsoft.Build.Execution.ProjectItemInstance.TaskItem.ProjectItemInstanceFactory;

namespace Microsoft.Build.BackEnd
{
    using ILoggingService = Microsoft.Build.BackEnd.Logging.ILoggingService;

    // ItemVectorPartitionCollection is designed to contains a set of project items which have possibly undergone transforms.
    // The outer dictionary it usually keyed by item type, so if items originally came from 
    // an expression like @(Foo), the outer dictionary would have a key of "Foo" in it.
    // Under that is a dictionary of expressions to items resulting from the expression.
    // For instance, if items were generated from an expression @(Foo->'%(Filename).obj'), then
    // the inner dictionary would have a key of "@(Foo->'%(Filename).obj')", in which would be 
    // contained a list of the items which were created/transformed using that pattern.    
    using ItemVectorPartitionCollection = Dictionary<string, Dictionary<string, IList<ProjectItemInstance>>>;
    using ItemVectorPartition = Dictionary<string, IList<ProjectItemInstance>>;

    /// <summary>
    /// Enumeration of the results of target dependency analysis.
    /// </summary>
    internal enum DependencyAnalysisResult
    {
        SkipUpToDate,
        SkipNoInputs,
        SkipNoOutputs,
        IncrementalBuild,
        FullBuild
    }

    /// <summary>
    /// This class is used for performing dependency analysis on targets to determine if they should be built/rebuilt/skipped.
    /// </summary>
    internal sealed class TargetUpToDateChecker
    {
        #region Constructors

        /// <summary>
        /// Creates an instance of this class for the given target.
        /// </summary>
        internal TargetUpToDateChecker(ProjectInstance project, ProjectTargetInstance targetToAnalyze, ILoggingService loggingServices, BuildEventContext buildEventContext)
        {
            ErrorUtilities.VerifyThrow(project != null, "Need a project.");
            ErrorUtilities.VerifyThrow(targetToAnalyze != null, "Need a target to analyze.");

            _project = project;
            _targetToAnalyze = targetToAnalyze;
            _targetInputSpecification = targetToAnalyze.Inputs;
            _targetOutputSpecification = targetToAnalyze.Outputs;
            _loggingService = loggingServices;
            _buildEventContext = buildEventContext;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the target to perform dependency analysis on.
        /// </summary>
        /// <value>Target object.</value>
        internal ProjectTargetInstance TargetToAnalyze
        {
            get
            {
                return _targetToAnalyze;
            }
        }

        /// <summary>
        /// Gets the value of the target's "Inputs" attribute.
        /// </summary>
        /// <value>Input specification string (can be empty).</value>
        private string TargetInputSpecification
        {
            get
            {
                ErrorUtilities.VerifyThrow(_targetInputSpecification != null, "targetInputSpecification is null");
                return _targetInputSpecification;
            }
        }

        /// <summary>
        /// Gets the value of the target's "Outputs" attribute.
        /// </summary>
        /// <value>Output specification string (can be empty).</value>
        private string TargetOutputSpecification
        {
            get
            {
                ErrorUtilities.VerifyThrow(_targetOutputSpecification != null, "targetOutputSpecification is null");
                return _targetOutputSpecification;
            }
        }
        #endregion

        #region Methods

        /// <summary>
        /// Compares the target's inputs against its outputs to determine if the target needs to be built/rebuilt/skipped.
        /// </summary>
        /// <remarks>
        /// The collections of changed and up-to-date inputs returned from this method are valid IFF this method decides an
        /// incremental build is needed.
        /// </remarks>
        /// <param name="bucket"></param>
        /// <param name="changedTargetInputs"></param>
        /// <param name="upToDateTargetInputs"></param>
        /// <returns>
        /// DependencyAnalysisResult.SkipUpToDate, if target is up-to-date;
        /// DependencyAnalysisResult.SkipNoInputs, if target has no inputs;
        /// DependencyAnalysisResult.SkipNoOutputs, if target has no outputs;
        /// DependencyAnalysisResult.IncrementalBuild, if only some target outputs are out-of-date;
        /// DependencyAnalysisResult.FullBuild, if target is out-of-date
        /// </returns>
        internal DependencyAnalysisResult PerformDependencyAnalysis
        (
            ItemBucket bucket,
            out ItemDictionary<ProjectItemInstance> changedTargetInputs,
            out ItemDictionary<ProjectItemInstance> upToDateTargetInputs
        )
        {
            // Clear any old dependency analysis logging details
            _dependencyAnalysisDetail.Clear();
            _uniqueTargetInputs.Clear();
            _uniqueTargetOutputs.Clear();

            ProjectErrorUtilities.VerifyThrowInvalidProject((TargetOutputSpecification.Length > 0) || (TargetInputSpecification.Length == 0),
                _targetToAnalyze.InputsLocation, "TargetInputsSpecifiedWithoutOutputs", TargetToAnalyze.Name);

            DependencyAnalysisResult result = DependencyAnalysisResult.SkipUpToDate;

            changedTargetInputs = null;
            upToDateTargetInputs = null;

            if (TargetOutputSpecification.Length == 0)
            {
                // if the target has no output specification, we always build it
                result = DependencyAnalysisResult.FullBuild;
            }
            else
            {
                ParseTargetInputOutputSpecifications(bucket,
                    out ItemVectorPartitionCollection itemVectorsInTargetInputs,
                    out ItemVectorPartitionCollection itemVectorTransformsInTargetInputs,
                    out Dictionary<string, string> discreteItemsInTargetInputs,
                    out ItemVectorPartitionCollection itemVectorsInTargetOutputs,
                    out Dictionary<string, string> discreteItemsInTargetOutputs,
                    out List<string> targetOutputItemSpecs);
                List<string> itemVectorsReferencedOnlyInTargetOutputs = null;

                // if the target has no outputs because the output specification evaluated to empty
                if (targetOutputItemSpecs.Count == 0)
                {
                    result = PerformDependencyAnalysisIfNoOutputs();
                }
                // if there are no discrete output items...
                else if (discreteItemsInTargetOutputs.Count == 0)
                {
                    // try to correlate inputs and outputs by checking:
                    // 1) which item vectors are referenced by both input and output items
                    // 2) which item vectors are referenced only by input items
                    // 3) which item vectors are referenced only by output items
                    // NOTE: two item vector transforms cannot be correlated, even if they reference the same item vector, because
                    // depending on the transform expression, there might be no relation between the results of the transforms; as
                    // a result, input items that are item vector transforms are treated as discrete items
                    DiffHashtables(itemVectorsInTargetInputs, itemVectorsInTargetOutputs,
                        out List<string> itemVectorsReferencedInBothTargetInputsAndOutputs,
                        out List<string> itemVectorsReferencedOnlyInTargetInputs,
                        out itemVectorsReferencedOnlyInTargetOutputs);

                    // if there are no item vectors only referenced by output items...
                    // NOTE: we consider output items that reference item vectors not referenced by any input item, as discrete
                    // items, since we cannot correlate them to any input items
                    if (itemVectorsReferencedOnlyInTargetOutputs.Count == 0)
                    {
                        /*
                         * At this point, we know the following:
                         * 1) the target has outputs
                         * 2) the target has NO discrete outputs
                         * 
                         * This implies:
                         * 1) the target only references vectors (incl. transforms) in its outputs
                         * 2) all vectors referenced in the outputs are also referenced in the inputs
                         * 3) the referenced vectors are not empty
                         * 
                         * We can thus conclude: the target MUST have (non-discrete) inputs
                         * 
                         */
                        ErrorUtilities.VerifyThrow(itemVectorsReferencedInBothTargetInputsAndOutputs.Count > 0, "The target must have inputs.");
                        ErrorUtilities.VerifyThrow(GetItemSpecsFromItemVectors(itemVectorsInTargetInputs).Count > 0, "The target must have inputs.");

                        result = PerformDependencyAnalysisIfDiscreteInputs(itemVectorsInTargetInputs,
                                    itemVectorTransformsInTargetInputs, discreteItemsInTargetInputs, itemVectorsReferencedOnlyInTargetInputs,
                                    targetOutputItemSpecs);

                        if (result != DependencyAnalysisResult.FullBuild)
                        {
                            // once the inputs and outputs have been correlated, we can do a 1-to-1 comparison between each input
                            // and its corresponding output, to discover which inputs have changed, and which are up-to-date...
                            result = PerformDependencyAnalysisIfCorrelatedInputsOutputs(itemVectorsInTargetInputs, itemVectorsInTargetOutputs,
                                itemVectorsReferencedInBothTargetInputsAndOutputs,
                                out changedTargetInputs, out upToDateTargetInputs);
                        }
                    }
                }

                // if there are any discrete items in the target outputs, then we have no obvious correlation to the inputs they
                // depend on, since any input can contribute to a discrete output, so we compare all inputs against all outputs
                // NOTE: output items are considered discrete, if
                // 1) they do not reference any item vector
                // 2) they reference item vectors that are not referenced by any input item
                if ((discreteItemsInTargetOutputs.Count > 0) ||
                    ((itemVectorsReferencedOnlyInTargetOutputs?.Count > 0)))
                {
                    result = PerformDependencyAnalysisIfDiscreteOutputs(
                                itemVectorsInTargetInputs, itemVectorTransformsInTargetInputs, discreteItemsInTargetInputs,
                                targetOutputItemSpecs);
                }

                if (result == DependencyAnalysisResult.SkipUpToDate)
                {
                    var skippedTargetEventArgs = new TargetSkippedEventArgs(message: null)
                    {
                        BuildEventContext = _buildEventContext,
                        TargetName = TargetToAnalyze.Name,
                        BuildReason = TargetBuiltReason.None,
                        SkipReason = TargetSkipReason.OutputsUpToDate,
                        OriginallySucceeded = true,
                        Importance = MessageImportance.Normal
                    };

                    _loggingService.LogBuildEvent(skippedTargetEventArgs);

                    // Log the target inputs & outputs
                    if (!_loggingService.OnlyLogCriticalEvents)
                    {
                        LogUniqueInputsAndOutputs();
                    }
                }
            }

            LogReasonForBuildingTarget(result);

            return result;
        }

        /// <summary>
        /// Does appropriate logging to indicate why this target is being built fully or partially.
        /// </summary>
        /// <param name="result"></param>
        private void LogReasonForBuildingTarget(DependencyAnalysisResult result)
        {
            // Only if we are not logging just critical events should we be logging the details
            if (!_loggingService.OnlyLogCriticalEvents)
            {
                if (result == DependencyAnalysisResult.FullBuild && _dependencyAnalysisDetail.Count > 0)
                {
                    // For the full build decision the are three possible outcomes
                    _loggingService.LogComment(_buildEventContext, MessageImportance.Low, "BuildTargetCompletely", _targetToAnalyze.Name);

                    foreach (DependencyAnalysisLogDetail logDetail in _dependencyAnalysisDetail)
                    {
                        string reason = GetFullBuildReason(logDetail);
                        _loggingService.LogCommentFromText(_buildEventContext, MessageImportance.Low, reason);
                    }
                }
                else if (result == DependencyAnalysisResult.IncrementalBuild)
                {
                    // For the partial build decision the are three possible outcomes
                    _loggingService.LogComment(_buildEventContext, MessageImportance.Normal, "BuildTargetPartially", _targetToAnalyze.Name);
                    foreach (DependencyAnalysisLogDetail logDetail in _dependencyAnalysisDetail)
                    {
                        string reason = GetIncrementalBuildReason(logDetail);
                        _loggingService.LogCommentFromText(_buildEventContext, MessageImportance.Low, reason);
                    }
                }
            }
        }

        /// <summary>
        /// Returns a string indicating why a full build is occurring.
        /// </summary>
        internal static string GetFullBuildReason(DependencyAnalysisLogDetail logDetail)
        {
            string reason = null;

            if (logDetail.Reason == OutofdateReason.NewerInput)
            {
                // One of the inputs was newer than all of the outputs
                reason = ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("BuildTargetCompletelyInputNewer", logDetail.Input, logDetail.Output);
            }
            else if (logDetail.Reason == OutofdateReason.MissingOutput)
            {
                // One of the outputs was missing
                reason = ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("BuildTargetCompletelyOutputDoesntExist", logDetail.Output);
            }
            else if (logDetail.Reason == OutofdateReason.MissingInput)
            {
                // One of the inputs was missing
                reason = ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("BuildTargetCompletelyInputDoesntExist", logDetail.Input);
            }

            return reason;
        }

        /// <summary>
        /// Returns a string indicating why an incremental build is occurring.
        /// </summary>
        private static string GetIncrementalBuildReason(DependencyAnalysisLogDetail logDetail)
        {
            string reason = null;

            if (logDetail.Reason == OutofdateReason.NewerInput)
            {
                // One of the inputs was newer than its corresponding output
                reason = ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("BuildTargetPartiallyInputNewer", logDetail.InputItemName, logDetail.Input, logDetail.Output);
            }
            else if (logDetail.Reason == OutofdateReason.MissingOutput)
            {
                // One of the outputs was missing
                reason = ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("BuildTargetPartiallyOutputDoesntExist", logDetail.OutputItemName, logDetail.Input, logDetail.Output);
            }
            else if (logDetail.Reason == OutofdateReason.MissingInput)
            {
                // One of the inputs was missing
                reason = ResourceUtilities.FormatResourceStringIgnoreCodeAndKeyword("BuildTargetPartiallyInputDoesntExist", logDetail.InputItemName, logDetail.Input, logDetail.Output);
            }

            return reason;
        }

        /// <summary>
        /// Extract only the unique inputs and outputs from all the inputs and outputs gathered
        /// during dependency analysis
        /// </summary>
        private void LogUniqueInputsAndOutputs()
        {
            var args = ItemGroupLoggingHelper.CreateTaskParameterEventArgs(
                _buildEventContext,
                TaskParameterMessageKind.SkippedTargetInputs,
                itemType: null,
                _uniqueTargetInputs.Keys.ToArray(),
                logItemMetadata: false,
                DateTime.UtcNow);
            _loggingService.LogBuildEvent(args);

            args = ItemGroupLoggingHelper.CreateTaskParameterEventArgs(
                _buildEventContext,
                TaskParameterMessageKind.SkippedTargetOutputs,
                itemType: null,
                _uniqueTargetOutputs.Keys.ToArray(),
                logItemMetadata: false,
                DateTime.UtcNow);
            _loggingService.LogBuildEvent(args);
        }

        /// <summary>
        /// Parses the target's "Inputs" and "Outputs" attributes and gathers up referenced items.
        /// </summary>
        /// <param name="bucket"></param>
        /// <param name="itemVectorsInTargetInputs"></param>
        /// <param name="itemVectorTransformsInTargetInputs"></param>
        /// <param name="discreteItemsInTargetInputs"></param>
        /// <param name="itemVectorsInTargetOutputs"></param>
        /// <param name="discreteItemsInTargetOutputs"></param>
        /// <param name="targetOutputItemSpecs"></param>
        private void ParseTargetInputOutputSpecifications
        (
            ItemBucket bucket,
            out ItemVectorPartitionCollection itemVectorsInTargetInputs,
            out ItemVectorPartitionCollection itemVectorTransformsInTargetInputs,
            out Dictionary<string, string> discreteItemsInTargetInputs,
            out ItemVectorPartitionCollection itemVectorsInTargetOutputs,
            out Dictionary<string, string> discreteItemsInTargetOutputs,
            out List<string> targetOutputItemSpecs
        )
        {
            // break down the input/output specifications along the standard separator, after expanding all embedded properties
            // and item metadata
            var targetInputs = bucket.Expander.ExpandIntoStringListLeaveEscaped(TargetInputSpecification, ExpanderOptions.ExpandPropertiesAndMetadata, _targetToAnalyze.InputsLocation);
            var targetOutputs = bucket.Expander.ExpandIntoStringListLeaveEscaped(TargetOutputSpecification, ExpanderOptions.ExpandPropertiesAndMetadata, _targetToAnalyze.OutputsLocation);

            itemVectorTransformsInTargetInputs = new ItemVectorPartitionCollection(MSBuildNameIgnoreCaseComparer.Default);

            // figure out which of the inputs are:
            // 1) item vectors
            // 2) item vectors with transforms
            // 3) "discrete" items i.e. items that do not reference item vectors
            SeparateItemVectorsFromDiscreteItems(
                targetInputs,
                bucket,
                out itemVectorsInTargetInputs,
                itemVectorTransformsInTargetInputs,
                out discreteItemsInTargetInputs,
                _targetToAnalyze.InputsLocation);

            // figure out which of the outputs are:
            // 1) item vectors (with or without transforms)
            // 2) "discrete" items i.e. items that do not reference item vectors
            SeparateItemVectorsFromDiscreteItems(
                targetOutputs,
                bucket,
                out itemVectorsInTargetOutputs,
                null /* don't want transforms separated */,
                out discreteItemsInTargetOutputs,
                _targetToAnalyze.OutputsLocation);

            // list out all the output item-specs
            targetOutputItemSpecs = GetItemSpecsFromItemVectors(itemVectorsInTargetOutputs);
            targetOutputItemSpecs.AddRange(discreteItemsInTargetOutputs.Values);
        }

        /// <summary>
        /// Determines if the target needs to be built/rebuilt/skipped if it has no inputs (because they evaluated to empty).
        /// </summary>
        private DependencyAnalysisResult PerformDependencyAnalysisIfNoInputs()
        {
            DependencyAnalysisResult result;

            // if the target did declare inputs, but the specification evaluated to nothing
            if (TargetInputSpecification.Length > 0)
            {
                _loggingService.LogComment(_buildEventContext, MessageImportance.Normal,
                    "SkipTargetBecauseNoInputs", TargetToAnalyze.Name);
                // detailed reason is low importance to keep log clean
                _loggingService.LogComment(_buildEventContext, MessageImportance.Low,
                    "SkipTargetBecauseNoInputsDetail");

                // don't build the target
                result = DependencyAnalysisResult.SkipNoInputs;
            }
            else
            {
                // There were no inputs specified, so build completely
                _loggingService.LogComment(_buildEventContext, MessageImportance.Low, "BuildTargetCompletely", _targetToAnalyze.Name);
                _loggingService.LogComment(_buildEventContext, MessageImportance.Low, "BuildTargetCompletelyNoInputsSpecified");

                // otherwise, do a full build
                result = DependencyAnalysisResult.FullBuild;
            }

            return result;
        }

        /// <summary>
        /// Determines if the target needs to be built/rebuilt/skipped if it has no outputs (because they evaluated to empty).
        /// </summary>
        /// <returns>Indication of how to build the target.</returns>
        private DependencyAnalysisResult PerformDependencyAnalysisIfNoOutputs()
        {
            DependencyAnalysisResult result = DependencyAnalysisResult.SkipNoOutputs;

            // If the target has no inputs declared and the outputs evaluated to empty, do a full build. Remember that somebody
            // may specify Outputs="@(blah)", where the item list "blah" is actually produced by some task within this target. So
            // at the beginning, when we're trying to do to the dependency analysis, there's nothing in the "blah" list, but after
            // the target executes, there will be.
            if (TargetInputSpecification.Length == 0)
            {
                result = DependencyAnalysisResult.FullBuild;
            }
            // otherwise, don't build the target
            else
            {
                _loggingService.LogComment(_buildEventContext, MessageImportance.Normal,
                    "SkipTargetBecauseNoOutputs", TargetToAnalyze.Name);
                // detailed reason is low importance to keep log clean
                _loggingService.LogComment(_buildEventContext, MessageImportance.Low,
                    "SkipTargetBecauseNoOutputsDetail");
            }

            return result;
        }

        /// <summary>
        /// Determines if the target needs to be built/rebuilt/skipped if it has discrete inputs.
        /// </summary>
        /// <param name="itemVectorsInTargetInputs"></param>
        /// <param name="itemVectorTransformsInTargetInputs"></param>
        /// <param name="discreteItemsInTargetInputs"></param>
        /// <param name="itemVectorsReferencedOnlyInTargetInputs"></param>
        /// <param name="targetOutputItemSpecs"></param>
        /// <returns>Indication of how to build the target.</returns>
        private DependencyAnalysisResult PerformDependencyAnalysisIfDiscreteInputs
        (
            ItemVectorPartitionCollection itemVectorsInTargetInputs,
            ItemVectorPartitionCollection itemVectorTransformsInTargetInputs,
            Dictionary<string, string> discreteItemsInTargetInputs,
            List<string> itemVectorsReferencedOnlyInTargetInputs,
            List<string> targetOutputItemSpecs
        )
        {
            DependencyAnalysisResult result = DependencyAnalysisResult.SkipUpToDate;

            // list out all the discrete input item-specs...
            // NOTE: we treat input items that are item vector transforms, as discrete items, since we cannot correlate them to
            // any output item
            List<string> discreteTargetInputItemSpecs = GetItemSpecsFromItemVectors(itemVectorTransformsInTargetInputs);
            discreteTargetInputItemSpecs.AddRange(discreteItemsInTargetInputs.Values);

            // we treat input items that reference item vectors not referenced by any output item, as discrete items, since we
            // cannot correlate them to any output item
            foreach (string itemVectorType in itemVectorsReferencedOnlyInTargetInputs)
            {
                discreteTargetInputItemSpecs.AddRange(GetItemSpecsFromItemVectors(itemVectorsInTargetInputs, itemVectorType));
            }

            // if there are any discrete input items, we can treat them as "meta" inputs, because:
            // 1) we have already confirmed there are no discrete output items
            // 2) apart from the discrete input items, we can correlate all input items to all output items, since we know they
            //    both reference the same item vectors
            // NOTES:
            // 1) a typical example of a "meta" input is when the project file itself is listed as an input -- this forces
            //    rebuilds when the project file changes, even if no actual inputs have changed
            // 2) discrete input items and discrete output items are not treated symmetrically, because it is more likely that
            //    an uncorrelated input is a "meta" input, than an uncorrelated output is a "meta" output, since outputs can
            //    typically be built out of more than one set of inputs
            if (discreteTargetInputItemSpecs.Count > 0)
            {
                List<string> inputs = CollectionHelpers.RemoveNulls<string>(discreteTargetInputItemSpecs);
                List<string> outputs = CollectionHelpers.RemoveNulls<string>(targetOutputItemSpecs);

                if (inputs.Count == 0)
                {
                    return PerformDependencyAnalysisIfNoInputs();
                }

                if (outputs.Count == 0)
                {
                    return PerformDependencyAnalysisIfNoOutputs();
                }

                // if any output item is out-of-date w.r.t. any discrete input item, do a full build
                bool someOutOfDate = IsAnyOutOfDate(out DependencyAnalysisLogDetail dependencyAnalysisDetailEntry, _project.Directory, inputs, outputs);

                if (someOutOfDate)
                {
                    _dependencyAnalysisDetail.Add(dependencyAnalysisDetailEntry);
                    result = DependencyAnalysisResult.FullBuild;
                }
                else
                {
                    RecordUniqueInputsAndOutputs(inputs, outputs);
                }
            }

            return result;
        }

        /// <summary>
        /// Determines if the target needs to be built/rebuilt/skipped if its inputs and outputs can be correlated.
        /// </summary>
        /// <param name="itemVectorsInTargetInputs">The set of items which are in the inputs</param>
        /// <param name="itemVectorsInTargetOutputs">The set of items which are in the outputs.</param>
        /// <param name="itemVectorsReferencedInBothTargetInputsAndOutputs">A list of item types referenced in both the inputs and the outputs</param>
        /// <param name="changedTargetInputs">The inputs which are "changed" and require a build</param>
        /// <param name="upToDateTargetInputs">The inpurt which are "up to date" and do not require a build</param>
        /// <returns>Indication of how to build the target.</returns>
        private DependencyAnalysisResult PerformDependencyAnalysisIfCorrelatedInputsOutputs
        (
            ItemVectorPartitionCollection itemVectorsInTargetInputs,
            ItemVectorPartitionCollection itemVectorsInTargetOutputs,
            List<string> itemVectorsReferencedInBothTargetInputsAndOutputs,
            out ItemDictionary<ProjectItemInstance> changedTargetInputs,
            out ItemDictionary<ProjectItemInstance> upToDateTargetInputs
        )
        {
            DependencyAnalysisResult result = DependencyAnalysisResult.SkipUpToDate;

            changedTargetInputs = new ItemDictionary<ProjectItemInstance>();
            upToDateTargetInputs = new ItemDictionary<ProjectItemInstance>();

            // indicates if an incremental build is really just a full build, because all input items have changed
            int numberOfInputItemVectorsWithAllChangedItems = 0;

            foreach (string itemVectorType in itemVectorsReferencedInBothTargetInputsAndOutputs)
            {
                ItemVectorPartition inputItemVectors = itemVectorsInTargetInputs[itemVectorType];
                ItemVectorPartition outputItemVectors = itemVectorsInTargetOutputs[itemVectorType];

                // NOTE: recall that transforms have been separated out already
                ErrorUtilities.VerifyThrow(inputItemVectors.Count == 1,
                    "There should only be one item vector of a particular type in the target inputs that can be filtered.");

                // NOTE: Because the input items which were transformed have already been pulled out, this loop
                // will only execute a single time.
                foreach (IList<ProjectItemInstance> inputItems in inputItemVectors.Values)
                {
                    if (inputItems.Count > 0)
                    {
                        // By default, we assume that all of the input items are up to date.  As we go through
                        // our checks below, we will remove some of these and place them in the changed items dictionary
                        // which gets returned to the caller.
                        List<ProjectItemInstance> upToDateInputItems = new List<ProjectItemInstance>(inputItems);
                        int itemsChanged = 0;

                        // Iterate over each of the correlated lists of output items.  The keys to the outputItemVectors ItemDictionary
                        // are the transform expressions, not the item type from which the items were originally derived.
                        foreach (KeyValuePair<string, IList<ProjectItemInstance>> outputEntry in outputItemVectors)
                        {
                            IList<ProjectItemInstance> outputItems = outputEntry.Value;

                            // We count backwards so that as we remove items, we are removing them from the end, thereby
                            // not invalidating our iteration.

                            if (upToDateInputItems.Count == outputItems.Count)
                            {
                                for (int i = 0; i < upToDateInputItems.Count; i++)
                                {
                                    // If we have already determined this item is out of date, don't check again.
                                    if (upToDateInputItems[i] != null)
                                    {
                                        // Perform the out-of-date check only if we have an output-specification.
                                        if (outputItems[i] != null)
                                        {
                                            // check if it has changed
                                            bool outOfDate = IsOutOfDate(((IItem)upToDateInputItems[i]).EvaluatedIncludeEscaped, ((IItem)outputItems[i]).EvaluatedIncludeEscaped, upToDateInputItems[i].ItemType, outputItems[i].ItemType);
                                            if (outOfDate)
                                            {
                                                changedTargetInputs.Add(upToDateInputItems[i]);
                                                itemsChanged++;
                                                upToDateInputItems[i] = null;

                                                result = DependencyAnalysisResult.IncrementalBuild;
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // if any input is newer than any output, do a full build
                                bool someOutOfDate = IsAnyOutOfDate(out DependencyAnalysisLogDetail dependencyAnalysisDetailEntry, _project.Directory, upToDateInputItems, outputItems);

                                if (someOutOfDate)
                                {
                                    _dependencyAnalysisDetail.Add(dependencyAnalysisDetailEntry);
                                    itemsChanged = inputItems.Count;
                                    result = DependencyAnalysisResult.IncrementalBuild;
                                }
                                else
                                {
                                    RecordUniqueInputsAndOutputs(upToDateInputItems, outputItems);
                                    result = DependencyAnalysisResult.SkipUpToDate;
                                }
                            }

                            // If we have exhausted all of the input items of this type, move on to the next.
                            if (itemsChanged == inputItems.Count)
                            {
                                numberOfInputItemVectorsWithAllChangedItems++;
                                break;
                            }
                        }

                        // Add all of the items which remain up-to-date to the up-to-date target inputs dictionary.
                        if (itemsChanged < inputItems.Count)
                        {
                            foreach (ProjectItemInstance item in upToDateInputItems)
                            {
                                if (item != null)
                                {
                                    upToDateTargetInputs.Add(item);
                                }
                            }
                        }

                        // If we end up with no items of a particular type in the changed set,
                        // then add an empty marker so that lookups will correctly *not* find
                        // them.
                        if (!changedTargetInputs.ItemTypes.Contains(inputItems[0].ItemType))
                        {
                            changedTargetInputs.AddEmptyMarker(inputItems[0].ItemType);
                        }

                        // We need to perform the same operation on the up-to-date side
                        // too.
                        if (!upToDateTargetInputs.ItemTypes.Contains(inputItems[0].ItemType))
                        {
                            upToDateTargetInputs.AddEmptyMarker(inputItems[0].ItemType);
                        }
                    }
                }
            }

            ErrorUtilities.VerifyThrow(numberOfInputItemVectorsWithAllChangedItems <= itemVectorsReferencedInBothTargetInputsAndOutputs.Count,
                "The number of vectors containing all changed items cannot exceed the number of correlated vectors.");

            // if all correlated input items have changed
            if (numberOfInputItemVectorsWithAllChangedItems == itemVectorsReferencedInBothTargetInputsAndOutputs.Count)
            {
                ErrorUtilities.VerifyThrow(result == DependencyAnalysisResult.IncrementalBuild,
                    "If inputs have changed, this must be an incremental build.");

                // then the incremental build is really a full build
                result = DependencyAnalysisResult.FullBuild;
            }

            return result;
        }

        /// <summary>
        /// Determines if the target needs to be built/rebuilt/skipped if it has discrete outputs.
        /// </summary>
        /// <param name="itemVectorsInTargetInputs"></param>
        /// <param name="itemVectorTransformsInTargetInputs"></param>
        /// <param name="discreteItemsInTargetInputs"></param>
        /// <param name="targetOutputItemSpecs"></param>
        /// <returns>Indication of how to build the target.</returns>
        private DependencyAnalysisResult PerformDependencyAnalysisIfDiscreteOutputs
        (
            ItemVectorPartitionCollection itemVectorsInTargetInputs,
            ItemVectorPartitionCollection itemVectorTransformsInTargetInputs,
            Dictionary<string, string> discreteItemsInTargetInputs,
            List<string> targetOutputItemSpecs
        )
        {
            List<string> targetInputItemSpecs = GetItemSpecsFromItemVectors(itemVectorsInTargetInputs);
            targetInputItemSpecs.AddRange(GetItemSpecsFromItemVectors(itemVectorTransformsInTargetInputs));
            targetInputItemSpecs.AddRange(discreteItemsInTargetInputs.Values);

            List<string> inputs = CollectionHelpers.RemoveNulls<string>(targetInputItemSpecs);
            List<string> outputs = CollectionHelpers.RemoveNulls<string>(targetOutputItemSpecs);

            if (inputs.Count == 0)
            {
                return PerformDependencyAnalysisIfNoInputs();
            }

            if (outputs.Count == 0)
            {
                return PerformDependencyAnalysisIfNoOutputs();
            }

            // if any input is newer than any output, do a full build
            bool someOutOfDate = IsAnyOutOfDate(out DependencyAnalysisLogDetail dependencyAnalysisDetailEntry, _project.Directory, inputs, outputs);

            DependencyAnalysisResult result;
            if (someOutOfDate)
            {
                _dependencyAnalysisDetail.Add(dependencyAnalysisDetailEntry);
                result = DependencyAnalysisResult.FullBuild;
            }
            else
            {
                RecordUniqueInputsAndOutputs(inputs, outputs);
                result = DependencyAnalysisResult.SkipUpToDate;
            }

            return result;
        }

        /// <summary>
        /// Separates item vectors from discrete items, and discards duplicates. If requested, item vector transforms are also
        /// separated out. The item vectors (and the transforms) are partitioned by type, since there can be more than one item
        /// vector of the same type.
        /// </summary>
        /// <remarks>
        /// The item vector collection is a table of tables, where the top-level table is indexed by item type, and
        /// each "partition" table is indexed by the item vector itself.
        /// </remarks>
        /// <param name="items"></param>
        /// <param name="bucket"></param>
        /// <param name="itemVectors">Collection for item vectors</param>
        /// <param name="itemVectorTransforms">Collection for transforms if they should be collected separately, else null</param>
        /// <param name="discreteItems"></param>
        /// <param name="elementLocation"></param>
        private void SeparateItemVectorsFromDiscreteItems
        (
            SemiColonTokenizer items,
            ItemBucket bucket,
            out ItemVectorPartitionCollection itemVectors,
            ItemVectorPartitionCollection itemVectorTransforms,
            out Dictionary<string, string> discreteItems,
            ElementLocation elementLocation
        )
        {
            itemVectors = new ItemVectorPartitionCollection(MSBuildNameIgnoreCaseComparer.Default);
            discreteItems = new Dictionary<string, string>(MSBuildNameIgnoreCaseComparer.Default);

            // Iterate over all of the items specified.  Each of these may be in one of the following forms:
            // 1. A discrete item. e.g. foo.cs
            // 2. An item vector.  e.g. @(Foo)
            // 3. An item vector transform. e.g. @(Foo->'%(Filename).obj')
            foreach (string item in items)
            {
                // Expand the items in the item expression.  Note that the items returned will have the same type as the original expression
                // specified.  For example, both @(Foo) and @(Foo->'%(Filename).obj) will return items of type 'Foo'.  If the item in question
                // is discrete, itemVectorContents will be null.

                ProjectItemInstanceFactory itemFactory = new ProjectItemInstanceFactory(
                    _project /* no item type specified; use item type of vector itself */);

                IList<ProjectItemInstance> itemVectorContents = bucket.Expander.ExpandSingleItemVectorExpressionIntoItems(item, itemFactory, ExpanderOptions.ExpandItems, true /* include null entries from transforms */, out bool isTransformExpression, elementLocation);

                if (itemVectorContents != null)
                {
                    // There were item expressions

                    if (itemVectorContents.Count > 0)
                    {

                        // Expander set the item type it found
                        string itemVectorType = itemFactory.ItemType;

                        ItemVectorPartitionCollection itemVectorCollection;
                        if (itemVectorTransforms == null || !isTransformExpression)
                        {
                            // We either don't want transforms separated out, or this was not a transform.
                            itemVectorCollection = itemVectors;
                        }
                        else
                        {
                            itemVectorCollection = itemVectorTransforms;
                        }

                        // Do we already have a partition for this?
                        if (!itemVectorCollection.ContainsKey(itemVectorType))
                        {
                            // Nope, create one.
                            itemVectorCollection[itemVectorType] = new ItemVectorPartition(MSBuildNameIgnoreCaseComparer.Default);
                        }

                        ItemVectorPartition itemVectorPartition = itemVectorCollection[itemVectorType];

                        ErrorUtilities.VerifyThrow(!itemVectorCollection[itemVectorType].ContainsKey(item), "ItemVectorPartition already contains a vector for items with the expression '{0}'", item);
                        itemVectorPartition[item] = itemVectorContents;

                        ErrorUtilities.VerifyThrow((itemVectorTransforms == null) || (itemVectorCollection.Equals(itemVectorTransforms)) || (itemVectorPartition.Count == 1),
                            "If transforms have been separated out, there should only be one item vector per partition.");
                    }
                }
                else
                {
                    // There was no item expression
                    discreteItems[item] = item;
                }
            }
        }

        /// <summary>
        /// Retrieves the item-specs of all items in the given item vector collection.
        /// </summary>
        /// <param name="itemVectors"></param>
        /// <returns>list of item-specs</returns>
        private static List<string> GetItemSpecsFromItemVectors(ItemVectorPartitionCollection itemVectors)
        {
            List<string> itemSpecs = new List<string>();

            foreach (string itemType in itemVectors.Keys)
            {
                itemSpecs.AddRange(GetItemSpecsFromItemVectors(itemVectors, itemType));
            }

            return itemSpecs;
        }

        /// <summary>
        /// Retrieves the item-specs of all items of the specified type in the given item vector collection.
        /// </summary>
        /// <param name="itemVectors"></param>
        /// <param name="itemType"></param>
        /// <returns>list of item-specs</returns>
        private static List<string> GetItemSpecsFromItemVectors(ItemVectorPartitionCollection itemVectors, string itemType)
        {
            List<string> itemSpecs = new List<string>();

            ItemVectorPartition itemVectorPartition = itemVectors[itemType];

            if (itemVectorPartition != null)
            {
                foreach (IList<ProjectItemInstance> items in itemVectorPartition.Values)
                {
                    foreach (ProjectItemInstance item in items)
                    {
                        // The item can be null in the case of an item transform.
                        // eg., @(Compile->'%(NonExistentMetadata)')
                        // Nevertheless, include these, so that correlation can still occur.
                        itemSpecs.Add((item == null) ? null : ((IItem)item).EvaluatedIncludeEscaped);
                    }
                }
            }

            return itemSpecs;
        }

        /// <summary>
        /// Finds the differences in the keys between the two given hashtables.
        /// </summary>
        /// <param name="h1"></param>
        /// <param name="h2"></param>
        /// <param name="commonKeys"></param>
        /// <param name="uniqueKeysInH1"></param>
        /// <param name="uniqueKeysInH2"></param>
        private static void DiffHashtables<K, V>(IDictionary<K, V> h1, IDictionary<K, V> h2, out List<K> commonKeys, out List<K> uniqueKeysInH1, out List<K> uniqueKeysInH2) where K : class, IEquatable<K> where V : class
        {
            commonKeys = new List<K>();
            uniqueKeysInH1 = new List<K>();
            uniqueKeysInH2 = new List<K>();

            foreach (K h1Key in h1.Keys)
            {
                if (h2.ContainsKey(h1Key))
                {
                    commonKeys.Add(h1Key);
                }
                else
                {
                    uniqueKeysInH1.Add(h1Key);
                }
            }

            foreach (K h2Key in h2.Keys)
            {
                if (!h1.ContainsKey(h2Key))
                {
                    uniqueKeysInH2.Add(h2Key);
                }
            }
        }

        /// <summary>
        /// Compares the set of files/directories designated as "inputs" against the set of files/directories designated as
        /// "outputs", and indicates if any "output" file/directory is out-of-date w.r.t. any "input" file/directory.
        /// </summary>
        /// <remarks>
        /// NOTE: Internal for unit test purposes only.
        /// </remarks>
        /// <returns>true, if any "input" is newer than any "output", or if any input or output does not exist.</returns>
        internal static bool IsAnyOutOfDate<T>(out DependencyAnalysisLogDetail dependencyAnalysisDetailEntry, string projectDirectory, IList<T> inputs, IList<T> outputs)
        {
            ErrorUtilities.VerifyThrow((inputs.Count > 0) && (outputs.Count > 0), "Need to specify inputs and outputs.");
            if (inputs.Count > 0)
            {
                ErrorUtilities.VerifyThrow(inputs[0] is string || inputs[0] is ProjectItemInstance, "Must be either string or ProjectItemInstance");
            }

            if (outputs.Count > 0)
            {
                ErrorUtilities.VerifyThrow(outputs[0] is string || outputs[0] is ProjectItemInstance, "Must be either string or ProjectItemInstance");
            }

            // Algorithm: walk through all the outputs to find the oldest output
            //            walk through the inputs as far as we need to until we find one that's newer (if any)

            // PERF -- we could change this to ensure that we walk the shortest list first (because we walk that one entirely): 
            //         possibly the outputs list isn't actually the shortest list. However it always is the shortest
            //         in the cases I've seen, and adding this optimization would make the code hard to read.

            string oldestOutput = EscapingUtilities.UnescapeAll(FileUtilities.FixFilePath(outputs[0].ToString()));
            ErrorUtilities.ThrowIfTypeDoesNotImplementToString(outputs[0]);

            DateTime oldestOutputFileTime = DateTime.MinValue;
            try
            {
                string oldestOutputFullPath = Path.Combine(projectDirectory, oldestOutput);
                oldestOutputFileTime = NativeMethodsShared.GetLastWriteFileUtcTime(oldestOutputFullPath);
            }
            catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
            {
                // Output does not exist
                oldestOutputFileTime = DateTime.MinValue;
            }

            if (oldestOutputFileTime == DateTime.MinValue)
            {
                // First output is missing: we must build the target
                string arbitraryInput = EscapingUtilities.UnescapeAll(FileUtilities.FixFilePath(inputs[0].ToString()));
                ErrorUtilities.ThrowIfTypeDoesNotImplementToString(inputs[0]);
                dependencyAnalysisDetailEntry = new DependencyAnalysisLogDetail(arbitraryInput, oldestOutput, null, null, OutofdateReason.MissingOutput);
                return true;
            }

            for (int i = 1; i < outputs.Count; i++)
            {
                string candidateOutput = EscapingUtilities.UnescapeAll(FileUtilities.FixFilePath(outputs[i].ToString()));
                ErrorUtilities.ThrowIfTypeDoesNotImplementToString(outputs[i]);
                DateTime candidateOutputFileTime = DateTime.MinValue;
                try
                {
                    string candidateOutputFullPath = Path.Combine(projectDirectory, candidateOutput);
                    candidateOutputFileTime = NativeMethodsShared.GetLastWriteFileUtcTime(candidateOutputFullPath);
                }
                catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
                {
                    // Output does not exist
                    candidateOutputFileTime = DateTime.MinValue;
                }

                if (candidateOutputFileTime == DateTime.MinValue)
                {
                    // An output is missing: we must build the target
                    string arbitraryInput =
                        EscapingUtilities.UnescapeAll(FileUtilities.FixFilePath(inputs[0].ToString()));
                    ErrorUtilities.ThrowIfTypeDoesNotImplementToString(inputs[0]);
                    dependencyAnalysisDetailEntry = new DependencyAnalysisLogDetail(arbitraryInput, candidateOutput, null, null, OutofdateReason.MissingOutput);
                    return true;
                }

                if (oldestOutputFileTime > candidateOutputFileTime)
                {
                    // This output is older than the previous record holder
                    oldestOutputFileTime = candidateOutputFileTime;
                    oldestOutput = candidateOutput;
                }
            }

            // Now compare the oldest output with each input and break out if we find one newer.
            foreach (T input in inputs)
            {
                string unescapedInput = EscapingUtilities.UnescapeAll(FileUtilities.FixFilePath(input.ToString()));
                ErrorUtilities.ThrowIfTypeDoesNotImplementToString(input);
                DateTime inputFileTime = DateTime.MaxValue;
                try
                {
                    string unescapedInputFullPath = Path.Combine(projectDirectory, unescapedInput);
                    inputFileTime = NativeMethodsShared.GetLastWriteFileUtcTime(unescapedInputFullPath);
                }
                catch (Exception e) when (ExceptionHandling.IsIoRelatedException(e))
                {
                    // Output does not exist
                    inputFileTime = DateTime.MinValue;
                }

                if (inputFileTime == DateTime.MinValue)
                {
                    // An input is missing: we must build the target
                    dependencyAnalysisDetailEntry = new DependencyAnalysisLogDetail(unescapedInput, oldestOutput, null, null, OutofdateReason.MissingInput);
                    return true;
                }
                else
                {
                    if (inputFileTime > oldestOutputFileTime)
                    {
                        // This input is newer than the oldest output: we must build the target
                        dependencyAnalysisDetailEntry = new DependencyAnalysisLogDetail(unescapedInput, oldestOutput, null, null, OutofdateReason.NewerInput);
                        return true;
                    }
                }
            }

            // All exist and no inputs are newer than any outputs; up to date
            dependencyAnalysisDetailEntry = null;
            return false;
        }

        /// <summary>
        /// Record the unique input and output files so that the "up to date" message
        /// can list them in the log later.
        /// </summary>
        private void RecordUniqueInputsAndOutputs<T>(IList<T> inputs, IList<T> outputs)
        {
            if (inputs.Count > 0)
            {
                ErrorUtilities.VerifyThrow(inputs[0] is string || inputs[0] is ProjectItemInstance, "Must be either string or ProjectItemInstance");
            }

            if (outputs.Count > 0)
            {
                ErrorUtilities.VerifyThrow(outputs[0] is string || outputs[0] is ProjectItemInstance, "Must be either string or ProjectItemInstance");
            }

            // Only if we are not logging just critical events should we be gathering full details
            if (!_loggingService.OnlyLogCriticalEvents)
            {
                foreach (T input in inputs)
                {
                    ErrorUtilities.ThrowIfTypeDoesNotImplementToString(input);
                    if (!_uniqueTargetInputs.ContainsKey(input.ToString()))
                    {
                        _uniqueTargetInputs.Add(input.ToString(), null);
                    }
                }
                foreach (T output in outputs)
                {
                    ErrorUtilities.ThrowIfTypeDoesNotImplementToString(output);
                    if (!_uniqueTargetOutputs.ContainsKey(output.ToString()))
                    {
                        _uniqueTargetOutputs.Add(output.ToString(), null);
                    }
                }
            }
        }
        /// <summary>
        /// Compares the file/directory designated as "input" against the file/directory designated as "output", and indicates if
        /// the "output" file/directory is out-of-date w.r.t. the "input" file/directory.
        /// </summary>
        /// <remarks>
        /// If the "input" does not exist on disk, we treat its disappearance as a change, and consider the "input" to be newer
        /// than the "output", regardless of whether the "output" itself exists.
        /// </remarks>
        /// <param name="input"></param>
        /// <param name="output"></param>
        /// <param name="inputItemName"></param>
        /// <param name="outputItemName"></param>
        /// <returns>true, if "input" is newer than "output"</returns>
        private bool IsOutOfDate(string input, string output, string inputItemName, string outputItemName)
        {
            input = EscapingUtilities.UnescapeAll(FileUtilities.FixFilePath(input));
            output = EscapingUtilities.UnescapeAll(FileUtilities.FixFilePath(output));
            bool outOfDate = (CompareLastWriteTimes(input, output, out bool inputDoesNotExist, out bool outputDoesNotExist) == 1) || inputDoesNotExist;

            // Only if we are not logging just critical events should we be gathering full details
            if (!_loggingService.OnlyLogCriticalEvents)
            {
                // Make a note of unique inputs
                if (!_uniqueTargetInputs.ContainsKey(input))
                {
                    _uniqueTargetInputs.Add(input, null);
                }

                // Make a note of unique outputs
                if (!_uniqueTargetOutputs.ContainsKey(output))
                {
                    _uniqueTargetOutputs.Add(output, null);
                }
            }

            RecordComparisonResults(input, output, inputItemName, outputItemName, inputDoesNotExist, outputDoesNotExist, outOfDate);

            return outOfDate;
        }

        /// <summary>
        /// Add timestamp comparison results to a list, to log them together later.
        /// </summary>
        private void RecordComparisonResults(string input, string output, string inputItemName, string outputItemName, bool inputDoesNotExist, bool outputDoesNotExist, bool outOfDate)
        {
            // Only if we are not logging just critical events should we be gathering full details
            if (!_loggingService.OnlyLogCriticalEvents)
            {
                // Record the details of the out-of-date decision
                if (inputDoesNotExist)
                {
                    _dependencyAnalysisDetail.Add(new DependencyAnalysisLogDetail(input, output, inputItemName, outputItemName, OutofdateReason.MissingInput));
                }
                else if (outputDoesNotExist)
                {
                    _dependencyAnalysisDetail.Add(new DependencyAnalysisLogDetail(input, output, inputItemName, outputItemName, OutofdateReason.MissingOutput));
                }
                else if (outOfDate)
                {
                    _dependencyAnalysisDetail.Add(new DependencyAnalysisLogDetail(input, output, inputItemName, outputItemName, OutofdateReason.NewerInput));
                }
            }
        }

        /// <summary>
        /// Compares the last-write times of the given files/directories.
        /// </summary>
        /// <remarks>
        /// Existing files/directories are always considered newer than non-existent ones, and two non-existent files/directories
        /// are considered to have the same last-write time.
        /// </remarks>
        /// <param name="path1"></param>
        /// <param name="path2"></param>
        /// <param name="path1DoesNotExist">[out] indicates if the first file/directory does not exist on disk</param>
        /// <param name="path2DoesNotExist">[out] indicates if the second file/directory does not exist on disk</param>
        /// <returns>
        /// -1  if the first file/directory is older than the second;
        ///  0  if the files/directories were both last written to at the same time;
        /// +1  if the first file/directory is newer than the second
        /// </returns>
        private int CompareLastWriteTimes(string path1, string path2, out bool path1DoesNotExist, out bool path2DoesNotExist)
        {
            ErrorUtilities.VerifyThrow(!string.IsNullOrEmpty(path1) && !string.IsNullOrEmpty(path2),
                "Need to specify paths to compare.");

            path1 = Path.Combine(_project.Directory, path1);
            var path1WriteTime = NativeMethodsShared.GetLastWriteFileUtcTime(path1);

            path2 = Path.Combine(_project.Directory, path2);
            var path2WriteTime = NativeMethodsShared.GetLastWriteFileUtcTime(path2);

            path1DoesNotExist = (path1WriteTime == DateTime.MinValue);
            path2DoesNotExist = (path2WriteTime == DateTime.MinValue);

            if (path1DoesNotExist)
            {
                if (path2DoesNotExist)
                {
                    // Neither exist
                    return 0;
                }
                else
                {
                    // Only path 2 exists
                    return -1;
                }
            }
            else if (path2DoesNotExist)
            {
                // Only path 1 exists
                return +1;
            }

            // Both exist
            return DateTime.Compare(path1WriteTime, path2WriteTime);
        }

        #endregion

        // the project whose target we are analyzing.
        private ProjectInstance _project;
        // the target to analyze
        private ProjectTargetInstance _targetToAnalyze;

        // the value of the target's "Inputs" attribute
        private string _targetInputSpecification;
        // the value of the target's "Outputs" attribute
        private string _targetOutputSpecification;

        // Details of the dependency analysis for logging
        private readonly List<DependencyAnalysisLogDetail> _dependencyAnalysisDetail = new List<DependencyAnalysisLogDetail>();

        // Engine logging service which to log message to
        private ILoggingService _loggingService;
        // Event context information where event is raised from
        private BuildEventContext _buildEventContext;

        /// <summary>
        /// By default we do not sort target inputs and outputs as it has significant perf impact.
        /// But allow suites to enable this so they get consistent results.
        /// </summary>
        private static readonly bool s_sortInputsOutputs = (Environment.GetEnvironmentVariable("MSBUILDSORTINPUTSOUTPUTS") == "1");

        /// <summary>
        /// The unique target inputs.
        /// </summary>
        private IDictionary<string, object> _uniqueTargetInputs =
                   (s_sortInputsOutputs ? (IDictionary<string, object>)new SortedDictionary<string, object>(StringComparer.OrdinalIgnoreCase) : (IDictionary<string, object>)new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));

        /// <summary>
        /// The unique target outputs.
        /// </summary>
        private IDictionary<string, object> _uniqueTargetOutputs =
                   (s_sortInputsOutputs ? (IDictionary<string, object>)new SortedDictionary<string, object>(StringComparer.OrdinalIgnoreCase) : (IDictionary<string, object>)new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Why TLDA decided this entry was out of date
    /// </summary>
    internal enum OutofdateReason
    {
        MissingInput, // The input file was missing
        MissingOutput, // The output file was missing
        NewerInput // The input file was newer
    }

    /// <summary>
    /// A logging detail entry. Describes what TLDA decided about inputs / outputs
    /// </summary>
    internal class DependencyAnalysisLogDetail
    {
        private OutofdateReason _reason;
        private string _inputItemName;
        private string _outputItemName;
        private string _input;
        private string _output;

        /// <summary>
        /// The reason that we are logging this entry
        /// </summary>
        internal OutofdateReason Reason
        {
            get { return _reason; }
        }

        /// <summary>
        /// The input item name (can be null)
        /// </summary>
        public string InputItemName
        {
            get { return _inputItemName; }
        }

        /// <summary>
        /// The output item name (can be null)
        /// </summary>
        public string OutputItemName
        {
            get { return _outputItemName; }
        }

        /// <summary>
        /// The input file
        /// </summary>
        public string Input
        {
            get { return _input; }
        }

        /// <summary>
        /// The output file
        /// </summary>
        public string Output
        {
            get { return _output; }
        }

        /// <summary>
        /// Construct a log detail element
        /// </summary>
        /// <param name="input">Input file</param>
        /// <param name="output">Output file</param>
        /// <param name="inputItemName">Input item name (can be null)</param>
        /// <param name="outputItemName">Output item name (can be null)</param>
        /// <param name="reason">The reason we are logging</param>
        public DependencyAnalysisLogDetail(string input, string output, string inputItemName, string outputItemName, OutofdateReason reason)
        {
            _reason = reason;
            _inputItemName = inputItemName;
            _outputItemName = outputItemName;
            _input = input;
            _output = output;
        }
    }
}
