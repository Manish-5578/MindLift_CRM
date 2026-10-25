using MailKit.Security;
using MindLift.Infrastructure;
using MindLift.Infrastructure.Messaging;
using Shouldly;

namespace MindLift.UnitTests.Messaging;

public sealed class SmtpSecurityTests
{
    [Fact]
    public void Port_465_is_encrypted_from_the_start()
        => SmtpSecurity.Choose("smtp.example.com", 465, new SmtpOptions()).ShouldBe(SecureSocketOptions.SslOnConnect);

    [Fact]
    public void The_starttls_setting_is_honoured()
        => SmtpSecurity.Choose("smtp.example.com", 587, new SmtpOptions { UseStartTls = true }).ShouldBe(SecureSocketOptions.StartTls);

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void A_server_on_this_machine_may_skip_encryption(string host)
        => SmtpSecurity.Choose(host, 1025, new SmtpOptions()).ShouldBe(SecureSocketOptions.Auto);

    [Fact]
    public void A_remote_server_is_never_used_unencrypted_unless_allowed()
    {
        SmtpSecurity.Choose("mail.example.com", 25, new SmtpOptions()).ShouldBe(SecureSocketOptions.StartTls);
        SmtpSecurity.Choose("mail.example.com", 25, new SmtpOptions { AllowInsecure = true }).ShouldBe(SecureSocketOptions.Auto);
    }
}
