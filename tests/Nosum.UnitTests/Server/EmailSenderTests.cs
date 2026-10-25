// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Nosum.Abstractions.Email;
using Nosum.Contracts.Common;
using Nosum.Infrastructure.Email;
using Xunit;

namespace Nosum.UnitTests.Server;

public sealed class EmailSenderTests
{
    [Fact]
    public async Task LoggingEmailSender_LogsThatAMailWasSentButNeverTheCodeOrTheFullAddress()
    {
        List<string> lines = [];
        LoggingEmailSender sender = new(lines.Add);

        await sender.SendAsync(new EmailMessage(EmailKind.PasswordReset, "farmer01@gmail.com", GameLanguage.English, "12345678"), CancellationToken.None);

        lines.Should().ContainSingle().Which.Should().Contain("PasswordReset").And.Contain("f***@gmail.com")
            .And.NotContain("12345678").And.NotContain("farmer01");
    }

    [Theory]
    [InlineData(GameLanguage.English, "recovery code")]
    [InlineData(GameLanguage.Vietnamese, "khôi phục")]
    [InlineData(GameLanguage.Unspecified, "khôi phục")]
    public void SmtpEmailSender_Render_UsesTheRecipientsLanguageAndCarriesTheCode(GameLanguage language, string expectedPhrase)
    {
        (string subject, string body) = SmtpEmailSender.Render(new EmailMessage(EmailKind.PasswordReset, "farmer01@gmail.com", language, "87654321"));

        subject.Should().Contain(expectedPhrase);
        body.Should().Contain("87654321").And.Contain("15");
    }

    [Theory]
    [InlineData("farmer01@gmail.com", "f***@gmail.com")]
    [InlineData("@gmail.com", "***")]
    [InlineData("nope", "***")]
    public void Mask_HidesTheLocalPart(string address, string expected)
        => LoggingEmailSender.Mask(address).Should().Be(expected);
}
