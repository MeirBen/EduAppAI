using FamilyLearning.Api.TaskEngine;

namespace FamilyLearning.Api.Tests.TaskEngine;

public sealed class ConversationWindowTests
{
    [Fact]
    public void Window_keeps_the_newest_six_turns_and_stops_at_the_first_turn_over_the_length()
    {
        var turns = Enumerable.Range(0, 8).Select(i => "turn " + i).ToArray();
        Assert.Equal(turns[2..], ConversationWindow.Latest(turns, turn => turn));

        // The newest 12,000 characters fit; the short turn before them would exceed the length, so the window ends there.
        string[] full = ["short", new('a', 4000), new('b', 4000), new('c', 4000)];
        Assert.Equal(full[1..], ConversationWindow.Latest(full, turn => turn));
        Assert.Empty(ConversationWindow.Latest(Array.Empty<string>(), turn => turn));
    }
}
