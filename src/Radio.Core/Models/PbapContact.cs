namespace Radio.Core.Models;

public class PbapContact
{
  public string DisplayName { get; set; } = string.Empty;
  public List<string> PhoneNumbers { get; set; } = new();
}
