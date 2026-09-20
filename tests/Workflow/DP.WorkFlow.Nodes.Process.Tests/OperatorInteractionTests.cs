namespace DP.WorkFlow.Tests;

public sealed class OperatorInteractionTests
{
    private static readonly OperatorChoiceOption[] Choices = { new("Continue", "继续原操作"), new("Restart", "人工回初始点") };

    [Fact]
    public async Task Choice_RequiresMatchingIdentityAndConfiguredKey_AndCopiesOptions()
    {
        using var interaction = new WorkflowOperatorInteraction();
        var options = Choices.ToList();
        var task = interaction.AskChoiceAsync("轴异常", "选择工程师预设方案", options, default).AsTask();
        var prompt = Assert.IsType<WorkflowOperatorPrompt>(interaction.CurrentPrompt);
        options.Clear();
        Assert.Equal(2, prompt.Options.Count);
        Assert.False(task.IsCompleted);
        Assert.False(interaction.TryChoose(Guid.NewGuid(), "Continue"));
        Assert.False(interaction.TryChoose(prompt.Id, "Unconfigured"));
        Assert.False(interaction.TryConfirm(prompt.Id));
        Assert.False(task.IsCompleted);
        Assert.True(interaction.TryChoose(prompt.Id, "continue"));
        Assert.False(interaction.TryChoose(prompt.Id, "Restart"));
        Assert.Equal("Continue", await task);
        Assert.Null(interaction.CurrentPrompt);
    }

    [Fact]
    public async Task Confirmation_DoesNotAcceptChoiceOrAutoComplete()
    {
        using var interaction = new WorkflowOperatorInteraction();
        var task = interaction.ConfirmStepAsync("人工处置", "确认已完成", default).AsTask();
        var prompt = Assert.IsType<WorkflowOperatorPrompt>(interaction.CurrentPrompt);
        Assert.Empty(prompt.Options);
        Assert.False(task.IsCompleted);
        Assert.False(interaction.TryChoose(prompt.Id, "Confirm"));
        Assert.True(interaction.TryConfirm(prompt.Id));
        await task;
        Assert.False(interaction.TryConfirm(prompt.Id));
    }

    [Fact]
    public async Task CancelQueuedTask_DoesNotCancelCurrentOrDisplayCanceledTask()
    {
        using var interaction = new WorkflowOperatorInteraction();
        using var cancellation = new CancellationTokenSource();
        var first = interaction.AskChoiceAsync("first", "", Choices, default).AsTask();
        var current = interaction.CurrentPrompt!;
        var second = interaction.ConfirmStepAsync("second", "", cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(current.Id, interaction.CurrentPrompt!.Id);
        Assert.False(first.IsCompleted);
        Assert.True(interaction.TryChoose(current.Id, "Restart"));
        Assert.Equal("Restart", await first);
        Assert.Null(interaction.CurrentPrompt);
    }

    [Fact]
    public async Task OldResponseAndOldWindowClose_CannotAffectNextPrompt()
    {
        using var interaction = new WorkflowOperatorInteraction();
        var first = interaction.ConfirmStepAsync("first", "", default).AsTask();
        var oldId = interaction.CurrentPrompt!.Id;
        var nextReady = ObservePrompt(interaction, prompt => prompt.Title == "second");
        var second = interaction.ConfirmStepAsync("second", "", default).AsTask();
        Assert.Equal(oldId, interaction.CurrentPrompt!.Id);
        Assert.True(interaction.TryConfirm(oldId));
        await first;
        var next = await nextReady.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(oldId, next.Id);
        Assert.False(interaction.TryConfirm(oldId));
        Assert.False(interaction.TryDismiss(oldId));
        Assert.False(second.IsCompleted);
        Assert.True(interaction.TryConfirm(next.Id));
        await second;
    }

    [Fact]
    public async Task Dismiss_IsNotDefaultChoiceOrRunCancellation()
    {
        using var interaction = new WorkflowOperatorInteraction();
        var task = interaction.AskChoiceAsync("选择", "", Choices, default).AsTask();
        Assert.True(interaction.TryDismiss(interaction.CurrentPrompt!.Id));
        await Assert.ThrowsAsync<WorkflowOperatorPromptDismissedException>(() => task);
        Assert.Null(interaction.CurrentPrompt);
    }

    [Fact]
    public async Task Dispose_CancelsActiveAndQueuedAndRejectsNewRequests()
    {
        var interaction = new WorkflowOperatorInteraction();
        var first = interaction.ConfirmStepAsync("first", "", default).AsTask();
        var oldId = interaction.CurrentPrompt!.Id;
        var second = interaction.ConfirmStepAsync("second", "", default).AsTask();
        interaction.Dispose();
        interaction.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.False(interaction.TryConfirm(oldId));
        Assert.Null(interaction.CurrentPrompt);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => interaction.ConfirmStepAsync("third", "", default).AsTask());
    }

    [Fact]
    public async Task CancelActivePrompt_RejectsLateAnswer()
    {
        using var interaction = new WorkflowOperatorInteraction();
        using var cancellation = new CancellationTokenSource();
        var task = interaction.AskChoiceAsync("选择", "", Choices, cancellation.Token).AsTask();
        var id = interaction.CurrentPrompt!.Id;
        cancellation.Cancel();
        Assert.False(interaction.TryChoose(id, "Continue"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task ObserverException_DoesNotAutoAnswerOrBreakCleanup()
    {
        using var interaction = new WorkflowOperatorInteraction();
        interaction.PromptChanged += (_, _) => throw new InvalidOperationException("坏观察器");
        var task = interaction.ConfirmStepAsync("确认", "", default).AsTask();
        Assert.False(task.IsCompleted);
        Assert.True(interaction.TryConfirm(interaction.CurrentPrompt!.Id));
        await task;
        Assert.Null(interaction.CurrentPrompt);
    }

    [Fact]
    public async Task DuplicateChoiceKeys_AreRejectedBeforePublishing()
    {
        using var interaction = new WorkflowOperatorInteraction();
        var choices = new[] { new OperatorChoiceOption("a", "一"), new OperatorChoiceOption("A", "二") };
        await Assert.ThrowsAsync<ArgumentException>(() => interaction.AskChoiceAsync("选择", "", choices, default).AsTask());
        Assert.Null(interaction.CurrentPrompt);
    }

    private static Task<WorkflowOperatorPrompt> ObservePrompt(WorkflowOperatorInteraction interaction, Func<WorkflowOperatorPrompt, bool> predicate)
    {
        var ready = new TaskCompletionSource<WorkflowOperatorPrompt>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (interaction.CurrentPrompt is { } prompt && predicate(prompt))
            {
                interaction.PromptChanged -= handler;
                ready.TrySetResult(prompt);
            }
        };
        interaction.PromptChanged += handler;
        handler(null, EventArgs.Empty);
        return ready.Task;
    }
}
