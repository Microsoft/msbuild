using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Build.Tasks
{
    class GetCompatiblePlatform : TaskExtension
    {
        public ITaskItem[] AnnotatedProjects { get; set; }

        public string ParentProjectPlatform { get; set; }

        public string PlatformLookupTable { get; set; }

        [Output]
        public ITaskItem[] AssignedProjectsWithPlatform { get; set; }

        public override bool Execute()
        {
            Dictionary<string, string> translationTable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(PlatformLookupTable))
            {
                foreach (string s in PlatformLookupTable.Split(';'))
                {
                    // Minimum translation: a=b
                    if (s.Length < 3)
                    {
                        continue;
                    }
                    string key = s.Split('=')[0];
                    string val = s.Split('=')[1];
                    translationTable[key] = val;
                }
                Log.LogMessage($"Translation Table: {translationTable.Aggregate(new StringBuilder(), (sb, kvp) => sb.Append(kvp.Key + "=" + kvp.Value + ";"), sb => sb.ToString())}");
            }

            AssignedProjectsWithPlatform = new ITaskItem[AnnotatedProjects.Length];
            for (int i = 0; i < AnnotatedProjects.Length; i++)
            {
                AssignedProjectsWithPlatform[i] = new TaskItem(AnnotatedProjects[i]);

                if (string.IsNullOrEmpty(AssignedProjectsWithPlatform[i].GetMetadata("PlatformOptions")))
                {
                    Log.LogWarningWithCodeFromResources("GetCompatiblePlatform.NoPlatformsListed", AssignedProjectsWithPlatform[i].ItemSpec);
                    continue;
                }

                HashSet<string> childPlatforms = new HashSet<string>();
                foreach (string s in AssignedProjectsWithPlatform[i].GetMetadata("PlatformOptions").Split(';'))
                {
                    if (!string.IsNullOrEmpty(s))
                    {
                        childPlatforms.Add(s);
                    }
                }

                string buildChildProjectAs = "";

                // Translation table takes priority
                if (translationTable.ContainsKey(ParentProjectPlatform) &&
                          childPlatforms.Contains(translationTable[ParentProjectPlatform]))
                {
                    buildChildProjectAs = translationTable[ParentProjectPlatform];
                    Log.LogMessage($"Found '{ParentProjectPlatform}={buildChildProjectAs}' in the given translation table.");
                }
                // AnyCPU if possible
                else if (childPlatforms.Contains("AnyCPU"))
                {
                    buildChildProjectAs = "AnyCPU";
                    Log.LogMessage($"Defaulting to AnyCPU.");
                }
                // Prefer matching platforms
                else if (childPlatforms.Contains(ParentProjectPlatform))
                {
                    buildChildProjectAs = ParentProjectPlatform;
                    Log.LogMessage($"Child and parent have the same platform.");
                }
                else
                {
                    // Keep it empty, log a warning. Common.CurrentVersion.targets will undefine 
                    // Platform/PlatformTarget when this is the case.
                    Log.LogWarningWithCodeFromResources("GetCompatiblePlatform.NoCompatiblePlatformFound", AssignedProjectsWithPlatform[i].ItemSpec);
                }

                AssignedProjectsWithPlatform[i].SetMetadata("NearestPlatform", buildChildProjectAs);
                Log.LogMessage($"Project '{AssignedProjectsWithPlatform[i].ItemSpec}' will build with Platform: '{buildChildProjectAs}'");
            }

            return true;
        }
    }
}
