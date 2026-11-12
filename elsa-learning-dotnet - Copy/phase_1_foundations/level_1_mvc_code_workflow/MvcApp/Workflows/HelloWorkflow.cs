using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
// In 3.9.0 SetOutput lives in Elsa.Workflows.Management, in a namespace named after the class itself.
using Elsa.Workflows.Management.Activities.SetOutput;
using MvcApp.Models;

namespace MvcApp.Workflows;

// ELSA CONCEPT: a code-first workflow DEFINITION. Deriving from WorkflowBase and overriding Build()
// describes the blueprint; every run of it is a separate workflow INSTANCE with its own state.
public class HelloWorkflow : WorkflowBase
{
    // Shared with the controller so input/output names can't drift apart.
    public const string OrderInputName = "Order";
    public const string MessageOutputName = "Message";

    protected override void Build(IWorkflowBuilder builder)
    {
        // Naming convention LX_<descriptor>. The definition ID is stable across versions.
        builder.DefinitionId = "L1_Hello";
        builder.Name = "L1_Hello";

        // ELSA CONCEPT: workflow INPUT — data handed in by whoever starts the workflow.
        var orderInput = builder.WithInput<Order>(OrderInputName);

        // ELSA CONCEPT: workflow OUTPUT — a named result the caller can read after the run.
        var messageOutput = builder.WithOutput<string>(MessageOutputName);

        // ELSA CONCEPT: a workflow VARIABLE — typed state local to one instance.
        var greeting = builder.WithVariable<string>("Greeting", string.Empty);

        // ELSA CONCEPT: the root ACTIVITY. A Sequence runs its child activities one after another.
        builder.Root = new Sequence
        {
            Activities =
            {
                // WriteLine is a built-in activity that writes to the host's standard output
                // (look at the terminal running `dotnet run`). It is Elsa's demo activity, not app logging.
                new WriteLine("L1_Hello: workflow started."),

                // Compute a value from the input and store it in the variable.
                new SetVariable
                {
                    Variable = greeting,
                    Value = new(context =>
                    {
                        var order = context.GetInput<Order>(orderInput)
                            ?? throw new InvalidOperationException($"Workflow input '{OrderInputName}' is required.");
                        return $"Hello from Elsa! Order {order.OrderId} for '{order.Product}' ({order.Amount:0.00}) has status '{order.Status}'.";
                    })
                },

                // Expressions here are C# lambdas evaluated by the engine at run time.
                new WriteLine(context => $"L1_Hello: {greeting.Get(context)}"),

                // Copy the variable to the workflow output so the controller can read it.
                new SetOutput
                {
                    OutputName = new(messageOutput.Name),
                    OutputValue = new(context => greeting.Get(context))
                },

                new WriteLine("L1_Hello: workflow finished.")
            }
        };
    }
}
