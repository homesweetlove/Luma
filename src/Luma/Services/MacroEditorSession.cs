using System.Text.Json;
using Luma.Models;

namespace Luma.Services;

/// <summary>Detached tree used by the visual editor; cancelling never mutates the active macro.</summary>
public sealed class MacroEditorSession
{
    public sealed record Entry(string Path, MacroStep Step, List<MacroStep> Owner, int Index, int Depth)
    {
        public override string ToString()
        {
            var detail = Step.Kind switch
            {
                "navigate" => MacroService.IsRecordableNavigationUrl(Step.Url) ? Step.Url : "[URL hidden]",
                "click" or "wait" or "if" => Step.Selector,
                "input" => Step.Selector + " (value hidden)",
                _ => null
            };
            return new string(' ', Depth * 2) + Path + "  " + Step.Kind +
                (string.IsNullOrEmpty(detail) ? "" : " · " + detail);
        }
    }

    public List<MacroStep> Steps { get; }

    public MacroEditorSession(IEnumerable<MacroStep> steps)
    {
        Steps = JsonSerializer.Deserialize<List<MacroStep>>(JsonSerializer.Serialize(steps)) ?? [];
    }

    public IReadOnlyList<Entry> Flatten()
    {
        var result = new List<Entry>();
        static void Visit(List<MacroStep> list, string prefix, int depth, List<Entry> output)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var path = prefix + (i + 1);
                var item = list[i];
                output.Add(new Entry(path, item, list, i, depth));
                Visit(item.Steps, path + ".", depth + 1, output);
                Visit(item.ElseSteps, path + ".else.", depth + 1, output);
            }
        }
        Visit(Steps, "", 0, result);
        return result;
    }

    public MacroStep AddRoot() => Add(Steps);

    public MacroStep AddChild(Entry selected, bool elseBranch = false)
    {
        var parent = selected.Step;
        if (elseBranch && parent.Kind != "if")
            throw new InvalidOperationException("Only if steps can have an else branch.");
        if (parent.Kind is not ("if" or "loop" or "retry"))
            throw new InvalidOperationException("Select an if, loop or retry step to add children.");
        if (parent.Kind == "retry" && parent.Steps.Count != 0)
            throw new InvalidOperationException("Retry supports exactly one child action.");
        return Add(elseBranch ? parent.ElseSteps : parent.Steps);
    }

    private static MacroStep Add(List<MacroStep> destination)
    {
        var step = new MacroStep { Kind = "wait", Selector = "#target" };
        destination.Add(step);
        return step;
    }

    public void Delete(Entry selected) => selected.Owner.RemoveAt(selected.Index);

    public void Move(Entry selected, int offset)
    {
        var target = selected.Index + offset;
        if (target < 0 || target >= selected.Owner.Count) return;
        (selected.Owner[target], selected.Owner[selected.Index]) = (selected.Owner[selected.Index], selected.Owner[target]);
    }

    public void ValidateStructure()
    {
        static void Visit(IEnumerable<MacroStep> steps)
        {
            foreach (var step in steps)
            {
                if (step.Kind is not ("loop" or "if" or "retry") &&
                    (step.Steps.Count != 0 || step.ElseSteps.Count != 0))
                    throw new InvalidOperationException("Only if, loop and retry steps can contain children.");
                if (step.Kind != "if" && step.ElseSteps.Count != 0)
                    throw new InvalidOperationException("Only if steps can have an else branch.");
                Visit(step.Steps);
                Visit(step.ElseSteps);
            }
        }
        Visit(Steps);
    }
}
