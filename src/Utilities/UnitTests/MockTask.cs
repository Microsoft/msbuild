﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Resources;
using Microsoft.Build.Utilities;

namespace Microsoft.Build.UnitTests
{
    internal sealed class MockTask : Task
    {
        internal MockTask() : base()
        {
            RegisterResources();
        }

        internal MockTask(bool registerResources)
        {
            if (registerResources)
            {
                RegisterResources();
            }
        }

        private void RegisterResources()
        {
            ResourceManager rm = new ResourceManager("Microsoft.Build.Utilities.UnitTests.strings",
                typeof(MockTask).Assembly);
            this.Log.TaskResources = rm;
        }

        public override bool Execute()
        {
            return true;
        }
    }
}
