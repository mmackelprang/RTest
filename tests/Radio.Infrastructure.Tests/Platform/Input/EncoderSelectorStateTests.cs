using Radio.Core.Interfaces.Input;
using Radio.Infrastructure.Platform.Input;

namespace Radio.Infrastructure.Tests.Platform.Input;

/// <summary>
/// Covers the shared selector preview machine (ENC-5 Task 5).
///
/// <para>
/// ENC-7's PRESETS list constructs a second instance of the class under test and writes none of
/// this grammar again, so these assertions are about both knobs even though only SOURCE exists yet.
/// </para>
/// </summary>
public class EncoderSelectorStateTests
{
  private static EncoderSelectorRow Row(string id, bool isCurrent = false) => new()
  {
    Id = id,
    Primary = id.ToUpperInvariant(),
    IsCurrent = isCurrent,
  };

  private static List<EncoderSelectorRow> Rows(params EncoderSelectorRow[] rows) => [.. rows];

  [Fact]
  public void Open_SeedsHighlightOnTheCurrentRow()
  {
    var state = new EncoderSelectorState();
    state.SetRows(Rows(Row("a"), Row("b", isCurrent: true), Row("c")));

    state.Open();

    Assert.True(state.IsOpen);
    Assert.Equal(1, state.HighlightIndex);
  }

  [Fact]
  public void Open_SeedsFirstRow_WhenNothingIsCurrent()
  {
    var state = new EncoderSelectorState();
    state.SetRows(Rows(Row("a"), Row("b")));

    state.Open();

    Assert.Equal(0, state.HighlightIndex);
  }

  [Fact]
  public void Open_IsIdempotent_AndDoesNotResetTheHighlight()
  {
    // A press on an open overlay must not throw the highlight back to "current" - that would make
    // the second half of the one-rule press commit something the user did not aim at.
    var state = new EncoderSelectorState();
    state.SetRows(Rows(Row("a", isCurrent: true), Row("b"), Row("c")));
    state.Open();
    state.Move(2);

    state.Open();

    Assert.Equal(2, state.HighlightIndex);
  }

  [Fact]
  public void Move_WrapsForward_PastTheEnd()
  {
    var state = new EncoderSelectorState();
    state.SetRows(Rows(Row("a"), Row("b"), Row("c")));
    state.Open();

    state.Move(1);
    state.Move(1);
    state.Move(1);

    Assert.Equal(0, state.HighlightIndex);
  }

  [Fact]
  public void Move_WrapsBackward_PastTheStart()
  {
    var state = new EncoderSelectorState();
    state.SetRows(Rows(Row("a"), Row("b"), Row("c")));
    state.Open();

    state.Move(-1);

    Assert.Equal(2, state.HighlightIndex);
  }

  [Fact]
  public void Move_OnAnEmptyList_LeavesHighlightAtMinusOne()
  {
    var state = new EncoderSelectorState();
    state.Open();

    state.Move(1);

    Assert.Equal(-1, state.HighlightIndex);
    Assert.Null(state.Highlighted);
  }

  [Fact]
  public void SetRows_KeepsTheHighlightOnTheSameId_WhenTheListReorders()
  {
    var state = new EncoderSelectorState();
    state.SetRows(Rows(Row("a"), Row("b"), Row("c")));
    state.Open();
    state.Move(2);
    Assert.Equal("c", state.Highlighted!.Id);

    state.SetRows(Rows(Row("c"), Row("a"), Row("b")));

    // Identity, not position: the row the user was on is still the row they are on.
    Assert.Equal("c", state.Highlighted!.Id);
    Assert.Equal(0, state.HighlightIndex);
  }

  [Fact]
  public void SetRows_FallsBackToCurrent_WhenTheHighlightedIdIsGone()
  {
    var state = new EncoderSelectorState();
    state.SetRows(Rows(Row("a"), Row("b"), Row("c", isCurrent: true)));
    // Open seeds on the current row ("c"), so one detent back is "b".
    state.Open();
    state.Move(-1);
    Assert.Equal("b", state.Highlighted!.Id);

    state.SetRows(Rows(Row("a"), Row("c", isCurrent: true)));

    Assert.Equal("c", state.Highlighted!.Id);
  }

  [Fact]
  public void SetRows_Empty_ClearsTheHighlight()
  {
    var state = new EncoderSelectorState();
    state.SetRows(Rows(Row("a"), Row("b")));
    state.Open();

    state.SetRows([]);

    Assert.Equal(-1, state.HighlightIndex);
    Assert.Null(state.Highlighted);
  }
}
