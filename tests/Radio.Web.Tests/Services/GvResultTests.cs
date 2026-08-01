using System.Net;
using Radio.Web.Models;
using Radio.Web.Services.ApiClients;

namespace Radio.Web.Tests.Services;

/// <summary>
/// GV-8 (UAT F-1). GvBridgeApiService used to collapse every non-2xx, timeout and
/// deserialization error to a bare null, so a caller could not tell "the load failed"
/// from "there is nothing there" — and PhonePage rendered the failure as an empty
/// conversation. These tests pin the discrimination GV-6 will also depend on.
/// </summary>
public class GvResultTests
{
  private static readonly SmsThreadMessagesDto Dto =
    new("t1", Array.Empty<SmsMessageDto>(), DateTime.UtcNow);

  [Fact]
  public void Success_CarriesTheValue_AndReportsSuccess()
  {
    var result = GvResult<SmsThreadMessagesDto>.Success(Dto);

    Assert.True(result.IsSuccess);
    Assert.False(result.IsFailure);
    Assert.Equal(GvCallOutcome.Success, result.Outcome);
    Assert.Same(Dto, result.Value);
    Assert.Null(result.StatusCode);
    Assert.Null(result.ErrorCode);
  }

  [Fact]
  public void HttpError_CarriesStatusAndErrorCode_AndReportsFailure()
  {
    var result = GvResult<SmsThreadMessagesDto>.HttpError(
      HttpStatusCode.Conflict, "markread_disabled");

    Assert.False(result.IsSuccess);
    Assert.True(result.IsFailure);
    Assert.Equal(GvCallOutcome.HttpError, result.Outcome);
    Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
    Assert.Equal("markread_disabled", result.ErrorCode);
    Assert.Null(result.Value);
  }

  [Fact]
  public void HttpError_AllowsAnAbsentErrorCode()
  {
    var result = GvResult<SmsThreadMessagesDto>.HttpError(HttpStatusCode.BadGateway);

    Assert.Equal(HttpStatusCode.BadGateway, result.StatusCode);
    Assert.Null(result.ErrorCode);
  }

  [Fact]
  public void Timeout_Transport_And_Malformed_AllReportFailure_WithNoValueOrStatus()
  {
    var timeout = GvResult<SmsThreadMessagesDto>.Timeout();
    var transport = GvResult<SmsThreadMessagesDto>.Transport();
    var malformed = GvResult<SmsThreadMessagesDto>.Malformed();

    Assert.Equal(GvCallOutcome.Timeout, timeout.Outcome);
    Assert.Equal(GvCallOutcome.Transport, transport.Outcome);
    Assert.Equal(GvCallOutcome.Malformed, malformed.Outcome);

    foreach (var result in new[] { timeout, transport, malformed })
    {
      Assert.False(result.IsSuccess);
      Assert.True(result.IsFailure);
      Assert.Null(result.Value);
      Assert.Null(result.StatusCode);
    }
  }
}
