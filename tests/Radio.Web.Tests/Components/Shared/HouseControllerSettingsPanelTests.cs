using System.Net;
using System.Reflection;
using System.Text;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Radio.Core.Utilities;
using Radio.Web.Components.Shared.HouseAudio;
using Radio.Web.Models;
using Radio.Web.Services;
using Radio.Web.Tests.TestHelpers;

namespace Radio.Web.Tests.Components.Shared;

/// <summary>
/// The Controller settings panel against the real client, on the fake controller. Nothing here is
/// timed: every command is awaited to its end through the click that sends it. Whether a field clears
/// the on-screen keyboard needs a browser and is measured there; these hold the structure that makes it
/// so, which is that every text field lives in a kiosk entry card and none in the panel.
/// </summary>
public class HouseControllerSettingsPanelTests : TestContext
{
  private const string Panel = "[data-testid='house-controller-panel']";
  private const string Entry = ".kiosk-entry-overlay > .kiosk-entry-card";
  private const string TypedPassword = "typed-secret";

  private static readonly HouseSystemInfo System = new("Mackelhome Controller", "192.168.86.49", true, "1.1.1");

  private static readonly HouseSource[] ThreeSources =
  [
    new(0, "Audiocast 1", 7),
    new(1, "Audiocast 2", 7),
    new(2, "Pi Audio", 11)
  ];

  private readonly FakeHouseController _controller = new();
  private readonly List<Func<HouseAudioSnapshot, HouseAudioSnapshot>> _saved = [];
  private HouseAudioPasswordStore _passwords = FakeHouseController.StoreWithPassword();

  public HouseControllerSettingsPanelTests()
  {
    JSInterop.Mode = JSRuntimeMode.Loose;
  }

  private static HouseAudioSnapshot Snapshot(
    IReadOnlyList<HouseSource>? sources = null,
    HouseSystemInfo? system = null,
    HouseAudioHealth health = HouseAudioHealth.Ok) =>
    new(health, system ?? System, [], sources ?? ThreeSources, Stale: health != HouseAudioHealth.Ok);

  private IRenderedComponent<HouseControllerSettingsPanel> Render(HouseAudioSnapshot? snapshot)
  {
    Services.AddSingleton(_controller.CreateClient(_passwords));
    Services.AddSingleton(_passwords);
    return RenderComponent<HouseControllerSettingsPanel>(parameters => parameters
      .Add(panel => panel.Snapshot, snapshot)
      .Add(panel => panel.OnSaved, EventCallback.Factory.Create<Func<HouseAudioSnapshot, HouseAudioSnapshot>>(this, _saved.Add)));
  }

  private IRenderedComponent<HouseControllerSettingsPanel> Render() => Render(Snapshot());

  private static IElement Test(IRenderedComponent<HouseControllerSettingsPanel> cut, string id) =>
    cut.Find($"[data-testid='{id}']");

  private static IElement SourceRow(IRenderedComponent<HouseControllerSettingsPanel> cut, string name) =>
    cut.FindAll("[data-testid='house-source-row']").Single(row => row.TextContent.Contains(name));

  private static Task TapAsync(IRenderedComponent<HouseControllerSettingsPanel> cut, string id) =>
    cut.InvokeAsync(() => Test(cut, id).ClickAsync(new MouseEventArgs()));

  private static Task TapSourceAsync(IRenderedComponent<HouseControllerSettingsPanel> cut, string name) =>
    cut.InvokeAsync(() => SourceRow(cut, name).ClickAsync(new MouseEventArgs()));

  private static Task PickAsync(IRenderedComponent<HouseControllerSettingsPanel> cut, string label) =>
    cut.InvokeAsync(() => cut.FindAll("[data-testid='house-entry-option']")
      .Single(option => option.TextContent.Trim() == label)
      .ClickAsync(new MouseEventArgs()));

  private static List<string> Options(IRenderedComponent<HouseControllerSettingsPanel> cut) =>
    cut.FindAll("[data-testid='house-entry-option']").Select(option => option.TextContent.Trim()).ToList();

  private static IElement NameField(IRenderedComponent<HouseControllerSettingsPanel> cut) =>
    cut.Find($"{Entry} input[type='text']");

  private static void TypeName(IRenderedComponent<HouseControllerSettingsPanel> cut, string name) =>
    NameField(cut).Change(name);

  private List<SeenRequest> Writes => _controller.Seen.Where(request => request.Method != HttpMethod.Get).ToList();

  [Fact]
  public async Task Rename_CallsRenameController()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-controller-name");
    NameField(cut).GetAttribute("value").Should().Be("Mackelhome Controller");

    TypeName(cut, "  Upstairs  ");
    await TapAsync(cut, "house-entry-save");

    SeenRequest write = Writes.Should().ContainSingle().Subject;
    write.Method.Should().Be(HttpMethod.Put);
    write.Uri.AbsolutePath.Should().Be("/system/name");
    write.Body.Should().Contain("\"Upstairs\"");
    cut.FindAll(Entry).Should().BeEmpty();
  }

  [Fact]
  public async Task Rename_ShowsAtOnce_BeforeThePollReportsIt()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-controller-name");
    TypeName(cut, "Upstairs");

    await TapAsync(cut, "house-entry-save");

    _saved.Should().ContainSingle().Which(Snapshot()).System!.Name.Should().Be("Upstairs");
  }

  [Theory]
  [InlineData(HttpStatusCode.BadRequest, "refused")]
  [InlineData(HttpStatusCode.Unauthorized, "password")]
  [InlineData(HttpStatusCode.InternalServerError, "reach the controller")]
  public async Task Rename_Failure_KeepsThePanelOpen_WithTheEdit(HttpStatusCode status, string reason)
  {
    _controller.WriteStatus = status;
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-controller-name");
    TypeName(cut, "Upstairs");

    await TapAsync(cut, "house-entry-save");

    cut.Find(Entry).Should().NotBeNull();
    NameField(cut).GetAttribute("value").Should().Be("Upstairs");
    Test(cut, "house-entry-error").TextContent.Should().Contain(reason);
    _saved.Should().BeEmpty();
  }

  [Theory]
  [InlineData("", "Enter a name")]
  [InlineData("   ", "Enter a name")]
  [InlineData("a\0b", "can't contain")]
  public async Task Rename_RefusesABadName_InWords_AndSendsNothing(string name, string words)
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-controller-name");
    TypeName(cut, name);

    await TapAsync(cut, "house-entry-save");

    Test(cut, "house-entry-error").TextContent.Should().Contain(words);
    Writes.Should().BeEmpty();
  }

  [Theory]
  [InlineData(252, 'a', true)]
  [InlineData(253, 'a', false)]
  [InlineData(126, 'é', true)]
  [InlineData(127, 'é', false)]
  public async Task Rename_LimitIs252Bytes_NotCharacters(int count, char letter, bool sent)
  {
    string name = new(letter, count);
    Encoding.UTF8.GetByteCount(name).Should().BeCloseTo(sent ? 252 : 254, 1);
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-controller-name");
    TypeName(cut, name);

    await TapAsync(cut, "house-entry-save");

    Writes.Should().HaveCount(sent ? 1 : 0);
    if (!sent)
    {
      Test(cut, "house-entry-error").TextContent.Should().Contain("252");
    }
  }

  [Fact]
  public void Sources_ShowNameAndType()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();

    IElement row = SourceRow(cut, "Audiocast 1");
    row.TextContent.Should().Contain("Google Cast");
    row.TextContent.Should().Contain("Input 1");
    SourceRow(cut, "Pi Audio").TextContent.Should().Contain("MP3").And.Contain("Input 3");
  }

  [Fact]
  public void Sources_NameATypeOutsideTheTable_ByItsNumber()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(Snapshot([new(0, "Odd", 25)]));

    SourceRow(cut, "Odd").TextContent.Should().Contain("Type 25");
  }

  [Fact]
  public async Task AddSource_OffersOnlyFreeInputs()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-source-add");

    await TapAsync(cut, "house-entry-input");

    Options(cut).Should().Equal("Input 4", "Input 5", "Input 6");
  }

  [Fact]
  public async Task AddSource_OffersTheGapInTheInputs()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(Snapshot(
      [new(0, "A", 0), new(2, "B", 0), new(3, "C", 0), new(4, "D", 0), new(5, "E", 0)]));
    await TapAsync(cut, "house-source-add");

    await TapAsync(cut, "house-entry-input");

    Options(cut).Should().Equal("Input 2");
  }

  [Fact]
  public async Task AddSource_SendsZeroBasedId()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-source-add");
    Test(cut, "house-entry-input").TextContent.Should().Contain("Input 4");
    TypeName(cut, "  Turntable ");
    await TapAsync(cut, "house-entry-type");
    await PickAsync(cut, "Phono");

    await TapAsync(cut, "house-entry-save");

    SeenRequest write = Writes.Should().ContainSingle().Subject;
    write.Method.Should().Be(HttpMethod.Post);
    write.Uri.AbsolutePath.Should().Be("/sources");
    write.Body.Should().Contain("\"sourceID\":3").And.Contain("\"name\":\"Turntable\"").And.Contain("\"type\":13");
    cut.FindAll(Entry).Should().BeEmpty();
    _saved.Should().ContainSingle().Which(Snapshot()).Sources.Select(source => source.SourceId).Should().Equal(0, 1, 2, 3);
  }

  [Fact]
  public async Task AddSource_AlreadyReportedByAPoll_ShowsOneRow()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-source-add");
    TypeName(cut, "Turntable");

    await TapAsync(cut, "house-entry-save");

    HouseAudioSnapshot polledFirst = Snapshot([.. ThreeSources, new HouseSource(3, "Turntable", 0)]);
    _saved.Should().ContainSingle().Which(polledFirst).Sources.Select(source => source.SourceId).Should().Equal(0, 1, 2, 3);
  }

  [Fact]
  public async Task AddSource_PickingAnotherInput_SendsThatInput()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-source-add");
    TypeName(cut, "Turntable");
    await TapAsync(cut, "house-entry-input");
    await PickAsync(cut, "Input 6");

    NameField(cut).GetAttribute("value").Should().Be("Turntable");
    await TapAsync(cut, "house-entry-save");

    Writes.Should().ContainSingle().Which.Body.Should().Contain("\"sourceID\":5");
  }

  [Fact]
  public async Task TypePicker_OffersEveryType_AndKeepsTheEdit()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-source-add");
    TypeName(cut, "Turntable");

    await TapAsync(cut, "house-entry-type");

    Options(cut).Should().Equal(HouseSourceTypes.Labels);
    Options(cut).Should().HaveCount(20);
    await TapAsync(cut, "house-entry-pick-cancel");
    NameField(cut).GetAttribute("value").Should().Be("Turntable");
    Test(cut, "house-entry-type").TextContent.Should().Contain("Generic");
  }

  [Fact]
  public void AddSource_Hidden_WhenAllSixInputsUsed()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(Snapshot(
      Enumerable.Range(0, 6).Select(id => new HouseSource(id, $"Source {id}", 0)).ToList()));

    cut.FindAll("[data-testid='house-source-row']").Should().HaveCount(6);
    cut.FindAll("[data-testid='house-source-add']").Should().BeEmpty();
  }

  [Fact]
  public void AddSource_Hidden_WhileTheSourcesAreNotKnown()
  {
    // The amp link is down: /system answers, /sources never has. Empty here means unknown, not six free.
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(Snapshot([], health: HouseAudioHealth.AmpDisconnected));

    cut.FindAll("[data-testid='house-source-add']").Should().BeEmpty();
    Test(cut, "house-sources-unknown").Should().NotBeNull();
  }

  [Fact]
  public void AddSource_Shown_WhenTheControllerReallyHasNoSources()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(Snapshot([]));

    cut.FindAll("[data-testid='house-source-add']").Should().ContainSingle();
  }

  [Fact]
  public async Task AddSource_Conflict_ShowsTheError_WithTheEdit()
  {
    _controller.WriteStatus = HttpStatusCode.Conflict;
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-source-add");
    TypeName(cut, "Turntable");

    await TapAsync(cut, "house-entry-save");

    Test(cut, "house-entry-error").TextContent.Should().Contain("Input 4").And.Contain("in use");
    NameField(cut).GetAttribute("value").Should().Be("Turntable");
    _saved.Should().BeEmpty();
  }

  [Fact]
  public async Task EditSource_SendsTheEdit_ForThatSource()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapSourceAsync(cut, "Pi Audio");
    NameField(cut).GetAttribute("value").Should().Be("Pi Audio");
    Test(cut, "house-entry-type").TextContent.Should().Contain("MP3");
    cut.FindAll("[data-testid='house-entry-input']").Should().BeEmpty("an existing source stays on its input");

    TypeName(cut, "Porch Pi");
    await TapAsync(cut, "house-entry-type");
    await PickAsync(cut, "Computer");
    await TapAsync(cut, "house-entry-save");

    SeenRequest write = Writes.Should().ContainSingle().Subject;
    write.Method.Should().Be(HttpMethod.Put);
    write.Uri.AbsolutePath.Should().Be("/sources/2");
    write.Body.Should().Contain("\"name\":\"Porch Pi\"").And.Contain("\"type\":5");
    _saved.Should().ContainSingle().Which(Snapshot()).Sources.Should().Contain(new HouseSource(2, "Porch Pi", 5));
  }

  [Fact]
  public async Task EditSource_Failure_KeepsTheEdit()
  {
    _controller.WriteStatus = HttpStatusCode.NotFound;
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapSourceAsync(cut, "Pi Audio");
    TypeName(cut, "Porch Pi");

    await TapAsync(cut, "house-entry-save");

    NameField(cut).GetAttribute("value").Should().Be("Porch Pi");
    Test(cut, "house-entry-error").TextContent.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task DeleteSource_AsksFirst()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapSourceAsync(cut, "Pi Audio");

    await TapAsync(cut, "house-entry-delete");

    Test(cut, "house-delete-source-prompt").TextContent.Should().Contain("Pi Audio");
    Writes.Should().BeEmpty();

    await TapAsync(cut, "house-delete-source-cancel");
    Writes.Should().BeEmpty();
    NameField(cut).GetAttribute("value").Should().Be("Pi Audio");

    await TapAsync(cut, "house-entry-delete");
    await TapAsync(cut, "house-delete-source-confirm");

    SeenRequest write = Writes.Should().ContainSingle().Subject;
    write.Method.Should().Be(HttpMethod.Delete);
    write.Uri.AbsolutePath.Should().Be("/sources/2");
    cut.FindAll(Entry).Should().BeEmpty();
    _saved.Should().ContainSingle().Which(Snapshot()).Sources.Select(source => source.Name).Should().NotContain("Pi Audio");
  }

  [Fact]
  public async Task DeleteSource_InUse_SaysWhy()
  {
    _controller.WriteStatus = HttpStatusCode.Conflict;
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapSourceAsync(cut, "Pi Audio");
    await TapAsync(cut, "house-entry-delete");

    await TapAsync(cut, "house-delete-source-confirm");

    Test(cut, "house-entry-error").TextContent.Should().Contain("Pi Audio").And.Contain("using it");
    cut.Find(Entry).Should().NotBeNull();
    _saved.Should().BeEmpty();
  }

  [Fact]
  public async Task DeleteSource_IsNotOfferedWhenAdding()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();

    await TapAsync(cut, "house-source-add");

    cut.FindAll("[data-testid='house-entry-delete']").Should().BeEmpty();
  }

  [Fact]
  public async Task Password_IsWriteOnly()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    cut.Markup.Should().NotContain(FakeHouseController.Password);

    await TapAsync(cut, "house-controller-password");

    IElement field = cut.Find($"{Entry} input[type='password']");
    (field.GetAttribute("value") ?? string.Empty).Should().BeEmpty();
    cut.Markup.Should().NotContain(FakeHouseController.Password);
  }

  [Fact]
  public async Task Password_Save_StoresWhatWasTyped_AndClosesTheDialog()
  {
    _passwords = FakeHouseController.CreateStore(new EphemeralDataProtectionProvider(), null, new RecordingHandler());
    JSInterop.Setup<string>("Reflect.get", _ => true).SetResult(TypedPassword);
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(Snapshot(health: HouseAudioHealth.NoPassword));

    await TapAsync(cut, "house-controller-password");
    await TapAsync(cut, "house-entry-save");

    _passwords.Get().Should().Be(TypedPassword);
    cut.FindAll(Entry).Should().BeEmpty();
    cut.Markup.Should().NotContain(TypedPassword);
    Test(cut, "house-controller-password").TextContent.Should().Contain("Saved");
  }

  [Fact]
  public async Task Password_FailedSave_KeepsTheDialog_AndARetryStillSends()
  {
    RecordingHandler configApi = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
    _passwords = FakeHouseController.CreateStore(new EphemeralDataProtectionProvider(), null, configApi);
    JSInterop.Setup<string>("Reflect.get", _ => true).SetResult(TypedPassword);
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(Snapshot(health: HouseAudioHealth.NoPassword));
    await TapAsync(cut, "house-controller-password");

    await TapAsync(cut, "house-entry-save");

    Test(cut, "house-entry-error").TextContent.Should().Contain("wasn't saved");
    cut.Find($"{Entry} input[type='password']").Should().NotBeNull();
    int posts = configApi.Count((method, _) => method == HttpMethod.Post);

    await TapAsync(cut, "house-entry-save");

    configApi.Count((method, _) => method == HttpMethod.Post).Should().Be(posts + 1, "the field still holds it, so a retry sends it");
  }

  [Fact]
  public async Task Password_IsNotKeptInTheComponent_AfterAFailedSave()
  {
    RecordingHandler configApi = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
    _passwords = FakeHouseController.CreateStore(new EphemeralDataProtectionProvider(), null, configApi);
    JSInterop.Setup<string>("Reflect.get", _ => true).SetResult(TypedPassword);
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-controller-password");

    await TapAsync(cut, "house-entry-save");

    typeof(HouseControllerSettingsPanel)
      .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
      .Where(field => field.FieldType == typeof(string))
      .Select(field => field.GetValue(cut.Instance))
      .Should().NotContain(TypedPassword);
    cut.Markup.Should().NotContain(TypedPassword);
  }

  [Fact]
  public async Task Password_Empty_SaysSo_AndSavesNothing()
  {
    RecordingHandler configApi = new();
    _passwords = FakeHouseController.CreateStore(new EphemeralDataProtectionProvider(), null, configApi);
    JSInterop.Setup<string>("Reflect.get", _ => true).SetResult(string.Empty);
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-controller-password");

    await TapAsync(cut, "house-entry-save");

    Test(cut, "house-entry-error").TextContent.Should().Contain("Enter the password");
    configApi.Count((_, _) => true).Should().Be(0);
  }

  [Fact]
  public async Task Password_FieldThatCannotBeRead_ReportsAFailure_InsteadOfEndingTheCircuit()
  {
    JSInterop.Setup<string>("Reflect.get", _ => true).SetException(new Microsoft.JSInterop.JSException("gone"));
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-controller-password");

    await TapAsync(cut, "house-entry-save");

    Test(cut, "house-entry-error").TextContent.Should().Contain("wasn't saved");
  }

  [Theory]
  [InlineData(HouseAudioHealth.NoPassword, "Not set")]
  [InlineData(HouseAudioHealth.WrongPassword, "Refused")]
  [InlineData(HouseAudioHealth.Ok, "Saved")]
  public void Password_Row_SaysWhereItStands(HouseAudioHealth health, string words)
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(Snapshot(health: health));

    Test(cut, "house-controller-password").TextContent.Should().Contain(words);
  }

  [Fact]
  public async Task Password_Row_SaysSavedUntilTheNextPoll_ThenWhatThatPollFound()
  {
    JSInterop.Setup<string>("Reflect.get", _ => true).SetResult(TypedPassword);
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(Snapshot(health: HouseAudioHealth.NoPassword));
    await TapAsync(cut, "house-controller-password");
    await TapAsync(cut, "house-entry-save");
    Test(cut, "house-controller-password").TextContent.Should().Contain("Checking");

    cut.SetParametersAndRender(parameters => parameters.Add(panel => panel.Snapshot, Snapshot(health: HouseAudioHealth.WrongPassword)));

    Test(cut, "house-controller-password").TextContent.Should().Contain("Refused");
  }

  [Fact]
  public async Task Poll_LeavesAnOpenEditAlone()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapSourceAsync(cut, "Pi Audio");
    TypeName(cut, "Porch Pi");

    cut.SetParametersAndRender(parameters => parameters.Add(panel => panel.Snapshot, Snapshot()));

    NameField(cut).GetAttribute("value").Should().Be("Porch Pi");
  }

  [Fact]
  public async Task Password_CanBeEntered_BeforeAnyPollHasAnswered()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(null);

    await TapAsync(cut, "house-controller-password");

    cut.Find($"{Entry} input[type='password']").Should().NotBeNull();
    Test(cut, "house-controller-name").HasAttribute("disabled").Should().BeTrue();
  }

  [Theory]
  [InlineData("house-controller-name")]
  [InlineData("house-controller-password")]
  [InlineData("house-source-add")]
  public async Task TextFields_OpenInKioskEntry_NotInlineInThePanel(string value)
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    cut.FindAll("input").Should().BeEmpty("the panel shows values, and tapping one opens the entry card");

    await TapAsync(cut, value);

    cut.FindAll("input").Should().ContainSingle();
    cut.FindAll($"{Panel} input").Should().BeEmpty();
    cut.FindAll($"{Entry} input").Should().ContainSingle();
    cut.FindAll($"{Panel} .kiosk-entry-overlay").Should().BeEmpty("the panel must not be its stacking context");
  }

  [Fact]
  public async Task TextFields_OpenInKioskEntry_ForAnExistingSource()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();

    await TapSourceAsync(cut, "Pi Audio");

    cut.FindAll($"{Panel} input").Should().BeEmpty();
    cut.FindAll($"{Entry} input").Should().ContainSingle();
  }

  [Theory]
  [InlineData("house-controller-name", "text")]
  [InlineData("house-controller-password", "password")]
  [InlineData("house-source-add", "text")]
  public async Task TextFields_AreRealInputs(string value, string type)
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();

    await TapAsync(cut, value);

    cut.Find("input").GetAttribute("type").Should().Be(type);
    cut.FindAll("[data-keyboard]").Should().BeEmpty("a text field wants the QWERTY layout");
  }

  [Theory]
  [InlineData("house-controller-name")]
  [InlineData("house-controller-password")]
  [InlineData("house-source-add")]
  public async Task TappingAValue_FocusesTheField_SoTheKeyboardOpens(string value)
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();

    await TapAsync(cut, value);

    cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke(), RecordingHandler.RendezvousTimeout);
  }

  [Fact]
  public async Task Cancel_DiscardsTheEdit_AndSendsNothing()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-controller-name");
    TypeName(cut, "Upstairs");

    await TapAsync(cut, "house-entry-cancel");

    cut.FindAll(Entry).Should().BeEmpty();
    Writes.Should().BeEmpty();
    await TapAsync(cut, "house-controller-name");
    NameField(cut).GetAttribute("value").Should().Be("Mackelhome Controller");
  }

  [Fact]
  public async Task Saving_CannotBeSentTwice()
  {
    TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    _controller.WriteGate = gate;
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();
    await TapAsync(cut, "house-controller-name");
    TypeName(cut, "Upstairs");

    Task first = TapAsync(cut, "house-entry-save");
    await _controller.Handler.WaitForAsync((method, _) => method == HttpMethod.Put, 1);
    cut.WaitForAssertion(() => Test(cut, "house-entry-save").HasAttribute("disabled").Should().BeTrue(), RecordingHandler.RendezvousTimeout);
    Test(cut, "house-entry-cancel").HasAttribute("disabled").Should().BeTrue();

    gate.SetResult();
    await first;
    Writes.Should().ContainSingle();
  }

  [Fact]
  public void About_ShowsAddressAndConnection()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render();

    Test(cut, "house-about-address").TextContent.Should().Contain("192.168.86.49");
    Test(cut, "house-about-connection").TextContent.Should().Contain("Connected");
    Test(cut, "house-about-build").TextContent.Should().Contain("Console build")
      .And.Contain(AssemblyBuildInfo.For(typeof(HouseControllerSettingsPanel).Assembly).GitShaShort);
    Test(cut, "house-about-controller-version").TextContent.Should().Contain("Controller version").And.Contain("1.1.1");
  }

  [Fact]
  public void About_NamesAnAmpLinkThatIsDown_AndAnAddressItDoesNotHave()
  {
    IRenderedComponent<HouseControllerSettingsPanel> cut = Render(Snapshot(
      system: System with { Address = null, SerialConnected = false }, health: HouseAudioHealth.AmpDisconnected));

    Test(cut, "house-about-address").TextContent.Should().Contain("Not reported");
    Test(cut, "house-about-connection").TextContent.Should().Contain("Not connected");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Closing_SlidesThePanelOut(bool closing)
  {
    Services.AddSingleton(_controller.CreateClient(_passwords));
    Services.AddSingleton(_passwords);
    IRenderedComponent<HouseControllerSettingsPanel> cut = RenderComponent<HouseControllerSettingsPanel>(parameters => parameters
      .Add(panel => panel.Snapshot, Snapshot())
      .Add(panel => panel.Closing, closing));

    Test(cut, "house-controller-panel").ClassList.Contains("is-closing").Should().Be(closing);
  }

  [Fact]
  public void Close_RaisesOnClose()
  {
    bool closed = false;
    Services.AddSingleton(_controller.CreateClient(_passwords));
    Services.AddSingleton(_passwords);
    IRenderedComponent<HouseControllerSettingsPanel> cut = RenderComponent<HouseControllerSettingsPanel>(parameters => parameters
      .Add(panel => panel.Snapshot, Snapshot())
      .Add(panel => panel.OnClose, () => closed = true));

    Test(cut, "house-controller-close").Click();

    closed.Should().BeTrue();
  }

  [Fact]
  public void SourceTypes_AreTheTwentyTheControllerKnows()
  {
    HouseSourceTypes.Labels.Should().HaveCount(20);
    HouseSourceTypes.Label(7).Should().Be("Google Cast");
    HouseSourceTypes.Label(11).Should().Be("MP3");
    HouseSourceTypes.Label(19).Should().Be("VCR");
    HouseSourceTypes.Label(20).Should().Be("Type 20");
    HouseSourceTypes.Label(-1).Should().Be("Type -1");
  }
}
