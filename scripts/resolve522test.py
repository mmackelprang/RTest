import io, sys

# Rewrites #522's blank-value test to the reconciled contract. Run against the file wherever it
# lands - the trial worktree now, the real branch after #522 merges.
path = sys.argv[1]
s = io.open(path, encoding="utf-8").read()

old = '''  [Fact]
  public async Task PostingAnEmptyValue_StillClearsTheSecret()
  {
    var provider = new InMemorySecretsProvider();
    await provider.SetSecretAsync("tts_azure_region", ShortSecret);
    var controller = CreateController(provider);

    await controller.SetSectionSecrets(
      "tts", new Dictionary<string, string> { ["AzureRegion"] = "" });

    Assert.Null(await provider.GetSecretAsync("tts_azure_region"));
  }'''

new = '''  [Fact]
  public async Task PostingAnEmptyValue_LeavesTheSecretAlone()
  {
    // This assertion was inverted deliberately, and it is the one place the two fixes for this
    // bug disagreed. An empty value used to mean "delete". It now means "unchanged", because the
    // Secrets page presents a configured secret as an empty box with a hint rather than as
    // editable masked text - so under the old rule an ordinary Save would have deleted every
    // secret the user did not retype, which is worse than the overwrite being fixed here.
    // Clearing is explicit instead: DELETE /api/secrets/{section}/{property} for one,
    // DELETE /api/secrets/{section} for all. Both are covered in SecretsControllerTests.
    var provider = new InMemorySecretsProvider();
    await provider.SetSecretAsync("tts_azure_region", ShortSecret);
    var controller = CreateController(provider);

    await controller.SetSectionSecrets(
      "tts", new Dictionary<string, string> { ["AzureRegion"] = "" });

    Assert.Equal(ShortSecret, await provider.GetSecretAsync("tts_azure_region"));
  }'''

assert s.count(old) == 1, "blank-value test: found %d occurrences" % s.count(old)
io.open(path, "w", encoding="utf-8", newline="").write(s.replace(old, new))
print("blank-value test reconciled")
