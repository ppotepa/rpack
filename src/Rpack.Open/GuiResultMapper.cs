using Rpack.Core;

namespace Rpack.Open;

internal sealed class GuiResultMapper
{
    public GuiIssueViewModel FromCheckFailure(string message, bool allowDirty, bool ignoreSpaceChange)
    {
        var problem = ErrorFormatter.FromCheckFailure(message, allowDirty, ignoreSpaceChange);
        return new GuiIssueViewModel(problem.Title, problem.Summary, problem.Suggestion, problem.Details);
    }

    public GuiIssueViewModel FromException(Exception exception)
    {
        var problem = ErrorFormatter.SummarizeException(exception);
        return new GuiIssueViewModel(problem.Title, problem.Summary, problem.Suggestion, problem.Details);
    }
}
