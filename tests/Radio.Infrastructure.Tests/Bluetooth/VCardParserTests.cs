using Radio.Infrastructure.Bluetooth;

namespace Radio.Infrastructure.Tests.Bluetooth;

public class VCardParserTests
{
  [Fact]
  public void Parse_SimpleVCard30_ShouldExtractNameAndNumber()
  {
    var vcf = """
        BEGIN:VCARD
        VERSION:3.0
        FN:John Smith
        TEL;TYPE=CELL:+1-555-123-4567
        END:VCARD
        """;

    var contacts = VCardParser.Parse(vcf);

    Assert.Single(contacts);
    Assert.Equal("John Smith", contacts[0].DisplayName);
    Assert.Single(contacts[0].PhoneNumbers);
    Assert.Equal("5551234567", contacts[0].PhoneNumbers[0]);
  }

  [Fact]
  public void Parse_MultipleNumbers_ShouldExtractAll()
  {
    var vcf = """
        BEGIN:VCARD
        VERSION:3.0
        FN:Jane Doe
        TEL;TYPE=HOME:(555) 987-6543
        TEL;TYPE=CELL:555.111.2222
        TEL;TYPE=WORK:15553334444
        END:VCARD
        """;

    var contacts = VCardParser.Parse(vcf);

    Assert.Single(contacts);
    Assert.Equal(3, contacts[0].PhoneNumbers.Count);
    Assert.Contains("9876543", contacts[0].PhoneNumbers.Select(p => p[^7..]));
  }

  [Fact]
  public void Parse_MultipleContacts_ShouldParseAll()
  {
    var vcf = """
        BEGIN:VCARD
        VERSION:3.0
        FN:Alice
        TEL:1111111
        END:VCARD
        BEGIN:VCARD
        VERSION:3.0
        FN:Bob
        TEL:2222222
        END:VCARD
        """;

    var contacts = VCardParser.Parse(vcf);

    Assert.Equal(2, contacts.Count);
    Assert.Equal("Alice", contacts[0].DisplayName);
    Assert.Equal("Bob", contacts[1].DisplayName);
  }

  [Fact]
  public void Parse_VCard21WithQuotedPrintable_ShouldDecode()
  {
    var vcf = "BEGIN:VCARD\r\nVERSION:2.1\r\nFN;ENCODING=QUOTED-PRINTABLE:Jos=C3=A9 Garc=C3=ADa\r\nTEL:5551234567\r\nEND:VCARD";

    var contacts = VCardParser.Parse(vcf);

    Assert.Single(contacts);
    Assert.Equal("José García", contacts[0].DisplayName);
  }

  [Fact]
  public void Parse_NoFN_ShouldSkipContact()
  {
    var vcf = """
        BEGIN:VCARD
        VERSION:3.0
        TEL:5551234567
        END:VCARD
        """;

    var contacts = VCardParser.Parse(vcf);

    Assert.Empty(contacts);
  }

  [Fact]
  public void Parse_NoTEL_ShouldSkipContact()
  {
    var vcf = """
        BEGIN:VCARD
        VERSION:3.0
        FN:No Phone
        END:VCARD
        """;

    var contacts = VCardParser.Parse(vcf);

    Assert.Empty(contacts);
  }

  [Fact]
  public void Parse_LineFolding_ShouldUnfold()
  {
    // RFC 2426: continuation lines start with space or tab
    var vcf = "BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Very Long\r\n  Name Here\r\nTEL:5551234567\r\nEND:VCARD";

    var contacts = VCardParser.Parse(vcf);

    Assert.Single(contacts);
    Assert.Equal("Very Long Name Here", contacts[0].DisplayName);
  }

  [Fact]
  public void Parse_EmptyInput_ShouldReturnEmpty()
  {
    Assert.Empty(VCardParser.Parse(""));
    Assert.Empty(VCardParser.Parse("   "));
  }
}
