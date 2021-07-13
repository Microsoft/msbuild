// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Microsoft.Build.Framework
{
    /// <summary>
    /// Will provide location information for an event, this is especially 
    /// needed in a multi processor environment
    /// </summary>
    [Serializable]
    public class BuildEventContext
    {
        #region Data

        /// <summary>
        /// Node event was in 
        /// </summary>
        private readonly int _nodeId;

        /// <summary>
        /// Target event was in
        /// </summary>
        private readonly int _targetId;

        /// <summary>
        ///The node-unique project request context the event was in
        /// </summary>
        private readonly int _projectContextId;

        /// <summary>
        /// Id of the task the event was caused from
        /// </summary>
        private readonly int _taskId;

        /// <summary>
        /// The id of the project instance to which this event refers.
        /// </summary>
        private readonly int _projectInstanceId;

        /// <summary>
        /// The id of the submission.
        /// </summary>
        private readonly int _submissionId;

        /// <summary>
        /// The id of the evaluation
        /// </summary>
        private readonly int _evaluationId;

        #endregion

        #region Constructor

        /// <summary>
        /// This is the original constructor.  No one should ever use this except internally for backward compatibility.
        /// </summary>
        public BuildEventContext
        (
            int nodeId,
            int targetId,
            int projectContextId,
            int taskId
        )
            : this(InvalidSubmissionId, nodeId, InvalidEvaluationId, InvalidProjectInstanceId, projectContextId, targetId, taskId)
        {
            // UNDONE: This is obsolete.
        }

        /// <summary>
        /// Constructs a BuildEventContext with a specified project instance id.
        /// </summary>
        public BuildEventContext
        (
            int nodeId,
            int projectInstanceId,
            int projectContextId,
            int targetId,
            int taskId
        )
            : this(InvalidSubmissionId, nodeId, InvalidEvaluationId, projectInstanceId, projectContextId, targetId, taskId)
        {
        }

        /// <summary>
        /// Constructs a BuildEventContext with a specific submission id
        /// </summary>
        public BuildEventContext
        (
            int submissionId,
            int nodeId,
            int projectInstanceId,
            int projectContextId,
            int targetId,
            int taskId
        )
            :this(submissionId, nodeId, InvalidEvaluationId, projectInstanceId, projectContextId, targetId, taskId)
        {
        }

        /// <summary>
        /// Constructs a BuildEventContext
        /// </summary>
        public BuildEventContext
        (
            int submissionId,
            int nodeId,
            int evaluationId,
            int projectInstanceId,
            int projectContextId,
            int targetId,
            int taskId
        )
        {
            _submissionId = submissionId;
            _nodeId = nodeId;
            _evaluationId = evaluationId;
            _targetId = targetId;
            _projectContextId = projectContextId;
            _taskId = taskId;
            _projectInstanceId = projectInstanceId;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns a default invalid BuildEventContext
        /// </summary>
        public static BuildEventContext Invalid => new BuildEventContext(InvalidNodeId, InvalidTargetId, InvalidProjectContextId, InvalidTaskId);

        /// <summary>
        /// Retrieves the Evaluation id.
        /// </summary>
        public int EvaluationId => _evaluationId;

        /// <summary>
        /// NodeId where event took place
        /// </summary>
        public int NodeId => _nodeId;

        /// <summary>
        /// Id of the target the event was in when the event was fired
        /// </summary>
        public int TargetId => _targetId;

        /// <summary>
        /// Retrieves the Project Context id.
        /// </summary>
        public int ProjectContextId => _projectContextId;

        /// <summary>
        /// Retrieves the task id.
        /// </summary>
        public int TaskId => _taskId;

        /// <summary>
        /// Retrieves the project instance id.
        /// </summary>
        public int ProjectInstanceId => _projectInstanceId;

        /// <summary>
        /// Retrieves the Submission id.
        /// </summary>
        public int SubmissionId => _submissionId;

        /// <summary>
        /// Retrieves the BuildRequest id.  Note that this is not the same as the global request id on a BuildRequest or BuildResult.
        /// </summary>
        public long BuildRequestId => GetHashCode();

        #endregion

        #region Constants
        /// <summary>
        /// Indicates an invalid project context identifier.
        /// </summary>
        public const int InvalidProjectContextId = -2;
        /// <summary>
        /// Indicates an invalid task identifier.
        /// </summary>
        public const int InvalidTaskId = -1;
        /// <summary>
        /// Indicates an invalid target identifier.
        /// </summary>
        public const int InvalidTargetId = -1;
        /// <summary>
        /// Indicates an invalid node identifier.
        /// </summary>
        public const int InvalidNodeId = -2;
        /// <summary>
        /// Indicates an invalid project instance identifier.
        /// </summary>
        public const int InvalidProjectInstanceId = -1;
        /// <summary>
        /// Indicates an invalid submission identifier.
        /// </summary>
        public const int InvalidSubmissionId = -1;
        /// <summary>
        /// Indicates an invalid evaluation identifier.
        /// </summary>
        public const int InvalidEvaluationId = -1;

        #endregion

        #region Equals

        /// <summary>
        /// Retrieves a hash code for this BuildEventContext.
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            var hash = 17;
            // submission ID does not contribute to equality
            //hash = hash * 31 + _submissionId;
            hash = (hash * 31) + _nodeId;
            hash = (hash * 31) + _evaluationId;
            hash = (hash * 31) + _targetId;
            hash = (hash * 31) + _projectContextId;
            hash = (hash * 31) + _taskId;
            hash = (hash * 31) + _projectInstanceId;

            return hash;
        }

        /// <summary>
        /// Compare a BuildEventContext with this BuildEventContext. 
        /// A build event context is compared in the following way.
        /// 
        /// 1. If the object references are the same the contexts are equivalent
        /// 2. If the object type is the same and the Id values in the context are the same, the contexts are equivalent
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            // If the references are the same no need to do any more comparing
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj is null)
            {
                return false;
            }

            // The types do not match, they cannot be the same
            if (GetType() != obj.GetType())
            {
                return false;
            }

            return InternalEquals((BuildEventContext)obj);
        }
        /// <summary>
        /// Override == so the  equals comparison using this operator will be the same as
        /// .Equals
        /// </summary>
        /// <param name="left">Left hand side operand</param>
        /// <param name="right">Right hand side operand</param>
        /// <returns>True if the object values are identical, false if they are not identical</returns>
        public static bool operator ==(BuildEventContext left, BuildEventContext right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left is null)
            {
                return false;
            }

            return left.Equals(right);
        }

        /// <summary>
        /// Override != so the  equals comparison using this operator will be the same as
        ///  ! Equals
        /// </summary>
        /// <param name="left">Left hand side operand</param>
        /// <param name="right">Right hand side operand</param>
        /// <returns>True if the object values are not identical, false if they are identical</returns>
        public static bool operator !=(BuildEventContext left, BuildEventContext right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Verify the fields are identical
        /// </summary>
        /// <param name="buildEventContext">BuildEventContext to compare to this instance</param>
        /// <returns>True if the value fields are the same, false if otherwise</returns>
        private bool InternalEquals(BuildEventContext buildEventContext)
        {
            return _nodeId == buildEventContext.NodeId
                   && _projectContextId == buildEventContext.ProjectContextId
                   && _targetId == buildEventContext.TargetId
                   && _taskId == buildEventContext.TaskId
                   && _evaluationId == buildEventContext._evaluationId
                   && _projectInstanceId == buildEventContext._projectInstanceId;
        }
        #endregion

        public override string ToString()
        {
            return $"Node={NodeId} Submission={SubmissionId} ProjectContext={ProjectContextId} ProjectInstance={ProjectInstanceId} Eval={EvaluationId} Target={TargetId} Task={TaskId}";
        }
    }
}
