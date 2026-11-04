using Gise.Infrastructure.Payments;

namespace Gise.UnitTests;

public sealed class WebhookSignatureTests
{
    private const string Secret = "shared-secret";
    private const string Body = """{"id":"evt_1","type":"payment.succeeded"}""";

    [Fact]
    public void A_signature_made_with_the_shared_secret_checks_out()
    {
        var header = WebhookSignature.Sign(Secret, Body, Builders.Now);

        Assert.Matches("^t=[0-9]+,v1=[0-9a-f]{64}$", header);
        Assert.True(WebhookSignature.IsValid(Secret, header, Body, Builders.Now.AddMinutes(4)));
    }

    [Fact]
    public void Another_secret_another_body_or_an_old_timestamp_do_not()
    {
        var header = WebhookSignature.Sign(Secret, Body, Builders.Now);

        Assert.False(WebhookSignature.IsValid("other-secret", header, Body, Builders.Now));
        Assert.False(WebhookSignature.IsValid(Secret, header, Body.Replace("succeeded", "failed", StringComparison.Ordinal), Builders.Now));
        Assert.False(WebhookSignature.IsValid(Secret, header, Body, Builders.Now.AddMinutes(6)));
        Assert.False(WebhookSignature.IsValid(Secret, header, Body, Builders.Now.AddMinutes(-6)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1=abc")]
    [InlineData("t=notanumber,v1=abc")]
    public void Malformed_headers_are_refused(string? header) =>
        Assert.False(WebhookSignature.IsValid(Secret, header, Body, Builders.Now));

    [Fact]
    public void Any_of_several_signatures_may_match_while_secrets_are_rotated()
    {
        var good = WebhookSignature.Sign(Secret, Body, Builders.Now);
        var header = $"{good},v1={new string('0', 64)}";

        Assert.True(WebhookSignature.IsValid(Secret, header, Body, Builders.Now));
    }
}
