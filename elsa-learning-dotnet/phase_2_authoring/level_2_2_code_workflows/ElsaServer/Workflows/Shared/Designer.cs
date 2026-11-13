using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Models;
using Endpoint = Elsa.Workflows.Activities.Flowchart.Models.Endpoint;

namespace ElsaServer.Workflows.Shared;

/// <summary>
/// Makes a C# workflow look in Studio exactly like its designed (JSON) twin in level 2.1.
/// Studio draws a Flowchart from three things the JSON files store for every activity:
///   - a fixed ID and name        → "id", "name"
///   - the text on the box        → metadata.displayText
///   - where the box sits         → metadata.designer.position {x, y}
/// and from the Connections between activity ports ("Done", "True", "False" …).
///
/// Fixed IDs also matter at run time: Elsa stores which activity an instance is waiting on by its ID, so
/// stable IDs keep waiting instances resumable when the code around them changes.
/// </summary>
public static class Designer
{
    /// <summary>Give an activity its ID, name, box text and canvas position.</summary>
    public static T At<T>(this T activity, string id, string name, string displayText, double x, double y) where T : Activity
    {
        activity.Id = id;
        activity.Name = name;
        activity.Metadata["displayText"] = displayText;
        activity.Metadata["designer"] = new Dictionary<string, object>
        {
            ["position"] = new Dictionary<string, object> { ["x"] = x, ["y"] = y }
        };
        return activity;
    }

    /// <summary>An arrow from <paramref name="from"/>'s outcome port to <paramref name="to"/>'s "In" port.</summary>
    public static Connection Flow(IActivity from, IActivity to, string port = "Done") =>
        new(new Endpoint(from, port), new Endpoint(to, "In"));
}
